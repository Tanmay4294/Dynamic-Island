using System.Net;
using System.Net.Http;
using Spotifly.App.Models;
using Spotifly.App.Services;
using Xunit;

namespace Spotifly.Tests;

public class SpotifyPhase10PlaybackTests
{
    private class MockAsyncHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public MockAsyncHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }

    private class MockTokenStore : ISecureTokenStore
    {
        private SpotifyTokenData? _tokens;
        public SpotifyTokenData? LoadTokens() => _tokens;
        public void SaveTokens(SpotifyTokenData tokenData) => _tokens = tokenData;
        public void ClearTokens() => _tokens = null;
        public bool HasTokens() => _tokens != null;
    }

    private static (SpotifyService service, MockTokenStore store) CreateServiceWithHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        var config = new SpotifyConfig();
        var tokenStore = new MockTokenStore();
        tokenStore.SaveTokens(new SpotifyTokenData
        {
            AccessToken = "valid_phase10_token",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        });

        var httpClient = new HttpClient(new MockAsyncHttpMessageHandler(handler));
        var authService = new SpotifyAuthService(config, tokenStore, httpClient);
        var spotifyService = new SpotifyService(authService, config, httpClient);

        return (spotifyService, tokenStore);
    }

    [Fact]
    public async Task PlayTrackWithResultAsync_Success_ReturnsSuccessResultOn204()
    {
        string? capturedBody = null;
        var (service, _) = CreateServiceWithHandler(async req =>
        {
            Assert.Equal(HttpMethod.Put, req.Method);
            Assert.Contains("/me/player/play", req.RequestUri?.ToString());
            if (req.Content != null)
            {
                capturedBody = await req.Content.ReadAsStringAsync();
            }
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var result = await service.PlayTrackWithResultAsync("spotify:track:song_123", "spotify:playlist:list_456");

        Assert.True(result.IsSuccess);
        Assert.Equal(PlaybackResultStatus.Success, result.Status);
        Assert.Contains("spotify:track:song_123", capturedBody);
        Assert.Contains("spotify:playlist:list_456", capturedBody);
    }

    [Fact]
    public async Task PlayTrackWithResultAsync_NoDevice_ReturnsNoDeviceOn404()
    {
        var (service, _) = CreateServiceWithHandler(req =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var result = await service.PlayTrackWithResultAsync("spotify:track:song_123");

        Assert.False(result.IsSuccess);
        Assert.Equal(PlaybackResultStatus.NoActiveDevice, result.Status);
        Assert.Contains("No active device", result.Message);
    }

    [Fact]
    public async Task PlayTrackWithResultAsync_Forbidden_ReturnsRestrictedOn403()
    {
        var (service, _) = CreateServiceWithHandler(req =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        });

        var result = await service.PlayTrackWithResultAsync("spotify:track:song_123");

        Assert.False(result.IsSuccess);
        Assert.Equal(PlaybackResultStatus.RestrictedOrNoPremium, result.Status);
        Assert.Contains("Premium is required", result.Message);
    }

    [Fact]
    public async Task PlayTrackWithResultAsync_RateLimited_ReturnsRateLimitedWithRetryAfterOn429()
    {
        var (service, _) = CreateServiceWithHandler(req =>
        {
            var res = new HttpResponseMessage((HttpStatusCode)429);
            res.Headers.Add("Retry-After", "8");
            return Task.FromResult(res);
        });

        var result = await service.PlayTrackWithResultAsync("spotify:track:song_123");

        Assert.False(result.IsSuccess);
        Assert.Equal(PlaybackResultStatus.RateLimited, result.Status);
        Assert.Equal(8, result.RetryAfterSeconds);
        Assert.Contains("8s", result.Message);
    }

    [Fact]
    public async Task PlayTrackWithResultAsync_TokenExpired401_RetriesAfterRefresh()
    {
        int callCount = 0;
        var (service, store) = CreateServiceWithHandler(req =>
        {
            if (req.RequestUri?.AbsolutePath == "/api/token")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"refreshed_phase10_token\",\"token_type\":\"Bearer\",\"expires_in\":3600}")
                });
            }

            callCount++;
            if (callCount == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }

            Assert.Equal("refreshed_phase10_token", req.Headers.Authorization?.Parameter);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });

        // Set refresh token so refresh succeeds
        var tokens = store.LoadTokens();
        tokens!.RefreshToken = "valid_refresh";
        store.SaveTokens(tokens);

        var result = await service.PlayTrackWithResultAsync("spotify:track:song_123");

        Assert.True(result.IsSuccess);
        Assert.Equal(PlaybackResultStatus.Success, result.Status);
        Assert.True(callCount >= 2);
    }

    [Fact]
    public async Task PlayTrackWithResultAsync_NetworkError_ReturnsNetworkError()
    {
        var (service, _) = CreateServiceWithHandler(req =>
        {
            throw new HttpRequestException("DNS resolution failed");
        });

        var result = await service.PlayTrackWithResultAsync("spotify:track:song_123");

        Assert.False(result.IsSuccess);
        Assert.Equal(PlaybackResultStatus.NetworkError, result.Status);
        Assert.Contains("DNS resolution failed", result.Message);
    }

    [Fact]
    public async Task PlayTrackWithResultAsync_EmptyUri_ReturnsFailureWithoutApiCall()
    {
        int apiCallCount = 0;
        var (service, _) = CreateServiceWithHandler(req =>
        {
            apiCallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });

        var result = await service.PlayTrackWithResultAsync("");

        Assert.False(result.IsSuccess);
        Assert.Equal(0, apiCallCount);
    }
}

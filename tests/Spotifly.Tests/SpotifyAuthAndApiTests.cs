using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Spotifly.App.Models;
using Spotifly.App.Services;
using Xunit;

namespace Spotifly.Tests;

public class SpotifyAuthAndApiTests
{
    [Fact]
    public void PkceHelper_GeneratesValidVerifierAndChallenge()
    {
        string verifier = PkceHelper.GenerateCodeVerifier(64);
        Assert.NotNull(verifier);
        Assert.Equal(64, verifier.Length);

        // Characters must be unreserved (A-Z, a-z, 0-9, -, ., _, ~)
        foreach (char c in verifier)
        {
            Assert.True(
                char.IsLetterOrDigit(c) || c == '-' || c == '.' || c == '_' || c == '~',
                $"Verifier contains invalid character: {c}");
        }

        string challenge = PkceHelper.GenerateCodeChallenge(verifier);
        Assert.NotNull(challenge);
        Assert.NotEmpty(challenge);
        Assert.DoesNotContain("=", challenge);
        Assert.DoesNotContain("+", challenge);
        Assert.DoesNotContain("/", challenge);

        // Challenge generation must be deterministic for the same verifier
        string challenge2 = PkceHelper.GenerateCodeChallenge(verifier);
        Assert.Equal(challenge, challenge2);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(129)]
    public void PkceHelper_ThrowsForInvalidLength(int invalidLength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PkceHelper.GenerateCodeVerifier(invalidLength));
    }

    [Fact]
    public void PkceHelper_GeneratesHighEntropyRandomState()
    {
        string state1 = PkceHelper.GenerateState();
        string state2 = PkceHelper.GenerateState();

        Assert.NotNull(state1);
        Assert.NotNull(state2);
        Assert.NotEqual(state1, state2);
        Assert.True(state1.Length >= 32);
    }

    [Fact]
    public void SpotifyTokenData_ExpiryEvaluation_WorksCorrectly()
    {
        var validToken = new SpotifyTokenData
        {
            AccessToken = "valid_token",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10)
        };
        Assert.False(validToken.IsExpired(bufferSeconds: 60));

        var expiringToken = new SpotifyTokenData
        {
            AccessToken = "expiring_token",
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(30)
        };
        Assert.True(expiringToken.IsExpired(bufferSeconds: 60));

        var expiredToken = new SpotifyTokenData
        {
            AccessToken = "expired_token",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5)
        };
        Assert.True(expiredToken.IsExpired(bufferSeconds: 60));
    }

    [Fact]
    public void WindowsDpapiTokenStore_SavesLoadsAndClearsTokens()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"spotifly_test_token_{Guid.NewGuid()}.dat");
        try
        {
            var store = new WindowsDpapiTokenStore(tempFile);
            Assert.False(store.HasTokens());
            Assert.Null(store.LoadTokens());

            var sample = new SpotifyTokenData
            {
                AccessToken = "test_access_token_123",
                RefreshToken = "test_refresh_token_456",
                ExpiresIn = 3600,
                ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
                Scope = "user-read-private"
            };

            store.SaveTokens(sample);
            Assert.True(store.HasTokens());

            var loaded = store.LoadTokens();
            Assert.NotNull(loaded);
            Assert.Equal("test_access_token_123", loaded.AccessToken);
            Assert.Equal("test_refresh_token_456", loaded.RefreshToken);
            Assert.Equal("user-read-private", loaded.Scope);

            store.ClearTokens();
            Assert.False(store.HasTokens());
            Assert.Null(store.LoadTokens());
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
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

    [Fact]
    public async Task SpotifyApiService_GetCurrentUserProfile_ReturnsDeserializedProfileOn200()
    {
        var config = new SpotifyConfig();
        var tokenStore = new MockTokenStore();
        tokenStore.SaveTokens(new SpotifyTokenData
        {
            AccessToken = "valid_token",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        });

        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
            Assert.Equal("valid_token", req.Headers.Authorization?.Parameter);

            var profileJson = JsonSerializer.Serialize(new SpotifyUserProfile
            {
                Id = "test_user_id",
                DisplayName = "Spotifly User",
                Email = "test@example.com",
                Product = "premium"
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(profileJson)
            };
        });

        var httpClient = new HttpClient(handler);
        var authService = new SpotifyAuthService(config, tokenStore, httpClient);
        var apiService = new SpotifyApiService(authService, config, httpClient);

        var profile = await apiService.GetCurrentUserProfileAsync();
        Assert.NotNull(profile);
        Assert.Equal("test_user_id", profile.Id);
        Assert.Equal("Spotifly User", profile.DisplayName);
        Assert.Equal("premium", profile.Product);
    }

    [Fact]
    public async Task SpotifyApiService_Handles403ForbiddenWithInformativeError()
    {
        var config = new SpotifyConfig();
        var tokenStore = new MockTokenStore();
        tokenStore.SaveTokens(new SpotifyTokenData
        {
            AccessToken = "forbidden_token",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        });

        var handler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        });

        var httpClient = new HttpClient(handler);
        var authService = new SpotifyAuthService(config, tokenStore, httpClient);
        var apiService = new SpotifyApiService(authService, config, httpClient);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => apiService.GetCurrentUserProfileAsync());
        Assert.Contains("Development Mode", ex.Message);
        Assert.Contains("Allowlist", ex.Message);
    }

    [Fact]
    public async Task SpotifyApiService_Handles429RateLimitWithRetryAfter()
    {
        var config = new SpotifyConfig();
        var tokenStore = new MockTokenStore();
        tokenStore.SaveTokens(new SpotifyTokenData
        {
            AccessToken = "ratelimited_token",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        });

        var handler = new MockHttpMessageHandler(req =>
        {
            var res = new HttpResponseMessage((HttpStatusCode)429);
            res.Headers.Add("Retry-After", "12");
            return res;
        });

        var httpClient = new HttpClient(handler);
        var authService = new SpotifyAuthService(config, tokenStore, httpClient);
        var apiService = new SpotifyApiService(authService, config, httpClient);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => apiService.GetCurrentUserProfileAsync());
        Assert.Contains("rate limit reached", ex.Message);
        Assert.Contains("12 seconds", ex.Message);
    }

    [Fact]
    public async Task SpotifyAuthService_Disconnect_PurgesTokensAndNotifies()
    {
        var config = new SpotifyConfig();
        var tokenStore = new MockTokenStore();
        tokenStore.SaveTokens(new SpotifyTokenData
        {
            AccessToken = "sample_token",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        });

        var authService = new SpotifyAuthService(config, tokenStore);
        Assert.True(authService.IsConnected);

        bool eventRaised = false;
        authService.ConnectionStatusChanged += (s, connected) =>
        {
            if (!connected) eventRaised = true;
        };

        await authService.DisconnectAsync();

        Assert.False(authService.IsConnected);
        Assert.False(tokenStore.HasTokens());
        Assert.True(eventRaised);
    }
}

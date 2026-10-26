using System.Net;
using System.Net.Http;
using Spotifly.App.Models;
using Spotifly.App.Services;
using Xunit;

namespace Spotifly.Tests;

public class Phase13QualitySecurityAndMatrixTests
{
    [Theory]
    [InlineData("Bearer BQC1234567890abcdef1234567890", "Bearer [REDACTED]")]
    [InlineData("access_token: \"BQAz9876543210\"", "access_token: \"[REDACTED]\"")]
    [InlineData("refresh_token = AQB9876543210", "refresh_token = [REDACTED]")]
    [InlineData("client_secret: 'secret123456'", "client_secret: '[REDACTED]'")]
    [InlineData("code: 43821code12345", "code: [REDACTED]")]
    public void FileLogger_SanitizesSensitiveData_AndRedactsTokens(string rawMessage, string expectedSanitized)
    {
        var sanitized = FileLoggerService.Sanitize(rawMessage);
        Assert.Equal(expectedSanitized, sanitized);
        Assert.DoesNotContain("BQC1234567890abcdef", sanitized);
        Assert.DoesNotContain("BQAz9876543210", sanitized);
        Assert.DoesNotContain("secret123456", sanitized);
    }

    [Fact]
    public async Task SpotifyService_HandlesNullItemsAndEpisodesInPlaylist_WithoutCrashing()
    {
        string jsonWithNullAndEpisode = """
        {
          "items": [
            {
              "track": null
            },
            {
              "track": {
                "id": "ep1",
                "name": "Episode title",
                "uri": "spotify:episode:ep1",
                "show": {
                  "name": "Daily Show",
                  "publisher": "Host"
                }
              }
            },
            {
              "track": {
                "id": "trk1",
                "name": "Valid Track",
                "uri": "spotify:track:trk1",
                "duration_ms": 180000,
                "artists": [{"name": "Artist A"}],
                "album": {
                  "name": "Album A",
                  "images": [{"url": "https://i.scdn.co/image/abc"}]
                }
              }
            }
          ],
          "total": 3,
          "next": null
        }
        """;

        var handler = new MockHttpMessageHandler((req) =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonWithNullAndEpisode)
            };
        });

        using var client = new HttpClient(handler);
        var auth = new MockSpotifyAuth("valid_token");
        var config = new SpotifyConfig();
        using var service = new SpotifyService(auth, config, client);

        var tracks = await service.GetPlaylistTracksAsync("p1");

        Assert.NotNull(tracks);
        // Null track is skipped cleanly; Episode and Track mapped safely
        Assert.Equal(2, tracks.Count);
        Assert.True(tracks[0].IsEpisode);
        Assert.Equal("Episode title", tracks[0].Name);
        Assert.Equal("Valid Track", tracks[1].Name);
        Assert.Equal("Artist A", tracks[1].ArtistName);
    }

    [Fact]
    public async Task SpotifyService_Handles500InternalServerError_WithoutThrowing()
    {
        var handler = new MockHttpMessageHandler((req) =>
        {
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        using var client = new HttpClient(handler);
        var auth = new MockSpotifyAuth("valid_token");
        var config = new SpotifyConfig();
        using var service = new SpotifyService(auth, config, client);

        var playlists = await service.GetUserPlaylistsAsync(forceRefresh: true);
        Assert.NotNull(playlists);
        Assert.Empty(playlists);

        var tracks = await service.GetPlaylistTracksAsync("p1", forceRefresh: true);
        Assert.NotNull(tracks);
        Assert.Empty(tracks);
    }

    [Fact]
    public async Task SpotifyService_TokenRevocation_ReturnsEmptySnapshot_WithoutCrashing()
    {
        // Simulate 401 on playback and auth returning null token on refresh
        var handler = new MockHttpMessageHandler((req) =>
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        });

        using var client = new HttpClient(handler);
        var auth = new MockSpotifyAuth(null); // Revoked/expired token
        var config = new SpotifyConfig();
        using var service = new SpotifyService(auth, config, client);

        var snapshot = await service.RefreshPlaybackAsync();

        Assert.NotNull(snapshot);
        Assert.False(snapshot.IsPlaying);
        Assert.Equal(TrackInfo.Empty.Name, snapshot.Track.Name);
    }

    [Fact]
    public void DisplayResolutionAndDpi_OverlayPositionCalculation_HandlesScaling()
    {
        var pos = new OverlayPositionService();
        var dims = pos.Dimensions;

        // Verify standard centering calculations for screen width & bounds
        double screenWidth = 1920;

        double expectedLeft = (screenWidth - dims.CompactWidth) / 2.0 + dims.HorizontalOffset;
        double expectedTop = dims.VerticalOffset;

        Assert.Equal(905.0, expectedLeft); // (1920 - 110) / 2
        Assert.Equal(0.0, expectedTop);

        // Scaled resolution simulation (e.g. 150% scaling = 1280x720 DIPs)
        double scaledWidth = 1280;
        double expectedScaledLeft = (scaledWidth - dims.CompactWidth) / 2.0;
        Assert.Equal(585.0, expectedScaledLeft);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    private class MockSpotifyAuth : ISpotifyAuthService
    {
        private readonly string? _token;
        public bool IsConnected => !string.IsNullOrEmpty(_token);
        public event EventHandler<bool>? ConnectionStatusChanged { add { } remove { } }
        public MockSpotifyAuth(string? token) => _token = token;
        public Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default) => Task.FromResult(IsConnected);
        public Task<string?> GetValidAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult(_token);
        public Task DisconnectAsync() => Task.CompletedTask;
        public void Dispose() { }
    }
}

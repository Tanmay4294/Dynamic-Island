using System.Net;
using System.Net.Http;
using System.Text.Json;
using Spotifly.App.Models;
using Spotifly.App.Services;
using Xunit;

namespace Spotifly.Tests;

public class SpotifyPlaybackServiceTests
{
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

    private static (SpotifyService service, MockTokenStore store) CreateServiceWithHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var config = new SpotifyConfig();
        var tokenStore = new MockTokenStore();
        tokenStore.SaveTokens(new SpotifyTokenData
        {
            AccessToken = "valid_playback_token",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        });

        var httpClient = new HttpClient(new MockHttpMessageHandler(handler));
        var authService = new SpotifyAuthService(config, tokenStore, httpClient);
        var spotifyService = new SpotifyService(authService, config, httpClient);

        return (spotifyService, tokenStore);
    }

    [Fact]
    public async Task RefreshPlaybackAsync_ReturnsSnapshotWithTrackAndArtworkOn200()
    {
        var (service, _) = CreateServiceWithHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Contains("/me/player", req.RequestUri?.ToString());

            var responseObj = new SpotifyPlaybackResponse
            {
                IsPlaying = true,
                ProgressMs = 45000,
                CurrentlyPlayingType = "track",
                Device = new SpotifyDevice
                {
                    Name = "Desktop-PC",
                    Type = "Computer",
                    IsActive = true
                },
                Item = new SpotifyItem
                {
                    Id = "track_123",
                    Uri = "spotify:track:track_123",
                    Name = "Blinding Lights",
                    DurationMs = 200000,
                    Artists = new List<SpotifyArtistRef>
                    {
                        new() { Name = "The Weeknd" }
                    },
                    Album = new SpotifyAlbumRef
                    {
                        Name = "After Hours",
                        Images = new List<SpotifyImage>
                        {
                            new() { Url = "https://i.scdn.co/image/art_large.jpg", Width = 300, Height = 300 }
                        }
                    },
                    ExternalUrls = new Dictionary<string, string>
                    {
                        { "spotify", "https://open.spotify.com/track/track_123" }
                    }
                }
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(responseObj))
            };
        });

        var snapshot = await service.RefreshPlaybackAsync();

        Assert.NotNull(snapshot);
        Assert.True(snapshot.IsPlaying);
        Assert.True(snapshot.HasActiveDevice);
        Assert.Equal("Desktop-PC", snapshot.DeviceName);
        Assert.Equal("Blinding Lights", snapshot.Track.Name);
        Assert.Equal("The Weeknd", snapshot.Track.ArtistName);
        Assert.Equal("After Hours", snapshot.Track.AlbumName);
        Assert.Equal("https://i.scdn.co/image/art_large.jpg", snapshot.Track.ArtworkUrl);
        Assert.True(snapshot.Track.HasArtwork);
        Assert.Equal("https://open.spotify.com/track/track_123", snapshot.Track.SpotifyWebUrl);
    }

    [Fact]
    public async Task RefreshPlaybackAsync_Handles204NoContent_ReturnsIdleSnapshot()
    {
        var (service, _) = CreateServiceWithHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var snapshot = await service.RefreshPlaybackAsync();

        Assert.NotNull(snapshot);
        Assert.False(snapshot.IsPlaying);
        Assert.False(snapshot.HasActiveDevice);
        Assert.Equal(TrackInfo.Empty.Name, snapshot.Track.Name);
        Assert.Contains("idle", snapshot.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefreshPlaybackAsync_MapsPodcastEpisodeGracefully()
    {
        var (service, _) = CreateServiceWithHandler(req =>
        {
            var responseObj = new SpotifyPlaybackResponse
            {
                IsPlaying = true,
                CurrentlyPlayingType = "episode",
                Device = new SpotifyDevice { IsActive = true, Name = "Living Room Speaker" },
                Item = new SpotifyItem
                {
                    Id = "episode_999",
                    Name = "Episode 42: Tech Horizon",
                    DurationMs = 3600000,
                    Show = new SpotifyShowRef
                    {
                        Name = "The Tech Show",
                        Publisher = "Tech Daily"
                    },
                    Images = new List<SpotifyImage>
                    {
                        new() { Url = "https://i.scdn.co/image/podcast_art.jpg" }
                    }
                }
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(responseObj))
            };
        });

        var snapshot = await service.RefreshPlaybackAsync();

        Assert.NotNull(snapshot);
        Assert.True(snapshot.Track.IsEpisode);
        Assert.Equal("Episode 42: Tech Horizon", snapshot.Track.Name);
        Assert.Equal("Tech Daily", snapshot.Track.ArtistName);
        Assert.Equal("The Tech Show", snapshot.Track.AlbumName);
        Assert.Equal("https://i.scdn.co/image/podcast_art.jpg", snapshot.Track.ArtworkUrl);
    }

    [Fact]
    public async Task RefreshPlaybackAsync_HandlesMissingArtworkGracefully()
    {
        var (service, _) = CreateServiceWithHandler(req =>
        {
            var responseObj = new SpotifyPlaybackResponse
            {
                IsPlaying = false,
                Item = new SpotifyItem
                {
                    Id = "local_1",
                    Name = "My Local Demo Track",
                    IsLocal = true
                }
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(responseObj))
            };
        });

        var snapshot = await service.RefreshPlaybackAsync();

        Assert.NotNull(snapshot);
        Assert.True(snapshot.Track.IsLocal);
        Assert.False(snapshot.Track.HasArtwork);
        Assert.Null(snapshot.Track.ArtworkUrl);
    }

    [Fact]
    public async Task PlaybackCommands_ReturnTrueOnlyOn204NoContent()
    {
        int requestCount = 0;
        var (service, _) = CreateServiceWithHandler(req =>
        {
            requestCount++;
            if (req.RequestUri?.ToString().Contains("play") == true ||
                req.RequestUri?.ToString().Contains("pause") == true ||
                req.RequestUri?.ToString().Contains("next") == true ||
                req.RequestUri?.ToString().Contains("previous") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        });

        Assert.True(await service.PlayAsync());
        Assert.True(await service.PauseAsync());
        Assert.True(await service.NextAsync());
        Assert.True(await service.PreviousAsync());
    }

    [Fact]
    public async Task PlaybackCommands_ReturnFalseOn404NoActiveDevice()
    {
        var (service, _) = CreateServiceWithHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        bool playResult = await service.PlayAsync();
        Assert.False(playResult, "Play command must return false when no active player exists");

        bool nextResult = await service.NextAsync();
        Assert.False(nextResult, "Next command must return false when no active player exists");
    }

    [Fact]
    public async Task PlaybackCommands_ReturnFalseWhenUnauthenticated()
    {
        var config = new SpotifyConfig();
        var tokenStore = new MockTokenStore(); // No token saved
        var httpClient = new HttpClient(new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)));
        var authService = new SpotifyAuthService(config, tokenStore, httpClient);
        var service = new SpotifyService(authService, config, httpClient);

        bool result = await service.PlayAsync();
        Assert.False(result);
    }
}

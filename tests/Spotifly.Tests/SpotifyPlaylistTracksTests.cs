using System.Net;
using System.Net.Http;
using System.Text.Json;
using Spotifly.App.Models;
using Spotifly.App.Services;
using Xunit;

namespace Spotifly.Tests;

public class SpotifyPlaylistTracksTests
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
            AccessToken = "valid_playlist_tracks_token",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        });

        var httpClient = new HttpClient(new MockHttpMessageHandler(handler));
        var authService = new SpotifyAuthService(config, tokenStore, httpClient);
        var spotifyService = new SpotifyService(authService, config, httpClient);

        return (spotifyService, tokenStore);
    }

    [Fact]
    public async Task GetPlaylistTracksAsync_HandlesPagination_AggregatesAllTracks()
    {
        int pageCount = 0;
        var (service, _) = CreateServiceWithHandler(req =>
        {
            pageCount++;
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Contains("/playlists/test_pl_1/tracks", req.RequestUri?.ToString());

            if (req.RequestUri?.ToString().Contains("offset=0") == true)
            {
                var page1 = new SpotifyPlaylistTracksResponse
                {
                    Limit = 2,
                    Offset = 0,
                    Total = 3,
                    Next = "https://api.spotify.com/v1/playlists/test_pl_1/tracks?limit=2&offset=2",
                    Items = new List<SpotifyPlaylistTrackItem>
                    {
                        new()
                        {
                            Track = new SpotifyItem
                            {
                                Id = "track_1",
                                Uri = "spotify:track:track_1",
                                Name = "Starboy",
                                Artists = new() { new() { Name = "The Weeknd" }, new() { Name = "Daft Punk" } },
                                Album = new() { Name = "Starboy", Images = new() { new() { Url = "https://i.scdn.co/starboy.jpg" } } },
                                DurationMs = 230000
                            }
                        },
                        new()
                        {
                            Track = new SpotifyItem
                            {
                                Id = "track_2",
                                Uri = "spotify:track:track_2",
                                Name = "Blinding Lights",
                                Artists = new() { new() { Name = "The Weeknd" } },
                                Album = new() { Name = "After Hours", Images = new() { new() { Url = "https://i.scdn.co/afterhours.jpg" } } },
                                DurationMs = 200000
                            }
                        }
                    }
                };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(page1))
                };
            }
            else
            {
                var page2 = new SpotifyPlaylistTracksResponse
                {
                    Limit = 2,
                    Offset = 2,
                    Total = 3,
                    Next = null,
                    Items = new List<SpotifyPlaylistTrackItem>
                    {
                        new()
                        {
                            Track = new SpotifyItem
                            {
                                Id = "track_3",
                                Uri = "spotify:track:track_3",
                                Name = "Save Your Tears",
                                Artists = new() { new() { Name = "The Weeknd" } },
                                Album = new() { Name = "After Hours" },
                                DurationMs = 215000
                            }
                        }
                    }
                };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(page2))
                };
            }
        });

        var tracks = await service.GetPlaylistTracksAsync("test_pl_1", forceRefresh: true);

        Assert.NotNull(tracks);
        Assert.Equal(3, tracks.Count);
        Assert.Equal(2, pageCount);
        Assert.Equal("Starboy", tracks[0].Name);
        Assert.Equal("The Weeknd, Daft Punk", tracks[0].ArtistName);
        Assert.Equal("https://i.scdn.co/starboy.jpg", tracks[0].ArtworkUrl);
        Assert.Equal("Save Your Tears", tracks[2].Name);
        Assert.False(tracks[2].HasArtwork);
    }

    [Fact]
    public async Task GetPlaylistTracksAsync_HandlesNullAndLocalAndEpisodeEntriesGracefully()
    {
        var (service, _) = CreateServiceWithHandler(req =>
        {
            var res = new SpotifyPlaylistTracksResponse
            {
                Items = new List<SpotifyPlaylistTrackItem>
                {
                    new() { Track = null }, // Null entry (e.g. removed or unavailable track)
                    new()
                    {
                        IsLocal = true,
                        Track = new SpotifyItem
                        {
                            Id = "local_track",
                            Uri = "spotify:local:artist:album:title:123",
                            Name = "Custom Local Recording",
                            IsLocal = true
                        }
                    },
                    new()
                    {
                        Track = new SpotifyItem
                        {
                            Id = "episode_track",
                            Uri = "spotify:episode:episode_track",
                            Name = "Tech News Today",
                            Show = new() { Name = "Tech News Daily", Publisher = "Daily News" },
                            Images = new() { new() { Url = "https://i.scdn.co/episode.jpg" } }
                        }
                    }
                }
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(res))
            };
        });

        var tracks = await service.GetPlaylistTracksAsync("mixed_pl", forceRefresh: true);

        Assert.Equal(2, tracks.Count); // Null item skipped without crashing
        Assert.True(tracks[0].IsLocal);
        Assert.Equal("Custom Local Recording", tracks[0].Name);

        Assert.True(tracks[1].IsEpisode);
        Assert.Equal("Tech News Today", tracks[1].Name);
        Assert.Equal("Daily News", tracks[1].ArtistName);
        Assert.Equal("https://i.scdn.co/episode.jpg", tracks[1].ArtworkUrl);
    }

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

    [Fact]
    public async Task PlayTrackAsync_SendsProperBody_ReturnsTrueOn204()
    {
        string? sentBody = null;
        var config = new SpotifyConfig();
        var tokenStore = new MockTokenStore();
        tokenStore.SaveTokens(new SpotifyTokenData
        {
            AccessToken = "valid_token",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        });

        var handler = new MockAsyncHttpMessageHandler(async req =>
        {
            Assert.Equal(HttpMethod.Put, req.Method);
            Assert.Contains("/me/player/play", req.RequestUri?.ToString());
            if (req.Content != null)
            {
                sentBody = await req.Content.ReadAsStringAsync();
            }
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var httpClient = new HttpClient(handler);
        var authService = new SpotifyAuthService(config, tokenStore, httpClient);
        var service = new SpotifyService(authService, config, httpClient);

        bool success = await service.PlayTrackAsync("spotify:track:target_123", "spotify:playlist:parent_pl");

        Assert.True(success);
        Assert.NotNull(sentBody);
        Assert.Contains("spotify:playlist:parent_pl", sentBody);
        Assert.Contains("spotify:track:target_123", sentBody);
    }

    [Fact]
    public async Task PlayTrackAsync_ReturnsFalseOn404NoActiveDevice()
    {
        var (service, _) = CreateServiceWithHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        bool success = await service.PlayTrackAsync("spotify:track:target_123");
        Assert.False(success);
    }

    [Fact]
    public async Task GetPlaylistTracksAsync_ReturnsEmptyWhenUnauthenticated()
    {
        var config = new SpotifyConfig();
        var tokenStore = new MockTokenStore(); // Empty
        var httpClient = new HttpClient(new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)));
        var authService = new SpotifyAuthService(config, tokenStore, httpClient);
        var service = new SpotifyService(authService, config, httpClient);

        var tracks = await service.GetPlaylistTracksAsync("pl_1", forceRefresh: true);
        Assert.Empty(tracks);
    }
}

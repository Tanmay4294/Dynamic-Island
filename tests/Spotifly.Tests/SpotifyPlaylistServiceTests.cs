using System.Net;
using System.Net.Http;
using System.Text.Json;
using Spotifly.App.Models;
using Spotifly.App.Services;
using Xunit;

namespace Spotifly.Tests;

public class SpotifyPlaylistServiceTests
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
            AccessToken = "valid_playlist_token",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        });

        var httpClient = new HttpClient(new MockHttpMessageHandler(handler));
        var authService = new SpotifyAuthService(config, tokenStore, httpClient);
        var spotifyService = new SpotifyService(authService, config, httpClient);

        return (spotifyService, tokenStore);
    }

    [Fact]
    public async Task GetUserPlaylistsAsync_HandlesPagination_AggregatesAllPlaylists()
    {
        int pageCount = 0;
        var (service, _) = CreateServiceWithHandler(req =>
        {
            pageCount++;
            Assert.Equal(HttpMethod.Get, req.Method);

            if (req.RequestUri?.ToString().Contains("offset=0") == true)
            {
                var page1 = new SpotifyPlaylistsResponse
                {
                    Limit = 2,
                    Offset = 0,
                    Total = 3,
                    Next = "https://api.spotify.com/v1/me/playlists?limit=2&offset=2",
                    Items = new List<SpotifyPlaylistItem>
                    {
                        new()
                        {
                            Id = "pl_1",
                            Name = "Morning Vibes",
                            Tracks = new() { Total = 25 },
                            Images = new() { new() { Url = "https://i.scdn.co/image/pl_1.jpg" } }
                        },
                        new()
                        {
                            Id = "pl_2",
                            Name = "Deep Focus",
                            Tracks = new() { Total = 50 },
                            Images = new() { new() { Url = "https://i.scdn.co/image/pl_2.jpg" } }
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
                var page2 = new SpotifyPlaylistsResponse
                {
                    Limit = 2,
                    Offset = 2,
                    Total = 3,
                    Next = null,
                    Items = new List<SpotifyPlaylistItem>
                    {
                        new()
                        {
                            Id = "pl_3",
                            Name = "Late Night Lo-Fi",
                            Tracks = new() { Total = 18 },
                            Images = new() { new() { Url = "https://i.scdn.co/image/pl_3.jpg" } }
                        }
                    }
                };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(page2))
                };
            }
        });

        var playlists = await service.GetUserPlaylistsAsync(forceRefresh: true);

        Assert.NotNull(playlists);
        Assert.Equal(3, playlists.Count);
        Assert.Equal(2, pageCount);
        Assert.Equal("Morning Vibes", playlists[0].Name);
        Assert.Equal("Deep Focus", playlists[1].Name);
        Assert.Equal("Late Night Lo-Fi", playlists[2].Name);
        Assert.True(playlists[0].HasCoverImage);
        Assert.Equal("https://i.scdn.co/image/pl_1.jpg", playlists[0].CoverImageUrl);
    }

    [Fact]
    public async Task GetUserPlaylistsAsync_HandlesMissingArtworkGracefully()
    {
        var (service, _) = CreateServiceWithHandler(req =>
        {
            var res = new SpotifyPlaylistsResponse
            {
                Items = new List<SpotifyPlaylistItem>
                {
                    new()
                    {
                        Id = "pl_no_art",
                        Name = "Empty Art Playlist",
                        Images = new List<SpotifyImage>()
                    }
                }
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(res))
            };
        });

        var playlists = await service.GetUserPlaylistsAsync(forceRefresh: true);

        Assert.Single(playlists);
        Assert.Equal("Empty Art Playlist", playlists[0].Name);
        Assert.False(playlists[0].HasCoverImage);
        Assert.Null(playlists[0].CoverImageUrl);
    }

    [Fact]
    public async Task GetUserPlaylistsAsync_ReturnsCachedResultsWhenNotForceRefreshed()
    {
        int fetchCount = 0;
        var (service, _) = CreateServiceWithHandler(req =>
        {
            fetchCount++;
            var res = new SpotifyPlaylistsResponse
            {
                Items = new List<SpotifyPlaylistItem>
                {
                    new() { Id = "pl_cached", Name = "Cached Playlist" }
                }
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(res))
            };
        });

        var firstCall = await service.GetUserPlaylistsAsync(forceRefresh: false);
        var secondCall = await service.GetUserPlaylistsAsync(forceRefresh: false);

        Assert.Equal(1, fetchCount); // Should only make HTTP call once
        Assert.Same(firstCall, secondCall);
    }

    [Fact]
    public async Task GetUserPlaylistsAsync_HandlesRateLimitGracefully()
    {
        var (service, _) = CreateServiceWithHandler(req =>
        {
            return new HttpResponseMessage((HttpStatusCode)429);
        });

        var playlists = await service.GetUserPlaylistsAsync(forceRefresh: true);
        Assert.NotNull(playlists);
        Assert.Empty(playlists);
    }

    [Fact]
    public async Task GetUserPlaylistsAsync_ReturnsEmptyWhenUnauthenticated()
    {
        var config = new SpotifyConfig();
        var tokenStore = new MockTokenStore(); // Empty token store
        var httpClient = new HttpClient(new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)));
        var authService = new SpotifyAuthService(config, tokenStore, httpClient);
        var service = new SpotifyService(authService, config, httpClient);

        var playlists = await service.GetUserPlaylistsAsync(forceRefresh: true);
        Assert.Empty(playlists);
    }
}

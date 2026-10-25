using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Spotifly.App.Models;

namespace Spotifly.App.Services;

public interface ISpotifyService
{
    PlaybackSnapshot CurrentPlayback { get; }
    event EventHandler<PlaybackSnapshot>? PlaybackChanged;
    Task<PlaybackSnapshot> RefreshPlaybackAsync(CancellationToken cancellationToken = default);
    Task<bool> PlayAsync(CancellationToken cancellationToken = default);
    Task<bool> PauseAsync(CancellationToken cancellationToken = default);
    Task<bool> TogglePlayPauseAsync(CancellationToken cancellationToken = default);
    Task<bool> NextAsync(CancellationToken cancellationToken = default);
    Task<bool> PreviousAsync(CancellationToken cancellationToken = default);
    void OpenSpotifyUriOrWeb(string? uri, string? webUrl);
}

/// <summary>
/// Dedicated Spotify playback management service.
/// Uses authenticated HTTP calls to /v1/me/player endpoints, maps responses to PlaybackSnapshot,
/// handles missing tracks, podcasts, inactive devices, rate limits (HTTP 429), and player command execution.
/// Never reports a command successful unless the API returns 204 No Content.
/// </summary>
public sealed class SpotifyService : ISpotifyService
{
    private readonly ISpotifyAuthService _authService;
    private readonly SpotifyConfig _config;
    private readonly HttpClient _httpClient;
    private readonly ILoggerService? _logger;
    private PlaybackSnapshot _currentPlayback = PlaybackSnapshot.Empty;

    public PlaybackSnapshot CurrentPlayback => _currentPlayback;
    public event EventHandler<PlaybackSnapshot>? PlaybackChanged;

    public SpotifyService(
        ISpotifyAuthService authService,
        SpotifyConfig config,
        HttpClient? httpClient = null,
        ILoggerService? logger = null)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _httpClient = httpClient ?? new HttpClient();
        _logger = logger;
    }

    public async Task<PlaybackSnapshot> RefreshPlaybackAsync(CancellationToken cancellationToken = default)
    {
        string? token = await _authService.GetValidAccessTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(token))
        {
            SetPlayback(PlaybackSnapshot.Empty);
            return _currentPlayback;
        }

        string url = $"{_config.ApiBaseUrl}/me/player?additional_types=track,episode";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NoContent) // 204: Active session not playing / no active player
            {
                var inactive = new PlaybackSnapshot
                {
                    Track = TrackInfo.Empty,
                    IsPlaying = false,
                    HasActiveDevice = false,
                    IsSpotifyRunning = true,
                    StatusMessage = "Spotify is idle (no active device playing)"
                };
                SetPlayback(inactive);
                return inactive;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized) // 401: Token expired
            {
                _logger?.LogWarning("Playback request returned 401. Retrying once after token refresh.");
                token = await _authService.GetValidAccessTokenAsync(cancellationToken);
                if (string.IsNullOrEmpty(token))
                {
                    SetPlayback(PlaybackSnapshot.Empty);
                    return _currentPlayback;
                }

                using var retryReq = new HttpRequestMessage(HttpMethod.Get, url);
                retryReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var retryRes = await _httpClient.SendAsync(retryReq, cancellationToken);
                return await ParsePlaybackResponseAsync(retryRes, cancellationToken);
            }

            return await ParsePlaybackResponseAsync(response, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError("Network failure during playback refresh.", ex);
            var err = new PlaybackSnapshot
            {
                Track = _currentPlayback.Track,
                IsPlaying = false,
                HasActiveDevice = false,
                IsSpotifyRunning = false,
                StatusMessage = "Network connection to Spotify failed."
            };
            SetPlayback(err);
            return err;
        }
    }

    private async Task<PlaybackSnapshot> ParsePlaybackResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == (HttpStatusCode)429)
        {
            _logger?.LogWarning("Rate limit reached on playback endpoint (HTTP 429).");
            return _currentPlayback;
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            _logger?.LogWarning("Forbidden (403): User may lack Premium or playback permissions.");
            var err = new PlaybackSnapshot
            {
                Track = TrackInfo.Empty,
                IsPlaying = false,
                HasActiveDevice = false,
                IsSpotifyRunning = true,
                StatusMessage = "Spotify account not authorized or requires Premium."
            };
            SetPlayback(err);
            return err;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogWarning($"Playback query failed with HTTP {(int)response.StatusCode}.");
            return _currentPlayback;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            var empty = new PlaybackSnapshot
            {
                Track = TrackInfo.Empty,
                IsPlaying = false,
                HasActiveDevice = false,
                IsSpotifyRunning = true,
                StatusMessage = "Spotify is idle"
            };
            SetPlayback(empty);
            return empty;
        }

        var model = JsonSerializer.Deserialize<SpotifyPlaybackResponse>(json);
        if (model == null)
        {
            return _currentPlayback;
        }

        var track = MapItemToTrackInfo(model.Item, model.CurrentlyPlayingType);
        var snapshot = new PlaybackSnapshot
        {
            Track = track,
            IsPlaying = model.IsPlaying,
            ProgressMs = model.ProgressMs,
            DurationMs = model.Item?.DurationMs,
            DeviceName = model.Device?.Name,
            DeviceType = model.Device?.Type,
            HasActiveDevice = model.Device?.IsActive ?? false,
            IsSpotifyRunning = true,
            SnapshotTimeUtc = DateTime.UtcNow,
            StatusMessage = model.IsPlaying ? "Playing" : "Paused"
        };

        SetPlayback(snapshot);
        return snapshot;
    }

    private static TrackInfo MapItemToTrackInfo(SpotifyItem? item, string? itemType)
    {
        if (item == null)
        {
            return TrackInfo.Empty;
        }

        bool isEpisode = string.Equals(itemType, "episode", StringComparison.OrdinalIgnoreCase);

        // Artists string
        string artists;
        if (item.Artists != null && item.Artists.Count > 0)
        {
            artists = string.Join(", ", item.Artists.Select(a => a.Name ?? "Unknown"));
        }
        else if (isEpisode && item.Show != null)
        {
            artists = item.Show.Publisher ?? item.Show.Name ?? "Podcast";
        }
        else
        {
            artists = "Unknown Artist";
        }

        // Album string
        string album = item.Album?.Name ?? item.Show?.Name ?? (isEpisode ? "Podcast Episode" : "Single");

        // Artwork URL (select best resolution image, usually first in array)
        string? artworkUrl = null;
        if (item.Album?.Images != null && item.Album.Images.Count > 0)
        {
            artworkUrl = item.Album.Images[0].Url;
        }
        else if (item.Images != null && item.Images.Count > 0)
        {
            artworkUrl = item.Images[0].Url;
        }
        else if (item.Show?.Images != null && item.Show.Images.Count > 0)
        {
            artworkUrl = item.Show.Images[0].Url;
        }

        // Web URL
        string? webUrl = null;
        if (item.ExternalUrls != null && item.ExternalUrls.TryGetValue("spotify", out var url))
        {
            webUrl = url;
        }

        return new TrackInfo
        {
            Id = item.Id ?? string.Empty,
            Uri = item.Uri ?? string.Empty,
            Name = !string.IsNullOrWhiteSpace(item.Name) ? item.Name : "Untitled Track",
            ArtistName = artists,
            AlbumName = album,
            ArtworkUrl = artworkUrl,
            SpotifyWebUrl = webUrl,
            DurationMs = item.DurationMs,
            IsLocal = item.IsLocal,
            IsEpisode = isEpisode
        };
    }

    public async Task<bool> TogglePlayPauseAsync(CancellationToken cancellationToken = default)
    {
        if (_currentPlayback.IsPlaying)
        {
            return await PauseAsync(cancellationToken);
        }
        else
        {
            return await PlayAsync(cancellationToken);
        }
    }

    public async Task<bool> PlayAsync(CancellationToken cancellationToken = default)
    {
        return await SendPlayerCommandAsync(HttpMethod.Put, "me/player/play", cancellationToken);
    }

    public async Task<bool> PauseAsync(CancellationToken cancellationToken = default)
    {
        return await SendPlayerCommandAsync(HttpMethod.Put, "me/player/pause", cancellationToken);
    }

    public async Task<bool> NextAsync(CancellationToken cancellationToken = default)
    {
        return await SendPlayerCommandAsync(HttpMethod.Post, "me/player/next", cancellationToken);
    }

    public async Task<bool> PreviousAsync(CancellationToken cancellationToken = default)
    {
        return await SendPlayerCommandAsync(HttpMethod.Post, "me/player/previous", cancellationToken);
    }

    private async Task<bool> SendPlayerCommandAsync(HttpMethod method, string endpointPath, CancellationToken cancellationToken)
    {
        string? token = await _authService.GetValidAccessTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(token))
        {
            _logger?.LogWarning($"Cannot execute {method} {endpointPath}: Not authenticated.");
            return false;
        }

        string url = $"{_config.ApiBaseUrl}/{endpointPath}";
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NoContent) // 204: Successful player command
            {
                _logger?.LogInformation($"Player command {method} {endpointPath} succeeded (HTTP 204).");
                // Trigger refresh shortly after to sync snapshot
                _ = Task.Run(async () =>
                {
                    await Task.Delay(400, cancellationToken);
                    await RefreshPlaybackAsync(cancellationToken);
                }, cancellationToken);
                return true;
            }

            if (response.StatusCode == HttpStatusCode.NotFound) // 404: No active device found
            {
                _logger?.LogWarning($"Player command {method} {endpointPath} failed (HTTP 404): No active Spotify player device.");
                return false;
            }

            if (response.StatusCode == HttpStatusCode.Forbidden) // 403: Premium required or restricted
            {
                _logger?.LogWarning($"Player command {method} {endpointPath} failed (HTTP 403): Premium account or player permission required.");
                return false;
            }

            _logger?.LogWarning($"Player command {method} {endpointPath} returned HTTP {(int)response.StatusCode}.");
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Error sending player command {method} {endpointPath}.", ex);
            return false;
        }
    }

    public void OpenSpotifyUriOrWeb(string? uri, string? webUrl)
    {
        try
        {
            if (!string.IsNullOrEmpty(uri))
            {
                // Attempt native spotify protocol uri first (e.g. spotify:track:...)
                Process.Start(new ProcessStartInfo
                {
                    FileName = uri,
                    UseShellExecute = true
                });
                return;
            }

            if (!string.IsNullOrEmpty(webUrl))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = webUrl,
                    UseShellExecute = true
                });
                return;
            }

            // Fallback: launch desktop Spotify or Spotify web
            Process.Start(new ProcessStartInfo
            {
                FileName = "spotify:",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError("Failed to open Spotify URI or web link.", ex);
            if (!string.IsNullOrEmpty(webUrl))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = webUrl,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }
    }

    private void SetPlayback(PlaybackSnapshot snapshot)
    {
        _currentPlayback = snapshot;
        PlaybackChanged?.Invoke(this, snapshot);
    }
}

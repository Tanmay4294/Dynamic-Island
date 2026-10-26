using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Spotifly.App.Models;

namespace Spotifly.App.Services;

public interface ISpotifyService : IDisposable
{
    PlaybackSnapshot CurrentPlayback { get; }
    IReadOnlyList<PlaylistInfo> CachedPlaylists { get; }
    event EventHandler<PlaybackSnapshot>? PlaybackChanged;
    event EventHandler<IReadOnlyList<PlaylistInfo>>? PlaylistsChanged;
    Task<PlaybackSnapshot> RefreshPlaybackAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlaylistInfo>> GetUserPlaylistsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TrackInfo>> GetPlaylistTracksAsync(string playlistId, bool forceRefresh = false, CancellationToken cancellationToken = default);
    Task<bool> PlayTrackAsync(string trackUri, string? contextUri = null, CancellationToken cancellationToken = default);
    Task<PlaybackCommandResult> PlayTrackWithResultAsync(string trackUri, string? contextUri = null, CancellationToken cancellationToken = default);
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
public sealed class SpotifyService : ISpotifyService, IDisposable
{
    private readonly ISpotifyAuthService _authService;
    private readonly SpotifyConfig _config;
    private readonly HttpClient _httpClient;
    private readonly ILoggerService? _logger;
    private PlaybackSnapshot _currentPlayback = PlaybackSnapshot.Empty;
    private IReadOnlyList<PlaylistInfo> _cachedPlaylists = Array.Empty<PlaylistInfo>();
    private DateTime _lastPlaylistFetchUtc = DateTime.MinValue;
    private readonly SemaphoreSlim _playlistLock = new(1, 1);
    private readonly Dictionary<string, (IReadOnlyList<TrackInfo> tracks, DateTime cachedAt)> _playlistTrackCache = new();
    private readonly SemaphoreSlim _trackLock = new(1, 1);
    private readonly SemaphoreSlim _playbackRefreshLock = new(1, 1);
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    public PlaybackSnapshot CurrentPlayback => _currentPlayback;
    public IReadOnlyList<PlaylistInfo> CachedPlaylists => _cachedPlaylists;

    public event EventHandler<PlaybackSnapshot>? PlaybackChanged;
    public event EventHandler<IReadOnlyList<PlaylistInfo>>? PlaylistsChanged;

    public SpotifyService(
        ISpotifyAuthService authService,
        SpotifyConfig config,
        HttpClient? httpClient = null,
        ILoggerService? logger = null)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _ownsHttpClient = httpClient == null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _logger = logger;
    }

    public async Task<PlaybackSnapshot> RefreshPlaybackAsync(CancellationToken cancellationToken = default)
    {
        if (!await _playbackRefreshLock.WaitAsync(0, cancellationToken))
        {
            return _currentPlayback;
        }

        try
        {
            string? token = await _authService.GetValidAccessTokenAsync(cancellationToken);
            if (string.IsNullOrEmpty(token))
            {
                var unavailable = _authService.IsAuthenticating
                    ? new PlaybackSnapshot
                    {
                        Track = TrackInfo.Empty,
                        IsPlaying = false,
                        IsAuthenticated = false,
                        State = SpotifyPlaybackState.Connecting,
                        StatusMessage = "Complete Spotify authorization in your browser.",
                        SnapshotTimeUtc = DateTime.UtcNow
                    }
                    : _authService.IsConnected
                    ? CreateErrorSnapshot("Spotify authorization needs attention. Reconnect Spotify in Settings.")
                    : PlaybackSnapshot.Empty;
                SetPlayback(unavailable);
                _logger?.LogInformation(_authService.IsConnected
                    ? "Playback refresh skipped because valid authorization is temporarily unavailable."
                    : "Playback refresh skipped because Spotify is not connected.");
                return unavailable;
            }

            string url = $"{_config.ApiBaseUrl}/me/player?additional_types=track,episode";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NoContent) // 204: Active session not playing / no active player
            {
                return await ResolveNoPlaybackAsync(token, cancellationToken);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized) // 401: Token expired
            {
                _logger?.LogWarning("Playback request returned 401. Retrying once after token refresh.");
                token = await _authService.RefreshAccessTokenAsync(cancellationToken);
                if (string.IsNullOrEmpty(token))
                {
                    var unauthorized = _authService.IsConnected
                        ? CreateErrorSnapshot("Spotify authorization was rejected. Reconnect Spotify in Settings.")
                        : PlaybackSnapshot.Empty;
                    SetPlayback(unauthorized);
                    return _currentPlayback;
                }

                using var retryReq = new HttpRequestMessage(HttpMethod.Get, url);
                retryReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var retryRes = await _httpClient.SendAsync(retryReq, cancellationToken);
                if (retryRes.StatusCode == HttpStatusCode.NoContent)
                {
                    return await ResolveNoPlaybackAsync(token, cancellationToken);
                }
                return await ParsePlaybackResponseAsync(retryRes, cancellationToken);
            }

            return await ParsePlaybackResponseAsync(response, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError("Network failure during Spotify playback refresh.", ex);
            var error = CreateErrorSnapshot("Could not reach Spotify. Check your network and try again.");
            SetPlayback(error);
            return error;
        }
        catch (Exception)
        {
            _logger?.LogError("Unexpected error during Spotify playback refresh.");
            var error = CreateErrorSnapshot("Spotify playback could not be read. Reconnect in Settings if the problem continues.");
            SetPlayback(error);
            return error;
        }
        finally
        {
            _playbackRefreshLock.Release();
        }
    }

    private async Task<PlaybackSnapshot> ResolveNoPlaybackAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(token))
        {
            var unauthenticated = _authService.IsConnected
                ? CreateErrorSnapshot("Spotify authorization needs attention. Reconnect Spotify in Settings.")
                : PlaybackSnapshot.Empty;
            SetPlayback(unauthenticated);
            return unauthenticated;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_config.ApiBaseUrl}/me/player/devices");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger?.LogWarning("Spotify devices request returned HTTP 401; forcing a token refresh and retrying once.");
            token = await _authService.RefreshAccessTokenAsync(cancellationToken);
            if (string.IsNullOrEmpty(token))
            {
                var unauthorized = _authService.IsConnected
                    ? CreateErrorSnapshot("Spotify authorization was rejected. Reconnect Spotify in Settings.")
                    : PlaybackSnapshot.Empty;
                SetPlayback(unauthorized);
                return unauthorized;
            }

            using var retryRequest = new HttpRequestMessage(HttpMethod.Get, $"{_config.ApiBaseUrl}/me/player/devices");
            retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var retryResponse = await _httpClient.SendAsync(retryRequest, cancellationToken);
            return await ParseDevicesResponseAsync(retryResponse, cancellationToken);
        }
        return await ParseDevicesResponseAsync(response, cancellationToken);
    }

    private async Task<PlaybackSnapshot> ParseDevicesResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            return await ParsePlaybackResponseAsync(response, cancellationToken);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var devices = JsonSerializer.Deserialize<SpotifyDevicesResponse>(json)?.Devices ?? new List<SpotifyDevice>();
        var activeDevice = devices.FirstOrDefault(device => device.IsActive);
        var hasAvailableDevice = devices.Count > 0;
        var state = activeDevice != null ? SpotifyPlaybackState.Idle : SpotifyPlaybackState.NoActiveDevice;
        var message = activeDevice != null
            ? $"Spotify is idle on {activeDevice.Name}. Start playback in Spotify."
            : hasAvailableDevice
                ? "A Spotify device is available, but none is active. Select this PC in Spotify and start playback."
                : "No Spotify playback device is available. Open Spotify on this PC and start playback.";

        var snapshot = new PlaybackSnapshot
        {
            Track = TrackInfo.Empty,
            IsPlaying = false,
            HasActiveDevice = activeDevice != null,
            DeviceName = activeDevice?.Name,
            DeviceType = activeDevice?.Type,
            IsSpotifyRunning = activeDevice != null,
            IsAuthenticated = true,
            State = state,
            StatusMessage = message,
            SnapshotTimeUtc = DateTime.UtcNow
        };
        SetPlayback(snapshot);
        return snapshot;
    }

    private async Task<PlaybackSnapshot> ParsePlaybackResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == (HttpStatusCode)429)
        {
            var retryAfter = GetRetryAfterSeconds(response);
            _logger?.LogWarning($"Playback endpoint rate limited (HTTP 429); retry after {retryAfter} seconds.");
            var limited = CreateErrorSnapshot($"Spotify is rate limiting requests. Retrying in about {retryAfter} seconds.");
            SetPlayback(limited);
            return limited;
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            _logger?.LogWarning("Playback endpoint returned HTTP 403; authorization, account access, or required permissions may be insufficient.");
            var forbidden = CreateErrorSnapshot("Spotify denied playback access. Reconnect Spotify in Settings and check account playback eligibility.");
            SetPlayback(forbidden);
            return forbidden;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogWarning($"Playback query failed with HTTP {(int)response.StatusCode}.");
            var error = CreateErrorSnapshot($"Spotify playback request failed (HTTP {(int)response.StatusCode}). Try again shortly.");
            SetPlayback(error);
            return error;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            var error = CreateErrorSnapshot("Spotify returned an empty playback response. Try again shortly.");
            SetPlayback(error);
            return error;
        }

        var model = JsonSerializer.Deserialize<SpotifyPlaybackResponse>(json);
        if (model == null)
        {
            var error = CreateErrorSnapshot("Spotify returned an unreadable playback response. Try again shortly.");
            SetPlayback(error);
            return error;
        }

        var track = MapItemToTrackInfo(model.Item, model.CurrentlyPlayingType);
        var hasActiveDevice = model.Device?.IsActive ?? false;
        var hasTrack = model.Item != null;
        var state = !hasActiveDevice
            ? SpotifyPlaybackState.NoActiveDevice
            : model.IsPlaying
                ? SpotifyPlaybackState.Playing
                : hasTrack
                    ? SpotifyPlaybackState.Paused
                    : SpotifyPlaybackState.Idle;
        var statusMessage = state switch
        {
            SpotifyPlaybackState.NoActiveDevice => "No active Spotify device. Open Spotify on this PC and start playback.",
            SpotifyPlaybackState.Idle => "Spotify is idle. Start playback on this device.",
            SpotifyPlaybackState.Paused => "Paused",
            _ => model.IsPlaying ? "Playing" : "Paused"
        };
        var snapshot = new PlaybackSnapshot
        {
            Track = track,
            IsPlaying = model.IsPlaying,
            ProgressMs = model.ProgressMs,
            DurationMs = model.Item?.DurationMs,
            DeviceName = model.Device?.Name,
            DeviceType = model.Device?.Type,
            HasActiveDevice = hasActiveDevice,
            IsSpotifyRunning = true,
            IsAuthenticated = true,
            State = state,
            SnapshotTimeUtc = DateTime.UtcNow,
            StatusMessage = statusMessage
        };

        SetPlayback(snapshot);
        return snapshot;
    }

    private PlaybackSnapshot CreateErrorSnapshot(string message) => new()
    {
        Track = TrackInfo.Empty,
        IsPlaying = false,
        HasActiveDevice = false,
        IsSpotifyRunning = false,
        IsAuthenticated = _authService.IsConnected,
        State = SpotifyPlaybackState.Error,
        StatusMessage = message,
        SnapshotTimeUtc = DateTime.UtcNow
    };

    private static int GetRetryAfterSeconds(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delay)
        {
            return Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds));
        }
        if (response.Headers.RetryAfter?.Date is { } date)
        {
            return Math.Max(1, (int)Math.Ceiling((date - DateTimeOffset.UtcNow).TotalSeconds));
        }
        return 5;
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
                SchedulePlaybackRefresh(cancellationToken);
                return true;
            }

            if (response.StatusCode == HttpStatusCode.NotFound) // 404: No active device found
            {
                _logger?.LogWarning($"Player command {method} {endpointPath} failed with HTTP 404; there is no active Spotify device.");
                SetPlayback(new PlaybackSnapshot
                {
                    Track = TrackInfo.Empty,
                    IsAuthenticated = true,
                    State = SpotifyPlaybackState.NoActiveDevice,
                    StatusMessage = "No active Spotify device. Open Spotify on this PC and start playback.",
                    SnapshotTimeUtc = DateTime.UtcNow
                });
                return false;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger?.LogWarning($"Player command {method} {endpointPath} returned HTTP 401; refreshing authorization and retrying once.");
                token = await _authService.RefreshAccessTokenAsync(cancellationToken);
                if (string.IsNullOrEmpty(token))
                {
                    SetPlayback(_authService.IsConnected
                        ? CreateErrorSnapshot("Spotify authorization was rejected. Reconnect Spotify in Settings.")
                        : PlaybackSnapshot.Empty);
                    return false;
                }

                using var retryRequest = new HttpRequestMessage(method, url);
                retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var retryResponse = await _httpClient.SendAsync(retryRequest, cancellationToken);
                if (retryResponse.StatusCode == HttpStatusCode.NoContent)
                {
                    SchedulePlaybackRefresh(cancellationToken);
                    return true;
                }
                _logger?.LogWarning($"Retried player command {method} {endpointPath} returned HTTP {(int)retryResponse.StatusCode}.");
                if (retryResponse.StatusCode == HttpStatusCode.Unauthorized)
                {
                    SetPlayback(CreateErrorSnapshot("Spotify rejected the refreshed authorization. Reconnect Spotify in Settings."));
                }
                else if (retryResponse.StatusCode == HttpStatusCode.Forbidden)
                {
                    SetPlayback(CreateErrorSnapshot("Spotify denied playback control. Check account playback eligibility and reconnect in Settings."));
                }
                else if (retryResponse.StatusCode == (HttpStatusCode)429)
                {
                    SetPlayback(CreateErrorSnapshot("Spotify is rate limiting playback controls. Try again shortly."));
                }
                else if ((int)retryResponse.StatusCode >= 500)
                {
                    SetPlayback(CreateErrorSnapshot("Spotify playback controls are temporarily unavailable. Try again shortly."));
                }
                return false;
            }

            if (response.StatusCode == HttpStatusCode.Forbidden) // 403: Premium required or restricted
            {
                _logger?.LogWarning($"Player command {method} {endpointPath} failed with HTTP 403; account playback access or permissions may be insufficient.");
                SetPlayback(CreateErrorSnapshot("Spotify denied playback control. Check account playback eligibility and reconnect in Settings."));
                return false;
            }

            if (response.StatusCode == (HttpStatusCode)429)
            {
                SetPlayback(CreateErrorSnapshot("Spotify is rate limiting playback controls. Try again shortly."));
                return false;
            }

            _logger?.LogWarning($"Player command {method} {endpointPath} returned HTTP {(int)response.StatusCode}.");
            if ((int)response.StatusCode >= 500)
            {
                SetPlayback(CreateErrorSnapshot("Spotify playback controls are temporarily unavailable. Try again shortly."));
            }
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

    public async Task<IReadOnlyList<PlaylistInfo>> GetUserPlaylistsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        await _playlistLock.WaitAsync(cancellationToken);
        try
        {
            // Return cached playlists if fresh (less than 5 minutes old) and forceRefresh is false
            if (!forceRefresh && _cachedPlaylists.Count > 0 && DateTime.UtcNow - _lastPlaylistFetchUtc < TimeSpan.FromMinutes(5))
            {
                return CachedPlaylists;
            }

            string? token = await _authService.GetValidAccessTokenAsync(cancellationToken);
            if (string.IsNullOrEmpty(token))
            {
                _logger?.LogWarning("Cannot retrieve user playlists: not authenticated.");
                return Array.Empty<PlaylistInfo>();
            }

            var allPlaylists = new List<PlaylistInfo>();
            int limit = 50;
            int offset = 0;
            string? nextUrl = $"{_config.ApiBaseUrl}/me/playlists?limit={limit}&offset={offset}";

            while (!string.IsNullOrEmpty(nextUrl))
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var request = new HttpRequestMessage(HttpMethod.Get, nextUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var response = await _httpClient.SendAsync(request, cancellationToken);

                if (response.StatusCode == (HttpStatusCode)429) // Rate limit
                {
                    _logger?.LogWarning("Spotify rate limit encountered while paging playlists (HTTP 429).");
                    break;
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized) // 401
                {
                    token = await _authService.GetValidAccessTokenAsync(cancellationToken);
                    if (string.IsNullOrEmpty(token)) break;

                    using var retryReq = new HttpRequestMessage(HttpMethod.Get, nextUrl);
                    retryReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    using var retryRes = await _httpClient.SendAsync(retryReq, cancellationToken);
                    if (!retryRes.IsSuccessStatusCode) break;
                    nextUrl = await ProcessPlaylistPageAsync(retryRes, allPlaylists, cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger?.LogWarning($"Playlist request failed with HTTP {(int)response.StatusCode}.");
                    break;
                }

                nextUrl = await ProcessPlaylistPageAsync(response, allPlaylists, cancellationToken);
            }

            _cachedPlaylists = allPlaylists;
            _lastPlaylistFetchUtc = DateTime.UtcNow;
            _logger?.LogInformation($"Retrieved and cached {allPlaylists.Count} playlists from Spotify.");
            PlaylistsChanged?.Invoke(this, CachedPlaylists);
            return CachedPlaylists;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("Playlist retrieval was cancelled.");
            return CachedPlaylists;
        }
        catch (Exception ex)
        {
            _logger?.LogError("Error retrieving Spotify user playlists.", ex);
            return CachedPlaylists;
        }
        finally
        {
            _playlistLock.Release();
        }
    }

    private static async Task<string?> ProcessPlaylistPageAsync(
        HttpResponseMessage response,
        List<PlaylistInfo> accumulator,
        CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var page = JsonSerializer.Deserialize<SpotifyPlaylistsResponse>(json);
        if (page?.Items != null)
        {
            foreach (var item in page.Items)
            {
                if (item == null || string.IsNullOrEmpty(item.Id)) continue;

                string? coverUrl = null;
                if (item.Images != null && item.Images.Count > 0)
                {
                    coverUrl = item.Images[0].Url;
                }

                string? webUrl = null;
                if (item.ExternalUrls != null && item.ExternalUrls.TryGetValue("spotify", out var url))
                {
                    webUrl = url;
                }

                accumulator.Add(new PlaylistInfo
                {
                    Id = item.Id,
                    Uri = item.Uri ?? $"spotify:playlist:{item.Id}",
                    Name = !string.IsNullOrWhiteSpace(item.Name) ? item.Name : "Untitled Playlist",
                    Description = item.Description,
                    CoverImageUrl = coverUrl,
                    SpotifyWebUrl = webUrl,
                    TotalTracks = item.Tracks?.Total ?? 0,
                    OwnerName = item.Owner?.DisplayName,
                    IsCollaborative = item.Collaborative
                });
            }
        }

        return page?.Next;
    }

    public async Task<IReadOnlyList<TrackInfo>> GetPlaylistTracksAsync(string playlistId, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playlistId))
        {
            return Array.Empty<TrackInfo>();
        }

        await _trackLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _playlistTrackCache.TryGetValue(playlistId, out var cached) &&
                DateTime.UtcNow - cached.cachedAt < TimeSpan.FromMinutes(5))
            {
                return cached.tracks;
            }

            string? token = await _authService.GetValidAccessTokenAsync(cancellationToken);
            if (string.IsNullOrEmpty(token))
            {
                _logger?.LogWarning("Cannot retrieve playlist tracks: not authenticated.");
                return Array.Empty<TrackInfo>();
            }

            var allTracks = new List<TrackInfo>();
            int limit = 100;
            int offset = 0;
            // Spotify's current playlist endpoint is /items; /tracks now returns 403
            // for otherwise valid playlist permissions.
            string? nextUrl = $"{_config.ApiBaseUrl}/playlists/{playlistId}/items?limit={limit}&offset={offset}&additional_types=track,episode";

            while (!string.IsNullOrEmpty(nextUrl))
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var request = new HttpRequestMessage(HttpMethod.Get, nextUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var response = await _httpClient.SendAsync(request, cancellationToken);

                if (response.StatusCode == (HttpStatusCode)429) // Rate limit
                {
                    _logger?.LogWarning("Rate limit encountered while retrieving playlist tracks (HTTP 429).");
                    break;
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized) // 401
                {
                    token = await _authService.GetValidAccessTokenAsync(cancellationToken);
                    if (string.IsNullOrEmpty(token)) break;

                    using var retryReq = new HttpRequestMessage(HttpMethod.Get, nextUrl);
                    retryReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    using var retryRes = await _httpClient.SendAsync(retryReq, cancellationToken);
                    if (!retryRes.IsSuccessStatusCode) break;
                    nextUrl = await ProcessPlaylistTrackPageAsync(retryRes, allTracks, cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger?.LogWarning($"Playlist tracks request failed with HTTP {(int)response.StatusCode}.");
                    break;
                }

                nextUrl = await ProcessPlaylistTrackPageAsync(response, allTracks, cancellationToken);
            }

            var result = allTracks.AsReadOnly();
            _playlistTrackCache[playlistId] = (result, DateTime.UtcNow);
            _logger?.LogInformation($"Retrieved and cached {result.Count} Spotify playlist tracks.");
            return result;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("Playlist track retrieval was cancelled.");
            return Array.Empty<TrackInfo>();
        }
        catch (Exception)
        {
            _logger?.LogError("Error retrieving Spotify playlist tracks.");
            return Array.Empty<TrackInfo>();
        }
        finally
        {
            _trackLock.Release();
        }
    }

    private static async Task<string?> ProcessPlaylistTrackPageAsync(
        HttpResponseMessage response,
        List<TrackInfo> accumulator,
        CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var page = JsonSerializer.Deserialize<SpotifyPlaylistTracksResponse>(json);
        if (page?.Items != null)
        {
            foreach (var item in page.Items)
            {
                // Null entries, local files, or unsupported items handled gracefully
                if (item?.Track == null) continue;

                var trackInfo = MapItemToTrackInfo(item.Track, item.Track.Uri?.StartsWith("spotify:episode") == true ? "episode" : "track");
                accumulator.Add(trackInfo);
            }
        }

        return page?.Next;
    }

    public async Task<bool> PlayTrackAsync(string trackUri, string? contextUri = null, CancellationToken cancellationToken = default)
    {
        var result = await PlayTrackWithResultAsync(trackUri, contextUri, cancellationToken);
        return result.IsSuccess;
    }

    public async Task<PlaybackCommandResult> PlayTrackWithResultAsync(string trackUri, string? contextUri = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackUri))
        {
            return PlaybackCommandResult.GenericFailure("No track specified.");
        }

        string? token = await _authService.GetValidAccessTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(token))
        {
            _logger?.LogWarning("Cannot play track: Not authenticated.");
            return PlaybackCommandResult.Unauthorized();
        }

        string url = $"{_config.ApiBaseUrl}/me/player/play";
        object body;
        if (!string.IsNullOrEmpty(contextUri))
        {
            body = new
            {
                context_uri = contextUri,
                offset = new { uri = trackUri },
                position_ms = 0
            };
        }
        else
        {
            body = new
            {
                uris = new[] { trackUri },
                position_ms = 0
            };
        }

        string jsonBody = JsonSerializer.Serialize(body);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            // Handle 401: Token refresh once
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger?.LogWarning("PlayTrack returned 401. Refreshing token and retrying once.");
                token = await _authService.RefreshAccessTokenAsync(cancellationToken);
                if (string.IsNullOrEmpty(token))
                {
                    return PlaybackCommandResult.Unauthorized();
                }

                using var retryReq = new HttpRequestMessage(HttpMethod.Put, url);
                retryReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                retryReq.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");

                using var retryRes = await _httpClient.SendAsync(retryReq, cancellationToken);
                return await EvaluatePlaybackResponseAsync(retryRes, trackUri, cancellationToken);
            }

            return await EvaluatePlaybackResponseAsync(response, trackUri, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("PlayTrack request was cancelled.");
            return PlaybackCommandResult.GenericFailure("Request cancelled.");
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError("Network error during Spotify play request.");
            return PlaybackCommandResult.Network(ex.Message);
        }
        catch (Exception ex)
        {
            _logger?.LogError("Unexpected error executing Spotify play request.");
            return PlaybackCommandResult.GenericFailure(ex.Message);
        }
    }

    private async Task<PlaybackCommandResult> EvaluatePlaybackResponseAsync(HttpResponseMessage response, string trackUri, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NoContent) // 204
        {
            _logger?.LogInformation("Spotify accepted the playback start request (HTTP 204).");
            SchedulePlaybackRefresh(cancellationToken);
            return PlaybackCommandResult.SuccessResult();
        }

        if (response.StatusCode == HttpStatusCode.NotFound) // 404
        {
            _logger?.LogWarning("PlayTrack failed with HTTP 404; there is no active Spotify device.");
            SetPlayback(new PlaybackSnapshot
            {
                Track = TrackInfo.Empty,
                IsAuthenticated = true,
                State = SpotifyPlaybackState.NoActiveDevice,
                StatusMessage = "No active Spotify device. Open Spotify on this PC and start playback.",
                SnapshotTimeUtc = DateTime.UtcNow
            });
            return PlaybackCommandResult.NoDevice();
        }

        if (response.StatusCode == HttpStatusCode.Forbidden) // 403
        {
            _logger?.LogWarning("PlayTrack failed with HTTP 403; account playback access or permissions may be insufficient.");
            SetPlayback(CreateErrorSnapshot("Spotify denied playback control. Check account playback eligibility and reconnect in Settings."));
            return PlaybackCommandResult.Restricted();
        }

        if (response.StatusCode == (HttpStatusCode)429) // 429
        {
            int retrySec = GetRetryAfterSeconds(response);
            if (response.Headers.TryGetValues("Retry-After", out var vals) && int.TryParse(vals.FirstOrDefault(), out var headerRetry))
            {
                retrySec = Math.Max(1, headerRetry);
            }
            _logger?.LogWarning($"PlayTrack failed with HTTP 429; retry after {retrySec} seconds.");
            SetPlayback(CreateErrorSnapshot($"Spotify is rate limiting playback requests. Retry after {retrySec} seconds."));
            return PlaybackCommandResult.RateLimited(retrySec);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            SetPlayback(_authService.IsConnected
                ? CreateErrorSnapshot("Spotify authorization was rejected. Reconnect Spotify in Settings.")
                : PlaybackSnapshot.Empty);
            return PlaybackCommandResult.Unauthorized();
        }

        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger?.LogWarning($"PlayTrack returned HTTP {(int)response.StatusCode}.");

        if (errorBody.Contains("NO_ACTIVE_DEVICE", StringComparison.OrdinalIgnoreCase))
        {
            return PlaybackCommandResult.NoDevice();
        }

        if (errorBody.Contains("PREMIUM_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            return PlaybackCommandResult.Restricted();
        }

        if (errorBody.Contains("UNAVAILABLE", StringComparison.OrdinalIgnoreCase))
        {
            return PlaybackCommandResult.Unavailable();
        }

        return PlaybackCommandResult.GenericFailure($"Spotify returned HTTP {(int)response.StatusCode}.");
    }

    private void SetPlayback(PlaybackSnapshot snapshot)
    {
        if (_currentPlayback.State != snapshot.State ||
            _currentPlayback.IsAuthenticated != snapshot.IsAuthenticated ||
            _currentPlayback.HasActiveDevice != snapshot.HasActiveDevice)
        {
            _logger?.LogInformation($"Spotify playback state changed to {snapshot.State}; authenticated={snapshot.IsAuthenticated}; activeDevice={snapshot.HasActiveDevice}.");
        }
        _currentPlayback = snapshot;
        PlaybackChanged?.Invoke(this, snapshot);
    }

    private void SchedulePlaybackRefresh(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400, cancellationToken);
                await RefreshPlaybackAsync(cancellationToken);
            }
            catch (OperationCanceledException) { }
        }, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _playlistLock.Dispose();
        _trackLock.Dispose();

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}

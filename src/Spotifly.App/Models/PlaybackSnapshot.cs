namespace Spotifly.App.Models;

public enum SpotifyPlaybackState
{
    Disconnected,
    Connecting,
    Idle,
    Playing,
    Paused,
    NoActiveDevice,
    Error
}

/// <summary>
/// Snapshot of Spotify's playback state at a specific point in time.
/// Includes active track, playing/paused state, active device information,
/// and connection/playback status messages.
/// </summary>
public sealed class PlaybackSnapshot
{
    public TrackInfo Track { get; init; } = TrackInfo.Empty;
    public bool IsPlaying { get; init; }
    public long? ProgressMs { get; init; }
    public long? DurationMs { get; init; }
    public string? DeviceName { get; init; }
    public string? DeviceType { get; init; }
    public bool HasActiveDevice { get; init; }
    public bool IsSpotifyRunning { get; init; } = true;
    public bool IsAuthenticated { get; init; }
    public SpotifyPlaybackState State { get; init; } = SpotifyPlaybackState.Disconnected;
    public DateTime SnapshotTimeUtc { get; init; } = DateTime.UtcNow;
    public string? StatusMessage { get; init; }

    public static PlaybackSnapshot Empty { get; } = new PlaybackSnapshot
    {
        Track = TrackInfo.Empty,
        IsPlaying = false,
        ProgressMs = null,
        DurationMs = null,
        DeviceName = null,
        DeviceType = null,
        HasActiveDevice = false,
        IsSpotifyRunning = false,
        IsAuthenticated = false,
        State = SpotifyPlaybackState.Disconnected,
        SnapshotTimeUtc = DateTime.UtcNow,
        StatusMessage = "Connect Spotify in Settings to see playback."
    };
}

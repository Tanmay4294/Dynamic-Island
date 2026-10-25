namespace Spotifly.App.Models;

public enum PlaybackResultStatus
{
    Success,
    NoActiveDevice,
    RestrictedOrNoPremium,
    Unauthorized,
    RateLimited,
    UnavailableTrack,
    NetworkError,
    Failed
}

public sealed class PlaybackCommandResult
{
    public PlaybackResultStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public int? RetryAfterSeconds { get; init; }

    public bool IsSuccess => Status == PlaybackResultStatus.Success;

    public static PlaybackCommandResult SuccessResult() => new()
    {
        Status = PlaybackResultStatus.Success,
        Message = "Playback started."
    };

    public static PlaybackCommandResult NoDevice() => new()
    {
        Status = PlaybackResultStatus.NoActiveDevice,
        Message = "No active device. Open Spotify on desktop, web, or mobile and try again."
    };

    public static PlaybackCommandResult Restricted() => new()
    {
        Status = PlaybackResultStatus.RestrictedOrNoPremium,
        Message = "Playback restricted. Spotify Premium is required for remote playback controls."
    };

    public static PlaybackCommandResult Unauthorized() => new()
    {
        Status = PlaybackResultStatus.Unauthorized,
        Message = "Spotify authorization expired. Please reconnect in Settings."
    };

    public static PlaybackCommandResult RateLimited(int retryAfterSeconds) => new()
    {
        Status = PlaybackResultStatus.RateLimited,
        Message = $"Rate limit reached. Please wait {retryAfterSeconds}s before retrying.",
        RetryAfterSeconds = retryAfterSeconds
    };

    public static PlaybackCommandResult Unavailable() => new()
    {
        Status = PlaybackResultStatus.UnavailableTrack,
        Message = "This track is unavailable or restricted in your region."
    };

    public static PlaybackCommandResult Network(string error) => new()
    {
        Status = PlaybackResultStatus.NetworkError,
        Message = $"Network error: {error}"
    };

    public static PlaybackCommandResult GenericFailure(string reason) => new()
    {
        Status = PlaybackResultStatus.Failed,
        Message = reason
    };
}

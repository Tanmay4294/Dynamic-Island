namespace Spotifly.App.Services;

/// <summary>
/// Configuration for Spotify Web API integration.
/// Contains public Client ID and registered loopback redirect URI.
/// Never contains or requires a Client Secret.
/// </summary>
public sealed class SpotifyConfig
{
    /// <summary>
    /// Spotify Developer App Client ID for Spotifly Windows Dynamic Island.
    /// </summary>
    public string ClientId { get; set; } = "d5cfdbedd55142e3ad34e0cea43492a6";

    /// <summary>
    /// Loopback port for PKCE Authorization Code callback.
    /// </summary>
    public int LoopbackPort { get; set; } = 43821;

    /// <summary>
    /// Registered loopback redirect URI conforming to official Spotify guidelines.
    /// (Spotify requires explicit 127.0.0.1 loopback; localhost is disallowed).
    /// </summary>
    public string RedirectUri => $"http://127.0.0.1:{LoopbackPort}/callback";

    /// <summary>
    /// Scopes needed for Spotifly playback reading and player controls:
    /// - user-read-private, user-read-email (User Profile & Account status)
    /// - user-read-playback-state (Currently playing track, album art, progress, device)
    /// - user-modify-playback-state (Play, Pause, Next, Previous)
    /// </summary>
    public string Scopes => "user-read-private user-read-email user-read-playback-state user-modify-playback-state";

    /// <summary>
    /// Accounts service authorize URL.
    /// </summary>
    public string AuthorizeEndpoint => "https://accounts.spotify.com/authorize";

    /// <summary>
    /// Accounts service token exchange URL.
    /// </summary>
    public string TokenEndpoint => "https://accounts.spotify.com/api/token";

    /// <summary>
    /// Web API base URL.
    /// </summary>
    public string ApiBaseUrl => "https://api.spotify.com/v1";
}

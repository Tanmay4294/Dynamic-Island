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
    /// Only minimal scopes needed for Phase 6 connectivity test (GET /v1/me).
    /// Playback and playlist scopes are deliberately deferred until later phases.
    /// </summary>
    public string Scopes => "user-read-private user-read-email";

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

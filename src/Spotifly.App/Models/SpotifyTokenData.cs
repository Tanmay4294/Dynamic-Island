using System.Text.Json.Serialization;

namespace Spotifly.App.Models;

/// <summary>
/// Model representing OAuth tokens obtained from Spotify.
/// Stored strictly via Windows DPAPI, never in plain-text settings.
/// </summary>
public sealed class SpotifyTokenData
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "Bearer";

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expires_at_utc")]
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// Checks whether the access token has expired or is about to expire within 60 seconds.
    /// </summary>
    public bool IsExpired(int bufferSeconds = 60)
    {
        return DateTime.UtcNow.AddSeconds(bufferSeconds) >= ExpiresAtUtc;
    }
}

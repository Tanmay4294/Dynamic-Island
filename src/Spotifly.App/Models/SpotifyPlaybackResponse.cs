using System.Text.Json.Serialization;

namespace Spotifly.App.Models;

public sealed class SpotifyPlaybackResponse
{
    [JsonPropertyName("device")]
    public SpotifyDevice? Device { get; set; }

    [JsonPropertyName("is_playing")]
    public bool IsPlaying { get; set; }

    [JsonPropertyName("progress_ms")]
    public long? ProgressMs { get; set; }

    [JsonPropertyName("currently_playing_type")]
    public string? CurrentlyPlayingType { get; set; }

    [JsonPropertyName("item")]
    public SpotifyItem? Item { get; set; }
}

public sealed class SpotifyDevicesResponse
{
    [JsonPropertyName("devices")]
    public List<SpotifyDevice>? Devices { get; set; }
}

public sealed class SpotifyDevice
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("volume_percent")]
    public int? VolumePercent { get; set; }
}

public sealed class SpotifyItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("uri")]
    public string? Uri { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; set; }

    [JsonPropertyName("is_local")]
    public bool IsLocal { get; set; }

    [JsonPropertyName("artists")]
    public List<SpotifyArtistRef>? Artists { get; set; }

    [JsonPropertyName("album")]
    public SpotifyAlbumRef? Album { get; set; }

    [JsonPropertyName("external_urls")]
    public Dictionary<string, string>? ExternalUrls { get; set; }

    // For episodes / podcasts
    [JsonPropertyName("show")]
    public SpotifyShowRef? Show { get; set; }

    [JsonPropertyName("images")]
    public List<SpotifyImage>? Images { get; set; }
}

public sealed class SpotifyArtistRef
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class SpotifyAlbumRef
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("images")]
    public List<SpotifyImage>? Images { get; set; }
}

public sealed class SpotifyShowRef
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("publisher")]
    public string? Publisher { get; set; }

    [JsonPropertyName("images")]
    public List<SpotifyImage>? Images { get; set; }
}

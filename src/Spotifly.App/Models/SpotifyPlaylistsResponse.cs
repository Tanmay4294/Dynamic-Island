using System.Text.Json.Serialization;

namespace Spotifly.App.Models;

public sealed class SpotifyPlaylistsResponse
{
    [JsonPropertyName("href")]
    public string? Href { get; set; }

    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("next")]
    public string? Next { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("previous")]
    public string? Previous { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("items")]
    public List<SpotifyPlaylistItem>? Items { get; set; }
}

public sealed class SpotifyPlaylistItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("uri")]
    public string? Uri { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("collaborative")]
    public bool Collaborative { get; set; }

    [JsonPropertyName("images")]
    public List<SpotifyImage>? Images { get; set; }

    [JsonPropertyName("owner")]
    public SpotifyPlaylistOwner? Owner { get; set; }

    [JsonPropertyName("tracks")]
    public SpotifyPlaylistTracksRef? Tracks { get; set; }

    [JsonPropertyName("external_urls")]
    public Dictionary<string, string>? ExternalUrls { get; set; }
}

public sealed class SpotifyPlaylistOwner
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }
}

public sealed class SpotifyPlaylistTracksRef
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("href")]
    public string? Href { get; set; }
}

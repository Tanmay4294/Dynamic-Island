using System.Text.Json.Serialization;

namespace Spotifly.App.Models;

public sealed class SpotifyPlaylistTracksResponse
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
    public List<SpotifyPlaylistTrackItem>? Items { get; set; }
}

public sealed class SpotifyPlaylistTrackItem
{
    [JsonPropertyName("added_at")]
    public string? AddedAt { get; set; }

    [JsonPropertyName("is_local")]
    public bool IsLocal { get; set; }

    [JsonPropertyName("track")]
    public SpotifyItem? Track { get; set; }
}

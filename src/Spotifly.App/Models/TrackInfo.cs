namespace Spotifly.App.Models;

/// <summary>
/// Immutable or snapshot representation of an individual track, episode, or playable media item.
/// Handles tracks, podcasts/episodes, local files, and missing artwork gracefully.
/// </summary>
public sealed class TrackInfo
{
    public string Id { get; init; } = string.Empty;
    public string Uri { get; init; } = string.Empty;
    public string Name { get; init; } = "No Track Playing";
    public string ArtistName { get; init; } = "Unknown Artist";
    public string AlbumName { get; init; } = "Unknown Album";
    public string? ArtworkUrl { get; init; }
    public string? SpotifyWebUrl { get; init; }
    public long DurationMs { get; init; }
    public bool IsLocal { get; init; }
    public bool IsEpisode { get; init; }
    public bool HasArtwork => !string.IsNullOrEmpty(ArtworkUrl);

    public static TrackInfo Empty { get; } = new TrackInfo
    {
        Id = string.Empty,
        Uri = string.Empty,
        Name = "No Track Playing",
        ArtistName = "Spotify is idle",
        AlbumName = string.Empty,
        ArtworkUrl = null,
        SpotifyWebUrl = null,
        DurationMs = 0,
        IsLocal = false,
        IsEpisode = false
    };
}

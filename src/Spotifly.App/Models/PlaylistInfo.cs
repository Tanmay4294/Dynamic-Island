namespace Spotifly.App.Models;

/// <summary>
/// Model representing a user playlist retrieved from Spotify Web API.
/// </summary>
public sealed class PlaylistInfo
{
    public string Id { get; init; } = string.Empty;
    public string Uri { get; init; } = string.Empty;
    public string Name { get; init; } = "Untitled Playlist";
    public string? Description { get; init; }
    public string? CoverImageUrl { get; init; }
    public string? SpotifyWebUrl { get; init; }
    public int TotalTracks { get; init; }
    public string? OwnerName { get; init; }
    public bool IsCollaborative { get; init; }
    public bool HasCoverImage => !string.IsNullOrEmpty(CoverImageUrl);
}

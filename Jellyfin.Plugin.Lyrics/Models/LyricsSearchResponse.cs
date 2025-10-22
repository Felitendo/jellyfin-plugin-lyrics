using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Lyrics.Models;

/// <summary>
/// Response model.
/// </summary>
public class LyricsSearchResponse
{
    /// <summary>
    /// Gets or sets the lrclib id.
    /// </summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the track name.
    /// </summary>
    [JsonPropertyName("trackName")]
    public string? TrackName { get; set; }

    /// <summary>
    /// Gets or sets the artist name.
    /// </summary>
    [JsonPropertyName("artistName")]
    public string? ArtistName { get; set; }

    /// <summary>
    /// Gets or sets the album name.
    /// </summary>
    [JsonPropertyName("albumName")]
    public string? AlbumName { get; set; }

    /// <summary>
    /// Gets or sets the duration.
    /// </summary>
    [JsonPropertyName("duration")]
    public double? Duration { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is an instrumental.
    /// </summary>
    [JsonPropertyName("instrumental")]
    public bool? Instrumental { get; set; }

    /// <summary>
    /// Gets or sets the plain lyrics.
    /// </summary>
    [JsonPropertyName("plainLyrics")]
    public string? PlainLyrics { get; set; }

    /// <summary>
    /// Gets or sets the synced lyrics.
    /// </summary>
    [JsonPropertyName("syncedLyrics")]
    public string? SyncedLyrics { get; set; }
}

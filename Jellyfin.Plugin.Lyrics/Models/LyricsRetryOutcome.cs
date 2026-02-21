namespace Jellyfin.Plugin.Lyrics.Models;

/// <summary>
/// Track processing outcomes used for retry state.
/// </summary>
public enum LyricsRetryOutcome
{
    /// <summary>
    /// No outcome.
    /// </summary>
    None,

    /// <summary>
    /// No lyric result found.
    /// </summary>
    NoResult,

    /// <summary>
    /// Missing lyrics were downloaded.
    /// </summary>
    Downloaded,

    /// <summary>
    /// Existing plain lyrics were upgraded to synced.
    /// </summary>
    Upgraded,

    /// <summary>
    /// Processing failed due to an error.
    /// </summary>
    Error
}

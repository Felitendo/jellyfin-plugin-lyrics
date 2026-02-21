using System;

namespace Jellyfin.Plugin.Lyrics.Models;

/// <summary>
/// Retry state for one track.
/// </summary>
public class LyricsRetryEntry
{
    /// <summary>
    /// Gets or sets the signature that represents the track metadata used for search.
    /// </summary>
    public string TrackSignature { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of consecutive no-result outcomes.
    /// </summary>
    public int ConsecutiveNoResultCount { get; set; }

    /// <summary>
    /// Gets or sets the next retry time in UTC.
    /// </summary>
    public DateTime NextRetryUtc { get; set; }

    /// <summary>
    /// Gets or sets the last attempt timestamp in UTC.
    /// </summary>
    public DateTime LastAttemptUtc { get; set; }

    /// <summary>
    /// Gets or sets the last observed outcome.
    /// </summary>
    public LyricsRetryOutcome LastOutcome { get; set; }
}

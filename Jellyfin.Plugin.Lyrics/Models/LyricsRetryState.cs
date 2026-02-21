using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Lyrics.Models;

/// <summary>
/// Persisted retry and cursor state for lyric task runs.
/// </summary>
public class LyricsRetryState
{
    /// <summary>
    /// Gets or sets the next start cursor in the library query.
    /// </summary>
    public int Cursor { get; set; }

    /// <summary>
    /// Gets retry entries by track id.
    /// </summary>
    public Dictionary<string, LyricsRetryEntry> Entries { get; } = new(StringComparer.Ordinal);
}

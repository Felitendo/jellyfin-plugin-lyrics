using System.Diagnostics.CodeAnalysis;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Lyrics.Configuration;

/// <summary>
/// Configuration for lyrics plugin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether to use strict search.
    /// </summary>
    public bool UseStrictSearch { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to exclude artist name.
    /// </summary>
    public bool ExcludeArtistName { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether to exclude album name.
    /// </summary>
    public bool ExcludeAlbumName { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether to enable adaptive retry backoff.
    /// </summary>
    public bool EnableAdaptiveRetryBackoff { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to enforce a per-run processing cap.
    /// </summary>
    public bool EnableRunCap { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of tracks to process per run.
    /// </summary>
    public int MaxTracksPerRun { get; set; } = 2000;

    /// <summary>
    /// Gets or sets the state entry retention in days.
    /// </summary>
    public int FailureStateTtlDays { get; set; } = 90;

    /// <summary>
    /// Gets or sets the adaptive backoff schedule in days.
    /// </summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Plugin configuration is persisted and exchanged via simple array values.")]
    public int[] BackoffScheduleDays { get; set; } = [1, 3, 7, 30];

    /// <summary>
    /// Gets or sets the legacy state cursor value.
    /// </summary>
    public int StateCursor { get; set; }
}

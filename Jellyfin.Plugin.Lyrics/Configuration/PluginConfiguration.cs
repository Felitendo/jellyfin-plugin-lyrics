using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Lyrics.Configuration
{
    /// <summary>
    /// Configuration for lyrics plugin.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// Gets or sets a value indicating whether to use strict search.
        /// </summary>
        public bool UseStrictSearch { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether to exclude artist name.
        /// </summary>
        public bool ExcludeArtistName { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether to exclude album name.
        /// </summary>
        public bool ExcludeAlbumName { get; set; } = true;
    }
}
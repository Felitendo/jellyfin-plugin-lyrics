using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Lyrics.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Lyrics;

/// <summary>
/// Lyrics plugin.
/// </summary>
public class LyricsPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LyricsPlugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/>.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/>.</param>
    public LyricsPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Lyrics";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("f4a1b2c3-d4e5-6f7a-8b9c-0d1e2f3a4b5c");

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static LyricsPlugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.config.html"
        };
    }
}

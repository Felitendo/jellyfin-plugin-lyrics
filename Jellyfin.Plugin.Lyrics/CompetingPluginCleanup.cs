using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.Lyrics;

/// <summary>
/// Detects a side-by-side install of the official jellyfin-plugin-lrclib and marks it
/// for deletion via Jellyfin's own plugin manifest (<c>meta.json</c>).
/// Effect is realized on the next Jellyfin restart, when <c>PluginManager</c>
/// removes any plugin whose <c>Status</c> is <c>"Deleted"</c>.
/// </summary>
internal static class CompetingPluginCleanup
{
    private static readonly Guid LrcLibPluginId =
        Guid.Parse("D106EBE6-9CA8-4FBC-9CD1-A92A213DA9F9");

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    public static void Run(IApplicationPaths applicationPaths)
    {
        ArgumentNullException.ThrowIfNull(applicationPaths);

        try
        {
            var pluginsRoot = applicationPaths.PluginsPath;
            if (string.IsNullOrEmpty(pluginsRoot) || !Directory.Exists(pluginsRoot))
            {
                return;
            }

            foreach (var dir in Directory.EnumerateDirectories(pluginsRoot))
            {
                TryMarkDeleted(dir);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[Jellyfin.Plugin.Lyrics] Failed to scan for competing LrcLib plugin: {ex.Message}"));
        }
    }

    private static void TryMarkDeleted(string pluginDir)
    {
        try
        {
            var metaPath = Path.Combine(pluginDir, "meta.json");
            if (!File.Exists(metaPath))
            {
                return;
            }

            var json = File.ReadAllText(metaPath);
            if (JsonNode.Parse(json) is not JsonObject node)
            {
                return;
            }

            var idString = node["Id"]?.GetValue<string>();
            if (!Guid.TryParse(idString, out var id) || id != LrcLibPluginId)
            {
                return;
            }

            var currentStatus = node["Status"]?.GetValue<string>();
            if (string.Equals(currentStatus, "Deleted", StringComparison.Ordinal))
            {
                return;
            }

            node["Status"] = "Deleted";

            var tmp = metaPath + ".tmp";
            File.WriteAllText(tmp, node.ToJsonString(WriteOptions));
            File.Move(tmp, metaPath, overwrite: true);

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[Jellyfin.Plugin.Lyrics] Marked competing LrcLib plugin at '{pluginDir}' for removal. Restart Jellyfin to complete uninstall."));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[Jellyfin.Plugin.Lyrics] Could not process plugin manifest in '{pluginDir}': {ex.Message}"));
        }
    }
}

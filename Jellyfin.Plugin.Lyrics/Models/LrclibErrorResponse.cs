using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Lyrics.Models;

/// <summary>
/// Error body LRCLIB sends with a 429 response.
/// </summary>
public class LrclibErrorResponse
{
    /// <summary>
    /// Gets or sets the limit that was hit, "ip" or "application".
    /// </summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }
}

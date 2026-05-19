using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Jellyfin.Plugin.Lyrics.Models;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Lyrics;
using MediaBrowser.Model.Lyrics;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Lyrics;

/// <summary>
/// Lyric provider for Lyrics.
/// </summary>
public class LyricsProvider : ILyricProvider
{
    private const string DefaultBaseUrl = "https://lrclib.net";
    private const string SyncedSuffix = "synced";
    private const string PlainSuffix = "plain";
    private const string SyncedFormat = "lrc";
    private const string PlainFormat = "txt";

    private static readonly string[] ParenthesizedNoise =
    [
        "(Official Music Video)",
        "(Official Video)",
        "(Official Audio)",
        "(Official Lyric Video)",
        "(Lyric Video)",
        "(Music Video)",
        "(Visualizer)",
        "(Visualiser)",
        "(Audio)",
        "(Lyrics)",
        "(MV)",
        "(HD)",
        "(HQ)",
        "[Official Music Video]",
        "[Official Video]",
        "[Official Audio]",
        "[Official Lyric Video]",
        "[Lyric Video]",
        "[Music Video]",
        "[Visualizer]",
        "[Visualiser]",
        "[Audio]",
        "[Lyrics]",
        "[MV]",
        "[HD]",
        "[HQ]"
    ];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LyricsProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LyricsProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{LyricsProvider}"/>.</param>
    public LyricsProvider(IHttpClientFactory httpClientFactory, ILogger<LyricsProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private static string BaseUrl
    {
        get
        {
            var configured = LyricsPlugin.Instance?.Configuration.LrclibBaseUrl;
            if (string.IsNullOrWhiteSpace(configured))
            {
                return DefaultBaseUrl;
            }

            var trimmed = configured.Trim().TrimEnd('/');
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return trimmed;
            }

            return DefaultBaseUrl;
        }
    }

    private static bool UseStrictSearch => LyricsPlugin.Instance?.Configuration.UseStrictSearch ?? true;

    private static bool ExcludeArtistName => LyricsPlugin.Instance?.Configuration.ExcludeArtistName ?? false;

    private static bool ExcludeAlbumName => LyricsPlugin.Instance?.Configuration.ExcludeAlbumName ?? false;

    private static bool EnableDurationFilter => LyricsPlugin.Instance?.Configuration.EnableDurationFilter ?? true;

    private static int DurationToleranceSeconds
    {
        get
        {
            var configured = LyricsPlugin.Instance?.Configuration.DurationToleranceSeconds ?? 15;
            return configured > 0 ? configured : 15;
        }
    }

    /// <inheritdoc />
    public string Name => LyricsPlugin.Instance!.Name;

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteLyricInfo>> SearchAsync(
        LyricSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return UseStrictSearch
                ? await GetExactMatch(request, cancellationToken).ConfigureAwait(false)
                : await GetFuzzyMatch(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            var artist = request.ArtistNames is { Count: > 0 } ? request.ArtistNames[0] : null;
            _logger.LogDebug(
                ex,
                "Unable to get results for {Artist} - {Album} - {Song}",
                artist,
                request.AlbumName,
                request.SongName);
            return Enumerable.Empty<RemoteLyricInfo>();
        }
    }

    /// <inheritdoc />
    public async Task<LyricResponse?> GetLyricsAsync(string id, CancellationToken cancellationToken)
    {
        var splitId = id.Split('_', 2);
        if (splitId.Length != 2
            || string.IsNullOrWhiteSpace(splitId[0])
            || !IsSupportedLyricSuffix(splitId[1]))
        {
            _logger.LogDebug("Invalid lyric id format: {Id}", id);
            throw new ResourceNotFoundException($"Unable to get results for id {id}");
        }

        try
        {
            var requestUri = BuildLrclibUri($"api/get/{splitId[0]}");

            var response = await _httpClientFactory.CreateClient(NamedClient.Default)
                .GetFromJsonAsync<LyricsSearchResponse>(requestUri, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (response is null)
            {
                throw new ResourceNotFoundException($"Unable to get results for id {id}");
            }

            if (string.Equals(splitId[1], SyncedSuffix, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(response.SyncedLyrics))
            {
                var stream = new MemoryStream(Encoding.UTF8.GetBytes(response.SyncedLyrics));
                return new LyricResponse
                {
                    Format = SyncedFormat,
                    Stream = stream
                };
            }

            if (string.Equals(splitId[1], PlainSuffix, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(response.PlainLyrics))
            {
                var stream = new MemoryStream(Encoding.UTF8.GetBytes(response.PlainLyrics));
                return new LyricResponse
                {
                    Format = PlainFormat,
                    Stream = stream
                };
            }

            throw new ResourceNotFoundException($"Unable to get results for id {id}");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(
                ex,
                "Unable to get results for id {Id}",
                id);
            throw new ResourceNotFoundException($"Unable to get results for id {id}");
        }
    }

    // UriBuilder.Path overwrites any existing path, which breaks reverse-proxy
    // self-hosts mounted under a subpath (e.g. https://example.com/lrclib).
    // Compose against the base URI so the configured path is preserved.
    private static Uri BuildLrclibUri(string relativePath, string? query = null)
    {
        var builder = new UriBuilder(BaseUrl);
        var basePath = builder.Path ?? string.Empty;
        builder.Path = basePath.TrimEnd('/') + "/" + relativePath.TrimStart('/');
        if (!string.IsNullOrEmpty(query))
        {
            builder.Query = query;
        }

        return builder.Uri;
    }

    private static string CleanSongName(string songName, string? artistName)
    {
        var cleaned = songName;

        // Strip "Artist - " prefix from title (common in YouTube downloads).
        if (!string.IsNullOrEmpty(artistName))
        {
            var prefix = artistName + " - ";
            if (cleaned.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[prefix.Length..];
            }
        }

        // Strip parenthesized/bracketed YouTube-style suffixes.
        foreach (var noise in ParenthesizedNoise)
        {
            var index = cleaned.IndexOf(noise, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                cleaned = string.Concat(cleaned.AsSpan(0, index), cleaned.AsSpan(index + noise.Length));
            }
        }

        // Strip trailing " - ArtistName" (duplicate artist in title).
        if (!string.IsNullOrEmpty(artistName))
        {
            var suffix = " - " + artistName;
            if (cleaned.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[..^suffix.Length];
            }
        }

        return cleaned.Trim();
    }

    private static List<string> SplitArtists(string? artistName)
    {
        if (string.IsNullOrEmpty(artistName))
        {
            return [];
        }

        string[] separators = ["/", ";", " & ", ", ", " feat. ", " ft. ", " featuring "];
        var parts = new List<string> { artistName };

        foreach (var separator in separators)
        {
            if (artistName.Contains(separator, StringComparison.OrdinalIgnoreCase))
            {
                var split = artistName.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var part in split)
                {
                    if (!string.IsNullOrWhiteSpace(part)
                        && !parts.Contains(part, StringComparer.OrdinalIgnoreCase))
                    {
                        parts.Add(part);
                    }
                }
            }
        }

        return parts;
    }

    private static bool IsSupportedLyricSuffix(string suffix)
    {
        return string.Equals(suffix, SyncedSuffix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(suffix, PlainSuffix, StringComparison.OrdinalIgnoreCase);
    }

    // Reject results whose duration or artist is incompatible with the request, so a track named
    // "Faded (Interlude)" can't be matched against an unrelated song with the same title.
    private bool IsAcceptableMatch(LyricSearchRequest request, LyricsSearchResponse response)
    {
        // Tolerance accounts for trailing silence, LAME encoder padding, vinyl-rip fade-outs, and
        // minor master/remaster differences. The artist check above does the heavy lifting; this
        // is just a sanity net so a 30-second interlude can't get matched to a 4-minute pop song.
        if (EnableDurationFilter && request.Duration is not null && response.Duration is not null)
        {
            var tolerance = DurationToleranceSeconds;
            var requestSeconds = TimeSpan.FromTicks(request.Duration.Value).TotalSeconds;
            var responseSeconds = response.Duration.Value;
            if (Math.Abs(requestSeconds - responseSeconds) > tolerance)
            {
                _logger.LogDebug(
                    "Rejected LRCLIB match {Id}: duration mismatch (req {Requested:F0}s, got {Got:F0}s)",
                    response.Id,
                    requestSeconds,
                    responseSeconds);
                return false;
            }
        }

        if (request.ArtistNames is { Count: > 0 } && !string.IsNullOrEmpty(response.ArtistName))
        {
            var matched = false;
            foreach (var requested in request.ArtistNames)
            {
                foreach (var token in SplitArtists(requested))
                {
                    if (response.ArtistName.Contains(token, StringComparison.OrdinalIgnoreCase))
                    {
                        matched = true;
                        break;
                    }
                }

                if (matched)
                {
                    break;
                }
            }

            if (!matched)
            {
                _logger.LogDebug(
                    "Rejected LRCLIB match {Id}: artist mismatch (requested {Requested}, got {Got})",
                    response.Id,
                    request.ArtistNames,
                    response.ArtistName);
                return false;
            }
        }

        return true;
    }

    private async Task<IEnumerable<RemoteLyricInfo>> GetExactMatch(
        LyricSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.SongName))
        {
            _logger.LogInformation("Song name is required");
            return Enumerable.Empty<RemoteLyricInfo>();
        }

        string artist;
        if (request.ArtistNames is not null
            && request.ArtistNames.Count > 0)
        {
            artist = request.ArtistNames[0];
        }
        else
        {
            _logger.LogInformation("Artist name is required");
            return Enumerable.Empty<RemoteLyricInfo>();
        }

        if (string.IsNullOrEmpty(request.AlbumName))
        {
            _logger.LogInformation("Album name is required");
            return Enumerable.Empty<RemoteLyricInfo>();
        }

        if (request.Duration is null)
        {
            _logger.LogInformation("Duration is required");
            return Enumerable.Empty<RemoteLyricInfo>();
        }

        var trackName = CleanSongName(request.SongName, artist);
        var queryStringBuilder = new StringBuilder()
            .Append("track_name=")
            .Append(HttpUtility.UrlEncode(trackName))
            .Append("&artist_name=")
            .Append(HttpUtility.UrlEncode(artist))
            .Append("&album_name=")
            .Append(HttpUtility.UrlEncode(request.AlbumName))
            .Append("&duration=")
            .Append(TimeSpan.FromTicks(request.Duration.Value).TotalSeconds.ToString(CultureInfo.InvariantCulture));
        var requestUri = BuildLrclibUri("api/get", queryStringBuilder.ToString());

        var httpClient = _httpClientFactory.CreateClient(NamedClient.Default);

        var response = await httpClient
            .GetFromJsonAsync<LyricsSearchResponse>(requestUri, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (response is null)
        {
            return Enumerable.Empty<RemoteLyricInfo>();
        }

        if (!IsAcceptableMatch(request, response))
        {
            return Enumerable.Empty<RemoteLyricInfo>();
        }

        return GetRemoteLyrics(response);
    }

    private async Task<IEnumerable<RemoteLyricInfo>> GetFuzzyMatch(
        LyricSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.SongName))
        {
            _logger.LogInformation("Song name is required");
            return Enumerable.Empty<RemoteLyricInfo>();
        }

        var rawArtist = request.ArtistNames is { Count: > 0 } ? request.ArtistNames[0] : null;
        var trackName = CleanSongName(request.SongName, rawArtist);
        var albumName = ExcludeAlbumName ? null : request.AlbumName;
        var artists = ExcludeArtistName ? [] : SplitArtists(rawArtist);
        _logger.LogDebug("Fuzzy search: original song name {Original}, cleaned {Cleaned}, artists {Artists}, album {Album}", request.SongName, trackName, artists, request.AlbumName);

        // Try each artist variant (full combined name first, then individual artists).
        foreach (var artist in artists)
        {
            var results = await SearchLrclib(request, trackName, artist, albumName, cancellationToken).ConfigureAwait(false);
            if (results.Count > 0)
            {
                return results.OrderByDescending(x => x.Metadata.IsSynced);
            }

            // Retry without album if we had one.
            if (!string.IsNullOrEmpty(albumName))
            {
                _logger.LogDebug("No results with album for artist {Artist}, retrying without album for {Track}", artist, trackName);
                results = await SearchLrclib(request, trackName, artist, null, cancellationToken).ConfigureAwait(false);
                if (results.Count > 0)
                {
                    return results.OrderByDescending(x => x.Metadata.IsSynced);
                }
            }
        }

        // Track-name-only fallback only when no artist info exists at all.
        // When the user has artists, a track-name-only search would let "Faded (Interlude)"
        // match any "Faded" by any artist — exactly the bug we're trying to prevent.
        if (artists.Count == 0)
        {
            var results = await SearchLrclib(request, trackName, null, albumName, cancellationToken).ConfigureAwait(false);
            if (results.Count > 0)
            {
                return results.OrderByDescending(x => x.Metadata.IsSynced);
            }

            if (!string.IsNullOrEmpty(albumName))
            {
                results = await SearchLrclib(request, trackName, null, null, cancellationToken).ConfigureAwait(false);
                if (results.Count > 0)
                {
                    return results.OrderByDescending(x => x.Metadata.IsSynced);
                }
            }
        }

        return Enumerable.Empty<RemoteLyricInfo>();
    }

    private async Task<List<RemoteLyricInfo>> SearchLrclib(
        LyricSearchRequest request,
        string trackName,
        string? artistName,
        string? albumName,
        CancellationToken cancellationToken)
    {
        var queryStringBuilder = new StringBuilder()
            .Append("track_name=")
            .Append(HttpUtility.UrlEncode(trackName));

        if (!string.IsNullOrEmpty(artistName))
        {
            queryStringBuilder
                .Append("&artist_name=")
                .Append(HttpUtility.UrlEncode(artistName));
        }

        if (!string.IsNullOrEmpty(albumName))
        {
            queryStringBuilder
                .Append("&album_name=")
                .Append(HttpUtility.UrlEncode(albumName));
        }

        var requestUri = BuildLrclibUri("api/search", queryStringBuilder.ToString());

        var httpClient = _httpClientFactory.CreateClient(NamedClient.Default);

        var response = await httpClient
            .GetFromJsonAsync<IReadOnlyList<LyricsSearchResponse>>(requestUri, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (response is null)
        {
            return [];
        }

        var results = new List<RemoteLyricInfo>();
        foreach (var item in response)
        {
            if (!IsAcceptableMatch(request, item))
            {
                continue;
            }

            results.AddRange(GetRemoteLyrics(item));
        }

        return results;
    }

    private List<RemoteLyricInfo> GetRemoteLyrics(LyricsSearchResponse response)
    {
        var results = new List<RemoteLyricInfo>();

        if (response.Instrumental == true)
        {
            _logger.LogDebug("Skipping LRCLIB entry {Id} flagged as instrumental", response.Id);
            return results;
        }

        if (!string.IsNullOrEmpty(response.SyncedLyrics))
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(response.SyncedLyrics));
            results.Add(new RemoteLyricInfo
            {
                Id = $"{response.Id}_{SyncedSuffix}",
                ProviderName = Name,
                Metadata = new LyricMetadata
                {
                    Album = response.AlbumName,
                    Artist = response.ArtistName,
                    Title = response.TrackName,
                    Length = TimeSpan.FromSeconds(response.Duration ?? 0).Ticks,
                    IsSynced = true
                },
                Lyrics = new LyricResponse
                {
                    Format = SyncedFormat,
                    Stream = stream
                }
            });
        }

        if (!string.IsNullOrEmpty(response.PlainLyrics))
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(response.PlainLyrics));
            results.Add(new RemoteLyricInfo
            {
                Id = $"{response.Id}_{PlainSuffix}",
                ProviderName = Name,
                Metadata = new LyricMetadata
                {
                    Album = response.AlbumName,
                    Artist = response.ArtistName,
                    Title = response.TrackName,
                    Length = TimeSpan.FromSeconds(response.Duration ?? 0).Ticks,
                    IsSynced = false
                },
                Lyrics = new LyricResponse
                {
                    Format = PlainFormat,
                    Stream = stream
                }
            });
        }

        return results;
    }
}

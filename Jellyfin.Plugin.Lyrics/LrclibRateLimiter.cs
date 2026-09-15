using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Lyrics;

/// <summary>
/// Process-wide rate-limit gate for lrclib.net requests. Enforces a minimum 200 ms gap between
/// outbound calls and honors <see cref="HttpStatusCode.TooManyRequests"/> together with the
/// Retry-After header so a transient spike doesn't get logged as a generic per-track error.
/// </summary>
internal static class LrclibRateLimiter
{
    private const int Max429Retries = 1;
    private const string OfficialHost = "lrclib.net";
    private const string OfficialHostSuffix = "." + OfficialHost;

    private static readonly TimeSpan MinimumGap = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan MinimumDelay = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FallbackRetryAfter = TimeSpan.FromSeconds(5);

    private static readonly SemaphoreSlim _gate = new(1, 1);

    private static readonly ProductInfoHeaderValue _uaProduct =
        new("jellyfin-plugin-lyrics", ResolveVersion());

    private static readonly ProductInfoHeaderValue _uaComment =
        new("(+https://github.com/Felitendo/jellyfin-plugin-lyrics)");

    // Stopwatch rather than DateTime.UtcNow: a wall-clock jump backwards (NTP correction, VM
    // resume, a container starting with a bad clock) would otherwise leave the watermark in the
    // future and stall every request for the length of the jump.
    private static long _nextRequestTimestamp = Stopwatch.GetTimestamp();

    /// <summary>
    /// Gets a value indicating whether the inter-request gap is skipped. Only honored against a
    /// self-hosted instance; requests to the public lrclib.net are always rate limited.
    /// </summary>
    private static bool IsRateLimitDisabled
    {
        get
        {
            var configuration = LyricsPlugin.Instance?.Configuration;

            return configuration is not null
                && configuration.DisableRateLimit
                && !IsOfficialServer(configuration.LrclibBaseUrl);
        }
    }

    /// <summary>
    /// Sends a request to lrclib.net, waiting if necessary to respect the inter-request gap and
    /// any prior Retry-After backoff. On a 429 response, parses Retry-After, bumps the watermark,
    /// and retries once before returning to the caller.
    /// </summary>
    /// <param name="client">HTTP client to send the request on.</param>
    /// <param name="requestFactory">Factory that builds a fresh <see cref="HttpRequestMessage"/> per attempt; needed because messages can't be re-sent.</param>
    /// <param name="logger">Logger for backoff diagnostics.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The final response; caller is responsible for disposal and EnsureSuccessStatusCode.</returns>
    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        Func<HttpRequestMessage> requestFactory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (IsRateLimitDisabled)
        {
            var unlimitedRequest = requestFactory();
            ApplyUserAgent(unlimitedRequest);

            return await client
                .SendAsync(unlimitedRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }

        for (var attempt = 0; ; attempt++)
        {
            await WaitForSlotAsync(cancellationToken).ConfigureAwait(false);

            var request = requestFactory();
            ApplyUserAgent(request);
            var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt >= Max429Retries)
            {
                return response;
            }

            var retryAfter = ResolveRetryAfter(response.Headers.RetryAfter);
            logger.LogWarning("LRCLIB returned 429, backing off for {Delay}.", retryAfter);
            response.Dispose();

            await BumpWatermarkAsync(retryAfter, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WaitForSlotAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Task.Delay may fire fractionally early, so re-check rather than assuming a single
            // delay was enough to clear the gap.
            for (var remaining = GetRemainingGap(); remaining > TimeSpan.Zero; remaining = GetRemainingGap())
            {
                await Task.Delay(remaining < MinimumDelay ? MinimumDelay : remaining, cancellationToken).ConfigureAwait(false);
            }

            DelayNextRequest(MinimumGap);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task BumpWatermarkAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DelayNextRequest(delay);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void DelayNextRequest(TimeSpan delay)
    {
        var timestamp = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * delay.TotalSeconds);
        if (timestamp > _nextRequestTimestamp)
        {
            _nextRequestTimestamp = timestamp;
        }
    }

    private static TimeSpan GetRemainingGap()
        => Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), _nextRequestTimestamp);

    // A missing, unparsable or non-http(s) value is what the provider falls back to the public
    // instance for, so it counts as official and stays rate limited.
    private static bool IsOfficialServer(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return true;
        }

        return uri.Host.Equals(OfficialHost, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(OfficialHostSuffix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Stamps every outbound LRCLIB request with a fixed, identifiable User-Agent so the LRCLIB
    /// operators can differentiate this plugin from jellyfin/jellyfin-plugin-lrclib (see issue #49).
    /// </summary>
    private static void ApplyUserAgent(HttpRequestMessage request)
    {
        request.Headers.UserAgent.Clear();
        request.Headers.UserAgent.Add(_uaProduct);
        request.Headers.UserAgent.Add(_uaComment);
    }

    private static string ResolveVersion()
    {
        var version = LyricsPlugin.Instance?.Version
            ?? typeof(LrclibRateLimiter).Assembly.GetName().Version;
        return version?.ToString() ?? "unknown";
    }

    private static TimeSpan ResolveRetryAfter(RetryConditionHeaderValue? header)
    {
        TimeSpan? raw = null;
        if (header is not null)
        {
            if (header.Delta.HasValue)
            {
                raw = header.Delta.Value;
            }
            else if (header.Date.HasValue)
            {
                raw = header.Date.Value.UtcDateTime - DateTime.UtcNow;
            }
        }

        var resolved = raw ?? FallbackRetryAfter;
        if (resolved < MinimumGap)
        {
            resolved = MinimumGap;
        }
        else if (resolved > MaxRetryAfter)
        {
            resolved = MaxRetryAfter;
        }

        return resolved;
    }
}

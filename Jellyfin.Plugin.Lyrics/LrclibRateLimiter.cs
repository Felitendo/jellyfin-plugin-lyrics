using System;
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

    private static readonly TimeSpan MinimumGap = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FallbackRetryAfter = TimeSpan.FromSeconds(5);

    private static readonly SemaphoreSlim _gate = new(1, 1);

    private static readonly ProductInfoHeaderValue _uaProduct =
        new("jellyfin-plugin-lyrics", ResolveVersion());

    private static readonly ProductInfoHeaderValue _uaComment =
        new("(+https://github.com/Felitendo/jellyfin-plugin-lyrics)");

    private static DateTime _earliestNextRequestUtc = DateTime.MinValue;

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

            await BumpWatermarkAsync(DateTime.UtcNow.Add(retryAfter), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WaitForSlotAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = DateTime.UtcNow;
            var delay = _earliestNextRequestUtc - now;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            _earliestNextRequestUtc = DateTime.UtcNow.Add(MinimumGap);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task BumpWatermarkAsync(DateTime targetUtc, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (targetUtc > _earliestNextRequestUtc)
            {
                _earliestNextRequestUtc = targetUtc;
            }
        }
        finally
        {
            _gate.Release();
        }
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

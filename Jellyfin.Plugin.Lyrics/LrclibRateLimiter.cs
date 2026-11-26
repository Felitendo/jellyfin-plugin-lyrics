using System;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Lyrics.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Lyrics;

/// <summary>
/// Process-wide rate-limit gate for lrclib.net requests. Spaces outbound calls to the configured
/// requests per minute and honors <see cref="HttpStatusCode.TooManyRequests"/> and
/// <see cref="HttpStatusCode.ServiceUnavailable"/> together with the Retry-After header so a
/// transient spike doesn't get logged as a generic per-track error.
/// </summary>
internal static class LrclibRateLimiter
{
    /// <summary>
    /// Default requests per minute. Leaves headroom below lrclib.net's limit for other clients
    /// sharing the same IP.
    /// </summary>
    public const int DefaultRequestsPerMinute = 60;

    /// <summary>
    /// lrclib.net's documented per-IP limit for clients that send an identifying User-Agent.
    /// </summary>
    public const int OfficialMaxRequestsPerMinute = 90;

    private const int MaxThrottleRetries = 1;
    private const string OfficialHost = "lrclib.net";
    private const string OfficialHostSuffix = "." + OfficialHost;

    // lrclib.net asks clients to add a random jitter of up to 30% on top of Retry-After.
    private const double MaxRetryAfterJitter = 0.3;
    private const int MaxScopeLength = 32;

    private static readonly TimeSpan MinimumRetryAfter = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan MinimumDelay = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FallbackRetryAfter = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ScopeReadTimeout = TimeSpan.FromSeconds(5);

    private static readonly SemaphoreSlim _gate = new(1, 1);

    private static readonly ProductInfoHeaderValue _uaProduct =
        new("jellyfin-plugin-lyrics", ResolveVersion());

    private static readonly ProductInfoHeaderValue _uaComment =
        new("(+https://github.com/Felitendo/jellyfin-plugin-lyrics)");

    // Stopwatch rather than DateTime.UtcNow: a wall-clock jump backwards (NTP correction, VM
    // resume, a container starting with a bad clock) would otherwise leave the watermark in the
    // future and stall every request for the length of the jump.
    private static long _nextRequestTimestamp = Stopwatch.GetTimestamp();

    private static long _throttledResponseCount;
    private static int _consecutiveThrottledResponses;

    /// <summary>
    /// Gets the number of requests that were still throttled after their retry. Jellyfin's lyric
    /// manager swallows provider errors, so the lyrics task compares two readings of this to tell
    /// an empty result from a throttled one.
    /// </summary>
    public static long ThrottledResponseCount => Interlocked.Read(ref _throttledResponseCount);

    /// <summary>
    /// Gets the number of requests in a row that were still throttled after their retry. Any
    /// other response resets it.
    /// </summary>
    public static int ConsecutiveThrottledResponses => Volatile.Read(ref _consecutiveThrottledResponses);

    /// <summary>
    /// Gets the minimum time between the starts of two requests. Measured start to start, so it
    /// caps the rate however long each response takes.
    /// </summary>
    private static TimeSpan RequestInterval
    {
        get
        {
            var configuration = LyricsPlugin.Instance?.Configuration;
            var requestsPerMinute = configuration?.RequestsPerMinute ?? DefaultRequestsPerMinute;
            if (requestsPerMinute < 1)
            {
                requestsPerMinute = DefaultRequestsPerMinute;
            }

            if (IsOfficialServer(configuration?.LrclibBaseUrl))
            {
                requestsPerMinute = Math.Min(requestsPerMinute, OfficialMaxRequestsPerMinute);
            }

            return TimeSpan.FromMinutes(1) / requestsPerMinute;
        }
    }

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
    /// any prior Retry-After backoff. On a 429 or 503 response, parses Retry-After, adds jitter,
    /// bumps the watermark, and retries once before returning to the caller.
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

            if (!IsThrottleResponse(response.StatusCode))
            {
                Volatile.Write(ref _consecutiveThrottledResponses, 0);
                return response;
            }

            var retryAfter = ResolveRetryAfter(response.Headers.RetryAfter);
            TimeSpan wait;
            string status;

            // Apply the delay before the final return as well. Returning the last 429/503 without
            // moving the watermark lets the next track fire after only the normal gap, which
            // ignores the server's Retry-After instruction entirely.
            try
            {
                wait = await BumpWatermarkAsync(retryAfter, cancellationToken).ConfigureAwait(false);
                status = DescribeThrottle(
                    response.StatusCode,
                    await ReadThrottleScopeAsync(response, cancellationToken).ConfigureAwait(false));
            }
            catch
            {
                // Cancellation while waiting for the gate leaves the caller without a response,
                // so release this one here; successful calls keep caller ownership.
                response.Dispose();
                throw;
            }

            if (attempt >= MaxThrottleRetries)
            {
                Interlocked.Increment(ref _throttledResponseCount);
                Interlocked.Increment(ref _consecutiveThrottledResponses);
                logger.LogWarning(
                    "LRCLIB returned {Status} after {Attempts} attempt(s); next request in at least {DelaySeconds:0.0} s.",
                    status,
                    attempt + 1,
                    wait.TotalSeconds);
                return response;
            }

            logger.LogWarning(
                "LRCLIB returned {Status}, backing off for {DelaySeconds:0.0} s before retrying.",
                status,
                wait.TotalSeconds);
            response.Dispose();
        }
    }

    private static bool IsThrottleResponse(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;

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

            DelayNextRequest(RequestInterval);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Returns the actual wait until the next request: the Retry-After delay or what is left of
    // the regular gap, whichever is longer. Logging only the Retry-After value understates the
    // wait whenever the configured gap is longer than the server's Retry-After.
    private static async Task<TimeSpan> BumpWatermarkAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DelayNextRequest(delay);
            return GetRemainingGap();
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

    // Only a 429 body names the limit that was hit. Under heavy load the edge may answer with
    // something that isn't JSON, so an unreadable body just leaves the scope out of the log.
    private static async Task<string?> ReadThrottleScopeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode != HttpStatusCode.TooManyRequests)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ScopeReadTimeout);
        try
        {
            var body = await response.Content
                .ReadFromJsonAsync<LrclibErrorResponse>(timeout.Token)
                .ConfigureAwait(false);
            var scope = body?.Scope;

            return scope is { Length: > 0 and <= MaxScopeLength } && !scope.AsSpan().ContainsAnyExceptInRange('a', 'z')
                ? scope
                : null;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static string DescribeThrottle(HttpStatusCode statusCode, string? scope)
        => scope is null
            ? ((int)statusCode).ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{(int)statusCode} (scope: {scope})");

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
        if (resolved < MinimumRetryAfter)
        {
            resolved = MinimumRetryAfter;
        }
        else if (resolved > MaxRetryAfter)
        {
            resolved = MaxRetryAfter;
        }

        return resolved + (resolved * (Random.Shared.NextDouble() * MaxRetryAfterJitter));
    }
}

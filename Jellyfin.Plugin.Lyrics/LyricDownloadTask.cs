using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Lyrics.Configuration;
using Jellyfin.Plugin.Lyrics.Models;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Lyrics;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Lyrics;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Lyrics;

/// <summary>
/// Task to download lyrics.
/// </summary>
public class LyricDownloadTask : IScheduledTask
{
    private const int QueryPageLimit = 100;
    private const int DefaultMaxTracksPerRun = 2000;
    private const int MinimumMaxTracksPerRun = 100;
    private const int DefaultFailureStateTtlDays = 90;
    private const int MinimumFailureStateTtlDays = 1;
    private const int PeriodicStateSaveMutationThreshold = 200;

    private static readonly int[] DefaultBackoffScheduleDays = [1, 3, 7, 30];
    private static readonly TimeSpan ErrorRetryDelay = TimeSpan.FromDays(1);

    private static readonly BaseItemKind[] ItemKinds = [BaseItemKind.Audio];
    private static readonly MediaType[] MediaTypes = [MediaType.Audio];
    private static readonly SourceType[] SourceTypes = [SourceType.Library];
    private static readonly DtoOptions DtoOptions = new(false);

    private readonly ILibraryManager _libraryManager;
    private readonly ILyricManager _lyricManager;
    private readonly RetryStateStore _retryStateStore;
    private readonly ILogger<LyricDownloadTask> _logger;
    private readonly ILocalizationManager _localizationManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="LyricDownloadTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="lyricManager">Instance of the <see cref="ILyricManager"/> interface.</param>
    /// <param name="retryStateStore">Instance of the <see cref="RetryStateStore"/>.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{DownloaderScheduledTask}"/> interface.</param>
    /// <param name="localizationManager">Instance of the <see cref="ILocalizationManager"/> interface.</param>
    public LyricDownloadTask(
        ILibraryManager libraryManager,
        ILyricManager lyricManager,
        RetryStateStore retryStateStore,
        ILogger<LyricDownloadTask> logger,
        ILocalizationManager localizationManager)
    {
        _libraryManager = libraryManager;
        _lyricManager = lyricManager;
        _retryStateStore = retryStateStore;
        _logger = logger;
        _localizationManager = localizationManager;
    }

    /// <inheritdoc />
    public string Name => "Download and upgrade lyrics (new)";

    /// <inheritdoc />
    public string Key => "DLLyrics";

    /// <inheritdoc />
    public string Description => "Task to download missing lyrics and upgrade plain lyrics to synced lyrics from lrclib.net";

    /// <inheritdoc />
    public string Category => _localizationManager.GetLocalizedString("TasksLibraryCategory");

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var nowUtc = DateTime.UtcNow;

        var configuration = GetSanitizedConfiguration();
        var maxTracksPerRun = configuration.EnableRunCap ? configuration.MaxTracksPerRun : int.MaxValue;
        var backoffScheduleDays = configuration.BackoffScheduleDays.Length == 0
            ? DefaultBackoffScheduleDays
            : configuration.BackoffScheduleDays;

        var query = new InternalItemsQuery
        {
            Recursive = true,
            IsVirtualItem = false,
            IncludeItemTypes = ItemKinds,
            DtoOptions = DtoOptions,
            MediaTypes = MediaTypes,
            SourceTypes = SourceTypes,
            Limit = QueryPageLimit
        };

        var totalCount = _libraryManager.GetCount(query);
        if (totalCount == 0)
        {
            _logger.LogInformation("Lyrics task complete. No audio items found.");
            progress.Report(100);
            return;
        }

        var retryState = await _retryStateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var prunedEntriesCount = PruneExpiredEntries(retryState, nowUtc, configuration.FailureStateTtlDays);
        var stateMutations = prunedEntriesCount;

        var currentIndex = NormalizeCursor(retryState.Cursor, totalCount);
        var visitedItemCount = 0;
        var processedTrackCount = 0;
        var capReached = false;

        var missingDownloadedCount = 0;
        var upgradedToSyncedCount = 0;
        var alreadySyncedSkippedCount = 0;
        var plainNoSyncedFoundCount = 0;
        var missingNoLyricsFoundCount = 0;
        var backoffSkippedCount = 0;
        var errorsCount = 0;

        while (visitedItemCount < totalCount)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pageStart = currentIndex - (currentIndex % QueryPageLimit);
            query.StartIndex = pageStart;
            var queryResult = _libraryManager.GetItemsResult(query);
            var pageItems = queryResult.Items;
            if (pageItems.Count == 0)
            {
                _logger.LogWarning("Lyrics task stopped early because no items were returned for StartIndex {StartIndex}.", pageStart);
                break;
            }

            var pageOffset = currentIndex - pageStart;
            if (pageOffset >= pageItems.Count)
            {
                currentIndex = (pageStart + pageItems.Count) % totalCount;
                continue;
            }

            for (var pageIndex = pageOffset; pageIndex < pageItems.Count && visitedItemCount < totalCount; pageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var item = pageItems[pageIndex];
                visitedItemCount++;
                currentIndex = (currentIndex + 1) % totalCount;

                progress.Report(100d * visitedItemCount / totalCount);

                if (item is not Audio audioItem)
                {
                    continue;
                }

                if (processedTrackCount >= maxTracksPerRun)
                {
                    capReached = true;
                    break;
                }

                processedTrackCount++;

                var itemKey = audioItem.Id.ToString("N", CultureInfo.InvariantCulture);
                var trackSignature = ComputeTrackSignature(audioItem);
                var trackNowUtc = DateTime.UtcNow;

                if (retryState.Entries.TryGetValue(itemKey, out var retryEntry)
                    && !string.Equals(retryEntry.TrackSignature, trackSignature, StringComparison.Ordinal))
                {
                    retryState.Entries.Remove(itemKey);
                    retryEntry = null;
                    stateMutations++;
                }

                if (configuration.EnableAdaptiveRetryBackoff
                    && retryEntry is not null
                    && retryEntry.NextRetryUtc > trackNowUtc)
                {
                    backoffSkippedCount++;
                    continue;
                }

                try
                {
                    var existingLyrics = await _lyricManager.GetLyricsAsync(audioItem, cancellationToken).ConfigureAwait(false);

                    if (existingLyrics is null)
                    {
                        _logger.LogDebug("Searching for lyrics for {Path}", audioItem.Path);
                        var lyricResults = await _lyricManager.SearchLyricsAsync(audioItem, true, cancellationToken).ConfigureAwait(false);
                        if (lyricResults.Count != 0)
                        {
                            _logger.LogDebug("Saving lyrics for {Path}", audioItem.Path);
                            await _lyricManager.DownloadLyricsAsync(audioItem, lyricResults[0].Id, cancellationToken).ConfigureAwait(false);
                            missingDownloadedCount++;
                            stateMutations += ClearRetryState(retryState, itemKey);
                        }
                        else
                        {
                            missingNoLyricsFoundCount++;
                            if (configuration.EnableAdaptiveRetryBackoff)
                            {
                                stateMutations += UpdateNoResultEntry(retryState, itemKey, trackSignature, trackNowUtc, backoffScheduleDays);
                            }
                        }
                    }
                    else if (HasSyncedLyrics(existingLyrics))
                    {
                        alreadySyncedSkippedCount++;
                        stateMutations += ClearRetryState(retryState, itemKey);
                    }
                    else
                    {
                        _logger.LogDebug("Checking upgrade to synced lyrics for {Path}", audioItem.Path);
                        var lyricResults = await _lyricManager.SearchLyricsAsync(audioItem, true, cancellationToken).ConfigureAwait(false);
                        var syncedCandidate = SelectBestSyncedCandidate(lyricResults);
                        if (syncedCandidate is not null)
                        {
                            _logger.LogDebug("Upgrading to synced lyrics for {Path}", audioItem.Path);
                            await _lyricManager.DownloadLyricsAsync(audioItem, syncedCandidate.Id, cancellationToken).ConfigureAwait(false);
                            upgradedToSyncedCount++;
                            stateMutations += ClearRetryState(retryState, itemKey);
                        }
                        else
                        {
                            plainNoSyncedFoundCount++;
                            if (configuration.EnableAdaptiveRetryBackoff)
                            {
                                stateMutations += UpdateNoResultEntry(retryState, itemKey, trackSignature, trackNowUtc, backoffScheduleDays);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    errorsCount++;
                    _logger.LogError(ex, "Error processing lyrics for {Path}", audioItem.Path);
                    if (configuration.EnableAdaptiveRetryBackoff)
                    {
                        stateMutations += UpdateErrorEntry(retryState, itemKey, trackSignature, trackNowUtc);
                    }
                }

                if (stateMutations >= PeriodicStateSaveMutationThreshold)
                {
                    await _retryStateStore.SaveAsync(retryState, cancellationToken).ConfigureAwait(false);
                    stateMutations = 0;
                }
            }

            if (capReached)
            {
                break;
            }
        }

        retryState.Cursor = NormalizeCursor(currentIndex, totalCount);
        await _retryStateStore.SaveAsync(retryState, cancellationToken).ConfigureAwait(false);

        if (LyricsPlugin.Instance is not null)
        {
            LyricsPlugin.Instance.Configuration.StateCursor = retryState.Cursor;
        }

        _logger.LogInformation(
            "Lyrics task complete in {Elapsed}. Processed tracks: {ProcessedTrackCount}, visited items: {VisitedItemCount}/{TotalCount}, cap reached: {CapReached}, missing downloaded: {MissingDownloadedCount}, upgraded to synced: {UpgradedToSyncedCount}, already synced skipped: {AlreadySyncedSkippedCount}, missing with no lyrics found: {MissingNoLyricsFoundCount}, plain with no synced found: {PlainNoSyncedFoundCount}, backoff skipped: {BackoffSkippedCount}, pruned retry-state entries: {PrunedEntriesCount}, errors: {ErrorsCount}",
            stopwatch.Elapsed,
            processedTrackCount,
            visitedItemCount,
            totalCount,
            capReached,
            missingDownloadedCount,
            upgradedToSyncedCount,
            alreadySyncedSkippedCount,
            missingNoLyricsFoundCount,
            plainNoSyncedFoundCount,
            backoffSkippedCount,
            prunedEntriesCount,
            errorsCount);

        progress.Report(100);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromHours(24).Ticks
            }
        ];
    }

    private static bool HasSyncedLyrics(LyricDto existingLyrics)
    {
        return existingLyrics.Metadata?.IsSynced == true;
    }

    private static PluginConfiguration GetSanitizedConfiguration()
    {
        var configuration = LyricsPlugin.Instance?.Configuration ?? new PluginConfiguration();
        configuration.MaxTracksPerRun = configuration.MaxTracksPerRun <= 0
            ? DefaultMaxTracksPerRun
            : Math.Max(configuration.MaxTracksPerRun, MinimumMaxTracksPerRun);
        configuration.FailureStateTtlDays = configuration.FailureStateTtlDays <= 0
            ? DefaultFailureStateTtlDays
            : Math.Max(configuration.FailureStateTtlDays, MinimumFailureStateTtlDays);

        configuration.BackoffScheduleDays = (configuration.BackoffScheduleDays ?? DefaultBackoffScheduleDays)
            .Where(static value => value > 0)
            .Distinct()
            .ToArray();

        return configuration;
    }

    private static int NormalizeCursor(int cursor, int totalCount)
    {
        if (totalCount <= 0)
        {
            return 0;
        }

        if (cursor < 0)
        {
            cursor = 0;
        }

        return cursor % totalCount;
    }

    private static int PruneExpiredEntries(LyricsRetryState state, DateTime nowUtc, int ttlDays)
    {
        var cutoffUtc = nowUtc.AddDays(-ttlDays);
        var keysToRemove = state.Entries
            .Where(static kvp => kvp.Value.LastAttemptUtc != default)
            .Where(kvp => kvp.Value.LastAttemptUtc < cutoffUtc)
            .Select(static kvp => kvp.Key)
            .ToArray();

        foreach (var key in keysToRemove)
        {
            state.Entries.Remove(key);
        }

        return keysToRemove.Length;
    }

    private static int ClearRetryState(LyricsRetryState state, string itemKey)
    {
        return state.Entries.Remove(itemKey) ? 1 : 0;
    }

    private static int UpdateNoResultEntry(
        LyricsRetryState state,
        string itemKey,
        string trackSignature,
        DateTime nowUtc,
        int[] backoffScheduleDays)
    {
        if (!state.Entries.TryGetValue(itemKey, out var entry))
        {
            entry = new LyricsRetryEntry();
        }

        entry.TrackSignature = trackSignature;
        entry.ConsecutiveNoResultCount++;
        entry.LastAttemptUtc = nowUtc;
        entry.LastOutcome = LyricsRetryOutcome.NoResult;

        var retryIndex = Math.Min(entry.ConsecutiveNoResultCount - 1, backoffScheduleDays.Length - 1);
        entry.NextRetryUtc = nowUtc.AddDays(backoffScheduleDays[retryIndex]);

        state.Entries[itemKey] = entry;
        return 1;
    }

    private static int UpdateErrorEntry(
        LyricsRetryState state,
        string itemKey,
        string trackSignature,
        DateTime nowUtc)
    {
        if (!state.Entries.TryGetValue(itemKey, out var entry))
        {
            entry = new LyricsRetryEntry();
        }

        entry.TrackSignature = trackSignature;
        entry.LastAttemptUtc = nowUtc;
        entry.LastOutcome = LyricsRetryOutcome.Error;
        entry.NextRetryUtc = nowUtc.Add(ErrorRetryDelay);

        state.Entries[itemKey] = entry;
        return 1;
    }

    private static string ComputeTrackSignature(Audio audioItem)
    {
        var builder = new StringBuilder();
        builder.Append(NormalizeForSignature(audioItem.Name));
        builder.Append('|');
        builder.Append(NormalizeForSignature(audioItem.Path));
        builder.Append('|');
        builder.Append(audioItem.RunTimeTicks?.ToString(CultureInfo.InvariantCulture) ?? "0");
        builder.Append('|');
        builder.Append(NormalizeForSignature(GetStringProperty(audioItem, "Album")));
        builder.Append('|');
        builder.Append(NormalizeForSignature(GetJoinedEnumerableProperty(audioItem, "Artists")));
        builder.Append('|');
        builder.Append(NormalizeForSignature(GetJoinedEnumerableProperty(audioItem, "AlbumArtists")));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash);
    }

    private static string NormalizeForSignature(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();
    }

    private static string? GetStringProperty(object instance, string propertyName)
    {
        return instance.GetType().GetProperty(propertyName)?.GetValue(instance) as string;
    }

    private static string GetJoinedEnumerableProperty(object instance, string propertyName)
    {
        if (instance.GetType().GetProperty(propertyName)?.GetValue(instance) is not IEnumerable values)
        {
            return string.Empty;
        }

        var normalizedValues = new List<string>();
        foreach (var value in values)
        {
            if (value is string str && !string.IsNullOrWhiteSpace(str))
            {
                normalizedValues.Add(str.Trim());
            }
        }

        return string.Join('|', normalizedValues);
    }

    private static RemoteLyricInfoDto? SelectBestSyncedCandidate(IReadOnlyList<RemoteLyricInfoDto> lyricResults)
    {
        return lyricResults.FirstOrDefault(static x => x.Lyrics?.Metadata?.IsSynced == true);
    }
}

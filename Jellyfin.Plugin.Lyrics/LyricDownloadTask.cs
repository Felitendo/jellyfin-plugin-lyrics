using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Lyrics;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Lyrics;

/// <summary>
/// Task to download lyrics.
/// </summary>
public class LyricDownloadTask : IScheduledTask
{
    private const int QueryPageLimit = 100;

    private static readonly BaseItemKind[] ItemKinds = [BaseItemKind.Audio];
    private static readonly MediaType[] MediaTypes = [MediaType.Audio];
    private static readonly SourceType[] SourceTypes = [SourceType.Library];
    private static readonly DtoOptions DtoOptions = new(false);

    private readonly ILibraryManager _libraryManager;
    private readonly ILyricManager _lyricManager;
    private readonly ILogger<LyricDownloadTask> _logger;
    private readonly ILocalizationManager _localizationManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="LyricDownloadTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="lyricManager">Instance of the <see cref="ILyricManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{DownloaderScheduledTask}"/> interface.</param>
    /// <param name="localizationManager">Instance of the <see cref="ILocalizationManager"/> interface.</param>
    public LyricDownloadTask(
        ILibraryManager libraryManager,
        ILyricManager lyricManager,
        ILogger<LyricDownloadTask> logger,
        ILocalizationManager localizationManager)
    {
        _libraryManager = libraryManager;
        _lyricManager = lyricManager;
        _logger = logger;
        _localizationManager = localizationManager;
    }

    /// <inheritdoc />
    public string Name => "Download missing lyrics (new)";

    /// <inheritdoc />
    public string Key => "DLLyrics";

    /// <inheritdoc />
    public string Description => "Task to download missing lyrics from lrclib.net";

    /// <inheritdoc />
    public string Category => _localizationManager.GetLocalizedString("TasksLibraryCategory");

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
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

        var startIndex = 0;
        var completed = 0;

        while (startIndex < totalCount)
        {
            query.StartIndex = startIndex;
            var audioItems = _libraryManager.GetItemList(query);

            foreach (var audioItem in audioItems.OfType<Audio>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (audioItem.GetMediaStreams().All(s => s.Type != MediaStreamType.Lyric))
                    {
                        _logger.LogDebug("Searching for lyrics for {Path}", audioItem.Path);
                        var lyricResults = await _lyricManager.SearchLyricsAsync(audioItem, true, cancellationToken).ConfigureAwait(false);
                        if (lyricResults.Count != 0)
                        {
                            _logger.LogDebug("Saving lyrics for {Path}", audioItem.Path);
                            await _lyricManager.DownloadLyricsAsync(audioItem, lyricResults[0].Id, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error downloading lyrics for {Path}", audioItem.Path);
                }

                completed++;
                progress.Report(100d * completed / totalCount);
            }

            startIndex += QueryPageLimit;
        }

        progress.Report(100);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerInterval,
                IntervalTicks = TimeSpan.FromHours(24).Ticks
            }
        ];
    }
}
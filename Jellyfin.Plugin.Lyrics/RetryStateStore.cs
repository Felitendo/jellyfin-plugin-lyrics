using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Lyrics.Models;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Lyrics;

/// <summary>
/// Persists retry and cursor state for the lyric download task.
/// </summary>
public class RetryStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ILogger<RetryStateStore> _logger;
    private readonly string _stateFilePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="RetryStateStore"/> class.
    /// </summary>
    /// <param name="applicationPaths">Application paths.</param>
    /// <param name="logger">Logger.</param>
    public RetryStateStore(IApplicationPaths applicationPaths, ILogger<RetryStateStore> logger)
    {
        _logger = logger;

        var stateDir = Path.Combine(ResolveBasePath(applicationPaths), "plugins", "lyrics");
        Directory.CreateDirectory(stateDir);
        _stateFilePath = Path.Combine(stateDir, "retry-state.json");
    }

    /// <summary>
    /// Loads state from disk.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Loaded state or empty default state.</returns>
    public async Task<LyricsRetryState> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                return new LyricsRetryState();
            }

            using var stream = File.OpenRead(_stateFilePath);
            var state = await JsonSerializer.DeserializeAsync<LyricsRetryState>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
            return state ?? new LyricsRetryState();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load lyrics retry state from {Path}. Using empty state.", _stateFilePath);
            return new LyricsRetryState();
        }
    }

    /// <summary>
    /// Saves state to disk.
    /// </summary>
    /// <param name="state">State to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task completion.</returns>
    public async Task SaveAsync(LyricsRetryState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        try
        {
            var tempPath = _stateFilePath + ".tmp";

            using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, state, SerializerOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, _stateFilePath, true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save lyrics retry state to {Path}.", _stateFilePath);
        }
    }

    private static string ResolveBasePath(IApplicationPaths applicationPaths)
    {
        var candidateProperties = new[]
        {
            "ProgramDataPath",
            "DataPath",
            "CachePath"
        };

        var pathsType = applicationPaths.GetType();
        foreach (var propertyName in candidateProperties)
        {
            if (TryGetPathValue(pathsType, applicationPaths, propertyName) is string runtimePath)
            {
                return runtimePath;
            }

            if (TryGetPathValue(typeof(IApplicationPaths), applicationPaths, propertyName) is string interfacePath)
            {
                return interfacePath;
            }
        }

        throw new InvalidOperationException("Unable to resolve application data path for retry-state persistence.");
    }

    private static string? TryGetPathValue(Type sourceType, object instance, string propertyName)
    {
        var property = sourceType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property?.GetValue(instance) is string value
            && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return null;
    }
}

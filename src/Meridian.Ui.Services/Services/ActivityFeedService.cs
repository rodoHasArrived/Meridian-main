using System.Collections.ObjectModel;
using System.Text.Json;
using System.Threading.Channels;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.Storage.Archival;
using Meridian.Ui.Services.Collections;
using Meridian.Ui.Services.Contracts;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for tracking and displaying recent activity in the application.
/// Provides a timeline of events for user awareness and polls the backend
/// for server-side error events so the activity feed shows real backend logs.
/// </summary>
public sealed class ActivityFeedService : IAsyncDisposable
{
    private const string ActivityLogFileName = "activity_log.json";
    private const int MaxActivities = 100;
    private static readonly JsonSerializerOptions _configJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Lazy<ActivityFeedService> _instance = new(() => new ActivityFeedService());
    private readonly IConfigService _configService;
    private readonly string _activityLogPath;
    private readonly string _legacyActivityLogPath;
    private readonly BoundedObservableCollection<ActivityItem> _activities;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly object _stateGate = new();
    private readonly Channel<ActivityFeedPersistenceRequest> _persistenceRequests;
    private readonly Func<string, string, CancellationToken, Task> _persistAsync;
    private readonly Task _persistenceWorker;
    private readonly Task _initialization;
    private Exception? _lastPersistenceError;
    private int _disposeStarted;

    // Tracks IDs of server-side error events already added, to prevent duplicates
    // across repeated FetchServerEventsAsync calls.
    private readonly HashSet<string> _seenServerEventIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the singleton instance of the ActivityFeedService.
    /// </summary>
    public static ActivityFeedService Instance => _instance.Value;

    /// <summary>
    /// Gets the observable collection of activities.
    /// Uses BoundedObservableCollection for efficient O(1) prepend operations.
    /// </summary>
    public BoundedObservableCollection<ActivityItem> Activities => _activities;

    /// <summary>
    /// Gets the most recent activity-feed persistence failure, or <c>null</c> after a
    /// subsequent write succeeds.
    /// </summary>
    public Exception? LastPersistenceError
    {
        get
        {
            lock (_stateGate)
            {
                return _lastPersistenceError;
            }
        }
    }

    /// <summary>
    /// Gets the initial load task so callers that require a fully hydrated feed can await it.
    /// </summary>
    public Task Initialization => _initialization;

    /// <summary>
    /// Event raised when a new activity is added.
    /// </summary>
    public event EventHandler<ActivityItem>? ActivityAdded;

    /// <summary>
    /// Raised when the ordered persistence worker cannot commit an activity-feed snapshot.
    /// </summary>
    public event EventHandler<ActivityFeedPersistenceFailedEventArgs>? PersistenceFailed;

    private ActivityFeedService()
        : this(new ConfigService())
    {
    }

    internal ActivityFeedService(
        IConfigService configService,
        Func<string, string, CancellationToken, Task>? persistAsync = null)
    {
        _configService = configService;
        _activityLogPath = ResolveActivityLogPath();
        _legacyActivityLogPath = Path.Combine(AppContext.BaseDirectory, "data", "_logs", ActivityLogFileName);
        _activities = new BoundedObservableCollection<ActivityItem>(MaxActivities);
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        _persistAsync = persistAsync ?? ((path, content, ct) =>
            AtomicFileWriter.WriteAsync(path, content, ct));
        _persistenceRequests = Channel.CreateUnbounded<ActivityFeedPersistenceRequest>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
        _persistenceWorker = RunPersistenceWorkerAsync();

        _initialization = LoadActivitiesAsync();
        _ = _initialization.ContinueWith(
            t => System.Diagnostics.Trace.TraceError(
                $"Failed to load activities from {_activityLogPath}: {t.Exception?.InnerException?.Message}"),
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }

    /// <summary>
    /// Adds an activity item directly (for ViewModel convenience).
    /// Uses efficient Prepend operation - O(1) with automatic capacity management.
    /// </summary>
    public void AddActivity(ActivityItem activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ThrowIfDisposing();

        lock (_stateGate)
        {
            ThrowIfDisposing();
            PrepareActivity(activity);
            _activities.Prepend(activity);
            QueuePersistenceNoLock(awaitCompletion: false);
        }

        ActivityAdded?.Invoke(this, activity);
    }

    /// <summary>
    /// Logs a new activity event.
    /// Uses efficient Prepend operation - O(1) with automatic capacity management.
    /// </summary>
    public async Task LogActivityAsync(
        ActivityType type,
        string title,
        string? description = null,
        string? symbol = null,
        string? provider = null,
        Dictionary<string, object>? metadata = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposing();

        var activity = new ActivityItem
        {
            Id = Guid.NewGuid().ToString(),
            Type = type,
            Title = title,
            Description = description,
            Symbol = symbol,
            Provider = provider,
            Timestamp = DateTime.UtcNow,
            Metadata = metadata
        };

        Task persistence;
        lock (_stateGate)
        {
            ThrowIfDisposing();
            _activities.Prepend(activity);
            persistence = QueuePersistenceNoLock(awaitCompletion: true);
        }

        ActivityAdded?.Invoke(this, activity);
        await persistence.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Logs a collector status change.
    /// </summary>
    public Task LogCollectorStatusAsync(bool isConnected, string? provider = null)
    {
        return LogActivityAsync(
            isConnected ? ActivityType.CollectorStarted : ActivityType.CollectorStopped,
            isConnected ? "Collector Started" : "Collector Stopped",
            isConnected ? $"Data collection has started for {provider ?? "all providers"}" : "Data collection has been stopped",
            provider: provider
        );
    }

    /// <summary>
    /// Logs a backfill operation.
    /// </summary>
    public Task LogBackfillAsync(string[] symbols, string provider, bool success, int barsDownloaded)
    {
        return LogActivityAsync(
            success ? ActivityType.BackfillCompleted : ActivityType.BackfillFailed,
            success ? "Backfill Completed" : "Backfill Failed",
            success
                ? $"Downloaded {barsDownloaded:N0} bars for {symbols.Length} symbols from {provider}"
                : $"Backfill failed for {string.Join(", ", symbols)}",
            provider: provider,
            metadata: new Dictionary<string, object>
            {
                ["symbols"] = symbols,
                ["barsDownloaded"] = barsDownloaded
            }
        );
    }

    /// <summary>
    /// Logs a symbol subscription change.
    /// </summary>
    public Task LogSymbolChangeAsync(string symbol, bool added)
    {
        return LogActivityAsync(
            added ? ActivityType.SymbolAdded : ActivityType.SymbolRemoved,
            added ? "Symbol Added" : "Symbol Removed",
            added ? $"{symbol} has been added to your watchlist" : $"{symbol} has been removed from your watchlist",
            symbol: symbol
        );
    }

    /// <summary>
    /// Logs a data quality event.
    /// </summary>
    public Task LogDataQualityEventAsync(string symbol, string issue, string severity)
    {
        return LogActivityAsync(
            ActivityType.DataQualityIssue,
            $"Data Quality Alert - {symbol}",
            issue,
            symbol: symbol,
            metadata: new Dictionary<string, object>
            {
                ["severity"] = severity
            }
        );
    }

    /// <summary>
    /// Logs a storage event.
    /// </summary>
    public Task LogStorageEventAsync(string message, long bytesAffected = 0)
    {
        return LogActivityAsync(
            ActivityType.StorageEvent,
            "Storage Event",
            message,
            metadata: bytesAffected > 0
                ? new Dictionary<string, object> { ["bytesAffected"] = bytesAffected }
                : null
        );
    }

    /// <summary>
    /// Logs an export operation.
    /// </summary>
    public Task LogExportAsync(string format, string[] symbols, long bytesExported)
    {
        return LogActivityAsync(
            ActivityType.ExportCompleted,
            "Export Completed",
            $"Exported {symbols.Length} symbols to {format.ToUpperInvariant()} format ({FormatBytes(bytesExported)})",
            metadata: new Dictionary<string, object>
            {
                ["format"] = format,
                ["symbols"] = symbols,
                ["bytesExported"] = bytesExported
            }
        );
    }

    /// <summary>
    /// Logs a provider connection event.
    /// </summary>
    public Task LogProviderConnectionAsync(string provider, bool connected, string? message = null)
    {
        return LogActivityAsync(
            connected ? ActivityType.ProviderConnected : ActivityType.ProviderDisconnected,
            connected ? $"{provider} Connected" : $"{provider} Disconnected",
            message,
            provider: provider
        );
    }

    /// <summary>
    /// Adds a server-side event to the activity feed only if it has not been seen before.
    /// Deduplication is based on the item's <see cref="ActivityItem.Id"/>.
    /// Items without an ID are always added.
    /// </summary>
    /// <returns><c>true</c> if the item was new and added; <c>false</c> if it was a duplicate.</returns>
    public bool AddServerEventIfNew(ActivityItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ThrowIfDisposing();

        lock (_stateGate)
        {
            ThrowIfDisposing();
            PrepareActivity(item);

            if (!_seenServerEventIds.Add(item.Id))
            {
                return false;
            }

            _activities.Prepend(item);
            QueuePersistenceNoLock(awaitCompletion: false);
        }

        ActivityAdded?.Invoke(this, item);
        return true;
    }

    /// <summary>
    /// Polls the backend <c>/api/errors</c> endpoint and adds any new server-side error events
    /// to the activity feed. Duplicate events (same ID) are silently skipped.
    /// Errors from the HTTP call are silently swallowed so a missing backend never crashes the UI.
    /// </summary>
    public async Task FetchServerEventsAsync(CancellationToken ct = default)
    {
        try
        {
            var response = (await ApiClientService.Instance.GetWithResponseAsync<ErrorsResponseDto>(
                UiApiRoutes.Errors,
                ct).ConfigureAwait(false)).DataOrLoggedNull("Fetch server events");

            if (response?.Errors == null)
                return;

            foreach (var entry in response.Errors)
            {
                if (ct.IsCancellationRequested)
                    break;

                var activityType = entry.Level?.ToLowerInvariant() switch
                {
                    "critical" => ActivityType.DataQualityIssue,
                    "error" => ActivityType.DataQualityIssue,
                    "warning" => ActivityType.DataQualityIssue,
                    _ => ActivityType.ProviderConnected
                };

                var item = new ActivityItem
                {
                    Id = $"server:{entry.Id}",
                    Type = activityType,
                    Title = string.IsNullOrEmpty(entry.Source) ? "Server Event" : entry.Source,
                    Description = entry.Message,
                    Symbol = entry.Symbol,
                    Provider = entry.Provider,
                    Timestamp = entry.Timestamp.UtcDateTime
                };

                AddServerEventIfNew(item);
            }
        }
        catch (OperationCanceledException)
        {
            // Page navigated away — no action needed.
        }
        catch
        {
            // Backend unreachable or malformed response — silently ignore.
        }
    }

    /// <summary>
    /// Gets activities filtered by type.
    /// </summary>
    public IEnumerable<ActivityItem> GetActivitiesByType(ActivityType type)
    {
        return _activities.Where(a => a.Type == type);
    }

    /// <summary>
    /// Gets activities for a specific symbol.
    /// </summary>
    public IEnumerable<ActivityItem> GetActivitiesForSymbol(string symbol)
    {
        return _activities.Where(a =>
            string.Equals(a.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets activities within a time range.
    /// </summary>
    public IEnumerable<ActivityItem> GetActivitiesSince(DateTime since)
    {
        return _activities.Where(a => a.Timestamp >= since);
    }

    /// <summary>
    /// Clears all activities.
    /// </summary>
    public async Task ClearActivitiesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposing();

        Task persistence;
        lock (_stateGate)
        {
            ThrowIfDisposing();
            _activities.Clear();
            _seenServerEventIds.Clear();
            persistence = QueuePersistenceNoLock(awaitCompletion: true);
        }

        await persistence.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits until all snapshots queued before this call have been committed.
    /// </summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposing();

        Task persistence;
        lock (_stateGate)
        {
            ThrowIfDisposing();
            persistence = QueuePersistenceNoLock(awaitCompletion: true);
        }

        await persistence.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Merges persisted activities into the current feed without evicting newer
    /// in-memory items that may have been logged while the initial load was running.
    /// </summary>
    internal void MergeLoadedActivities(IEnumerable<ActivityItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        lock (_stateGate)
        {
            MergeLoadedActivitiesNoLock(items);
        }
    }

    private void MergeLoadedActivitiesNoLock(IEnumerable<ActivityItem> items)
    {
        var persistedItems = items.Take(MaxActivities).ToList();
        if (persistedItems.Count == 0)
        {
            return;
        }

        var merged = new List<ActivityItem>(MaxActivities);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AppendUniqueActivities(_activities.ToList(), merged, seenIds);
        AppendUniqueActivities(persistedItems, merged, seenIds);

        SeedSeenServerEventIds(merged);
        _activities.ReplaceAll(merged);
    }

    private async Task LoadActivitiesAsync(CancellationToken ct = default)
    {
        try
        {
            var loadPath = _activityLogPath;
            if (!File.Exists(loadPath) &&
                !PathsEqual(_activityLogPath, _legacyActivityLogPath) &&
                File.Exists(_legacyActivityLogPath))
            {
                loadPath = _legacyActivityLogPath;
            }

            if (File.Exists(loadPath))
            {
                var json = await File.ReadAllTextAsync(loadPath, ct);
                var items = JsonSerializer.Deserialize<List<ActivityItem>>(json, _jsonOptions);
                if (items != null)
                {
                    Task persistence;
                    lock (_stateGate)
                    {
                        MergeLoadedActivitiesNoLock(items);
                        // Always queue the merged snapshot. This prevents an in-memory activity
                        // logged during startup from racing an older pre-load snapshot to disk.
                        persistence = QueuePersistenceNoLock(awaitCompletion: true);
                    }

                    await persistence.WaitAsync(ct).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            System.Diagnostics.Trace.TraceWarning("Failed to load activity feed entries from {0}", _activityLogPath);
        }
    }

    private Task QueuePersistenceNoLock(bool awaitCompletion)
    {
        var completion = awaitCompletion
            ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
            : null;
        var json = JsonSerializer.Serialize(_activities.ToList(), _jsonOptions);
        var request = new ActivityFeedPersistenceRequest(json, completion);

        if (!_persistenceRequests.Writer.TryWrite(request))
        {
            throw new ObjectDisposedException(nameof(ActivityFeedService));
        }

        return completion?.Task ?? Task.CompletedTask;
    }

    private async Task RunPersistenceWorkerAsync()
    {
        await foreach (var request in _persistenceRequests.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await _persistAsync(_activityLogPath, request.Json, CancellationToken.None)
                    .ConfigureAwait(false);

                lock (_stateGate)
                {
                    _lastPersistenceError = null;
                }

                request.Completion?.TrySetResult();
            }
            catch (Exception ex)
            {
                lock (_stateGate)
                {
                    _lastPersistenceError = ex;
                }

                System.Diagnostics.Trace.TraceError(
                    "Failed to persist activity feed entries to {0}: {1}",
                    _activityLogPath,
                    ex.Message);
                RaisePersistenceFailed(ex);
                request.Completion?.TrySetException(ex);
            }
        }
    }

    private void RaisePersistenceFailed(Exception exception)
    {
        try
        {
            PersistenceFailed?.Invoke(
                this,
                new ActivityFeedPersistenceFailedEventArgs(_activityLogPath, exception));
        }
        catch (Exception callbackException)
        {
            System.Diagnostics.Trace.TraceError(
                "Activity-feed persistence failure callback threw: {0}",
                callbackException.Message);
        }
    }

    private static void PrepareActivity(ActivityItem activity)
    {
        if (string.IsNullOrEmpty(activity.Id))
        {
            activity.Id = Guid.NewGuid().ToString();
        }

        if (activity.Timestamp == default)
        {
            activity.Timestamp = DateTime.UtcNow;
        }
    }

    private void ThrowIfDisposing()
    {
        if (Volatile.Read(ref _disposeStarted) != 0)
        {
            throw new ObjectDisposedException(nameof(ActivityFeedService));
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            await _persistenceWorker.ConfigureAwait(false);
            return;
        }

        await _initialization.ConfigureAwait(false);

        Task finalPersistence;
        lock (_stateGate)
        {
            finalPersistence = QueuePersistenceNoLock(awaitCompletion: true);
            _persistenceRequests.Writer.TryComplete();
        }

        try
        {
            await finalPersistence.ConfigureAwait(false);
        }
        finally
        {
            await _persistenceWorker.ConfigureAwait(false);
        }
    }

    private string ResolveActivityLogPath()
    {
        var config = TryLoadConfig();
        var dataRoot = MeridianPathDefaults.ResolveDataRoot(_configService.ConfigPath, config?.DataRoot);
        return Path.Combine(dataRoot, "_logs", ActivityLogFileName);
    }

    private AppConfig? TryLoadConfig()
    {
        try
        {
            if (File.Exists(_configService.ConfigPath))
            {
                var json = File.ReadAllText(_configService.ConfigPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, _configJsonOptions);
                if (config != null)
                {
                    config.DataRoot = MeridianPathDefaults.ResolveConfiguredDataRootFromJson(json, config.DataRoot);
                }

                return config;
            }
        }
        catch
        {
            // Fall back to a pre-computed config task when the service is not file-backed.
        }

        try
        {
            var loadTask = _configService.LoadConfigAsync();
            if (loadTask.IsCompletedSuccessfully)
            {
                return loadTask.GetAwaiter().GetResult();
            }
        }
        catch
        {
            // Ignore config load errors and use the default data root.
        }

        return null;
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);

    private static void AppendUniqueActivities(
        IEnumerable<ActivityItem> source,
        List<ActivityItem> destination,
        HashSet<string> seenIds)
    {
        foreach (var item in source)
        {
            if (destination.Count >= MaxActivities)
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(item.Id) && !seenIds.Add(item.Id))
            {
                continue;
            }

            destination.Add(item);
        }
    }

    private void SeedSeenServerEventIds(IEnumerable<ActivityItem> items)
    {
        foreach (var item in items)
        {
            if (!string.IsNullOrWhiteSpace(item.Id) &&
                item.Id.StartsWith("server:", StringComparison.OrdinalIgnoreCase))
            {
                _seenServerEventIds.Add(item.Id);
            }
        }
    }

    private static string FormatBytes(long bytes) => FormatHelpers.FormatBytes(bytes);

    private sealed record ActivityFeedPersistenceRequest(
        string Json,
        TaskCompletionSource? Completion);
}

/// <summary>
/// Describes a failed activity-feed persistence attempt.
/// </summary>
public sealed class ActivityFeedPersistenceFailedEventArgs(
    string path,
    Exception exception) : EventArgs
{
    public string Path { get; } = path;
    public Exception Exception { get; } = exception;
}

/// <summary>
/// Types of activity events.
/// </summary>
public enum ActivityType : byte
{
    // Collector events
    CollectorStarted,
    CollectorStopped,
    CollectorPaused,
    CollectorResumed,

    // Provider events
    ProviderConnected,
    ProviderDisconnected,
    ProviderError,

    // Symbol events
    SymbolAdded,
    SymbolRemoved,
    SymbolSubscribed,
    SymbolUnsubscribed,

    // Data events
    BackfillStarted,
    BackfillCompleted,
    BackfillFailed,
    BackfillProgress,

    // Quality events
    DataQualityIssue,
    GapDetected,
    GapRepaired,
    IntegrityError,

    // Storage events
    StorageEvent,
    ArchiveCreated,
    ArchiveVerified,
    CompressionCompleted,

    // Export events
    ExportStarted,
    ExportCompleted,
    ExportFailed,

    // System events
    SystemStarted,
    SystemStopped,
    ConfigurationChanged,
    Error,
    Warning,
    Info
}

/// <summary>
/// Individual activity item.
/// </summary>
public sealed class ActivityItem
{
    public string Id { get; set; } = string.Empty;
    public ActivityType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Symbol { get; set; }
    public string? Provider { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>
    /// Gets the icon glyph for this activity type.
    /// </summary>
    public string Icon => Type switch
    {
        ActivityType.CollectorStarted => "\uE768",
        ActivityType.CollectorStopped => "\uE71A",
        ActivityType.CollectorPaused => "\uE769",
        ActivityType.CollectorResumed => "\uE768",
        ActivityType.ProviderConnected => "\uE703",
        ActivityType.ProviderDisconnected => "\uE8CD",
        ActivityType.ProviderError => "\uE783",
        ActivityType.SymbolAdded => "\uE710",
        ActivityType.SymbolRemoved => "\uE74D",
        ActivityType.SymbolSubscribed => "\uE8AB",
        ActivityType.SymbolUnsubscribed => "\uE8D8",
        ActivityType.BackfillStarted => "\uE787",
        ActivityType.BackfillCompleted => "\uE73E",
        ActivityType.BackfillFailed => "\uE783",
        ActivityType.BackfillProgress => "\uE895",
        ActivityType.DataQualityIssue => "\uE7BA",
        ActivityType.GapDetected => "\uE946",
        ActivityType.GapRepaired => "\uE73E",
        ActivityType.IntegrityError => "\uE783",
        ActivityType.StorageEvent => "\uE8B7",
        ActivityType.ArchiveCreated => "\uE8F1",
        ActivityType.ArchiveVerified => "\uE73E",
        ActivityType.CompressionCompleted => "\uE8AA",
        ActivityType.ExportStarted => "\uEDE1",
        ActivityType.ExportCompleted => "\uE73E",
        ActivityType.ExportFailed => "\uE783",
        ActivityType.SystemStarted => "\uE7F4",
        ActivityType.SystemStopped => "\uE7F5",
        ActivityType.ConfigurationChanged => "\uE713",
        ActivityType.Error => "\uE783",
        ActivityType.Warning => "\uE7BA",
        ActivityType.Info => "\uE946",
        _ => "\uE946"
    };

    /// <summary>
    /// Gets the color category for this activity type.
    /// </summary>
    public string ColorCategory => Type switch
    {
        ActivityType.CollectorStarted or ActivityType.CollectorResumed or ActivityType.ProviderConnected
            or ActivityType.SymbolAdded or ActivityType.SymbolSubscribed or ActivityType.BackfillCompleted
            or ActivityType.GapRepaired or ActivityType.ArchiveVerified or ActivityType.ExportCompleted
            or ActivityType.SystemStarted => "Success",

        ActivityType.CollectorStopped or ActivityType.CollectorPaused or ActivityType.ProviderDisconnected
            or ActivityType.SymbolRemoved or ActivityType.SymbolUnsubscribed or ActivityType.SystemStopped => "Neutral",

        ActivityType.ProviderError or ActivityType.BackfillFailed or ActivityType.IntegrityError
            or ActivityType.ExportFailed or ActivityType.Error => "Error",

        ActivityType.DataQualityIssue or ActivityType.GapDetected or ActivityType.Warning => "Warning",

        _ => "Info"
    };

    /// <summary>
    /// Gets the relative time string (e.g., "5 minutes ago").
    /// </summary>
    public string RelativeTime
    {
        get
        {
            var diff = DateTime.UtcNow - Timestamp;
            if (diff.TotalSeconds < 60)
                return "Just now";
            if (diff.TotalMinutes < 60)
                return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24)
                return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7)
                return $"{(int)diff.TotalDays}d ago";
            return Timestamp.ToString("MMM d");
        }
    }
}

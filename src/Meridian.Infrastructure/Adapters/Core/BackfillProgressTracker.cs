using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Tracks per-symbol backfill progress and relays provider-attempt observations through a bounded,
/// drop-oldest notification channel. Snapshot updates happen synchronously and cheaply; slow event
/// subscribers never block provider fetches.
/// </summary>
public sealed class BackfillProgressTracker : IDisposable
{
    private const int DefaultNotificationCapacity = 256;
    private const int DefaultHistoryCapacity = 128;

    private readonly ConcurrentDictionary<string, SymbolProgress> _progress = new(StringComparer.OrdinalIgnoreCase);
    private readonly Channel<ProviderBackfillProgress> _notifications;
    private readonly CancellationTokenSource _notificationCts = new();
    private readonly Task _notificationTask;
    private readonly object _historyLock = new();
    private readonly Queue<ProviderBackfillProgress> _recentProviderAttempts = new();
    private readonly int _historyCapacity;
    private long _droppedNotifications;
    private int _disposed;

    public BackfillProgressTracker(
        int notificationCapacity = DefaultNotificationCapacity,
        int historyCapacity = DefaultHistoryCapacity)
    {
        if (notificationCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(notificationCapacity));
        if (historyCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(historyCapacity));

        _historyCapacity = historyCapacity;
        _notifications = Channel.CreateBounded<ProviderBackfillProgress>(
            new BoundedChannelOptions(notificationCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.DropOldest
            },
            _ => Interlocked.Increment(ref _droppedNotifications));
        _notificationTask = Task.Run(() => DispatchNotificationsAsync(_notificationCts.Token));
    }

    /// <summary>
    /// Raised from the tracker notification worker. A slow subscriber may cause older queued
    /// notifications to be dropped, but it cannot delay provider requests or snapshot updates.
    /// </summary>
    public event Action<ProviderBackfillProgress>? ProgressPublished;

    /// <summary>
    /// Registers a symbol's requested range for tracking.
    /// </summary>
    public void RegisterSymbol(string symbol, DateOnly? fromDate, DateOnly? toDate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        var totalDays = fromDate.HasValue && toDate.HasValue && toDate.Value >= fromDate.Value
            ? toDate.Value.DayNumber - fromDate.Value.DayNumber + 1
            : 0;
        _progress[symbol] = new SymbolProgress(
            fromDate ?? DateOnly.MinValue,
            toDate ?? DateOnly.MinValue,
            totalDays);
    }

    /// <summary>
    /// Records completion units for a symbol. Existing callers use bars or completed request
    /// slices; the value is clamped only when the registered range supplies a finite total.
    /// </summary>
    public void RecordProgress(string symbol, int barsCompleted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        if (barsCompleted < 0)
            throw new ArgumentOutOfRangeException(nameof(barsCompleted));

        _progress.AddOrUpdate(
            symbol,
            _ => new SymbolProgress(DateOnly.MinValue, DateOnly.MinValue, 0)
            {
                CompletedDays = barsCompleted,
                LastUpdatedAt = DateTimeOffset.UtcNow
            },
            (_, existing) => existing with
            {
                CompletedDays = existing.TotalDays > 0
                    ? Math.Min(existing.TotalDays, existing.CompletedDays + barsCompleted)
                    : existing.CompletedDays + barsCompleted,
                LastUpdatedAt = DateTimeOffset.UtcNow
            });
    }

    /// <summary>
    /// Updates the current provider-attempt snapshot and queues a non-blocking subscriber
    /// notification. Returns false only after the tracker has begun disposal.
    /// </summary>
    public bool Publish(ProviderBackfillProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (Volatile.Read(ref _disposed) != 0)
            return false;

        RecordProviderProgress(progress);
        return _notifications.Writer.TryWrite(progress);
    }

    /// <summary>Marks a symbol as completed.</summary>
    public void MarkCompleted(string symbol)
    {
        UpdateIfPresent(symbol, existing => existing with
        {
            CompletedDays = existing.TotalDays,
            IsCompleted = true,
            IsFailed = false,
            IsSkipped = false,
            CurrentStatus = "completed",
            Error = null,
            LastUpdatedAt = DateTimeOffset.UtcNow
        });
    }

    /// <summary>Marks a symbol as skipped because its requested range was already covered.</summary>
    public void MarkSkipped(string symbol)
    {
        UpdateIfPresent(symbol, existing => existing with
        {
            CompletedDays = existing.TotalDays,
            IsCompleted = true,
            IsFailed = false,
            IsSkipped = true,
            CurrentStatus = "skipped",
            Error = null,
            LastUpdatedAt = DateTimeOffset.UtcNow
        });
    }

    /// <summary>Marks a symbol as failed.</summary>
    public void MarkFailed(string symbol, string? error)
    {
        UpdateIfPresent(symbol, existing => existing with
        {
            Error = error,
            IsFailed = true,
            IsCompleted = false,
            IsSkipped = false,
            CurrentStatus = "failed",
            LastUpdatedAt = DateTimeOffset.UtcNow
        });
    }

    /// <summary>Gets a coherent snapshot for API and SSE consumers.</summary>
    public BackfillProgressSnapshot GetSnapshot()
    {
        var items = new Dictionary<string, BackfillSymbolProgress>(StringComparer.OrdinalIgnoreCase);
        var totalDays = 0;
        var completedDays = 0;

        foreach (var (symbol, state) in _progress)
        {
            var pct = state.TotalDays > 0
                ? (double)state.CompletedDays / state.TotalDays * 100.0
                : state.IsCompleted ? 100.0 : 0.0;

            items[symbol] = new BackfillSymbolProgress(
                Symbol: symbol,
                FromDate: state.FromDate,
                ToDate: state.ToDate,
                TotalDays: state.TotalDays,
                CompletedDays: state.CompletedDays,
                PercentComplete: Math.Clamp(pct, 0.0, 100.0),
                IsCompleted: state.IsCompleted,
                IsFailed: state.IsFailed,
                Error: state.Error,
                CurrentProvider: state.CurrentProvider,
                CurrentStatus: state.CurrentStatus,
                ProviderAttempt: state.ProviderAttempt,
                RetryRound: state.RetryRound,
                AttemptStartedAt: state.AttemptStartedAt,
                LastUpdatedAt: state.LastUpdatedAt,
                Operation: state.Operation,
                IsSkipped: state.IsSkipped);

            totalDays += state.TotalDays;
            completedDays += state.CompletedDays;
        }

        ProviderBackfillProgress[] recent;
        lock (_historyLock)
        {
            recent = _recentProviderAttempts.ToArray();
        }

        var overallPct = totalDays > 0
            ? (double)completedDays / totalDays * 100.0
            : items.Count > 0 && items.Values.All(static item => item.IsCompleted) ? 100.0 : 0.0;

        return new BackfillProgressSnapshot(
            Symbols: items,
            OverallPercentComplete: Math.Clamp(overallPct, 0.0, 100.0),
            TotalSymbols: items.Count,
            CompletedSymbols: items.Count(static item => item.Value.IsCompleted),
            FailedSymbols: items.Count(static item => item.Value.IsFailed),
            Timestamp: DateTimeOffset.UtcNow,
            RecentProviderAttempts: recent,
            DroppedProviderNotifications: Interlocked.Read(ref _droppedNotifications));
    }

    /// <summary>Clears progress and bounded provider-attempt history.</summary>
    public void Clear()
    {
        _progress.Clear();
        lock (_historyLock)
        {
            _recentProviderAttempts.Clear();
        }
        Interlocked.Exchange(ref _droppedNotifications, 0);
    }

    private void RecordProviderProgress(ProviderBackfillProgress progress)
    {
        var observedAt = progress.ObservedAt ?? DateTimeOffset.UtcNow;
        _progress.AddOrUpdate(
            progress.Symbol,
            _ => new SymbolProgress(
                progress.RangeStart ?? DateOnly.MinValue,
                progress.RangeEnd ?? DateOnly.MinValue,
                CalculateTotalDays(progress.RangeStart, progress.RangeEnd))
            {
                CurrentProvider = progress.Provider,
                CurrentStatus = progress.CurrentStatus,
                ProviderAttempt = progress.ProviderAttempt,
                RetryRound = progress.RetryRound,
                AttemptStartedAt = progress.StartedAt,
                LastUpdatedAt = observedAt,
                Operation = progress.Operation,
                Error = progress.Error,
                IsFailed = string.Equals(progress.CurrentStatus, "all-providers-failed", StringComparison.OrdinalIgnoreCase)
            },
            (_, existing) => existing with
            {
                FromDate = existing.FromDate == DateOnly.MinValue && progress.RangeStart.HasValue
                    ? progress.RangeStart.Value
                    : existing.FromDate,
                ToDate = existing.ToDate == DateOnly.MinValue && progress.RangeEnd.HasValue
                    ? progress.RangeEnd.Value
                    : existing.ToDate,
                TotalDays = existing.TotalDays == 0
                    ? CalculateTotalDays(progress.RangeStart, progress.RangeEnd)
                    : existing.TotalDays,
                // BarsDownloaded describes the provider attempt, not completed calendar days.
                // Keep range progress honest until the coordinator records completion units or
                // marks the symbol terminal; the bounded attempt history carries the bar count.
                CompletedDays = existing.CompletedDays,
                CurrentProvider = progress.Provider,
                CurrentStatus = progress.CurrentStatus,
                ProviderAttempt = progress.ProviderAttempt,
                RetryRound = progress.RetryRound,
                AttemptStartedAt = progress.StartedAt,
                LastUpdatedAt = observedAt,
                Operation = progress.Operation,
                Error = progress.Error,
                IsFailed = string.Equals(progress.CurrentStatus, "all-providers-failed", StringComparison.OrdinalIgnoreCase)
                    || existing.IsFailed
            });

        lock (_historyLock)
        {
            _recentProviderAttempts.Enqueue(progress);
            while (_recentProviderAttempts.Count > _historyCapacity)
                _recentProviderAttempts.Dequeue();
        }
    }

    private void UpdateIfPresent(string symbol, Func<SymbolProgress, SymbolProgress> update)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        while (_progress.TryGetValue(symbol, out var existing))
        {
            if (_progress.TryUpdate(symbol, update(existing), existing))
                return;
        }
    }

    private async Task DispatchNotificationsAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var progress in _notifications.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                var handlers = ProgressPublished;
                if (handlers is null)
                    continue;

                foreach (Action<ProviderBackfillProgress> handler in handlers.GetInvocationList())
                {
                    try
                    {
                        handler(progress);
                    }
                    catch
                    {
                        // Subscriber isolation is intentional. Owning subscribers should log their
                        // own failure; one broken observer must not stop later observations.
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private static int CalculateTotalDays(DateOnly? fromDate, DateOnly? toDate)
        => fromDate.HasValue && toDate.HasValue && toDate.Value >= fromDate.Value
            ? toDate.Value.DayNumber - fromDate.Value.DayNumber + 1
            : 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _notifications.Writer.TryComplete();
        _notificationCts.Cancel();
        try
        {
            _notificationTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(static inner => inner is OperationCanceledException))
        {
        }

        if (_notificationTask.IsCompleted)
            _notificationCts.Dispose();
    }

    private sealed record SymbolProgress(DateOnly FromDate, DateOnly ToDate, int TotalDays)
    {
        public int CompletedDays { get; init; }
        public bool IsCompleted { get; init; }
        public bool IsFailed { get; init; }
        public bool IsSkipped { get; init; }
        public string? Error { get; init; }
        public string? CurrentProvider { get; init; }
        public string? CurrentStatus { get; init; }
        public int ProviderAttempt { get; init; }
        public int RetryRound { get; init; }
        public DateTimeOffset? AttemptStartedAt { get; init; }
        public DateTimeOffset? LastUpdatedAt { get; init; }
        public string? Operation { get; init; }
    }
}

/// <summary>Snapshot of backfill progress across all symbols.</summary>
public sealed record BackfillProgressSnapshot(
    IReadOnlyDictionary<string, BackfillSymbolProgress> Symbols,
    double OverallPercentComplete,
    int TotalSymbols,
    int CompletedSymbols,
    int FailedSymbols,
    DateTimeOffset Timestamp,
    IReadOnlyList<ProviderBackfillProgress>? RecentProviderAttempts = null,
    long DroppedProviderNotifications = 0);

/// <summary>Progress for a single symbol's backfill operation.</summary>
public sealed record BackfillSymbolProgress(
    string Symbol,
    DateOnly FromDate,
    DateOnly ToDate,
    int TotalDays,
    int CompletedDays,
    double PercentComplete,
    bool IsCompleted,
    bool IsFailed,
    string? Error,
    string? CurrentProvider = null,
    string? CurrentStatus = null,
    int ProviderAttempt = 0,
    int RetryRound = 0,
    DateTimeOffset? AttemptStartedAt = null,
    DateTimeOffset? LastUpdatedAt = null,
    string? Operation = null,
    bool IsSkipped = false);

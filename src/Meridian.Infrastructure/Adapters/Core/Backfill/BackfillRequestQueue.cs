using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using Meridian.Core.Logging;
using Meridian.Core.Pipeline;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Intelligent queue for backfill requests with prioritization, rate-limit awareness,
/// and provider-specific scheduling.
/// </summary>
public sealed class BackfillRequestQueue : IDisposable
{
    private readonly PriorityQueue<BackfillRequest, int> _pendingRequests = new();
    private readonly ConcurrentDictionary<string, int> _activeRequestsByProvider = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _providerCooldowns = new();
    private readonly ConcurrentDictionary<BackfillRequestAttemptToken, BackfillRequest> _inFlightRequests = new();
    private readonly Channel<BackfillRequest> _completedChannel;
    private readonly ProviderRateLimitTracker _rateLimitTracker;
    private readonly SemaphoreSlim _queueLock = new(1, 1);
    private readonly ILogger _log;
    private long _nextAttemptId;
    private int _pendingCount;
    private bool _disposed;

    /// <summary>
    /// Maximum concurrent requests across all providers.
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 3;

    /// <summary>
    /// Maximum concurrent requests per provider.
    /// </summary>
    public int MaxConcurrentPerProvider { get; set; } = 2;

    /// <summary>
    /// Event raised when a request is ready to be processed.
    /// </summary>
#pragma warning disable CS0067 // Event is never used - Reserved for future extensibility
    public event Func<BackfillRequest, Task>? OnRequestReady;
#pragma warning restore CS0067

    /// <summary>
    /// Event raised when queue state changes.
    /// </summary>
    public event Action<QueueStateChangedEventArgs>? OnQueueStateChanged;

    public int PendingCount => Volatile.Read(ref _pendingCount);
    public int InFlightCount => _inFlightRequests.Count;
    public int TotalCount => PendingCount + InFlightCount;
    public bool IsEmpty => TotalCount == 0;

    public BackfillRequestQueue(ProviderRateLimitTracker rateLimitTracker, ILogger? log = null)
    {
        _rateLimitTracker = rateLimitTracker;
        _log = log ?? LoggingSetup.ForContext<BackfillRequestQueue>();
        // Use EventPipelinePolicy for consistent backpressure settings across the application.
        // CompletionQueue preset: bounded (500 capacity), Wait mode (no drops), metrics disabled.
        _completedChannel = EventPipelinePolicy.CompletionQueue.CreateChannel<BackfillRequest>(
            singleReader: true, singleWriter: false);
    }

    /// <summary>
    /// Enqueue a batch of requests from a backfill job.
    /// </summary>
    public async Task<IReadOnlyList<BackfillRequest>> EnqueueJobRequestsAsync(
        BackfillJob job,
        GapAnalysisResult gapAnalysis,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(gapAnalysis);

        var stagedRequests = new List<BackfillRequest>();
        var stagedProgress = new List<(string Symbol, int TotalRequests, List<DateOnly> DatesToFill)>();

        // Build the whole batch before taking queue ownership. Cancellation can therefore
        // abandon an incomplete batch without leaving partially admitted requests behind.
        foreach (var (symbol, gaps) in gapAnalysis.SymbolGaps)
        {
            ct.ThrowIfCancellationRequested();
            if (!gaps.HasGaps)
                continue;

            var ranges = gaps.GetGapRanges(job.Options.BatchSizeDays);
            foreach (var (from, to) in ranges)
            {
                ct.ThrowIfCancellationRequested();
                stagedRequests.Add(new BackfillRequest
                {
                    JobId = job.JobId,
                    Symbol = symbol,
                    FromDate = from,
                    ToDate = to,
                    Granularity = job.Granularity,
                    PreferredProviders = job.PreferredProviders.ToList(),
                    Priority = CalculatePriority(job, symbol, from),
                    MaxRetries = job.Options.MaxRetries,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }

            stagedProgress.Add((symbol, ranges.Count, [.. gaps.GapDates]));
        }

        await _queueLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // This is the commit point. There are no awaits or cancellation observations
            // between this check and returning the complete admitted batch.
            ct.ThrowIfCancellationRequested();
            foreach (var request in stagedRequests)
            {
                _pendingRequests.Enqueue(request, request.Priority);
            }
            _pendingCount += stagedRequests.Count;

            foreach (var (symbol, totalRequests, datesToFill) in stagedProgress)
            {
                // Update job progress tracking
                if (job.SymbolProgress.TryGetValue(symbol, out var progress))
                {
                    progress.TotalRequests = totalRequests;
                    progress.DatesToFill = datesToFill;
                }
                else
                {
                    job.SymbolProgress[symbol] = new SymbolBackfillProgress
                    {
                        Symbol = symbol,
                        TotalRequests = totalRequests,
                        DatesToFill = datesToFill
                    };
                }
            }

            _log.Information("Enqueued {RequestCount} requests for job {JobId} ({Symbols} symbols)",
                stagedRequests.Count, job.JobId, gapAnalysis.SymbolsWithGaps);

            NotifyQueueStateChanged();
            return stagedRequests;
        }
        finally
        {
            _queueLock.Release();
        }
    }

    /// <summary>
    /// Rolls back one not-yet-persisted batch from the pending queue. This is intentionally
    /// silent: a cancelled job start never committed ownership of these requests and must not
    /// publish false terminal completions.
    /// </summary>
    internal async Task RollbackPendingRequestsAsync(
        IReadOnlyCollection<BackfillRequest> requests,
        CancellationToken ct = default)
    {
        if (requests.Count == 0)
            return;

        var requestSet = new HashSet<BackfillRequest>(
            requests,
            ReferenceEqualityComparer.Instance);

        await _queueLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            FilterPendingRequests(requestSet.Contains, removeMatching: true);
            NotifyQueueStateChanged();
        }
        finally
        {
            _queueLock.Release();
        }
    }

    /// <summary>
    /// Reports whether the queue still owns any request from an exact reference-identity batch.
    /// </summary>
    internal async Task<bool> ContainsAnyRequestsAsync(
        IReadOnlyCollection<BackfillRequest> requests,
        CancellationToken ct = default)
    {
        if (requests.Count == 0)
            return false;

        var requestSet = new HashSet<BackfillRequest>(
            requests,
            ReferenceEqualityComparer.Instance);

        await _queueLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _pendingRequests.UnorderedItems.Any(
                       item => requestSet.Contains(item.Element)) ||
                   _inFlightRequests.Values.Any(requestSet.Contains);
        }
        finally
        {
            _queueLock.Release();
        }
    }

    /// <summary>
    /// Enqueue a single request.
    /// </summary>
    public async Task EnqueueAsync(BackfillRequest request, CancellationToken ct = default)
    {
        await _queueLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _pendingRequests.Enqueue(request, request.Priority);
            _pendingCount++;
            NotifyQueueStateChanged();
        }
        finally
        {
            _queueLock.Release();
        }
    }

    /// <summary>
    /// Try to get the next request that can be processed (respecting rate limits and concurrency).
    /// </summary>
    public async Task<BackfillRequestAttempt?> TryDequeueAsync(CancellationToken ct = default)
    {
        await _queueLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_pendingRequests.Count == 0)
                return null;

            // Check global concurrency limit
            if (_inFlightRequests.Count >= MaxConcurrentRequests)
            {
                _log.Debug("Global concurrency limit reached ({Count}/{Max})",
                    _inFlightRequests.Count, MaxConcurrentRequests);
                return null;
            }

            // Find a request that can be processed
            var skipped = new List<(BackfillRequest Request, int Priority)>();
            BackfillRequest? selected = null;

            while (_pendingRequests.TryDequeue(out var request, out var priority))
            {
                var canProcess = await CanProcessRequestAsync(request, ct).ConfigureAwait(false);

                if (canProcess)
                {
                    selected = request;
                    break;
                }

                skipped.Add((request, priority));
            }

            // Re-enqueue skipped requests
            foreach (var (req, pri) in skipped)
            {
                _pendingRequests.Enqueue(req, pri);
            }

            if (selected != null)
            {
                _pendingCount--;
                selected.Status = BackfillRequestStatus.InProgress;
                selected.StartedAt = DateTimeOffset.UtcNow;
                var attemptToken = new BackfillRequestAttemptToken(
                    Interlocked.Increment(ref _nextAttemptId));
                _inFlightRequests[attemptToken] = selected;

                // Track active requests per provider
                var provider = selected.AssignedProvider ?? "unknown";
                _activeRequestsByProvider.AddOrUpdate(provider, 1, (_, count) => count + 1);

                NotifyQueueStateChanged();
                return new BackfillRequestAttempt(selected, attemptToken);
            }

            return null;
        }
        finally
        {
            _queueLock.Release();
        }
    }

    /// <summary>
    /// Check if a request can be processed (rate limits, cooldowns, concurrency).
    /// </summary>
    private Task<bool> CanProcessRequestAsync(BackfillRequest request, CancellationToken ct)
    {
        // Get available providers for this request
        var providers = request.PreferredProviders.Count > 0
            ? request.PreferredProviders
            : (IList<string>)["alpaca", "yahoo", "stooq", "nasdaq"];

        foreach (var provider in providers)
        {
            // Check per-provider concurrency
            var activeForProvider = _activeRequestsByProvider.GetValueOrDefault(provider, 0);
            if (activeForProvider >= MaxConcurrentPerProvider)
                continue;

            // Check provider cooldown
            if (_providerCooldowns.TryGetValue(provider, out var cooldownUntil))
            {
                if (DateTimeOffset.UtcNow < cooldownUntil)
                    continue;
                _providerCooldowns.TryRemove(provider, out _);
            }

            // Check rate limit
            if (_rateLimitTracker.IsRateLimited(provider))
                continue;

            if (_rateLimitTracker.IsApproachingLimit(provider, 0.95))
                continue;

            // This provider can handle the request
            request.AssignedProvider = provider;
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// Mark a request as completed (success or failure).
    /// </summary>
    public Task CompleteRequestAsync(
        BackfillRequest request,
        BackfillRequestAttemptToken attemptToken,
        bool success,
        string? error = null,
        CancellationToken ct = default)
        => CompleteRequestAttemptAsync(request, attemptToken, success, error, ct);

    internal async Task CompleteRequestAttemptAsync(
        BackfillRequest request,
        BackfillRequestAttemptToken attemptToken,
        bool success,
        string? error = null,
        CancellationToken ct = default)
    {
        var transitioned = false;

        await _queueLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!TryRemoveCurrentAttempt(request, attemptToken))
            {
                _log.Warning(
                    "Ignored stale completion for backfill request {RequestId}, attempt {AttemptToken}",
                    request.RequestId,
                    attemptToken.Value);
                return;
            }

            var provider = request.AssignedProvider ?? "unknown";
            _activeRequestsByProvider.AddOrUpdate(provider, 0, (_, count) => Math.Max(0, count - 1));

            request.CompletedAt = DateTimeOffset.UtcNow;
            request.Status = success ? BackfillRequestStatus.Completed : BackfillRequestStatus.Failed;
            request.ErrorMessage = error;

            if (success)
            {
                _log.Debug("Request completed: {Symbol} {From}-{To} via {Provider}",
                    request.Symbol, request.FromDate, request.ToDate, provider);
            }
            else
            {
                _log.Warning("Request failed: {Symbol} {From}-{To} via {Provider}: {Error}",
                    request.Symbol, request.FromDate, request.ToDate, provider, error);

                // Check if we should retry
                if (request.RetryCount < request.MaxRetries && IsRetryableError(error))
                {
                    request.RetryCount++;
                    request.Status = BackfillRequestStatus.Pending;
                    request.AssignedProvider = null;
                    request.Priority += 10; // Lower priority on retry

                    _pendingRequests.Enqueue(request, request.Priority);
                    _pendingCount++;
                    _log.Information("Requeued request for retry ({Retry}/{Max}): {Symbol}",
                        request.RetryCount, request.MaxRetries, request.Symbol);
                }
            }

            transitioned = true;
            NotifyQueueStateChanged();
        }
        finally
        {
            _queueLock.Release();
        }

        if (transitioned)
            await PublishCompletionAsync(request).ConfigureAwait(false);
    }

    /// <summary>
    /// Atomically releases ownership of the current in-flight attempt and records cancellation.
    /// A stale token cannot remove a newer attempt of the same request object.
    /// </summary>
    public Task<bool> CancelInFlightRequestAsync(
        BackfillRequest request,
        BackfillRequestAttemptToken attemptToken,
        string reason,
        CancellationToken ct = default)
        => CancelInFlightAttemptAsync(request, attemptToken, reason, ct);

    internal async Task<bool> CancelInFlightAttemptAsync(
        BackfillRequest request,
        BackfillRequestAttemptToken attemptToken,
        string reason,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        await _queueLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!TryRemoveCurrentAttempt(request, attemptToken))
                return false;

            var provider = request.AssignedProvider ?? "unknown";
            _activeRequestsByProvider.AddOrUpdate(provider, 0, (_, count) => Math.Max(0, count - 1));

            request.CompletedAt = DateTimeOffset.UtcNow;
            request.Status = BackfillRequestStatus.Cancelled;
            request.ErrorMessage = reason;
            NotifyQueueStateChanged();
        }
        finally
        {
            _queueLock.Release();
        }

        await PublishCompletionAsync(request).ConfigureAwait(false);
        return true;
    }

    internal async Task<bool> RequeueInFlightAttemptAsync(
        BackfillRequest request,
        BackfillRequestAttemptToken attemptToken,
        string reason,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        await _queueLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!TryRemoveCurrentAttempt(request, attemptToken))
                return false;

            var provider = request.AssignedProvider ?? "unknown";
            _activeRequestsByProvider.AddOrUpdate(provider, 0, (_, count) => Math.Max(0, count - 1));

            request.Status = BackfillRequestStatus.Pending;
            request.AssignedProvider = null;
            request.StartedAt = null;
            request.ErrorMessage = reason;
            _pendingRequests.Enqueue(request, request.Priority);
            _pendingCount++;
            NotifyQueueStateChanged();
            return true;
        }
        finally
        {
            _queueLock.Release();
        }
    }

    private bool TryRemoveCurrentAttempt(
        BackfillRequest request,
        BackfillRequestAttemptToken attemptToken)
    {
        if (attemptToken.Value <= 0 ||
            !_inFlightRequests.TryGetValue(attemptToken, out var current) ||
            !ReferenceEquals(current, request))
        {
            return false;
        }

        return _inFlightRequests.TryRemove(attemptToken, out _);
    }

    private async Task PublishCompletionAsync(BackfillRequest request)
    {
        // Terminal queue ownership commits before publication. Once committed, cancellation of
        // the initiating call must not silently drop the corresponding completion notification.
        await _completedChannel.Writer.WriteAsync(request, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Record that a provider hit a rate limit.
    /// </summary>
    public void RecordProviderRateLimitHit(string provider, TimeSpan? cooldown = null)
    {
        var cooldownDuration = cooldown ?? TimeSpan.FromMinutes(1);
        _providerCooldowns[provider] = DateTimeOffset.UtcNow + cooldownDuration;
        _rateLimitTracker.RecordRateLimitHit(provider, cooldown);

        _log.Information("Provider {Provider} rate-limited, cooling down for {Duration}",
            provider, cooldownDuration);
    }

    /// <summary>
    /// Get all pending requests for a specific job.
    /// </summary>
    public async Task<List<BackfillRequest>> GetJobRequestsAsync(string jobId, CancellationToken ct = default)
    {
        await _queueLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var matchingPending = FilterPendingRequests(r => r.JobId == jobId);
            var matchingInFlight = _inFlightRequests.Values.Where(r => r.JobId == jobId);
            return [.. matchingPending, .. matchingInFlight];
        }
        finally
        {
            _queueLock.Release();
        }
    }

    /// <summary>
    /// Cancel all pending requests for a specific job.
    /// </summary>
    public async Task CancelJobRequestsAsync(string jobId, CancellationToken ct = default)
    {
        List<BackfillRequest> cancelled;
        await _queueLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            cancelled = FilterPendingRequests(r => r.JobId == jobId, removeMatching: true);
            foreach (var request in cancelled)
            {
                request.Status = BackfillRequestStatus.Cancelled;
                request.CompletedAt = DateTimeOffset.UtcNow;
                request.ErrorMessage = "Cancelled with the owning backfill job.";
            }

            _log.Information("Cancelled pending requests for job {JobId}", jobId);
            NotifyQueueStateChanged();
        }
        finally
        {
            _queueLock.Release();
        }

        foreach (var request in cancelled)
            await PublishCompletionAsync(request).ConfigureAwait(false);
    }

    /// <summary>
    /// Filters pending requests, optionally keeping only those that match the predicate.
    /// </summary>
    private List<BackfillRequest> FilterPendingRequests(Func<BackfillRequest, bool> predicate, bool removeMatching = false)
    {
        var matching = new List<BackfillRequest>();
        var tempQueue = new PriorityQueue<BackfillRequest, int>();

        while (_pendingRequests.TryDequeue(out var req, out var pri))
        {
            var isMatch = predicate(req);
            if (isMatch)
                matching.Add(req);

            if (!removeMatching || !isMatch)
                tempQueue.Enqueue(req, pri);
        }

        while (tempQueue.TryDequeue(out var req, out var pri))
            _pendingRequests.Enqueue(req, pri);

        if (removeMatching)
            _pendingCount -= matching.Count;

        return matching;
    }

    /// <summary>
    /// Get the channel reader for completed requests.
    /// </summary>
    public ChannelReader<BackfillRequest> CompletedRequests => _completedChannel.Reader;

    /// <summary>
    /// Closes completion publication after every admitted producer has quiesced. The reader can
    /// then drain the bounded channel to completion without losing terminal notifications.
    /// </summary>
    internal void CompleteCompletionNotifications()
        => _completedChannel.Writer.TryComplete();

    /// <summary>
    /// Get queue statistics.
    /// </summary>
    public QueueStatistics GetStatistics()
    {
        return new QueueStatistics
        {
            PendingRequests = PendingCount,
            InFlightRequests = InFlightCount,
            ActiveByProvider = new Dictionary<string, int>(_activeRequestsByProvider),
            CooldownsByProvider = _providerCooldowns.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value - DateTimeOffset.UtcNow
            ).Where(kvp => kvp.Value > TimeSpan.Zero)
             .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
        };
    }

    /// <summary>
    /// Calculate priority for a request (lower = higher priority).
    /// </summary>
    private int CalculatePriority(BackfillJob job, string symbol, DateOnly date)
    {
        var basePriority = job.Options.Priority;

        // More recent dates get higher priority
        var daysAgo = DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - date.DayNumber;
        var recencyBonus = Math.Min(50, daysAgo / 30); // Up to 50 for older data

        // Symbols that failed previously get lower priority
        if (job.SymbolProgress.TryGetValue(symbol, out var progress) && progress.FailedRequests > 0)
        {
            basePriority += progress.FailedRequests * 5;
        }

        return basePriority + recencyBonus;
    }

    private static bool IsRetryableError(string? error)
    {
        if (string.IsNullOrEmpty(error))
            return true;

        // Non-retryable errors
        ReadOnlySpan<string> nonRetryable =
        [
            "not found", "404",
            "invalid symbol",
            "authentication failed", "403",
            "unauthorized", "401"
        ];

        foreach (var e in nonRetryable)
            if (error.Contains(e, StringComparison.OrdinalIgnoreCase))
                return false;

        return true;
    }

    private void NotifyQueueStateChanged()
    {
        var handlers = OnQueueStateChanged;
        if (handlers is null)
            return;

        var notification = new QueueStateChangedEventArgs
        {
            PendingCount = PendingCount,
            InFlightCount = InFlightCount,
            Timestamp = DateTimeOffset.UtcNow
        };

        foreach (var handler in handlers.GetInvocationList().Cast<Action<QueueStateChangedEventArgs>>())
        {
            try
            {
                handler(notification);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Backfill queue-state observer failed");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _queueLock.Dispose();
        _completedChannel.Writer.TryComplete();
    }
}

/// <summary>
/// Represents a single backfill request.
/// </summary>
public sealed class BackfillRequest
{
    public string RequestId { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public string JobId { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public DateOnly FromDate { get; init; }
    public DateOnly ToDate { get; init; }
    public DataGranularity Granularity { get; init; } = DataGranularity.Daily;
    public List<string> PreferredProviders { get; init; } = [];
    public string? AssignedProvider { get; set; }
    public int Priority { get; set; } = 10;
    public int MaxRetries { get; init; } = 3;
    public int RetryCount { get; set; }
    public BackfillRequestStatus Status { get; set; } = BackfillRequestStatus.Pending;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public int BarsRetrieved { get; set; }
}

/// <summary>
/// One immutable dequeue lease. A request can be requeued, but an earlier lease retains only its
/// original token and cannot discover or act on the replacement attempt.
/// </summary>
public readonly record struct BackfillRequestAttempt
{
    internal BackfillRequestAttempt(
        BackfillRequest request,
        BackfillRequestAttemptToken token)
    {
        Request = request;
        Token = token;
    }

    public BackfillRequest Request { get; }
    public BackfillRequestAttemptToken Token { get; }
}

/// <summary>
/// Opaque queue-issued capability for one dequeue attempt.
/// </summary>
public readonly record struct BackfillRequestAttemptToken
{
    internal BackfillRequestAttemptToken(long value)
    {
        Value = value;
    }

    internal long Value { get; }
}

/// <summary>
/// Status of a backfill request.
/// </summary>
public enum BackfillRequestStatus : byte
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Queue statistics.
/// </summary>
public sealed class QueueStatistics
{
    public int PendingRequests { get; init; }
    public int InFlightRequests { get; init; }
    public Dictionary<string, int> ActiveByProvider { get; init; } = [];
    public Dictionary<string, TimeSpan> CooldownsByProvider { get; init; } = [];
}

/// <summary>
/// Event args for queue state changes.
/// </summary>
public sealed class QueueStateChangedEventArgs
{
    public int PendingCount { get; init; }
    public int InFlightCount { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

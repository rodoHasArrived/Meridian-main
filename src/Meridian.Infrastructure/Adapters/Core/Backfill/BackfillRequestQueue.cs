using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using Meridian.Application.Logging;
using Meridian.Application.Pipeline;
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
    private readonly ConcurrentDictionary<string, BackfillRequest> _inFlightRequests = new();
    private readonly Channel<BackfillRequest> _completedChannel;
    private readonly ProviderRateLimitTracker _rateLimitTracker;
    private readonly SemaphoreSlim _queueLock = new(1, 1);
    private readonly ILogger _log;
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

    public int PendingCount => _pendingRequests.Count;
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
    public async Task EnqueueJobRequestsAsync(BackfillJob job, GapAnalysisResult gapAnalysis, CancellationToken ct = default)
    {
        await _queueLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var (symbol, gaps) in gapAnalysis.SymbolGaps)
            {
                if (!gaps.HasGaps)
                    continue;

                // Get consolidated date ranges to minimize requests
                var ranges = gaps.GetGapRanges(job.Options.BatchSizeDays);

                foreach (var (from, to) in ranges)
                {
                    var request = new BackfillRequest
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
                    };

                    _pendingRequests.Enqueue(request, request.Priority);
                }

                // Update job progress tracking
                if (job.SymbolProgress.TryGetValue(symbol, out var progress))
                {
                    progress.TotalRequests = ranges.Count;
                    progress.DatesToFill = gaps.GapDates;
                }
                else
                {
                    job.SymbolProgress[symbol] = new SymbolBackfillProgress
                    {
                        Symbol = symbol,
                        TotalRequests = ranges.Count,
                        DatesToFill = gaps.GapDates
                    };
                }
            }

            _log.Information("Enqueued {RequestCount} requests for job {JobId} ({Symbols} symbols)",
                _pendingRequests.Count, job.JobId, gapAnalysis.SymbolsWithGaps);

            NotifyQueueStateChanged();
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
    public async Task<BackfillRequest?> TryDequeueAsync(CancellationToken ct = default)
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
                selected.Status = BackfillRequestStatus.InProgress;
                selected.StartedAt = DateTimeOffset.UtcNow;
                _inFlightRequests[selected.RequestId] = selected;

                // Track active requests per provider
                var provider = selected.AssignedProvider ?? "unknown";
                _activeRequestsByProvider.AddOrUpdate(provider, 1, (_, count) => count + 1);

                NotifyQueueStateChanged();
            }

            return selected;
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
    public async Task CompleteRequestAsync(BackfillRequest request, bool success, string? error = null, CancellationToken ct = default)
    {
        await _queueLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _inFlightRequests.TryRemove(request.RequestId, out _);

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
                    _log.Information("Requeued request for retry ({Retry}/{Max}): {Symbol}",
                        request.RetryCount, request.MaxRetries, request.Symbol);
                }
            }

            await _completedChannel.Writer.WriteAsync(request, ct).ConfigureAwait(false);
            NotifyQueueStateChanged();
        }
        finally
        {
            _queueLock.Release();
        }
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
        await _queueLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            FilterPendingRequests(r => r.JobId != jobId, removeMatching: true);
            _log.Information("Cancelled pending requests for job {JobId}", jobId);
            NotifyQueueStateChanged();
        }
        finally
        {
            _queueLock.Release();
        }
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
            if (predicate(req))
            {
                if (!removeMatching)
                    tempQueue.Enqueue(req, pri);
                matching.Add(req);
            }
            else
            {
                if (removeMatching)
                    tempQueue.Enqueue(req, pri);
            }
        }

        while (tempQueue.TryDequeue(out var req, out var pri))
            _pendingRequests.Enqueue(req, pri);

        return matching;
    }

    /// <summary>
    /// Get the channel reader for completed requests.
    /// </summary>
    public ChannelReader<BackfillRequest> CompletedRequests => _completedChannel.Reader;

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
        OnQueueStateChanged?.Invoke(new QueueStateChangedEventArgs
        {
            PendingCount = PendingCount,
            InFlightCount = InFlightCount,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _queueLock.Dispose();
        _completedChannel.Writer.Complete();
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

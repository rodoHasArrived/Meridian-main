using System.Collections.Concurrent;
using System.Threading;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Priority level for backfill jobs.
/// </summary>
public enum BackfillPriority : byte
{
    /// <summary>System-critical gaps (highest priority).</summary>
    Critical = 0,

    /// <summary>User-requested immediate backfill.</summary>
    High = 10,

    /// <summary>Standard backfill operations.</summary>
    Normal = 50,

    /// <summary>Background fill when idle.</summary>
    Low = 100,

    /// <summary>Fill when no other work is pending.</summary>
    Deferred = 200
}

/// <summary>
/// Request for creating a new backfill job.
/// </summary>
public sealed record BackfillJobRequest
{
    public required string Symbol { get; init; }
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }
    public DataGranularity Granularity { get; init; } = DataGranularity.Daily;
    public BackfillPriority Priority { get; init; } = BackfillPriority.Normal;
    public string[]? PreferredProviders { get; init; }
    public bool FillGapsOnly { get; init; } = true;
    public bool ValidateAfterComplete { get; init; } = true;
    public string[]? DependsOnJobIds { get; init; }
    public string? DependsOnSymbol { get; init; }
    public string? RequestedBy { get; init; }
}

/// <summary>
/// Result of enqueueing a batch of backfill jobs.
/// </summary>
public sealed record BatchEnqueueResult(
    IReadOnlyList<BackfillJob> EnqueuedJobs,
    IReadOnlyList<BatchEnqueueError> Errors
)
{
    public int TotalEnqueued => EnqueuedJobs.Count;
    public int TotalFailed => Errors.Count;
    public bool AllSucceeded => Errors.Count == 0;
}

/// <summary>
/// Error information for a failed batch enqueue operation.
/// </summary>
public sealed record BatchEnqueueError(
    string Symbol,
    string ErrorMessage
);

/// <summary>
/// Options for batch enqueueing.
/// </summary>
public sealed record BatchEnqueueOptions
{
    /// <summary>Continue enqueueing if individual jobs fail.</summary>
    public bool ContinueOnError { get; init; } = true;

    /// <summary>Create dependency chain between jobs (each depends on previous).</summary>
    public bool CreateDependencyChain { get; init; } = false;

    /// <summary>Default priority for all jobs in batch.</summary>
    public BackfillPriority DefaultPriority { get; init; } = BackfillPriority.Normal;
}

/// <summary>
/// Statistics about the backfill queue.
/// </summary>
public sealed record BackfillQueueStatistics(
    int TotalJobs,
    int PendingJobs,
    int RunningJobs,
    int CompletedJobs,
    int FailedJobs,
    int PausedJobs,
    Dictionary<BackfillPriority, int> JobsByPriority
);

/// <summary>
/// Event args for job status changes.
/// </summary>
public sealed class JobStatusChangedEventArgs : EventArgs
{
    public required BackfillJob Job { get; init; }
    public required BackfillJobStatus PreviousStatus { get; init; }
    public required BackfillJobStatus CurrentStatus { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Priority-based backfill job queue with dependency management.
/// Thread-safe for concurrent access.
/// </summary>
public sealed class PriorityBackfillQueue : IDisposable
{
    private readonly PriorityQueue<BackfillJob, int> _priorityQueue = new();
    private readonly ConcurrentDictionary<string, BackfillJob> _allJobs = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens = new();
    private readonly SemaphoreSlim _queueLock = new(1, 1);
    private readonly ILogger _log;
    private bool _disposed;

    public event EventHandler<JobStatusChangedEventArgs>? JobStatusChanged;

    public PriorityBackfillQueue(ILogger? log = null)
    {
        _log = log ?? LoggingSetup.ForContext<PriorityBackfillQueue>();
    }

    /// <summary>
    /// Enqueue a new backfill job.
    /// </summary>
    public async Task<BackfillJob> EnqueueAsync(BackfillJobRequest request, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var job = new BackfillJob
        {
            Name = $"{request.Symbol} {request.StartDate:yyyy-MM-dd} to {request.EndDate:yyyy-MM-dd}",
            Symbols = new List<string> { request.Symbol },
            FromDate = request.StartDate,
            ToDate = request.EndDate,
            Granularity = request.Granularity,
            PreferredProviders = request.PreferredProviders?.ToList() ?? new List<string>(),
            Options = new BackfillJobOptions
            {
                Priority = (int)request.Priority,
                FillGapsOnly = request.FillGapsOnly
            }
        };

        await _queueLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _allJobs[job.JobId] = job;
            _cancellationTokens[job.JobId] = new CancellationTokenSource();

            // Check dependencies
            if (HasUnmetDependencies(job, request.DependsOnJobIds))
            {
                job.Status = BackfillJobStatus.Paused;
                job.StatusReason = "Waiting for dependencies";
            }
            else
            {
                job.Status = BackfillJobStatus.Pending;
                _priorityQueue.Enqueue(job, GetPriorityScore(job, request.Priority));
            }

            _log.Information(
                "Enqueued backfill job {JobId} for {Symbol} ({StartDate} to {EndDate}), priority {Priority}",
                job.JobId, request.Symbol, request.StartDate, request.EndDate, request.Priority);

            OnJobStatusChanged(job, BackfillJobStatus.Pending, job.Status);

            return job;
        }
        finally
        {
            _queueLock.Release();
        }
    }

    /// <summary>
    /// Enqueue multiple jobs as a batch.
    /// </summary>
    public async Task<BatchEnqueueResult> EnqueueBatchAsync(
        IEnumerable<BackfillJobRequest> requests,
        BatchEnqueueOptions? options = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        options ??= new BatchEnqueueOptions();

        var results = new List<BackfillJob>();
        var errors = new List<BatchEnqueueError>();
        BackfillJob? previousJob = null;

        foreach (var request in requests)
        {
            try
            {
                // Apply dependency chain if requested
                var effectiveRequest = request;
                if (options.CreateDependencyChain && previousJob != null)
                {
                    effectiveRequest = request with
                    {
                        DependsOnJobIds = new[] { previousJob.JobId }
                    };
                }

                var job = await EnqueueAsync(effectiveRequest, ct).ConfigureAwait(false);
                results.Add(job);
                previousJob = job;
            }
            catch (Exception ex)
            {
                errors.Add(new BatchEnqueueError(request.Symbol, ex.Message));
                if (!options.ContinueOnError)
                    break;
            }
        }

        return new BatchEnqueueResult(results, errors);
    }

    /// <summary>
    /// Dequeue the next job to process.
    /// </summary>
    public async Task<BackfillJob?> DequeueNextAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _queueLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            while (_priorityQueue.TryDequeue(out var job, out _))
            {
                // Skip if cancelled or already running
                if (job.Status is BackfillJobStatus.Cancelled or BackfillJobStatus.Running)
                    continue;

                // Check dependencies again (they might have completed)
                if (job.Status == BackfillJobStatus.Paused && job.StatusReason?.Contains("dependencies") == true)
                {
                    // Dependencies not yet met, re-queue
                    _priorityQueue.Enqueue(job, GetPriorityScore(job, (BackfillPriority)job.Options.Priority) + 1000);
                    continue;
                }

                return job;
            }

            return null;
        }
        finally
        {
            _queueLock.Release();
        }
    }

    /// <summary>
    /// Get the cancellation token for a job.
    /// </summary>
    public CancellationToken GetCancellationToken(string jobId)
    {
        return _cancellationTokens.TryGetValue(jobId, out var cts)
            ? cts.Token
            : CancellationToken.None;
    }

    /// <summary>
    /// Get all jobs matching criteria.
    /// </summary>
    public IReadOnlyList<BackfillJob> GetJobs(Func<BackfillJob, bool>? predicate = null)
    {
        var jobs = _allJobs.Values.AsEnumerable();
        if (predicate != null)
            jobs = jobs.Where(predicate);
        return jobs.OrderBy(j => j.Options.Priority).ThenBy(j => j.CreatedAt).ToList();
    }

    /// <summary>
    /// Get a specific job by ID.
    /// </summary>
    public BackfillJob? GetJob(string jobId)
    {
        return _allJobs.TryGetValue(jobId, out var job) ? job : null;
    }

    /// <summary>
    /// Update job priority.
    /// </summary>
    public bool SetPriority(string jobId, BackfillPriority priority)
    {
        if (!_allJobs.TryGetValue(jobId, out var job))
            return false;

        if (job.IsComplete)
            return false;

        job.Options = job.Options with { Priority = (int)priority };
        _log.Information("Updated priority for job {JobId} to {Priority}", jobId, priority);
        return true;
    }

    /// <summary>
    /// Pause a running or queued job.
    /// </summary>
    public bool PauseJob(string jobId, string? reason = null)
    {
        if (!_allJobs.TryGetValue(jobId, out var job))
            return false;

        if (!job.CanPause)
            return false;

        var previousStatus = job.Status;
        job.Status = BackfillJobStatus.Paused;
        job.PausedAt = DateTimeOffset.UtcNow;
        job.StatusReason = reason ?? "Paused by user";

        OnJobStatusChanged(job, previousStatus, BackfillJobStatus.Paused);
        _log.Information("Paused job {JobId}: {Reason}", jobId, reason);
        return true;
    }

    /// <summary>
    /// Resume a paused job.
    /// </summary>
    public async Task<bool> ResumeJobAsync(string jobId, CancellationToken ct = default)
    {
        if (!_allJobs.TryGetValue(jobId, out var job))
            return false;

        if (job.Status != BackfillJobStatus.Paused)
            return false;

        var previousStatus = job.Status;
        job.Status = BackfillJobStatus.Pending;
        job.StatusReason = null;

        await _queueLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _priorityQueue.Enqueue(job, GetPriorityScore(job, (BackfillPriority)job.Options.Priority));
        }
        finally
        {
            _queueLock.Release();
        }

        OnJobStatusChanged(job, previousStatus, BackfillJobStatus.Pending);
        _log.Information("Resumed job {JobId}", jobId);
        return true;
    }

    /// <summary>
    /// Cancel a job.
    /// </summary>
    public bool CancelJob(string jobId)
    {
        if (!_allJobs.TryGetValue(jobId, out var job))
            return false;

        if (job.IsComplete)
            return false;

        var previousStatus = job.Status;

        // Cancel the operation
        if (_cancellationTokens.TryGetValue(jobId, out var cts))
        {
            cts.Cancel();
        }

        job.Status = BackfillJobStatus.Cancelled;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.StatusReason = "Cancelled by user";

        OnJobStatusChanged(job, previousStatus, BackfillJobStatus.Cancelled);
        _log.Information("Cancelled job {JobId}", jobId);
        return true;
    }

    /// <summary>
    /// Mark a job as completed.
    /// </summary>
    public async Task MarkCompletedAsync(string jobId, bool success, string? message = null, CancellationToken ct = default)
    {
        if (!_allJobs.TryGetValue(jobId, out var job))
            return;

        var previousStatus = job.Status;
        job.Status = success ? BackfillJobStatus.Completed : BackfillJobStatus.Failed;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.StatusReason = message;

        OnJobStatusChanged(job, previousStatus, job.Status);

        // Check if any dependent jobs can now run
        await CheckDependentJobsAsync(jobId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Re-queue a job with backoff delay.
    /// </summary>
    public async Task RequeueWithBackoffAsync(BackfillJob job, TimeSpan? delay = null, CancellationToken ct = default)
    {
        delay ??= TimeSpan.FromMinutes(1) * Math.Pow(2, job.Statistics.FailedRequests);
        delay = TimeSpan.FromMinutes(Math.Min(delay.Value.TotalMinutes, 30)); // Cap at 30 minutes

        _log.Information("Re-queuing job {JobId} after {Delay}", job.JobId, delay);

        await Task.Delay(delay.Value, ct).ConfigureAwait(false);

        await _queueLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            job.Status = BackfillJobStatus.Pending;
            _priorityQueue.Enqueue(job, GetPriorityScore(job, (BackfillPriority)job.Options.Priority) + 100);
        }
        finally
        {
            _queueLock.Release();
        }
    }

    /// <summary>
    /// Get queue statistics.
    /// </summary>
    public BackfillQueueStatistics GetStatistics()
    {
        var jobs = _allJobs.Values.ToList();
        var byPriority = jobs
            .GroupBy(j => (BackfillPriority)j.Options.Priority)
            .ToDictionary(g => g.Key, g => g.Count());

        return new BackfillQueueStatistics(
            TotalJobs: jobs.Count,
            PendingJobs: jobs.Count(j => j.Status == BackfillJobStatus.Pending),
            RunningJobs: jobs.Count(j => j.Status == BackfillJobStatus.Running),
            CompletedJobs: jobs.Count(j => j.Status == BackfillJobStatus.Completed),
            FailedJobs: jobs.Count(j => j.Status == BackfillJobStatus.Failed),
            PausedJobs: jobs.Count(j => j.Status == BackfillJobStatus.Paused),
            JobsByPriority: byPriority
        );
    }

    private bool HasUnmetDependencies(BackfillJob job, string[]? dependsOnJobIds)
    {
        if (dependsOnJobIds == null || dependsOnJobIds.Length == 0)
            return false;

        return dependsOnJobIds.Any(depId =>
            _allJobs.TryGetValue(depId, out var depJob) &&
            depJob.Status is not BackfillJobStatus.Completed);
    }

    private async Task CheckDependentJobsAsync(string completedJobId, CancellationToken ct = default)
    {
        foreach (var job in _allJobs.Values)
        {
            if (job.Status == BackfillJobStatus.Paused &&
                job.StatusReason?.Contains("dependencies") == true)
            {
                // Re-evaluate this job
                await ResumeJobAsync(job.JobId, ct).ConfigureAwait(false);
            }
        }
    }

    private int GetPriorityScore(BackfillJob job, BackfillPriority priority)
    {
        // Lower score = higher priority
        var baseScore = (int)priority;
        var ageBonus = (int)(DateTimeOffset.UtcNow - job.CreatedAt).TotalMinutes / 10;
        return Math.Max(0, baseScore - ageBonus);
    }

    private void OnJobStatusChanged(BackfillJob job, BackfillJobStatus previousStatus, BackfillJobStatus currentStatus)
    {
        JobStatusChanged?.Invoke(this, new JobStatusChangedEventArgs
        {
            Job = job,
            PreviousStatus = previousStatus,
            CurrentStatus = currentStatus
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        foreach (var cts in _cancellationTokens.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _cancellationTokens.Clear();
        _allJobs.Clear();
        _queueLock.Dispose();
    }
}

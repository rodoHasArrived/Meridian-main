using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using Meridian.Core.IO;
using Meridian.Core.Logging;
using Meridian.Storage.Archival;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Manages backfill jobs lifecycle: creation, persistence, start/stop, and progress tracking.
/// Jobs can be persisted to disk and resumed across application restarts.
/// </summary>
public sealed class BackfillJobManager : IDisposable
{
    internal const string HostShutdownPauseReason =
        "Paused during host shutdown; resume the job to rebuild pending requests.";
    internal const string InterruptedHostPauseReason =
        "Recovered after an interrupted host lifecycle; resume the job to rebuild pending requests.";

    private readonly ConcurrentDictionary<string, BackfillJob> _jobs = new();
    private readonly DataGapAnalyzer _gapAnalyzer;
    private readonly BackfillRequestQueue _requestQueue;
    private readonly string _jobsDirectory;
    private readonly RootedPathGuard _jobsPathGuard;
    private readonly Func<string, string, CancellationToken, Task> _atomicWriteAsync;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _persistLock = new(1, 1);
    private readonly object _jobCancellationHandlerSync = new();
    private Func<string, CancellationToken, Task>? _jobCancellationHandler;
    private Func<IReadOnlyCollection<BackfillRequest>, Task>? _uncommittedBatchCancellationHandler;
    private bool _disposed;

    /// <summary>
    /// Event raised when a job's status changes.
    /// </summary>
    public event Action<BackfillJob, BackfillJobStatus>? OnJobStatusChanged;

    /// <summary>
    /// Event raised when job progress is updated.
    /// </summary>
    public event Action<BackfillJob>? OnJobProgressUpdated;

    public BackfillJobManager(
        DataGapAnalyzer gapAnalyzer,
        BackfillRequestQueue requestQueue,
        string jobsDirectory,
        ILogger? log = null)
        : this(
            gapAnalyzer,
            requestQueue,
            jobsDirectory,
            AtomicFileWriter.WriteAsync,
            log)
    {
    }

    internal BackfillJobManager(
        DataGapAnalyzer gapAnalyzer,
        BackfillRequestQueue requestQueue,
        string jobsDirectory,
        Func<string, string, CancellationToken, Task> atomicWriteAsync,
        ILogger? log = null)
    {
        _gapAnalyzer = gapAnalyzer;
        _requestQueue = requestQueue;
        _jobsPathGuard = new RootedPathGuard(jobsDirectory);
        _jobsDirectory = _jobsPathGuard.RootPath;
        _atomicWriteAsync = atomicWriteAsync
            ?? throw new ArgumentNullException(nameof(atomicWriteAsync));
        _log = log ?? LoggingSetup.ForContext<BackfillJobManager>();

        // Ensure jobs directory exists
        if (!Directory.Exists(_jobsDirectory))
        {
            Directory.CreateDirectory(_jobsDirectory);
        }
    }

    /// <summary>
    /// Registers the worker-owned cancellation boundary for admitted provider attempts.
    /// The job manager remains usable without a worker, in which case cancellation is limited
    /// to pending queue entries.
    /// </summary>
    internal IDisposable RegisterJobCancellationHandler(
        Func<string, CancellationToken, Task> cancellationHandler,
        Func<IReadOnlyCollection<BackfillRequest>, Task> uncommittedBatchCancellationHandler)
    {
        ArgumentNullException.ThrowIfNull(cancellationHandler);
        ArgumentNullException.ThrowIfNull(uncommittedBatchCancellationHandler);

        lock (_jobCancellationHandlerSync)
        {
            if (_jobCancellationHandler is not null ||
                _uncommittedBatchCancellationHandler is not null)
            {
                throw new InvalidOperationException(
                    "A backfill job cancellation handler is already registered.");
            }

            _jobCancellationHandler = cancellationHandler;
            _uncommittedBatchCancellationHandler = uncommittedBatchCancellationHandler;
        }

        return new JobCancellationRegistration(
            this,
            cancellationHandler,
            uncommittedBatchCancellationHandler);
    }

    /// <summary>
    /// Load persisted jobs from disk.
    /// </summary>
    public async Task LoadJobsAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_jobsDirectory))
            return;

        var jobFiles = Directory.GetFiles(_jobsDirectory, "*.json");
        _log.Information("Loading {Count} persisted jobs", jobFiles.Length);

        foreach (var file in jobFiles)
        {
            try
            {
                _jobsPathGuard.EnsurePath(file);
                var json = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                var job = JsonSerializer.Deserialize<BackfillJob>(json);

                if (job != null)
                {
                    EnsurePersistedJobIdentity(job, file);
                    var previousStatus = job.Status;
                    if (job.Status is BackfillJobStatus.Running or BackfillJobStatus.RateLimited)
                    {
                        // Request-queue contents are intentionally process-local. Persisted jobs
                        // that claimed to be active when the previous process ended must not be
                        // reloaded as Running with no work behind them.
                        job.Status = BackfillJobStatus.Paused;
                        job.PausedAt = DateTimeOffset.UtcNow;
                        job.StatusReason = InterruptedHostPauseReason;
                    }

                    _jobs[job.JobId] = job;

                    if (job.Status != previousStatus)
                    {
                        await PersistJobAsync(job, ct).ConfigureAwait(false);
                        OnJobStatusChanged?.Invoke(job, previousStatus);
                    }

                    _log.Debug("Loaded job {JobId}: {Name} ({Status})", job.JobId, job.Name, job.Status);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to load job from {File}", file);
            }
        }
    }

    /// <summary>
    /// Create a new backfill job.
    /// </summary>
    public async Task<BackfillJob> CreateJobAsync(
        string name,
        IEnumerable<string> symbols,
        DateOnly from,
        DateOnly to,
        DataGranularity granularity = DataGranularity.Daily,
        BackfillJobOptions? options = null,
        IEnumerable<string>? preferredProviders = null,
        CancellationToken ct = default)
    {
        var job = new BackfillJob
        {
            Name = name,
            Symbols = symbols.Select(s => s.ToUpperInvariant()).Distinct().ToList(),
            FromDate = from,
            ToDate = to,
            Granularity = granularity,
            Options = options ?? new BackfillJobOptions(),
            PreferredProviders = preferredProviders?.ToList() ?? new List<string>()
        };

        _jobs[job.JobId] = job;

        await PersistJobAsync(job, ct).ConfigureAwait(false);

        _log.Information("Created job {JobId}: {Name} ({SymbolCount} symbols, {From} to {To})",
            job.JobId, job.Name, job.Symbols.Count, from, to);

        return job;
    }

    /// <summary>
    /// Start a job (analyze gaps and enqueue requests).
    /// </summary>
    public async Task StartJobAsync(string jobId, CancellationToken ct = default)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
            throw new InvalidOperationException($"Job {jobId} not found");

        if (!job.CanStart)
            throw new InvalidOperationException($"Job {jobId} cannot be started (status: {job.Status})");

        var startSnapshot = BackfillJobStartSnapshot.Capture(job);
        var previousStatus = startSnapshot.Status;
        IReadOnlyList<BackfillRequest> admittedRequests = Array.Empty<BackfillRequest>();
        var durableStartStatePersisted = false;
        job.Status = BackfillJobStatus.Running;
        job.StartedAt ??= DateTimeOffset.UtcNow;
        job.PausedAt = null;
        job.StatusReason = null;

        try
        {
            // Analyze gaps if not already done or if resuming
            if (job.Options.SkipExistingData || job.Options.FillGapsOnly)
            {
                _log.Information("Analyzing data gaps for job {JobId}...", jobId);

                var gapAnalysis = await _gapAnalyzer.AnalyzeAsync(
                    job.Symbols,
                    job.FromDate,
                    job.ToDate,
                    job.Granularity,
                    ct).ConfigureAwait(false);

                job.Statistics.GapsDetected = gapAnalysis.TotalGapDays;

                if (!gapAnalysis.HasGaps)
                {
                    _log.Information("No gaps detected for job {JobId}, completing immediately", jobId);
                    job.Status = BackfillJobStatus.Completed;
                    job.CompletedAt = DateTimeOffset.UtcNow;
                    job.StatusReason = "No data gaps detected";
                    await PersistJobAsync(job, ct).ConfigureAwait(false);
                    durableStartStatePersisted = true;
                    OnJobStatusChanged?.Invoke(job, previousStatus);
                    return;
                }

                // Enqueue requests for gaps
                admittedRequests = await _requestQueue.EnqueueJobRequestsAsync(
                    job,
                    gapAnalysis,
                    ct).ConfigureAwait(false);
            }
            else
            {
                // Full backfill (no gap analysis)
                var gapAnalysis = new GapAnalysisResult
                {
                    FromDate = job.FromDate,
                    ToDate = job.ToDate,
                    Granularity = job.Granularity,
                    TotalSymbols = job.Symbols.Count,
                    SymbolsWithGaps = job.Symbols.Count
                };

                foreach (var symbol in job.Symbols)
                {
                    gapAnalysis.SymbolGaps[symbol] = new SymbolGapInfo
                    {
                        Symbol = symbol,
                        FromDate = job.FromDate,
                        ToDate = job.ToDate,
                        Granularity = job.Granularity,
                        HasGaps = true,
                        GapDates = GenerateTradingDays(job.FromDate, job.ToDate)
                    };
                }

                admittedRequests = await _requestQueue.EnqueueJobRequestsAsync(
                    job,
                    gapAnalysis,
                    ct).ConfigureAwait(false);
            }

            await PersistJobAsync(job, ct).ConfigureAwait(false);
            durableStartStatePersisted = true;
            OnJobStatusChanged?.Invoke(job, previousStatus);

            _log.Information("Started job {JobId}: {PendingRequests} requests queued",
                jobId, _requestQueue.PendingCount);
        }
        catch (OperationCanceledException cancellation) when (ct.IsCancellationRequested)
        {
            if (durableStartStatePersisted)
                throw;

            var rollbackFailures = new List<Exception>();
            try
            {
                await RevokeUncommittedBatchAsync(admittedRequests).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                rollbackFailures.Add(new InvalidOperationException(
                    $"Failed to roll back requests admitted while starting backfill job {jobId}.",
                    ex));
            }

            startSnapshot.Restore(job);
            try
            {
                // The caller token is already cancelled. Use a non-cancellable durability
                // boundary so cancellation is never persisted as a normal job failure.
                await PersistJobAsync(job, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                rollbackFailures.Add(new InvalidOperationException(
                    $"Failed to persist the restored state for cancelled backfill job start {jobId}.",
                    ex));
            }

            if (rollbackFailures.Count > 0)
            {
                rollbackFailures.Insert(0, cancellation);
                throw new AggregateException(
                    $"Backfill job {jobId} start was cancelled and rollback completed with failures.",
                    rollbackFailures);
            }

            throw;
        }
        catch (Exception originalFailure)
        {
            if (durableStartStatePersisted)
                throw;

            var transitionFailures = new List<Exception>();
            try
            {
                // A Running-state persistence failure means this batch never acquired durable
                // ownership. Revoke pending entries and await any provider attempt that already
                // dequeued one before publishing a terminal job state.
                await RevokeUncommittedBatchAsync(admittedRequests).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                transitionFailures.Add(new InvalidOperationException(
                    $"Failed to revoke requests admitted while starting backfill job {jobId}.",
                    ex));
            }

            job.Status = BackfillJobStatus.Failed;
            job.StatusReason = originalFailure.Message;
            job.CompletedAt = DateTimeOffset.UtcNow;
            var failedStatePersisted = false;
            try
            {
                await PersistJobAsync(job, CancellationToken.None).ConfigureAwait(false);
                failedStatePersisted = true;
            }
            catch (Exception ex)
            {
                transitionFailures.Add(new InvalidOperationException(
                    $"Failed to persist the terminal state for backfill job {jobId}.",
                    ex));
            }

            if (failedStatePersisted)
            {
                try
                {
                    OnJobStatusChanged?.Invoke(job, previousStatus);
                }
                catch (Exception ex)
                {
                    transitionFailures.Add(new InvalidOperationException(
                        $"A backfill status observer failed while reporting job {jobId}.",
                        ex));
                }
            }

            if (transitionFailures.Count > 0)
            {
                transitionFailures.Insert(0, originalFailure);
                throw new AggregateException(
                    $"Backfill job {jobId} failed to start and terminal cleanup completed with failures.",
                    transitionFailures);
            }

            throw;
        }
    }

    /// <summary>
    /// Pause a running job.
    /// </summary>
    public async Task PauseJobAsync(string jobId, string? reason = null, CancellationToken ct = default)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
            throw new InvalidOperationException($"Job {jobId} not found");

        if (!job.CanPause)
            throw new InvalidOperationException($"Job {jobId} cannot be paused (status: {job.Status})");

        var previousStatus = job.Status;
        job.Status = BackfillJobStatus.Paused;
        job.PausedAt = DateTimeOffset.UtcNow;
        job.StatusReason = reason ?? "Paused by user";

        await PersistJobAsync(job, ct).ConfigureAwait(false);
        OnJobStatusChanged?.Invoke(job, previousStatus);

        _log.Information("Paused job {JobId}: {Reason}", jobId, job.StatusReason);
    }

    /// <summary>
    /// Resume a paused job.
    /// </summary>
    public async Task ResumeJobAsync(string jobId, CancellationToken ct = default)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
            throw new InvalidOperationException($"Job {jobId} not found");

        if (job.Status != BackfillJobStatus.Paused && job.Status != BackfillJobStatus.RateLimited)
            throw new InvalidOperationException($"Job {jobId} is not paused (status: {job.Status})");

        var queuedRequests = await _requestQueue.GetJobRequestsAsync(jobId, ct).ConfigureAwait(false);
        if (queuedRequests.Count == 0)
        {
            // After restart there is no durable request queue. Re-run gap analysis so only
            // still-missing data is admitted, and reset stale per-attempt progress first.
            if (job.Status == BackfillJobStatus.RateLimited)
                job.Status = BackfillJobStatus.Paused;
            job.SymbolProgress.Clear();
            await StartJobAsync(jobId, ct).ConfigureAwait(false);
            return;
        }

        var previousStatus = job.Status;
        job.Status = BackfillJobStatus.Running;
        job.PausedAt = null;
        job.StatusReason = null;

        await PersistJobAsync(job, ct).ConfigureAwait(false);
        OnJobStatusChanged?.Invoke(job, previousStatus);

        _log.Information("Resumed job {JobId}", jobId);
    }

    /// <summary>
    /// Cancel a job.
    /// </summary>
    public async Task CancelJobAsync(string jobId, CancellationToken ct = default)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
            throw new InvalidOperationException($"Job {jobId} not found");

        if (job.IsComplete)
            throw new InvalidOperationException($"Job {jobId} is already complete");

        var previousStatus = job.Status;

        // Cancellation is a two-phase transition. First prevent new admissions and wait for
        // every provider attempt already owned by this job to observe cancellation. Only then
        // may the durable job record claim the terminal Cancelled state.
        Func<string, CancellationToken, Task>? cancellationHandler;
        lock (_jobCancellationHandlerSync)
        {
            cancellationHandler = _jobCancellationHandler;
        }

        if (cancellationHandler is null)
        {
            await _requestQueue.CancelJobRequestsAsync(
                jobId,
                CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            await cancellationHandler(jobId, ct).ConfigureAwait(false);
        }

        job.Status = BackfillJobStatus.Cancelled;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.StatusReason = "Cancelled by user";

        // Once queue/provider ownership has been revoked, persist the truthful terminal state
        // even if the initiating caller cancels immediately after that commit point.
        await PersistJobAsync(job, CancellationToken.None).ConfigureAwait(false);
        OnJobStatusChanged?.Invoke(job, previousStatus);

        _log.Information("Cancelled job {JobId}", jobId);
    }

    /// <summary>
    /// Update job progress from a completed request.
    /// </summary>
    public async Task UpdateJobProgressAsync(BackfillRequest request, CancellationToken ct = default)
    {
        if (!_jobs.TryGetValue(request.JobId, out var job))
            return;

        if (job.SymbolProgress.TryGetValue(request.Symbol, out var progress))
        {
            if (request.Status == BackfillRequestStatus.Completed)
            {
                progress.CompletedRequests++;
                progress.BarsRetrieved += request.BarsRetrieved;
                progress.SuccessfulProvider = request.AssignedProvider;

                // Mark dates as filled
                var current = request.FromDate;
                while (current <= request.ToDate)
                {
                    if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
                    {
                        progress.FilledDates.Add(current);
                    }
                    current = current.AddDays(1);
                }

                // Update stats
                job.Statistics.TotalBarsRetrieved += request.BarsRetrieved;
                job.Statistics.SuccessfulRequests++;
                job.Statistics.GapsFilled += progress.FilledDates.Count;

                if (request.AssignedProvider != null)
                {
                    job.Statistics.RequestsByProvider.TryGetValue(request.AssignedProvider, out var count);
                    job.Statistics.RequestsByProvider[request.AssignedProvider] = count + 1;

                    job.Statistics.BarsByProvider.TryGetValue(request.AssignedProvider, out var bars);
                    job.Statistics.BarsByProvider[request.AssignedProvider] = bars + request.BarsRetrieved;
                }
            }
            else if (request.Status == BackfillRequestStatus.Failed)
            {
                progress.FailedRequests++;
                progress.LastError = request.ErrorMessage;
                job.Statistics.FailedRequests++;
            }

            job.Statistics.TotalRequestsMade++;

            // Check if symbol is complete
            if (progress.CompletedRequests + progress.FailedRequests >= progress.TotalRequests)
            {
                progress.CompletedAt = DateTimeOffset.UtcNow;
                progress.Status = progress.FailedRequests == 0
                    ? SymbolBackfillStatus.Completed
                    : SymbolBackfillStatus.Failed;
            }
        }

        // Check if job is complete
        var allComplete = job.SymbolProgress.Values.All(p =>
            p.Status == SymbolBackfillStatus.Completed ||
            p.Status == SymbolBackfillStatus.Failed ||
            p.Status == SymbolBackfillStatus.Skipped);

        if (allComplete && job.Status == BackfillJobStatus.Running)
        {
            var hasFailures = job.SymbolProgress.Values.Any(p => p.Status == SymbolBackfillStatus.Failed);
            var previousStatus = job.Status;

            job.Status = hasFailures ? BackfillJobStatus.Failed : BackfillJobStatus.Completed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.StatusReason = hasFailures ? "Completed with errors" : "All symbols backfilled successfully";

            OnJobStatusChanged?.Invoke(job, previousStatus);

            _log.Information("Job {JobId} completed: {Status} ({SuccessRate}% success rate)",
                job.JobId, job.Status, job.Statistics.SuccessfulRequests * 100 / Math.Max(1, job.Statistics.TotalRequestsMade));
        }

        await PersistJobAsync(job, ct).ConfigureAwait(false);
        OnJobProgressUpdated?.Invoke(job);
    }

    /// <summary>
    /// Mark a job as rate-limited.
    /// </summary>
    public async Task SetJobRateLimitedAsync(string jobId, TimeSpan? resumeAfter = null, CancellationToken ct = default)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
            return;

        if (job.Status != BackfillJobStatus.Running)
            return;

        var previousStatus = job.Status;
        job.Status = BackfillJobStatus.RateLimited;
        job.StatusReason = resumeAfter.HasValue
            ? $"Rate limited, will resume after {resumeAfter.Value.TotalMinutes:F1} minutes"
            : "All providers rate limited";

        await PersistJobAsync(job, ct).ConfigureAwait(false);
        OnJobStatusChanged?.Invoke(job, previousStatus);

        _log.Information("Job {JobId} rate limited: {Reason}", jobId, job.StatusReason);
    }

    /// <summary>
    /// Get a job by ID.
    /// </summary>
    public BackfillJob? GetJob(string jobId)
    {
        _jobs.TryGetValue(jobId, out var job);
        return job;
    }

    /// <summary>
    /// Get all jobs.
    /// </summary>
    public IReadOnlyList<BackfillJob> GetAllJobs()
    {
        return _jobs.Values.ToList();
    }

    /// <summary>
    /// Get jobs by status.
    /// </summary>
    public IReadOnlyList<BackfillJob> GetJobsByStatus(BackfillJobStatus status)
    {
        return _jobs.Values.Where(j => j.Status == status).ToList();
    }

    /// <summary>
    /// Delete a job (must be completed or cancelled).
    /// </summary>
    public Task DeleteJobAsync(string jobId, CancellationToken ct = default)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
            return Task.CompletedTask;

        if (!job.IsComplete)
            throw new InvalidOperationException($"Cannot delete job {jobId} while it is {job.Status}");

        _jobs.TryRemove(jobId, out _);

        var filePath = GetJobFilePath(jobId);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        _log.Information("Deleted job {JobId}", jobId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Persist a job to disk.
    /// </summary>
    private async Task PersistJobAsync(BackfillJob job, CancellationToken ct)
    {
        await _persistLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var filePath = GetJobFilePath(job.JobId);
            var json = JsonSerializer.Serialize(job, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            _jobsPathGuard.EnsurePath(filePath);
            await _atomicWriteAsync(filePath, json, ct).ConfigureAwait(false);
        }
        finally
        {
            _persistLock.Release();
        }
    }

    private string GetJobFilePath(string jobId)
    {
        RootedPathGuard.ValidatePathSegment(jobId, nameof(jobId));
        return _jobsPathGuard.ResolvePath($"{jobId}.json");
    }

    private void EnsurePersistedJobIdentity(BackfillJob job, string sourcePath)
    {
        var expectedPath = GetJobFilePath(job.JobId);
        var actualPath = Path.GetFullPath(sourcePath);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!string.Equals(actualPath, expectedPath, comparison))
        {
            throw new InvalidDataException(
                $"Persisted backfill job identity '{job.JobId}' does not match file '{actualPath}'.");
        }
    }

    private static List<DateOnly> GenerateTradingDays(DateOnly from, DateOnly to)
    {
        var days = new List<DateOnly>();
        var current = from;

        while (current <= to)
        {
            if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
            {
                days.Add(current);
            }
            current = current.AddDays(1);
        }

        return days;
    }

    private void UnregisterJobCancellationHandler(
        Func<string, CancellationToken, Task> cancellationHandler,
        Func<IReadOnlyCollection<BackfillRequest>, Task> uncommittedBatchCancellationHandler)
    {
        lock (_jobCancellationHandlerSync)
        {
            if (ReferenceEquals(_jobCancellationHandler, cancellationHandler))
                _jobCancellationHandler = null;
            if (ReferenceEquals(
                    _uncommittedBatchCancellationHandler,
                    uncommittedBatchCancellationHandler))
            {
                _uncommittedBatchCancellationHandler = null;
            }
        }
    }

    private async Task RevokeUncommittedBatchAsync(
        IReadOnlyCollection<BackfillRequest> admittedRequests)
    {
        Func<IReadOnlyCollection<BackfillRequest>, Task>? batchCancellationHandler;
        lock (_jobCancellationHandlerSync)
        {
            batchCancellationHandler = _uncommittedBatchCancellationHandler;
        }

        if (batchCancellationHandler is null)
        {
            await _requestQueue.RollbackPendingRequestsAsync(
                admittedRequests,
                CancellationToken.None).ConfigureAwait(false);
            return;
        }

        // A live worker may already have dequeued part of the uncommitted batch. Revoke and
        // observe exactly these request objects before the job leaves its provisional state.
        await batchCancellationHandler(admittedRequests).ConfigureAwait(false);
    }

    private sealed class JobCancellationRegistration(
        BackfillJobManager owner,
        Func<string, CancellationToken, Task> cancellationHandler,
        Func<IReadOnlyCollection<BackfillRequest>, Task> uncommittedBatchCancellationHandler)
        : IDisposable
    {
        private BackfillJobManager? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?
                .UnregisterJobCancellationHandler(
                    cancellationHandler,
                    uncommittedBatchCancellationHandler);
        }
    }

    private sealed record BackfillJobStartSnapshot(
        BackfillJobStatus Status,
        DateTimeOffset? StartedAt,
        DateTimeOffset? CompletedAt,
        DateTimeOffset? PausedAt,
        string? StatusReason,
        int GapsDetected,
        IReadOnlyDictionary<string, SymbolBackfillProgress> SymbolProgress)
    {
        public static BackfillJobStartSnapshot Capture(BackfillJob job)
            => new(
                job.Status,
                job.StartedAt,
                job.CompletedAt,
                job.PausedAt,
                job.StatusReason,
                job.Statistics.GapsDetected,
                job.SymbolProgress.ToDictionary(
                    static pair => pair.Key,
                    static pair => CloneProgress(pair.Value),
                    StringComparer.Ordinal));

        public void Restore(BackfillJob job)
        {
            job.Status = Status;
            job.StartedAt = StartedAt;
            job.CompletedAt = CompletedAt;
            job.PausedAt = PausedAt;
            job.StatusReason = StatusReason;
            job.Statistics.GapsDetected = GapsDetected;
            job.SymbolProgress.Clear();
            foreach (var (symbol, progress) in SymbolProgress)
                job.SymbolProgress[symbol] = CloneProgress(progress);
        }

        private static SymbolBackfillProgress CloneProgress(SymbolBackfillProgress source)
        {
            var clone = new SymbolBackfillProgress
            {
                Symbol = source.Symbol,
                DatesToFill = [.. source.DatesToFill],
                TotalRequests = source.TotalRequests,
                CompletedRequests = source.CompletedRequests,
                FailedRequests = source.FailedRequests,
                BarsRetrieved = source.BarsRetrieved,
                SuccessfulProvider = source.SuccessfulProvider,
                LastError = source.LastError,
                StartedAt = source.StartedAt,
                CompletedAt = source.CompletedAt,
                Status = source.Status
            };

            clone.FilledDates.UnionWith(source.FilledDates);
            clone.FailedDates.UnionWith(source.FailedDates);
            return clone;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _persistLock.Dispose();
    }
}

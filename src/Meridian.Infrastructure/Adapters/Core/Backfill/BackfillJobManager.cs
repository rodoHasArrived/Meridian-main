using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Manages backfill jobs lifecycle: creation, persistence, start/stop, and progress tracking.
/// Jobs can be persisted to disk and resumed across application restarts.
/// </summary>
public sealed class BackfillJobManager : IDisposable
{
    private readonly ConcurrentDictionary<string, BackfillJob> _jobs = new();
    private readonly DataGapAnalyzer _gapAnalyzer;
    private readonly BackfillRequestQueue _requestQueue;
    private readonly string _jobsDirectory;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _persistLock = new(1, 1);
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
    {
        _gapAnalyzer = gapAnalyzer;
        _requestQueue = requestQueue;
        _jobsDirectory = jobsDirectory;
        _log = log ?? LoggingSetup.ForContext<BackfillJobManager>();

        // Ensure jobs directory exists
        if (!Directory.Exists(_jobsDirectory))
        {
            Directory.CreateDirectory(_jobsDirectory);
        }
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
                var json = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                var job = JsonSerializer.Deserialize<BackfillJob>(json);

                if (job != null)
                {
                    _jobs[job.JobId] = job;
                    _log.Debug("Loaded job {JobId}: {Name} ({Status})", job.JobId, job.Name, job.Status);
                }
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

        var previousStatus = job.Status;
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
                    OnJobStatusChanged?.Invoke(job, previousStatus);
                    return;
                }

                // Enqueue requests for gaps
                await _requestQueue.EnqueueJobRequestsAsync(job, gapAnalysis, ct).ConfigureAwait(false);
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

                await _requestQueue.EnqueueJobRequestsAsync(job, gapAnalysis, ct).ConfigureAwait(false);
            }

            await PersistJobAsync(job, ct).ConfigureAwait(false);
            OnJobStatusChanged?.Invoke(job, previousStatus);

            _log.Information("Started job {JobId}: {PendingRequests} requests queued",
                jobId, _requestQueue.PendingCount);
        }
        catch (Exception ex)
        {
            job.Status = BackfillJobStatus.Failed;
            job.StatusReason = ex.Message;
            job.CompletedAt = DateTimeOffset.UtcNow;
            await PersistJobAsync(job, ct).ConfigureAwait(false);
            OnJobStatusChanged?.Invoke(job, previousStatus);
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
        job.Status = BackfillJobStatus.Cancelled;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.StatusReason = "Cancelled by user";

        // Cancel pending requests
        await _requestQueue.CancelJobRequestsAsync(jobId, ct).ConfigureAwait(false);

        await PersistJobAsync(job, ct).ConfigureAwait(false);
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
            await File.WriteAllTextAsync(filePath, json, ct).ConfigureAwait(false);
        }
        finally
        {
            _persistLock.Release();
        }
    }

    private string GetJobFilePath(string jobId)
    {
        return Path.Combine(_jobsDirectory, $"{jobId}.json");
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

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _persistLock.Dispose();
    }
}

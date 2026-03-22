using System.Collections.Concurrent;
using System.Text.Json;
using Meridian.Application.Coordination;
using Meridian.Application.Logging;
using Meridian.Contracts.Pipeline;
using Serilog;

namespace Meridian.Application.Pipeline;

/// <summary>
/// Manages the lifecycle of unified <see cref="IngestionJob"/> instances,
/// bridging the contract model to the existing backfill and realtime infrastructure.
/// Provides create, transition, checkpoint, and query operations with disk persistence.
/// </summary>
/// <remarks>
/// Addresses P0 gap: "No unified job contract across realtime/backfill flows"
/// from the Ingestion Orchestration evaluation.
/// </remarks>
public sealed class IngestionJobService : IDisposable
{
    private readonly ConcurrentDictionary<string, IngestionJob> _jobs = new();
    private readonly ILogger _log = LoggingSetup.ForContext<IngestionJobService>();
    private readonly string _persistenceDir;
    private readonly IScheduledWorkOwnershipService? _ownershipService;
    private readonly SemaphoreSlim _persistLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private bool _disposed;

    /// <summary>
    /// Event raised when a job's state changes.
    /// </summary>
    public event Action<IngestionJob, IngestionJobState, IngestionJobState>? JobStateChanged;

    /// <summary>
    /// Event raised when a job's checkpoint is updated.
    /// </summary>
    public event Action<IngestionJob, IngestionCheckpointToken>? CheckpointUpdated;

    public IngestionJobService(
        string? persistenceDir = null,
        IScheduledWorkOwnershipService? ownershipService = null)
    {
        _persistenceDir = persistenceDir
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Meridian",
                "ingestion-jobs");
        _ownershipService = ownershipService;

        if (!Directory.Exists(_persistenceDir))
        {
            Directory.CreateDirectory(_persistenceDir);
        }
    }

    /// <summary>
    /// Loads persisted jobs from disk on startup.
    /// </summary>
    public async Task LoadJobsAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_persistenceDir))
            return;

        var files = Directory.GetFiles(_persistenceDir, "job_*.json");
        var loaded = 0;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var json = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                var job = JsonSerializer.Deserialize<IngestionJob>(json, _jsonOptions);
                if (job != null)
                {
                    _jobs[job.JobId] = job;
                    loaded++;
                }
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to load job from {File}", file);
            }
        }

        _log.Information("Loaded {Count} ingestion jobs from disk", loaded);
    }

    /// <summary>
    /// Creates a new ingestion job in Draft state.
    /// </summary>
    public async Task<IngestionJob> CreateJobAsync(
        IngestionWorkloadType workloadType,
        string[] symbols,
        string provider,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        IngestionSla? sla = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        if (symbols.Length == 0)
            throw new ArgumentException("At least one symbol is required.", nameof(symbols));

        var job = new IngestionJob
        {
            WorkloadType = workloadType,
            Symbols = symbols,
            Provider = provider,
            FromDate = fromDate,
            ToDate = toDate,
            Sla = sla ?? new IngestionSla(),
            SymbolProgress = symbols.Select(s => new IngestionSymbolProgress { Symbol = s }).ToList()
        };

        _jobs[job.JobId] = job;
        await PersistJobAsync(job, ct).ConfigureAwait(false);

        _log.Information(
            "Created ingestion job {JobId} ({WorkloadType}) for {SymbolCount} symbols via {Provider}",
            job.JobId, workloadType, symbols.Length, provider);

        return job;
    }

    /// <summary>
    /// Transitions a job to a new state if the transition is valid.
    /// </summary>
    public async Task<bool> TransitionAsync(
        string jobId,
        IngestionJobState newState,
        string? errorMessage = null,
        CancellationToken ct = default)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            _log.Warning("Job {JobId} not found for transition to {NewState}", jobId, newState);
            return false;
        }

        var previousState = job.State;

        if (newState == IngestionJobState.Running && _ownershipService is not null)
        {
            var acquired = await _ownershipService.TryAcquireJobAsync(jobId, ct).ConfigureAwait(false);
            if (!acquired.Acquired)
            {
                _log.Warning(
                    "Job {JobId} cannot transition to Running because lease is owned by {Owner} until {Expiry}",
                    jobId,
                    acquired.CurrentOwner,
                    acquired.CurrentExpiryUtc);
                return false;
            }
        }

        if (!job.TryTransition(newState))
        {
            _log.Warning(
                "Invalid transition for job {JobId}: {From} → {To}",
                jobId, previousState, newState);
            return false;
        }

        if (errorMessage != null)
        {
            job.ErrorMessage = errorMessage;
        }

        if (previousState == IngestionJobState.Running &&
            newState is IngestionJobState.Completed or IngestionJobState.Failed or IngestionJobState.Cancelled or IngestionJobState.Paused &&
            _ownershipService is not null)
        {
            await _ownershipService.ReleaseJobAsync(jobId, ct).ConfigureAwait(false);
        }

        // Handle retry: increment attempt count and schedule next retry
        if (previousState == IngestionJobState.Failed && newState == IngestionJobState.Queued)
        {
            job.RetryEnvelope.AttemptCount++;
            job.RetryEnvelope.NextRetryAt = DateTime.UtcNow + job.RetryEnvelope.NextDelay;
        }

        await PersistJobAsync(job, ct).ConfigureAwait(false);

        _log.Information(
            "Job {JobId} transitioned from {PreviousState} to {NewState}",
            jobId, previousState, newState);

        try
        {
            JobStateChanged?.Invoke(job, previousState, newState);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error in JobStateChanged handler for job {JobId}", jobId);
        }

        return true;
    }

    /// <summary>
    /// Updates the checkpoint token for a job, enabling resume from the last durable offset.
    /// </summary>
    public async Task UpdateCheckpointAsync(
        string jobId,
        IngestionCheckpointToken checkpoint,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        if (!_jobs.TryGetValue(jobId, out var job))
        {
            _log.Warning("Job {JobId} not found for checkpoint update", jobId);
            return;
        }

        checkpoint.CapturedAt = DateTime.UtcNow;
        job.CheckpointToken = checkpoint;
        await PersistJobAsync(job, ct).ConfigureAwait(false);

        _log.Debug(
            "Updated checkpoint for job {JobId}: symbol={Symbol}, date={Date}, offset={Offset}",
            jobId, checkpoint.LastSymbol, checkpoint.LastDate, checkpoint.LastOffset);

        try
        {
            CheckpointUpdated?.Invoke(job, checkpoint);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error in CheckpointUpdated handler for job {JobId}", jobId);
        }
    }

    /// <summary>
    /// Updates progress for a specific symbol within a job.
    /// </summary>
    public async Task UpdateSymbolProgressAsync(
        string jobId,
        string symbol,
        long dataPointsProcessed,
        long? expectedDataPoints = null,
        DateTime? lastCommittedDate = null,
        string? errorMessage = null,
        CancellationToken ct = default)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
            return;

        var progress = job.SymbolProgress.FirstOrDefault(p => p.Symbol == symbol);
        if (progress == null)
        {
            progress = new IngestionSymbolProgress { Symbol = symbol };
            job.SymbolProgress.Add(progress);
        }

        progress.DataPointsProcessed = dataPointsProcessed;
        if (expectedDataPoints.HasValue)
            progress.ExpectedDataPoints = expectedDataPoints.Value;
        if (lastCommittedDate.HasValue)
            progress.LastCommittedDate = lastCommittedDate.Value;
        if (errorMessage != null)
        {
            progress.ErrorMessage = errorMessage;
            progress.State = IngestionJobState.Failed;
            progress.RetryCount++;
        }
        else if (progress.ExpectedDataPoints > 0 && dataPointsProcessed >= progress.ExpectedDataPoints)
        {
            progress.State = IngestionJobState.Completed;
        }
        else
        {
            progress.State = IngestionJobState.Running;
        }

        // Auto-update checkpoint based on symbol progress
        job.CheckpointToken ??= new IngestionCheckpointToken();
        job.CheckpointToken.LastSymbol = symbol;
        if (lastCommittedDate.HasValue)
            job.CheckpointToken.LastDate = lastCommittedDate.Value;
        job.CheckpointToken.CapturedAt = DateTime.UtcNow;

        await PersistJobAsync(job, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a job by its ID.
    /// </summary>
    public IngestionJob? GetJob(string jobId)
    {
        return _jobs.TryGetValue(jobId, out var job) ? job : null;
    }

    /// <summary>
    /// Gets all jobs, optionally filtered by state or workload type.
    /// </summary>
    public IReadOnlyList<IngestionJob> GetJobs(
        IngestionJobState? stateFilter = null,
        IngestionWorkloadType? workloadFilter = null)
    {
        var query = _jobs.Values.AsEnumerable();

        if (stateFilter.HasValue)
            query = query.Where(j => j.State == stateFilter.Value);

        if (workloadFilter.HasValue)
            query = query.Where(j => j.WorkloadType == workloadFilter.Value);

        return query.OrderByDescending(j => j.CreatedAt).ToList();
    }

    /// <summary>
    /// Gets all jobs that are resumable (failed or paused with a checkpoint).
    /// </summary>
    public IReadOnlyList<IngestionJob> GetResumableJobs()
    {
        return _jobs.Values
            .Where(j => j.IsResumable)
            .OrderByDescending(j => j.CreatedAt)
            .ToList();
    }

    /// <summary>
    /// Gets a summary of all jobs.
    /// </summary>
    public IngestionJobSummary GetSummary()
    {
        var jobs = _jobs.Values.ToList();
        return new IngestionJobSummary
        {
            TotalJobs = jobs.Count,
            DraftJobs = jobs.Count(j => j.State == IngestionJobState.Draft),
            QueuedJobs = jobs.Count(j => j.State == IngestionJobState.Queued),
            RunningJobs = jobs.Count(j => j.State == IngestionJobState.Running),
            PausedJobs = jobs.Count(j => j.State == IngestionJobState.Paused),
            CompletedJobs = jobs.Count(j => j.State == IngestionJobState.Completed),
            FailedJobs = jobs.Count(j => j.State == IngestionJobState.Failed),
            CancelledJobs = jobs.Count(j => j.State == IngestionJobState.Cancelled),
            ResumableJobs = jobs.Count(j => j.IsResumable),
            RealtimeJobs = jobs.Count(j => j.WorkloadType == IngestionWorkloadType.Realtime),
            HistoricalJobs = jobs.Count(j => j.WorkloadType == IngestionWorkloadType.Historical),
            GapFillJobs = jobs.Count(j => j.WorkloadType == IngestionWorkloadType.GapFill),
        };
    }

    /// <summary>
    /// Deletes a terminal job by ID.
    /// </summary>
    public Task<bool> DeleteJobAsync(string jobId, CancellationToken ct = default)
    {
        if (!_jobs.TryRemove(jobId, out var job))
            return Task.FromResult(false);

        if (!job.IsTerminal)
        {
            // Put it back - can only delete terminal jobs
            _jobs[jobId] = job;
            return Task.FromResult(false);
        }

        var filePath = GetJobFilePath(jobId);
        if (File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to delete job file for {JobId}", jobId);
            }
        }

        _log.Information("Deleted job {JobId}", jobId);
        return Task.FromResult(true);
    }

    private string GetJobFilePath(string jobId) =>
        Path.Combine(_persistenceDir, $"job_{jobId}.json");

    private async Task PersistJobAsync(IngestionJob job, CancellationToken ct)
    {
        await _persistLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var filePath = GetJobFilePath(job.JobId);
            var json = JsonSerializer.Serialize(job, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to persist job {JobId}", job.JobId);
        }
        finally
        {
            _persistLock.Release();
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

/// <summary>
/// Summary statistics for all ingestion jobs.
/// </summary>
public sealed class IngestionJobSummary
{
    public int TotalJobs { get; set; }
    public int DraftJobs { get; set; }
    public int QueuedJobs { get; set; }
    public int RunningJobs { get; set; }
    public int PausedJobs { get; set; }
    public int CompletedJobs { get; set; }
    public int FailedJobs { get; set; }
    public int CancelledJobs { get; set; }
    public int ResumableJobs { get; set; }
    public int RealtimeJobs { get; set; }
    public int HistoricalJobs { get; set; }
    public int GapFillJobs { get; set; }
}

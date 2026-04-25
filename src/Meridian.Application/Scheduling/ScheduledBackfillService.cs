using Meridian.Application.Coordination;
using Meridian.Application.Backfill;
using Meridian.Infrastructure.Adapters.Core;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Scheduling;

/// <summary>
/// Background service that monitors backfill schedules and triggers executions at scheduled times.
/// Implements automatic gap-fill, catch-up for missed schedules, and job queue management.
/// </summary>
public sealed class ScheduledBackfillService : IAsyncDisposable
{
    private readonly ILogger<ScheduledBackfillService> _logger;
    private readonly BackfillScheduleManager _scheduleManager;
    private readonly BackfillJobManager _jobManager;
    private readonly BackfillWorkerService _workerService;
    private readonly DataGapAnalyzer _gapAnalyzer;
    private readonly ScheduledBackfillOptions _options;
    private readonly List<string> _defaultSymbols;
    private readonly IScheduledWorkOwnershipService? _ownershipService;
    private readonly AutoGapRemediationService? _autoGapRemediationService;

    private CancellationTokenSource? _cts;
    private Task? _schedulerTask;
    private Task? _executionTask;
    private readonly SemaphoreSlim _executionLock = new(1, 1);
    private readonly PriorityQueue<ScheduledExecution, DateTimeOffset> _executionQueue = new();
    private readonly object _queueLock = new();

    /// <summary>
    /// Event raised when a scheduled execution starts.
    /// </summary>
    public event EventHandler<BackfillExecutionLog>? ExecutionStarted;

    /// <summary>
    /// Event raised when a scheduled execution completes.
    /// </summary>
    public event EventHandler<BackfillExecutionLog>? ExecutionCompleted;

    public ScheduledBackfillService(
        ILogger<ScheduledBackfillService> logger,
        BackfillScheduleManager scheduleManager,
        BackfillJobManager jobManager,
        BackfillWorkerService workerService,
        DataGapAnalyzer gapAnalyzer,
        IScheduledWorkOwnershipService? ownershipService = null,
        ScheduledBackfillOptions? options = null,
        IEnumerable<string>? defaultSymbols = null,
        AutoGapRemediationService? autoGapRemediationService = null)
    {
        _logger = logger;
        _scheduleManager = scheduleManager;
        _jobManager = jobManager;
        _workerService = workerService;
        _gapAnalyzer = gapAnalyzer;
        _ownershipService = ownershipService;
        _options = options ?? new ScheduledBackfillOptions();
        _defaultSymbols = defaultSymbols?.ToList() ?? new List<string>();
        _autoGapRemediationService = autoGapRemediationService;
    }

    /// <summary>
    /// Whether the service is running.
    /// </summary>
    public bool IsRunning => _schedulerTask != null && !_schedulerTask.IsCompleted;

    /// <summary>
    /// Number of executions waiting in queue.
    /// </summary>
    public int QueuedExecutions
    {
        get
        {
            lock (_queueLock)
            {
                return _executionQueue.Count;
            }
        }
    }

    /// <summary>
    /// Start the scheduled backfill service.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning)
        {
            _logger.LogWarning("Scheduled backfill service is already running");
            return;
        }

        _logger.LogInformation("Starting scheduled backfill service");

        // Load schedules from disk
        await _scheduleManager.LoadSchedulesAsync(ct);

        // Handle missed schedules (catch-up)
        if (_options.CatchUpMissedSchedules)
        {
            await CatchUpMissedSchedulesAsync(ct);
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Start the scheduler loop
        _schedulerTask = RunSchedulerLoopAsync(_cts.Token);

        // Start the execution loop
        _executionTask = RunExecutionLoopAsync(_cts.Token);

        // Start the worker service if not already running
        _workerService.Start();

        _logger.LogInformation(
            "Scheduled backfill service started with {Count} schedules",
            _scheduleManager.GetEnabledSchedules().Count);
    }

    /// <summary>
    /// Stop the scheduled backfill service.
    /// </summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        if (!IsRunning)
            return;

        _logger.LogInformation("Stopping scheduled backfill service");

        _cts?.Cancel();

        var tasks = new List<Task>();
        if (_schedulerTask != null)
            tasks.Add(_schedulerTask);
        if (_executionTask != null)
            tasks.Add(_executionTask);

        try
        {
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30), ct);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }

        await _workerService.StopAsync();

        _logger.LogInformation("Scheduled backfill service stopped");
    }

    /// <summary>
    /// Manually trigger a schedule execution.
    /// </summary>
    public async Task<BackfillExecutionLog> TriggerManualExecutionAsync(
        string scheduleId,
        CancellationToken ct = default)
    {
        var schedule = _scheduleManager.GetSchedule(scheduleId)
            ?? throw new KeyNotFoundException($"Schedule not found: {scheduleId}");

        var execution = _scheduleManager.CreateManualExecution(schedule);

        if (_ownershipService is not null)
        {
            var acquired = await _ownershipService.TryAcquireScheduleAsync(scheduleId, ct).ConfigureAwait(false);
            if (!acquired.Acquired)
            {
                execution.Status = ExecutionStatus.Skipped;
                execution.ErrorMessage = $"Schedule lease is owned by {acquired.CurrentOwner}";
                _logger.LogInformation(
                    "Skipping manual schedule {ScheduleId} because lease is owned by {Owner}",
                    scheduleId,
                    acquired.CurrentOwner);
                return execution;
            }
        }

        _logger.LogInformation(
            "Manually triggering schedule {ScheduleId}: {Name}",
            scheduleId, schedule.Name);

        EnqueueExecution(schedule, execution, BackfillPriority.High);

        return execution;
    }

    /// <summary>
    /// Run an immediate gap-fill for specific symbols.
    /// </summary>
    public Task<BackfillExecutionLog> RunImmediateGapFillAsync(
        IEnumerable<string> symbols,
        int lookbackDays = 30,
        BackfillPriority priority = BackfillPriority.High,
        CancellationToken ct = default)
    {
        var symbolList = symbols.ToList();
        if (symbolList.Count == 0)
            throw new ArgumentException("At least one symbol is required");

        var execution = new BackfillExecutionLog
        {
            ScheduleId = "ad-hoc",
            ScheduleName = $"Immediate Gap-Fill ({symbolList.Count} symbols)",
            Trigger = ExecutionTrigger.Api,
            ScheduledAt = DateTimeOffset.UtcNow,
            FromDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-lookbackDays),
            ToDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            Symbols = symbolList
        };

        var schedule = new BackfillSchedule
        {
            Name = execution.ScheduleName,
            BackfillType = ScheduledBackfillType.GapFill,
            Symbols = symbolList,
            LookbackDays = lookbackDays,
            Priority = priority
        };

        EnqueueExecution(schedule, execution, priority);

        return Task.FromResult(execution);
    }

    private async Task RunSchedulerLoopAsync(CancellationToken ct)
    {
        _logger.LogDebug("Scheduler loop started");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Check for due schedules
                if (_ownershipService is not null)
                {
                    var leaderLease = await _ownershipService.TryAcquireDispatcherLeadershipAsync(ct).ConfigureAwait(false);
                    if (!leaderLease.Acquired)
                    {
                        await Task.Delay(_options.ScheduleCheckInterval, ct);
                        continue;
                    }
                }

                var dueSchedules = _scheduleManager.GetDueSchedules();

                foreach (var schedule in dueSchedules)
                {
                    if (_ownershipService is not null)
                    {
                        var acquired = await _ownershipService.TryAcquireScheduleAsync(schedule.ScheduleId, ct).ConfigureAwait(false);
                        if (!acquired.Acquired)
                            continue;
                    }

                    var execution = new BackfillExecutionLog
                    {
                        ScheduleId = schedule.ScheduleId,
                        ScheduleName = schedule.Name,
                        Trigger = ExecutionTrigger.Scheduled,
                        ScheduledAt = schedule.NextExecutionAt ?? DateTimeOffset.UtcNow,
                        FromDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-schedule.LookbackDays),
                        ToDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
                        Symbols = new List<string>(schedule.Symbols)
                    };

                    EnqueueExecution(schedule, execution, schedule.Priority);

                    // Update next execution time
                    schedule.NextExecutionAt = schedule.CalculateNextExecution();
                    await _scheduleManager.UpdateScheduleAsync(schedule, ct);
                }

                // Wait for next check interval
                await Task.Delay(_options.ScheduleCheckInterval, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in scheduler loop");
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
        }

        _logger.LogDebug("Scheduler loop stopped");
    }

    private async Task RunExecutionLoopAsync(CancellationToken ct)
    {
        _logger.LogDebug("Execution loop started");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                ScheduledExecution? scheduled = null;

                lock (_queueLock)
                {
                    if (_executionQueue.Count > 0)
                    {
                        _executionQueue.TryDequeue(out scheduled, out _);
                    }
                }

                if (scheduled != null)
                {
                    await ExecuteScheduledBackfillAsync(scheduled.Schedule, scheduled.Execution, ct);
                }
                else
                {
                    // No executions queued, wait a bit
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in execution loop");
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
        }

        _logger.LogDebug("Execution loop stopped");
    }

    private void EnqueueExecution(BackfillSchedule schedule, BackfillExecutionLog execution, BackfillPriority priority)
    {
        var priorityTime = DateTimeOffset.UtcNow.AddMinutes((int)priority);

        lock (_queueLock)
        {
            _executionQueue.Enqueue(new ScheduledExecution(schedule, execution), priorityTime);
        }

        _logger.LogInformation(
            "Enqueued execution for schedule {ScheduleId} with priority {Priority}",
            schedule.ScheduleId, priority);
    }

    private async Task ExecuteScheduledBackfillAsync(
        BackfillSchedule schedule,
        BackfillExecutionLog execution,
        CancellationToken ct)
    {
        await _executionLock.WaitAsync(ct);
        var jobLeaseHeld = false;

        try
        {
            execution.Status = ExecutionStatus.Running;
            execution.StartedAt = DateTimeOffset.UtcNow;
            ExecutionStarted?.Invoke(this, execution);

            _logger.LogInformation(
                "Starting scheduled backfill: {ScheduleName} ({ScheduleId}), type={Type}",
                schedule.Name, schedule.ScheduleId, schedule.BackfillType);

            // Resolve symbols
            var symbols = execution.Symbols.Count > 0
                ? execution.Symbols
                : _defaultSymbols;

            if (symbols.Count == 0)
            {
                execution.Status = ExecutionStatus.Skipped;
                execution.ErrorMessage = "No symbols configured for backfill";
                _logger.LogWarning("Skipping execution {ExecutionId}: no symbols", execution.ExecutionId);
                return;
            }

            execution.Statistics.TotalSymbols = symbols.Count;

            // Analyze gaps if needed
            if (schedule.BackfillType == ScheduledBackfillType.GapFill)
            {
                _logger.LogDebug("Analyzing gaps for {Count} symbols", symbols.Count);
                var gapAnalysis = await _gapAnalyzer.AnalyzeAsync(
                    symbols,
                    execution.FromDate,
                    execution.ToDate,
                    schedule.Granularity,
                    ct);

                if (_autoGapRemediationService is not null)
                {
                    await _autoGapRemediationService
                        .HandleGapAnalysisResultAsync(gapAnalysis, schedule.PreferredProviders.FirstOrDefault(), ct)
                        .ConfigureAwait(false);
                }

                execution.Statistics.GapsDetected = gapAnalysis.TotalGaps;

                if (gapAnalysis.TotalGaps == 0)
                {
                    execution.Status = ExecutionStatus.Completed;
                    execution.CompletedAt = DateTimeOffset.UtcNow;
                    _logger.LogInformation("No gaps detected, skipping backfill");
                    await _scheduleManager.RecordExecutionAsync(schedule, execution, ct);
                    ExecutionCompleted?.Invoke(this, execution);
                    return;
                }
            }

            // Create backfill job using the schedule template
            var templateJob = schedule.CreateJob(execution.FromDate, execution.ToDate);

            // Create and start the job via the manager
            var job = await _jobManager.CreateJobAsync(
                templateJob.Name,
                symbols,
                execution.FromDate,
                execution.ToDate,
                templateJob.Granularity,
                templateJob.Options,
                templateJob.PreferredProviders,
                ct);

            execution.JobId = job.JobId;

            if (_ownershipService is not null)
            {
                var jobLease = await _ownershipService.TryAcquireJobAsync(job.JobId, ct).ConfigureAwait(false);
                if (!jobLease.Acquired)
                {
                    execution.Status = ExecutionStatus.Skipped;
                    execution.ErrorMessage = $"Job lease is owned by {jobLease.CurrentOwner}";
                    execution.CompletedAt = DateTimeOffset.UtcNow;
                    _logger.LogInformation(
                        "Skipping job {JobId} because lease is owned by {Owner}",
                        job.JobId,
                        jobLease.CurrentOwner);
                    await _scheduleManager.RecordExecutionAsync(schedule, execution, ct);
                    ExecutionCompleted?.Invoke(this, execution);
                    return;
                }

                jobLeaseHeld = true;
            }

            await _jobManager.StartJobAsync(job.JobId, ct);

            // Wait for job completion
            var completed = await WaitForJobCompletionAsync(job.JobId, execution, ct);

            // Update execution status based on job result
            var finalJob = _jobManager.GetJob(job.JobId);
            if (finalJob != null)
            {
                execution.Statistics.TotalBarsRetrieved = finalJob.Statistics.TotalBarsRetrieved;
                execution.Statistics.TotalRequests = finalJob.Statistics.TotalRequestsMade;
                execution.Statistics.SuccessfulRequests = finalJob.Statistics.SuccessfulRequests;
                execution.Statistics.FailedRequests = finalJob.Statistics.FailedRequests;
                execution.Statistics.GapsFilled = finalJob.Statistics.GapsFilled;

                // Update symbol results
                foreach (var (symbol, progress) in finalJob.SymbolProgress)
                {
                    execution.SymbolResults[symbol] = new SymbolExecutionResult
                    {
                        Symbol = symbol,
                        Status = progress.Status switch
                        {
                            SymbolBackfillStatus.Completed => ExecutionStatus.Completed,
                            SymbolBackfillStatus.Failed => ExecutionStatus.Failed,
                            _ => ExecutionStatus.PartialSuccess
                        },
                        BarsRetrieved = progress.BarsRetrieved,
                        Provider = progress.SuccessfulProvider,
                        ErrorMessage = progress.LastError
                    };

                    if (progress.Status == SymbolBackfillStatus.Completed)
                        execution.Statistics.SuccessfulSymbols++;
                    else if (progress.Status == SymbolBackfillStatus.Failed)
                        execution.Statistics.FailedSymbols++;
                }

                execution.Status = finalJob.Status switch
                {
                    BackfillJobStatus.Completed => execution.Statistics.FailedSymbols > 0
                        ? ExecutionStatus.PartialSuccess
                        : ExecutionStatus.Completed,
                    BackfillJobStatus.Failed => ExecutionStatus.Failed,
                    BackfillJobStatus.Cancelled => ExecutionStatus.Cancelled,
                    _ => ExecutionStatus.PartialSuccess
                };
            }

            execution.CompletedAt = DateTimeOffset.UtcNow;

            _logger.LogInformation(
                "Scheduled backfill completed: {ScheduleName}, status={Status}, " +
                "bars={Bars}, gaps_filled={GapsFilled}",
                schedule.Name, execution.Status,
                execution.Statistics.TotalBarsRetrieved,
                execution.Statistics.GapsFilled);

            await _scheduleManager.RecordExecutionAsync(schedule, execution, ct);
            ExecutionCompleted?.Invoke(this, execution);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing scheduled backfill {ExecutionId}", execution.ExecutionId);

            execution.Status = ExecutionStatus.Failed;
            execution.ErrorMessage = ex.Message;
            execution.ErrorStackTrace = ex.StackTrace;
            execution.CompletedAt = DateTimeOffset.UtcNow;

            await _scheduleManager.RecordExecutionAsync(schedule, execution, ct);
            ExecutionCompleted?.Invoke(this, execution);
        }
        finally
        {
            if (_ownershipService is not null)
            {
                if (jobLeaseHeld && !string.IsNullOrWhiteSpace(execution.JobId))
                    await _ownershipService.ReleaseJobAsync(execution.JobId, ct).ConfigureAwait(false);

                await _ownershipService.ReleaseScheduleAsync(schedule.ScheduleId, ct).ConfigureAwait(false);
            }

            _executionLock.Release();
        }
    }

    private async Task<bool> WaitForJobCompletionAsync(
        string jobId,
        BackfillExecutionLog execution,
        CancellationToken ct)
    {
        var timeout = _options.MaxExecutionDuration;
        var startTime = DateTimeOffset.UtcNow;

        while (!ct.IsCancellationRequested)
        {
            var job = _jobManager.GetJob(jobId);
            if (job == null || job.IsComplete)
                return true;

            if (DateTimeOffset.UtcNow - startTime > timeout)
            {
                _logger.LogWarning(
                    "Job {JobId} timed out after {Timeout}",
                    jobId, timeout);
                await _jobManager.CancelJobAsync(jobId, ct);
                return false;
            }

            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }

        return false;
    }

    private Task CatchUpMissedSchedulesAsync(CancellationToken ct)
    {
        var schedules = _scheduleManager.GetEnabledSchedules();
        var now = DateTimeOffset.UtcNow;
        var catchUpWindow = _options.CatchUpWindow;

        foreach (var schedule in schedules)
        {
            if (!schedule.LastExecutedAt.HasValue)
                continue;

            // Check if we missed any executions
            var lastExecution = schedule.LastExecutedAt.Value;
            var missedCount = 0;
            var checkTime = lastExecution;
            const int maxIterations = 1000; // Safety limit to prevent infinite loops

            for (var iteration = 0; iteration < maxIterations; iteration++)
            {
                var nextExecution = schedule.CalculateNextExecution(checkTime);
                if (!nextExecution.HasValue || nextExecution.Value > now)
                    break;

                if (now - nextExecution.Value <= catchUpWindow)
                    missedCount++;

                checkTime = nextExecution.Value;
            }

            if (missedCount > 0)
            {
                _logger.LogInformation(
                    "Catching up {Count} missed executions for schedule {ScheduleId}: {Name}",
                    missedCount, schedule.ScheduleId, schedule.Name);

                // Create a catch-up execution
                var execution = new BackfillExecutionLog
                {
                    ScheduleId = schedule.ScheduleId,
                    ScheduleName = schedule.Name,
                    Trigger = ExecutionTrigger.CatchUp,
                    ScheduledAt = DateTimeOffset.UtcNow,
                    FromDate = DateOnly.FromDateTime(lastExecution.DateTime).AddDays(1),
                    ToDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
                    Symbols = new List<string>(schedule.Symbols)
                };

                EnqueueExecution(schedule, execution, BackfillPriority.Normal);
            }
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _cts?.Dispose();
        _executionLock.Dispose();
    }

    private sealed record ScheduledExecution(BackfillSchedule Schedule, BackfillExecutionLog Execution);
}

/// <summary>
/// Configuration options for the scheduled backfill service.
/// </summary>
public sealed record ScheduledBackfillOptions
{
    /// <summary>
    /// How often to check for due schedules.
    /// </summary>
    public TimeSpan ScheduleCheckInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Maximum duration for a single execution.
    /// </summary>
    public TimeSpan MaxExecutionDuration { get; init; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Whether to catch up missed schedules on startup.
    /// </summary>
    public bool CatchUpMissedSchedules { get; init; } = true;

    /// <summary>
    /// How far back to look for missed schedules.
    /// </summary>
    public TimeSpan CatchUpWindow { get; init; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Maximum concurrent scheduled executions.
    /// </summary>
    public int MaxConcurrentExecutions { get; init; } = 1;

    /// <summary>
    /// Whether to pause executions during market hours.
    /// </summary>
    public bool PauseDuringMarketHours { get; init; } = false;

    /// <summary>
    /// Market open time (Eastern Time).
    /// </summary>
    public TimeOnly MarketOpenTime { get; init; } = new(9, 30);

    /// <summary>
    /// Market close time (Eastern Time).
    /// </summary>
    public TimeOnly MarketCloseTime { get; init; } = new(16, 0);
}

using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Meridian.Contracts.Operations;
using Meridian.Core.Logging;
using Serilog;

namespace Meridian.Storage.Services;

/// <summary>
/// Service for scheduling and coordinating maintenance operations with trading hours awareness.
/// </summary>
public sealed class MaintenanceScheduler : IMaintenanceScheduler, IAsyncDisposable
{
    private const string MaintenanceCaseType = "maintenance-job";
    private const string MaintenanceActor = "system:maintenance-scheduler";
    private const string ExpectedPreviousCaseSequenceDataKey = "expectedPreviousCaseSequence";
    private const string ExpectedPreviousCaseRecordHashDataKey = "expectedPreviousCaseRecordHashSha256";

    private readonly ILogger _log = LoggingSetup.ForContext<MaintenanceScheduler>();
    private readonly OperationalScheduleConfig _config;
    private readonly IFileMaintenanceService _fileMaintenanceService;
    private readonly ITierMigrationService _tierMigrationService;
    private readonly IDataQualityService _dataQualityService;
    private readonly IStorageSearchService? _storageSearchService;
    private readonly IOperationalCaseHistoryStore? _caseHistoryStore;

    private readonly ConcurrentQueue<ScheduledJob> _jobQueue = new();
    private readonly ConcurrentDictionary<string, ScheduledJob> _runningJobs = new();
    private readonly ConcurrentDictionary<string, JobExecutionStatus> _jobHistory = new();
    private readonly ConcurrentDictionary<string, Task> _executionTasks = new();

    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _historyHydrationGate = new(1, 1);
    private Task? _schedulerTask;
    private bool _historyHydrated;
    private bool _isDisposed;

    public MaintenanceScheduler(
        OperationalScheduleConfig config,
        IFileMaintenanceService fileMaintenanceService,
        ITierMigrationService tierMigrationService,
        IDataQualityService dataQualityService,
        IStorageSearchService? storageSearchService = null,
        IOperationalCaseHistoryStore? caseHistoryStore = null)
    {
        _config = config;
        _fileMaintenanceService = fileMaintenanceService;
        _tierMigrationService = tierMigrationService;
        _dataQualityService = dataQualityService;
        _storageSearchService = storageSearchService;
        _caseHistoryStore = caseHistoryStore;
    }

    public void Start()
    {
        _schedulerTask = RunSchedulerLoopAsync(_cts.Token);
    }

    public Task<ScheduleDecision> CanRunNowAsync(
        MaintenanceType operation,
        ResourceRequirements requirements,
        CancellationToken ct = default)
    {
        var now = GetCurrentTime();
        var window = FindCurrentMaintenanceWindow(now);

        if (window == null)
        {
            var nextWindow = FindNextMaintenanceWindow(now);
            return Task.FromResult(new ScheduleDecision(
                Allowed: false,
                Reason: "No active maintenance window",
                CurrentWindow: null,
                WaitTime: nextWindow != null ? GetTimeUntilWindow(now, nextWindow) : null,
                ApplicableLimits: null
            ));
        }

        // Check if operation is allowed in this window
        if (window.AllowedOperations.Length > 0 &&
            !window.AllowedOperations.Contains(operation) &&
            !window.AllowedOperations.Contains(MaintenanceType.All))
        {
            return Task.FromResult(new ScheduleDecision(
                Allowed: false,
                Reason: $"Operation {operation} not allowed in {window.Name} window",
                CurrentWindow: window,
                WaitTime: null,
                ApplicableLimits: window.Limits
            ));
        }

        // Check resource availability
        var runningCount = _runningJobs.Count;
        if (runningCount >= window.MaxConcurrentJobs)
        {
            return Task.FromResult(new ScheduleDecision(
                Allowed: false,
                Reason: $"Max concurrent jobs ({window.MaxConcurrentJobs}) reached",
                CurrentWindow: window,
                WaitTime: TimeSpan.FromMinutes(5),
                ApplicableLimits: window.Limits
            ));
        }

        return Task.FromResult(new ScheduleDecision(
            Allowed: true,
            Reason: "Allowed",
            CurrentWindow: window,
            WaitTime: null,
            ApplicableLimits: window.Limits
        ));
    }

    public Task<ScheduleSlot?> FindNextWindowAsync(
        MaintenanceType operation,
        TimeSpan estimatedDuration,
        ResourceRequirements requirements,
        CancellationToken ct = default)
    {
        var now = GetCurrentTime();

        // Check current window first
        var currentWindow = FindCurrentMaintenanceWindow(now);
        if (currentWindow != null && IsOperationAllowed(currentWindow, operation))
        {
            var remainingTime = GetRemainingWindowTime(now, currentWindow);
            if (remainingTime >= estimatedDuration)
            {
                return Task.FromResult<ScheduleSlot?>(new ScheduleSlot(
                    Start: now,
                    End: now + remainingTime,
                    Window: currentWindow,
                    Limits: currentWindow.Limits,
                    AvailableConcurrencySlots: currentWindow.MaxConcurrentJobs - _runningJobs.Count
                ));
            }
        }

        // Find next suitable window
        for (int daysAhead = 0; daysAhead < 7; daysAhead++)
        {
            var checkDate = now.Date.AddDays(daysAhead);
            var dayOfWeek = checkDate.DayOfWeek;

            foreach (var window in _config.MaintenanceWindows)
            {
                if (!window.Days.Contains(dayOfWeek))
                    continue;

                if (!IsOperationAllowed(window, operation))
                    continue;

                var windowStart = checkDate + window.Start;
                var windowEnd = checkDate + window.End;

                // Handle overnight windows
                if (window.End < window.Start)
                    windowEnd = windowEnd.AddDays(1);

                if (windowStart > now && (windowEnd - windowStart) >= estimatedDuration)
                {
                    return Task.FromResult<ScheduleSlot?>(new ScheduleSlot(
                        Start: windowStart,
                        End: windowEnd,
                        Window: window,
                        Limits: window.Limits,
                        AvailableConcurrencySlots: window.MaxConcurrentJobs
                    ));
                }
            }
        }

        return Task.FromResult<ScheduleSlot?>(null);
    }

    public async Task<ScheduledJob> ScheduleAsync(
        MaintenanceJob job,
        ScheduleOptions options,
        CancellationToken ct = default)
    {
        await EnsureHistoryHydratedAsync(ct).ConfigureAwait(false);
        var slot = await FindNextWindowAsync(job.Type, job.EstimatedDuration, job.Requirements, ct);

        var scheduledId = string.IsNullOrWhiteSpace(job.Id) ? Guid.NewGuid().ToString() : job.Id;
        var scheduledJob = new ScheduledJob(
            Id: scheduledId,
            Job: job with { Id = scheduledId },
            ScheduledStart: slot?.Start ?? DateTimeOffset.UtcNow.AddDays(1),
            Status: JobStatus.Pending,
            CreatedAt: DateTimeOffset.UtcNow
        );

        await AppendScheduledHistoryAsync(scheduledJob, ct).ConfigureAwait(false);
        _jobQueue.Enqueue(scheduledJob);
        return scheduledJob;
    }

    public async Task<OperationalState> GetStateAsync(CancellationToken ct = default)
    {
        await EnsureHistoryHydratedAsync(ct).ConfigureAwait(false);
        var now = GetCurrentTime();
        var currentSession = GetCurrentTradingSession(now);
        var currentWindow = FindCurrentMaintenanceWindow(now);
        var nextWindow = currentWindow == null ? FindNextMaintenanceWindow(now) : null;

        return new OperationalState(
            IsRealTimeCollectionActive: currentSession != null && IsWithinTradingHours(now, currentSession),
            CurrentSession: currentSession,
            CurrentMaintenanceWindow: currentWindow,
            RunningMaintenanceJobs: _runningJobs.Count,
            PendingJobs: _jobQueue.ToArray(),
            NextMaintenanceWindowStart: nextWindow != null
                ? now.Date + nextWindow.Start
                : DateTimeOffset.UtcNow.AddDays(1)
        );
    }

    public bool IsRealTimeCollectionActive()
    {
        var now = GetCurrentTime();
        var session = GetCurrentTradingSession(now);
        return session != null && IsWithinTradingHours(now, session);
    }

    public TimeSpan? GetTimeUntilCollectionEnds()
    {
        var now = GetCurrentTime();
        var session = GetCurrentTradingSession(now);

        if (session == null || !IsWithinTradingHours(now, session))
            return null;

        var endTime = now.Date + session.AfterHoursEnd;
        return endTime - now;
    }

    public async Task<JobExecutionStatus> ExecuteJobAsync(MaintenanceJob job, CancellationToken ct = default)
    {
        var startTime = DateTimeOffset.UtcNow;
        var admissionRetained = false;
        OperationalCaseHistoryRecord? runningHistory = null;
        var status = new JobExecutionStatus(
            JobId: job.Id,
            Type: job.Type,
            StartedAt: startTime,
            CompletedAt: null,
            Status: JobStatus.Running,
            Progress: 0,
            Message: "Starting...",
            Errors: new List<string>()
        );

        if (_caseHistoryStore is null)
        {
            var completedAt = DateTimeOffset.UtcNow;
            var message = "Maintenance execution is blocked because operational case-history storage is not configured.";
            var result = MaintenanceOperationResult.Blocked(
                message,
                "Configure a durable IOperationalCaseHistoryStore, verify it can append and read maintenance cases, then retry the job.");
            status = status with
            {
                CompletedAt = completedAt,
                Status = JobStatus.Blocked,
                Progress = 0,
                Message = message,
                Errors = [message],
                AdmissionRetained = false,
                Outcome = BuildOutcome(job, startTime, completedAt, result)
            };
            _jobHistory[job.Id] = status;
            return status;
        }

        try
        {
            await EnsureHistoryHydratedAsync(ct).ConfigureAwait(false);
            var predecessor = await ReadLatestCaseHistoryAsync(job.Id, ct).ConfigureAwait(false);
            if (predecessor?.Transition?.CurrentState is { } retainedState &&
                !string.Equals(retainedState, JobStatus.Pending.ToString(), StringComparison.Ordinal))
            {
                throw new MaintenanceAdmissionBlockedException(
                    $"Maintenance job {job.Id} already retains state {retainedState}; reconcile that attempt before executing again.");
            }

            runningHistory = await AppendRunningHistoryAsync(job, startTime, predecessor, ct).ConfigureAwait(false);
            admissionRetained = true;
            _runningJobs[job.Id] = new ScheduledJob(job.Id, job, startTime, JobStatus.Running, startTime);
            status = status with { Message = $"Executing {job.Type}..." };

            // Execute based on job type
            var result = job.Type switch
            {
                MaintenanceType.HealthCheck => await ExecuteHealthCheckAsync(job, ct),
                MaintenanceType.IntegrityValidation => await ExecuteIntegrityValidationAsync(job, ct),
                MaintenanceType.Compaction => await ExecuteCompactionAsync(job, ct),
                MaintenanceType.TierMigration => await ExecuteTierMigrationAsync(job, ct),
                MaintenanceType.QualityScoring => await ExecuteQualityScoringAsync(job, ct),
                MaintenanceType.IndexRebuild => await ExecuteIndexRebuildAsync(job, ct),
                _ => MaintenanceOperationResult.Blocked(
                    $"Maintenance operation {job.Type} has no registered executor.",
                    $"Register and review an executor for {job.Type}, then retry the job.")
            };

            var completedAt = DateTimeOffset.UtcNow;
            var outcome = BuildOutcome(job, startTime, completedAt, result);

            status = status with
            {
                CompletedAt = completedAt,
                Status = result.State switch
                {
                    OperationTerminalState.Succeeded or OperationTerminalState.CompletedWithWarnings => JobStatus.Completed,
                    OperationTerminalState.Blocked => JobStatus.Blocked,
                    _ => JobStatus.Failed
                },
                Progress = result.State is OperationTerminalState.Succeeded or OperationTerminalState.CompletedWithWarnings ? 100 : 0,
                Message = result.Message,
                Outcome = outcome
            };
        }
        catch (OperationCanceledException) when (!admissionRetained)
        {
            _runningJobs.TryRemove(job.Id, out _);
            throw;
        }
        catch (OperationCanceledException cancellation)
        {
            var completedAt = DateTimeOffset.UtcNow;
            var message = $"Maintenance job {job.Id} was cancelled after admission and did not complete its required work.";
            var result = MaintenanceOperationResult.Failed(
                message,
                "Review retained maintenance evidence for partial side effects, repair incomplete work, and retry the job from a safe point.",
                cancellation);
            status = status with
            {
                CompletedAt = completedAt,
                Status = JobStatus.Failed,
                Progress = 0,
                Message = message,
                Errors = new List<string> { cancellation.ToString() },
                Outcome = BuildOutcome(job, startTime, completedAt, result)
            };
        }
        catch (MaintenanceAdmissionBlockedException blocked)
        {
            var completedAt = DateTimeOffset.UtcNow;
            var result = MaintenanceOperationResult.Blocked(
                blocked.Message,
                "Refresh the retained maintenance case, reconcile any interrupted or completed attempt, and schedule a new job identifier only when retry is safe.");
            status = status with
            {
                CompletedAt = completedAt,
                Status = JobStatus.Blocked,
                Progress = 0,
                Message = blocked.Message,
                Errors = new List<string> { blocked.Message },
                Outcome = BuildOutcome(job, startTime, completedAt, result)
            };
        }
        catch (Exception ex)
        {
            var completedAt = DateTimeOffset.UtcNow;
            var result = MaintenanceOperationResult.Failed(
                ex.Message,
                "Correct the recorded maintenance exception and retry the job.",
                ex);
            status = status with
            {
                CompletedAt = completedAt,
                Status = JobStatus.Failed,
                Message = ex.Message,
                Errors = new List<string> { ex.ToString() },
                Outcome = BuildOutcome(job, startTime, completedAt, result)
            };
        }

        status = status with { AdmissionRetained = admissionRetained };
        if (!admissionRetained)
        {
            _runningJobs.TryRemove(job.Id, out _);
            _jobHistory[job.Id] = status;
            return status;
        }

        try
        {
            await AppendTerminalHistoryAsync(job, status, runningHistory, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception historyException)
        {
            var completedAt = DateTimeOffset.UtcNow;
            var result = MaintenanceOperationResult.Failed(
                $"Maintenance terminal evidence could not be persisted: {historyException.Message}",
                "Repair operational case-history storage, then retry the maintenance job so its terminal evidence is retained.",
                historyException);
            status = status with
            {
                CompletedAt = completedAt,
                Status = JobStatus.Failed,
                Progress = 0,
                Message = result.Message,
                Errors = status.Errors.Concat([historyException.ToString()]).ToArray(),
                Outcome = BuildOutcome(job, startTime, completedAt, result)
            };
        }
        finally
        {
            _runningJobs.TryRemove(job.Id, out _);
            _jobHistory[job.Id] = status;
        }
        return status;
    }

    public IReadOnlyList<ScheduledJob> GetPendingJobs() => _jobQueue.ToArray();

    public IReadOnlyList<ScheduledJob> GetRunningJobs() => _runningJobs.Values.ToList();

    public JobExecutionStatus? GetJobStatus(string jobId)
    {
        return _jobHistory.TryGetValue(jobId, out var status) ? status : null;
    }

    private async Task RunSchedulerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProcessJobQueueAsync(ct);
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A single failed iteration must not tear down the scheduler loop, but it must
                // be recorded — silently swallowing it hid recurring maintenance failures.
                _log.Error(ex, "Maintenance scheduler loop iteration failed; continuing after 30s");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task ProcessJobQueueAsync(CancellationToken ct)
    {
        await EnsureHistoryHydratedAsync(ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        while (_jobQueue.TryPeek(out var job))
        {
            // Requeued admission failures carry a future retry instant. Respect it so a broken
            // history store cannot create a hot retry loop or an ever-growing set of executions.
            if (job.ScheduledStart > now)
                break;

            var decision = await CanRunNowAsync(job.Job.Type, job.Job.Requirements, ct);

            if (!decision.Allowed)
                break;

            if (_jobQueue.TryDequeue(out job))
            {
                TrackScheduledExecution(job, ct);
            }
        }
    }

    private void TrackScheduledExecution(ScheduledJob scheduledJob, CancellationToken ct)
    {
        var execution = ExecuteScheduledJobAsync(scheduledJob, ct);
        _executionTasks[scheduledJob.Id] = execution;
        _ = ObserveScheduledExecutionAsync(scheduledJob.Id, execution);
    }

    private async Task ExecuteScheduledJobAsync(ScheduledJob scheduledJob, CancellationToken ct)
    {
        try
        {
            var status = await ExecuteJobAsync(scheduledJob.Job, ct).ConfigureAwait(false);
            if (!status.AdmissionRetained && status.Status != JobStatus.Blocked && !ct.IsCancellationRequested)
            {
                _jobHistory.TryRemove(scheduledJob.Id, out _);
                _jobQueue.Enqueue(scheduledJob with
                {
                    Status = JobStatus.Pending,
                    ScheduledStart = DateTimeOffset.UtcNow.AddSeconds(30)
                });
                _log.Warning(
                    "Maintenance job {JobId} was requeued because its running admission could not be retained",
                    scheduledJob.Id);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Scheduler shutdown leaves durable scheduled/running evidence available for replay.
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                _jobQueue.Enqueue(scheduledJob with
                {
                    Status = JobStatus.Pending,
                    ScheduledStart = DateTimeOffset.UtcNow.AddSeconds(30)
                });
            }
            _log.Error(ex, "Scheduled maintenance job {JobId} faulted and was preserved for retry", scheduledJob.Id);
        }
    }

    private async Task ObserveScheduledExecutionAsync(string jobId, Task execution)
    {
        try
        {
            await execution.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // ExecuteScheduledJobAsync handles expected failures; this is a last-resort observation guard.
            _log.Error(ex, "Observed unexpected scheduled maintenance task fault for {JobId}", jobId);
        }
        finally
        {
            if (_executionTasks.TryGetValue(jobId, out var tracked) && ReferenceEquals(tracked, execution))
                _executionTasks.TryRemove(jobId, out _);
        }
    }

    private async Task<MaintenanceOperationResult> ExecuteHealthCheckAsync(MaintenanceJob job, CancellationToken ct)
    {
        var result = await _fileMaintenanceService.RunHealthCheckAsync(new HealthCheckOptions
        {
            ValidateChecksums = true,
            CheckSequenceContinuity = true,
            IdentifyCorruption = true,
            Paths = job.TargetPaths,
            ParallelChecks = 4
        }, ct);

        var message = $"Health check complete: {result.Summary.HealthyFiles} healthy, {result.Summary.CorruptedFiles} corrupted";
        return result.Summary.CorruptedFiles == 0
            ? MaintenanceOperationResult.Succeeded(message)
            : MaintenanceOperationResult.Failed(message, "Repair or restore the corrupted files, then rerun the health check.");
    }

    private async Task<MaintenanceOperationResult> ExecuteIntegrityValidationAsync(MaintenanceJob job, CancellationToken ct)
    {
        var result = await _fileMaintenanceService.RunHealthCheckAsync(new HealthCheckOptions
        {
            ValidateChecksums = true,
            ValidateSchemas = true,
            Paths = job.TargetPaths
        }, ct);

        var message = $"Integrity validation complete: {result.Summary.TotalFiles} files checked";
        return result.Summary.CorruptedFiles == 0
            ? MaintenanceOperationResult.Succeeded(message)
            : MaintenanceOperationResult.Failed(message, "Repair or restore files that failed integrity validation, then retry.");
    }

    private async Task<MaintenanceOperationResult> ExecuteCompactionAsync(MaintenanceJob job, CancellationToken ct)
    {
        var result = await _fileMaintenanceService.DefragmentAsync(new DefragOptions(), ct);
        var errors = result.Errors ?? [];
        var failedGroups = Math.Max(0, result.MergeGroupsAttempted - result.MergeGroupsSucceeded);
        var message =
            $"Compaction attempted {result.MergeGroupsAttempted} merge group(s): " +
            $"{result.MergeGroupsSucceeded} succeeded, {failedGroups} failed; " +
            $"{result.FilesProcessed} files processed and {result.BytesBefore - result.BytesAfter} bytes saved.";
        if (errors.Count == 0 && failedGroups == 0)
            return MaintenanceOperationResult.Succeeded(message);

        var errorSummary = string.Join("; ", errors.Take(3));
        return MaintenanceOperationResult.Failed(
            string.IsNullOrWhiteSpace(errorSummary) ? message : $"{message} {errorSummary}",
            "Inspect the failed merge/deletion evidence, remove any duplicate merged/source copies safely, and retry compaction.");
    }

    private async Task<MaintenanceOperationResult> ExecuteTierMigrationAsync(MaintenanceJob job, CancellationToken ct)
    {
        var plan = await _tierMigrationService.PlanMigrationAsync(TimeSpan.FromDays(1), ct);
        var results = new List<(PlannedMigrationAction Action, MigrationResult Result)>();

        foreach (var action in plan.Actions)
        {
            var result = await _tierMigrationService
                .MigrateAsync(action.SourcePath, action.TargetTier, new MigrationOptions(), ct)
                .ConfigureAwait(false);
            results.Add((action, result));
        }

        var filesProcessed = results.Sum(item => item.Result.FilesProcessed);
        var filesFailed = results.Sum(item => item.Result.FilesFailed);
        var failedActions = results
            .Where(item => !item.Result.Success || item.Result.FilesFailed > 0 || item.Result.Errors.Count > 0)
            .ToArray();
        var message =
            $"Tier migration attempted {plan.Actions.Count} action(s): {filesProcessed} files processed, " +
            $"{filesFailed} files failed, {failedActions.Length} action(s) unsuccessful.";
        if (failedActions.Length == 0)
            return MaintenanceOperationResult.Succeeded(message);

        var errorSummary = string.Join(
            "; ",
            failedActions.SelectMany(item => item.Result.Errors.Count == 0
                    ? [$"{item.Action.SourcePath}: migration returned Success=false"]
                    : item.Result.Errors.Select(error => $"{item.Action.SourcePath}: {error}"))
                .Take(3));
        return MaintenanceOperationResult.Failed(
            $"{message} {errorSummary}",
            "Inspect failed migration results, verify source and target copies and checksums, then retry every unsuccessful action.");
    }

    private async Task<MaintenanceOperationResult> ExecuteQualityScoringAsync(MaintenanceJob job, CancellationToken ct)
    {
        var report = await _dataQualityService.GenerateReportAsync(new QualityReportOptions(
            Paths: job.TargetPaths,
            MinScoreThreshold: 1.0,
            IncludeRecommendations: true
        ), ct);

        var message =
            $"Quality scoring attempted {report.FilesAttempted} input(s): {report.FilesSucceeded} succeeded, " +
            $"{report.FilesFailed} failed, avg successful score: {report.AverageScore:F2}.";
        if (report.FilesAttempted == 0)
        {
            return MaintenanceOperationResult.Blocked(
                "Quality scoring blocked because no eligible input files were discovered.",
                "Verify the configured target paths contain readable JSONL inputs, then retry quality scoring.");
        }

        if (report.FilesFailed == 0)
            return MaintenanceOperationResult.Succeeded(message);

        var issueSummary = string.Join(
            "; ",
            report.Issues.Take(3).Select(issue => $"{issue.Path}: {issue.Message}"));
        if (report.FilesSucceeded == 0)
        {
            return MaintenanceOperationResult.Failed(
                $"{message} {issueSummary}",
                "Restore access to the failed quality inputs, inspect retained exceptions, and retry the complete scoring job.");
        }

        return MaintenanceOperationResult.CompletedWithWarnings(
            $"{message} {issueSummary}",
            "Review and repair the failed quality inputs, then retry them before treating the report as complete.");
    }

    private async Task<MaintenanceOperationResult> ExecuteIndexRebuildAsync(MaintenanceJob job, CancellationToken ct)
    {
        if (_storageSearchService is null)
        {
            return MaintenanceOperationResult.Blocked(
                "Index rebuild blocked because no storage search service was supplied.",
                "Supply IStorageSearchService to MaintenanceScheduler and retry the index rebuild.");
        }

        var targetPaths = job.TargetPaths?
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (targetPaths.Length == 0)
        {
            return MaintenanceOperationResult.Blocked(
                "Index rebuild blocked because no nonempty target paths were requested.",
                "Provide at least one explicit file or directory scope containing indexable data, then retry the rebuild.");
        }

        var verification = await _storageSearchService
            .RebuildIndexAsync(targetPaths, new RebuildOptions(), ct)
            .ConfigureAwait(false);
        if (verification is null)
        {
            return MaintenanceOperationResult.Blocked(
                "Index rebuild returned no verification receipt; completion cannot be established.",
                "Configure an IStorageSearchService that returns indexed-count, digest, and readback proof, then retry the rebuild.");
        }

        if (!verification.AllDiscoveredFilesIndexed)
        {
            return MaintenanceOperationResult.Failed(
                $"Index rebuild indexed {verification.After.IndexedFileCount} of " +
                $"{verification.DiscoveredFileCount} discovered file(s); completion cannot be established.",
                "Inspect unreadable or omitted inputs, repair the requested scope, and retry the entire index rebuild.",
                verification: verification);
        }

        if (!verification.ReadbackVerified)
        {
            return MaintenanceOperationResult.Failed(
                "Index rebuild readback did not match the staged indexed count and digest.",
                "Preserve the retained before/after/readback evidence, repair index publication or concurrent mutation, and retry the rebuild.",
                verification: verification);
        }

        return MaintenanceOperationResult.Succeeded(
            $"Index rebuild verified {verification.After.IndexedFileCount} indexed file(s) across " +
            $"{targetPaths.Length} target path(s); readback digest {verification.Readback.DigestSha256}.",
            verification);
    }

    private async Task EnsureHistoryHydratedAsync(CancellationToken ct)
    {
        if (_caseHistoryStore is null || _historyHydrated)
            return;

        await _historyHydrationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_historyHydrated)
                return;

            var records = await _caseHistoryStore.ReadAsync(new OperationalCaseHistoryQuery
            {
                CaseType = MaintenanceCaseType
            }, ct).ConfigureAwait(false);

            foreach (var history in records
                         .GroupBy(record => record.CaseId, StringComparer.Ordinal)
                         .Select(group => group.OrderBy(record => record.Sequence).ToArray()))
            {
                var scheduledRecord = history.FirstOrDefault(record =>
                    string.Equals(record.EventType, "maintenance.scheduled", StringComparison.Ordinal));
                if (scheduledRecord is null || !TryReadJob(scheduledRecord.Data, out var job))
                    continue;

                var terminalRecord = history.LastOrDefault(record => record.TerminalOutcome is not null);
                if (terminalRecord?.TerminalOutcome is { } terminalOutcome)
                {
                    var startedAt = history.FirstOrDefault(record =>
                        string.Equals(record.EventType, "maintenance.running", StringComparison.Ordinal))?.OccurredAtUtc
                        ?? scheduledRecord.OccurredAtUtc;
                    _jobHistory[job.Id] = new JobExecutionStatus(
                        job.Id,
                        job.Type,
                        startedAt,
                        terminalOutcome.CompletedAtUtc,
                        MapJobStatus(terminalOutcome.State),
                        terminalOutcome.IsSuccessful ? 100 : 0,
                        terminalRecord.Reason,
                        terminalOutcome.Issues
                            .Where(issue => issue.Severity == OperationIssueSeverity.Error)
                            .Select(issue => issue.Message)
                            .ToArray())
                    {
                        Outcome = terminalOutcome
                    };
                    continue;
                }

                if (_jobQueue.Any(pending => string.Equals(pending.Id, job.Id, StringComparison.Ordinal)))
                    continue;

                var scheduledStart = scheduledRecord.Data.TryGetValue("scheduledStart", out var scheduledText) &&
                                     DateTimeOffset.TryParse(scheduledText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedStart)
                    ? parsedStart
                    : DateTimeOffset.UtcNow;
                _jobQueue.Enqueue(new ScheduledJob(
                    job.Id,
                    job,
                    scheduledStart,
                    JobStatus.Pending,
                    scheduledRecord.OccurredAtUtc));
            }

            _historyHydrated = true;
        }
        finally
        {
            _historyHydrationGate.Release();
        }
    }

    private async ValueTask AppendScheduledHistoryAsync(ScheduledJob scheduledJob, CancellationToken ct)
    {
        if (_caseHistoryStore is null)
            return;

        var occurredAtUtc = DateTimeOffset.UtcNow;
        await _caseHistoryStore.AppendAsync(new OperationalCaseHistoryAppendRequest
        {
            CaseId = scheduledJob.Id,
            CaseType = MaintenanceCaseType,
            HistoryEventId = $"maintenance:{scheduledJob.Id}:scheduled:{Guid.NewGuid():N}",
            EventType = "maintenance.scheduled",
            OccurredAtUtc = occurredAtUtc,
            ActorId = MaintenanceActor,
            Reason = "Maintenance job accepted into the durable pending queue.",
            CorrelationId = scheduledJob.Id,
            InputHashSha256 = ComputeInputHash(scheduledJob.Job),
            Data = WithExpectedPredecessor(
                CreateHistoryData(scheduledJob.Job, scheduledJob.ScheduledStart, JobStatus.Pending, "Scheduled"),
                predecessor: null),
            Transition = new OperationalCaseStateTransition
            {
                PreviousState = null,
                CurrentState = JobStatus.Pending.ToString(),
                TransitionedAtUtc = occurredAtUtc
            }
        }, ct).ConfigureAwait(false);
    }

    private async ValueTask<OperationalCaseHistoryRecord?> AppendRunningHistoryAsync(
        MaintenanceJob job,
        DateTimeOffset startedAtUtc,
        OperationalCaseHistoryRecord? predecessor,
        CancellationToken ct)
    {
        if (_caseHistoryStore is null)
            return null;

        try
        {
            return await _caseHistoryStore.AppendAsync(new OperationalCaseHistoryAppendRequest
            {
                CaseId = job.Id,
                CaseType = MaintenanceCaseType,
                HistoryEventId = $"maintenance:{job.Id}:running:{Guid.NewGuid():N}",
                EventType = "maintenance.running",
                OccurredAtUtc = startedAtUtc,
                ActorId = MaintenanceActor,
                Reason = $"Maintenance executor started {job.Type} attempt 1.",
                CorrelationId = job.Id,
                InputHashSha256 = ComputeInputHash(job),
                Data = WithExpectedPredecessor(
                    CreateHistoryData(job, null, JobStatus.Running, "Running"),
                    predecessor),
                Transition = new OperationalCaseStateTransition
                {
                    PreviousState = predecessor?.Transition?.CurrentState,
                    CurrentState = JobStatus.Running.ToString(),
                    TransitionedAtUtc = startedAtUtc
                },
                Retries = [new OperationalCaseRetry
                {
                    Attempt = 1,
                    AttemptedAtUtc = startedAtUtc,
                    Reason = "Initial maintenance execution attempt."
                }]
            }, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException conflict)
        {
            throw new MaintenanceAdmissionBlockedException(
                $"Maintenance job {job.Id} admission lost its expected case-history predecessor; refresh and reconcile before retrying.",
                conflict);
        }
    }

    private async ValueTask AppendTerminalHistoryAsync(
        MaintenanceJob job,
        JobExecutionStatus status,
        OperationalCaseHistoryRecord? predecessor,
        CancellationToken ct)
    {
        if (_caseHistoryStore is null)
            return;
        if (status.Outcome is null || status.CompletedAt is null)
            throw new InvalidOperationException("A terminal maintenance status must include a verified outcome and completion time.");

        var outcome = status.Outcome;
        var exceptions = outcome.Issues
            .Where(issue => issue.Severity == OperationIssueSeverity.Error)
            .Select(issue => new OperationalCaseException
            {
                ExceptionType = issue.ExceptionType ?? "MaintenanceOperationFailure",
                Message = issue.Message,
                OccurredAtUtc = status.CompletedAt.Value,
                EvidenceIds = issue.EvidenceId is null ? [] : [issue.EvidenceId]
            })
            .ToArray();
        await _caseHistoryStore.AppendAsync(new OperationalCaseHistoryAppendRequest
        {
            CaseId = job.Id,
            CaseType = MaintenanceCaseType,
            HistoryEventId = outcome.OperationId,
            EventType = $"maintenance.terminal.{outcome.State.ToString().ToLowerInvariant()}",
            OccurredAtUtc = status.CompletedAt.Value,
            ActorId = MaintenanceActor,
            Reason = status.Message,
            CorrelationId = job.Id,
            InputHashSha256 = ComputeInputHash(job),
            Data = WithExpectedPredecessor(
                CreateHistoryData(job, null, status.Status, status.Message),
                predecessor),
            Transition = new OperationalCaseStateTransition
            {
                PreviousState = predecessor?.Transition?.CurrentState,
                CurrentState = status.Status.ToString(),
                TransitionedAtUtc = status.CompletedAt.Value
            },
            Exceptions = exceptions,
            Evidence = outcome.Evidence,
            Artifacts = outcome.Artifacts,
            TerminalOutcome = outcome
        }, ct).ConfigureAwait(false);
    }

    private async ValueTask<OperationalCaseHistoryRecord?> ReadLatestCaseHistoryAsync(
        string caseId,
        CancellationToken ct)
    {
        if (_caseHistoryStore is null)
            return null;

        var records = await _caseHistoryStore.ReadAsync(new OperationalCaseHistoryQuery
        {
            CaseId = caseId,
            CaseType = MaintenanceCaseType
        }, ct).ConfigureAwait(false);
        return records.OrderBy(record => record.Sequence).LastOrDefault();
    }

    private static IReadOnlyDictionary<string, string> WithExpectedPredecessor(
        IReadOnlyDictionary<string, string> data,
        OperationalCaseHistoryRecord? predecessor)
    {
        var coordinated = new Dictionary<string, string>(data, StringComparer.Ordinal)
        {
            [ExpectedPreviousCaseSequenceDataKey] =
                (predecessor?.Sequence ?? 0).ToString(CultureInfo.InvariantCulture)
        };
        if (predecessor is not null)
            coordinated[ExpectedPreviousCaseRecordHashDataKey] = predecessor.RecordHashSha256;
        return coordinated;
    }

    private static IReadOnlyDictionary<string, string> CreateHistoryData(
        MaintenanceJob job,
        DateTimeOffset? scheduledStart,
        JobStatus status,
        string message)
    {
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["jobId"] = job.Id,
            ["type"] = job.Type.ToString(),
            ["priority"] = job.Priority.ToString(),
            ["description"] = Encode(job.Description),
            ["estimatedDurationTicks"] = job.EstimatedDuration.Ticks.ToString(CultureInfo.InvariantCulture),
            ["targetPaths"] = string.Join(',', job.TargetPaths.Select(Encode)),
            ["cpuCores"] = job.Requirements.CpuCores.ToString(CultureInfo.InvariantCulture),
            ["memoryBytes"] = job.Requirements.MemoryBytes.ToString(CultureInfo.InvariantCulture),
            ["diskIoMbps"] = job.Requirements.DiskIoMbps.ToString(CultureInfo.InvariantCulture),
            ["networkIoMbps"] = job.Requirements.NetworkIoMbps.ToString(CultureInfo.InvariantCulture),
            ["requiresExclusiveLock"] = job.Requirements.RequiresExclusiveLock.ToString(CultureInfo.InvariantCulture),
            ["exclusivePaths"] = string.Join(',', (job.Requirements.ExclusivePaths ?? []).Select(Encode)),
            ["interruptible"] = job.Interruptible.ToString(CultureInfo.InvariantCulture),
            ["maxRetries"] = job.MaxRetries.ToString(CultureInfo.InvariantCulture),
            ["status"] = status.ToString(),
            ["message"] = Encode(message)
        };
        if (scheduledStart.HasValue)
            data["scheduledStart"] = scheduledStart.Value.ToString("O", CultureInfo.InvariantCulture);
        if (job.Parameters is not null)
        {
            foreach (var (key, value) in job.Parameters)
                data[$"parameter:{Encode(key)}"] = Encode(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }

        return data;
    }

    private static bool TryReadJob(IReadOnlyDictionary<string, string> data, out MaintenanceJob job)
    {
        job = null!;
        if (!data.TryGetValue("jobId", out var jobId) || string.IsNullOrWhiteSpace(jobId) ||
            !data.TryGetValue("type", out var typeText) || !Enum.TryParse<MaintenanceType>(typeText, out var type) ||
            !data.TryGetValue("priority", out var priorityText) || !Enum.TryParse<JobPriority>(priorityText, out var priority))
        {
            return false;
        }

        var parameters = data
            .Where(pair => pair.Key.StartsWith("parameter:", StringComparison.Ordinal))
            .ToDictionary(
                pair => Decode(pair.Key["parameter:".Length..]),
                pair => (object)Decode(pair.Value),
                StringComparer.Ordinal);
        job = new MaintenanceJob(
            jobId,
            type,
            priority,
            data.TryGetValue("description", out var description) ? Decode(description) : string.Empty,
            new ResourceRequirements(
                ParseInt(data, "cpuCores", 1),
                ParseLong(data, "memoryBytes", 1_073_741_824),
                ParseLong(data, "diskIoMbps", 100),
                ParseLong(data, "networkIoMbps", 0),
                ParseBool(data, "requiresExclusiveLock", false),
                ParseEncodedArray(data, "exclusivePaths")),
            TimeSpan.FromTicks(ParseLong(data, "estimatedDurationTicks", TimeSpan.FromMinutes(5).Ticks)),
            ParseEncodedArray(data, "targetPaths"),
            parameters.Count == 0 ? null : parameters,
            ParseBool(data, "interruptible", true),
            ParseInt(data, "maxRetries", 3));
        return true;
    }

    private static JobStatus MapJobStatus(OperationTerminalState state) => state switch
    {
        OperationTerminalState.Succeeded or OperationTerminalState.CompletedWithWarnings => JobStatus.Completed,
        OperationTerminalState.Blocked => JobStatus.Blocked,
        _ => JobStatus.Failed
    };

    private static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));

    private static string Decode(string value) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(value));

    private static string[] ParseEncodedArray(IReadOnlyDictionary<string, string> data, string key) =>
        data.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Decode).ToArray()
            : [];

    private static int ParseInt(IReadOnlyDictionary<string, string> data, string key, int fallback) =>
        data.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static long ParseLong(IReadOnlyDictionary<string, string> data, string key, long fallback) =>
        data.TryGetValue(key, out var value) && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static bool ParseBool(IReadOnlyDictionary<string, string> data, string key, bool fallback) =>
        data.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static VerifiedOperationOutcome BuildOutcome(
        MaintenanceJob job,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        MaintenanceOperationResult result)
    {
        var operationId = $"maintenance:{job.Id}:{Guid.NewGuid():N}";
        var evidenceId = $"{operationId}:execution";
        var postconditionState = result.State is OperationTerminalState.Succeeded or OperationTerminalState.CompletedWithWarnings
            ? OperationPostconditionState.Satisfied
            : OperationPostconditionState.NotSatisfied;
        var issues = result.State switch
        {
            OperationTerminalState.CompletedWithWarnings =>
                (IReadOnlyList<OperationIssue>)[new OperationIssue("maintenance-warning", result.Message, OperationIssueSeverity.Warning, EvidenceId: evidenceId)],
            OperationTerminalState.Failed =>
                [new OperationIssue("maintenance-failed", result.Message, OperationIssueSeverity.Error, result.Exception?.GetType().FullName, evidenceId)],
            OperationTerminalState.Blocked =>
                [new OperationIssue("maintenance-blocked", result.Message, OperationIssueSeverity.Error, EvidenceId: evidenceId)
                {
                    IsBlocking = true
                }],
            _ => []
        };
        var recovery = result.RecoveryGuidance is null
            ? []
            : (IReadOnlyList<OperationRecoveryAction>)[new OperationRecoveryAction(
                "recover-maintenance-job",
                "Recover maintenance job",
                result.RecoveryGuidance,
                Retryable: true,
                RequiresHumanAction: true,
                Route: $"maintenance://jobs/{job.Id}")
            {
                EvidenceIds = [evidenceId]
            }];

        var postconditions = new List<OperationPostcondition>
        {
            new(
                "operation-executed",
                $"The {job.Type} maintenance operation completed its required work.",
                postconditionState,
                Required: true,
                EvidenceIds: [evidenceId])
        };
        var evidence = new List<OperationEvidenceReference>
        {
            new(
                evidenceId,
                "maintenance-execution",
                result.Message,
                Uri: $"maintenance://jobs/{job.Id}",
                CapturedAtUtc: completedAtUtc)
        };
        if (result.IndexRebuildVerification is { } verification)
        {
            var beforeEvidenceId = $"{operationId}:index-before";
            var afterEvidenceId = $"{operationId}:index-after";
            var readbackEvidenceId = $"{operationId}:index-readback";
            evidence.AddRange(
            [
                new OperationEvidenceReference(
                    beforeEvidenceId,
                    "index-snapshot-before",
                    $"Index before rebuild contained {verification.Before.IndexedFileCount} file(s).",
                    ContentHashSha256: verification.Before.DigestSha256,
                    CapturedAtUtc: verification.Before.CapturedAtUtc),
                new OperationEvidenceReference(
                    afterEvidenceId,
                    "index-snapshot-after",
                    $"Staged rebuild contained {verification.After.IndexedFileCount} of " +
                    $"{verification.DiscoveredFileCount} discovered file(s).",
                    ContentHashSha256: verification.After.DigestSha256,
                    CapturedAtUtc: verification.After.CapturedAtUtc),
                new OperationEvidenceReference(
                    readbackEvidenceId,
                    "index-snapshot-readback",
                    $"Post-swap readback contained {verification.Readback.IndexedFileCount} file(s).",
                    ContentHashSha256: verification.Readback.DigestSha256,
                    CapturedAtUtc: verification.Readback.CapturedAtUtc)
            ]);
            postconditions.AddRange(
            [
                new OperationPostcondition(
                    "index-rebuild-all-inputs-indexed",
                    "Every discovered file in the requested rebuild scope is represented in the staged index.",
                    verification.AllDiscoveredFilesIndexed
                        ? OperationPostconditionState.Satisfied
                        : OperationPostconditionState.NotSatisfied,
                    Required: true,
                    EvidenceIds: [beforeEvidenceId, afterEvidenceId]),
                new OperationPostcondition(
                    "index-rebuild-readback-verified",
                    "The post-swap live index count and canonical digest match the staged index.",
                    verification.ReadbackVerified
                        ? OperationPostconditionState.Satisfied
                        : OperationPostconditionState.NotSatisfied,
                    Required: true,
                    EvidenceIds: [afterEvidenceId, readbackEvidenceId])
            ]);
        }

        return VerifiedOperationOutcomeValidator.ValidateAndThrow(new VerifiedOperationOutcome(
            operationId,
            $"maintenance.{job.Type}",
            result.State,
            startedAtUtc,
            completedAtUtc,
            1,
            job.Id,
            ComputeInputHash(job),
            postconditions,
            evidence,
            [],
            issues,
            recovery));
    }

    internal static string ComputeInputHash(MaintenanceJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("id", job.Id);
            writer.WriteNumber("type", (byte)job.Type);
            writer.WriteNumber("priority", (byte)job.Priority);
            writer.WriteString("description", job.Description);
            writer.WriteNumber("estimatedDurationTicks", job.EstimatedDuration.Ticks);
            writer.WriteStartArray("targetPaths");
            foreach (var path in job.TargetPaths ?? [])
                writer.WriteStringValue(path);
            writer.WriteEndArray();

            writer.WriteStartObject("requirements");
            writer.WriteNumber("cpuCores", job.Requirements.CpuCores);
            writer.WriteNumber("memoryBytes", job.Requirements.MemoryBytes);
            writer.WriteNumber("diskIoMbps", job.Requirements.DiskIoMbps);
            writer.WriteNumber("networkIoMbps", job.Requirements.NetworkIoMbps);
            writer.WriteBoolean("requiresExclusiveLock", job.Requirements.RequiresExclusiveLock);
            writer.WriteStartArray("exclusivePaths");
            foreach (var path in job.Requirements.ExclusivePaths ?? [])
                writer.WriteStringValue(path);
            writer.WriteEndArray();
            writer.WriteEndObject();

            writer.WriteStartObject("parameters");
            foreach (var (key, value) in (job.Parameters ?? [])
                         .OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(key);
                WriteCanonicalParameter(writer, value);
            }
            writer.WriteEndObject();
            writer.WriteBoolean("interruptible", job.Interruptible);
            writer.WriteNumber("maxRetries", job.MaxRetries);
            writer.WriteEndObject();
        }

        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static void WriteCanonicalParameter(Utf8JsonWriter writer, object? value)
    {
        writer.WriteStartObject();
        if (value is null)
        {
            writer.WriteString("type", "null");
            writer.WriteNull("value");
            writer.WriteEndObject();
            return;
        }

        writer.WriteString("type", value.GetType().FullName ?? value.GetType().Name);
        switch (value)
        {
            case string text:
                writer.WriteString("value", text);
                break;
            case bool boolean:
                writer.WriteBoolean("value", boolean);
                break;
            case byte number:
                writer.WriteNumber("value", number);
                break;
            case sbyte number:
                writer.WriteNumber("value", number);
                break;
            case short number:
                writer.WriteNumber("value", number);
                break;
            case ushort number:
                writer.WriteNumber("value", number);
                break;
            case int number:
                writer.WriteNumber("value", number);
                break;
            case uint number:
                writer.WriteNumber("value", number);
                break;
            case long number:
                writer.WriteNumber("value", number);
                break;
            case ulong number:
                writer.WriteNumber("value", number);
                break;
            case decimal number:
                writer.WriteNumber("value", number);
                break;
            case float number:
                writer.WriteString("value", number.ToString("R", CultureInfo.InvariantCulture));
                break;
            case double number:
                writer.WriteString("value", number.ToString("R", CultureInfo.InvariantCulture));
                break;
            case DateTime dateTime:
                writer.WriteString("value", dateTime.ToString("O", CultureInfo.InvariantCulture));
                break;
            case DateTimeOffset dateTimeOffset:
                writer.WriteString("value", dateTimeOffset.ToString("O", CultureInfo.InvariantCulture));
                break;
            case TimeSpan timeSpan:
                writer.WriteNumber("value", timeSpan.Ticks);
                break;
            case Guid guid:
                writer.WriteString("value", guid);
                break;
            case char character:
                writer.WriteString("value", character.ToString());
                break;
            case DateOnly date:
                writer.WriteString("value", date.ToString("O", CultureInfo.InvariantCulture));
                break;
            case TimeOnly time:
                writer.WriteString("value", time.ToString("O", CultureInfo.InvariantCulture));
                break;
            case Uri uri:
                writer.WriteString("value", uri.OriginalString);
                break;
            case byte[] bytes:
                writer.WriteBase64String("value", bytes);
                break;
            case Enum enumValue:
                writer.WriteString("value", enumValue.ToString());
                break;
            case IDictionary dictionary:
                writer.WriteStartArray("value");
                foreach (var entry in dictionary.Cast<DictionaryEntry>()
                             .Select(entry => new
                             {
                                 Entry = entry,
                                 KeyType = entry.Key?.GetType().FullName ?? "null",
                                 KeyText = Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty
                             })
                             .OrderBy(item => item.KeyType, StringComparer.Ordinal)
                             .ThenBy(item => item.KeyText, StringComparer.Ordinal))
                {
                    writer.WriteStartObject();
                    writer.WriteString("keyType", entry.KeyType);
                    writer.WriteString("key", entry.KeyText);
                    writer.WritePropertyName("value");
                    WriteCanonicalParameter(writer, entry.Entry.Value);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                break;
            case IEnumerable sequence:
                writer.WriteStartArray("value");
                foreach (var item in sequence)
                    WriteCanonicalParameter(writer, item);
                writer.WriteEndArray();
                break;
            default:
                throw new InvalidOperationException(
                    $"Maintenance parameter type '{value.GetType().FullName}' cannot be canonically hashed.");
        }

        writer.WriteEndObject();
    }

    private sealed record MaintenanceOperationResult(
        OperationTerminalState State,
        string Message,
        string? RecoveryGuidance = null,
        Exception? Exception = null,
        IndexRebuildVerification? IndexRebuildVerification = null)
    {
        public static MaintenanceOperationResult Succeeded(
            string message,
            IndexRebuildVerification? verification = null) =>
            new(OperationTerminalState.Succeeded, message, IndexRebuildVerification: verification);

        public static MaintenanceOperationResult Failed(
            string message,
            string recoveryGuidance,
            Exception? exception = null,
            IndexRebuildVerification? verification = null) =>
            new(OperationTerminalState.Failed, message, recoveryGuidance, exception, verification);

        public static MaintenanceOperationResult CompletedWithWarnings(string message, string recoveryGuidance) =>
            new(OperationTerminalState.CompletedWithWarnings, message, recoveryGuidance);

        public static MaintenanceOperationResult Blocked(string message, string recoveryGuidance) =>
            new(OperationTerminalState.Blocked, message, recoveryGuidance);
    }

    private sealed class MaintenanceAdmissionBlockedException : InvalidOperationException
    {
        public MaintenanceAdmissionBlockedException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }

    private DateTimeOffset GetCurrentTime() => DateTimeOffset.Now;

    private MaintenanceWindow? FindCurrentMaintenanceWindow(DateTimeOffset now)
    {
        var timeOfDay = now.TimeOfDay;
        var dayOfWeek = now.DayOfWeek;

        foreach (var window in _config.MaintenanceWindows)
        {
            if (!window.Days.Contains(dayOfWeek))
                continue;

            var start = window.Start;
            var end = window.End;

            // Handle overnight windows
            if (end < start)
            {
                if (timeOfDay >= start || timeOfDay < end)
                    return window;
            }
            else
            {
                if (timeOfDay >= start && timeOfDay < end)
                    return window;
            }
        }

        return null;
    }

    private MaintenanceWindow? FindNextMaintenanceWindow(DateTimeOffset now)
    {
        var timeOfDay = now.TimeOfDay;

        // Check windows for today
        foreach (var window in _config.MaintenanceWindows.OrderBy(w => w.Start))
        {
            if (window.Days.Contains(now.DayOfWeek) && window.Start > timeOfDay)
                return window;
        }

        // Check tomorrow and subsequent days
        for (int i = 1; i < 7; i++)
        {
            var checkDate = now.AddDays(i);
            foreach (var window in _config.MaintenanceWindows.OrderBy(w => w.Start))
            {
                if (window.Days.Contains(checkDate.DayOfWeek))
                    return window;
            }
        }

        return null;
    }

    private TimeSpan? GetTimeUntilWindow(DateTimeOffset now, MaintenanceWindow window)
    {
        for (int i = 0; i < 7; i++)
        {
            var checkDate = now.Date.AddDays(i);
            if (window.Days.Contains(checkDate.DayOfWeek))
            {
                var windowStart = checkDate + window.Start;
                if (windowStart > now)
                    return windowStart - now;
            }
        }
        return null;
    }

    private TimeSpan GetRemainingWindowTime(DateTimeOffset now, MaintenanceWindow window)
    {
        var end = now.Date + window.End;
        if (window.End < window.Start)
            end = end.AddDays(1);
        return end - now;
    }

    private bool IsOperationAllowed(MaintenanceWindow window, MaintenanceType operation)
    {
        if (window.AllowedOperations.Length == 0)
            return true;

        return window.AllowedOperations.Contains(operation) ||
               window.AllowedOperations.Contains(MaintenanceType.All);
    }

    private TradingSession? GetCurrentTradingSession(DateTimeOffset now)
    {
        return _config.TradingSessions.FirstOrDefault(s =>
            s.ActiveDays.Contains(now.DayOfWeek) &&
            !_config.Holidays.Contains(now.Date.ToString("yyyy-MM-dd")));
    }

    private bool IsWithinTradingHours(DateTimeOffset now, TradingSession session)
    {
        var timeOfDay = now.TimeOfDay;
        var start = session.IncludesPreMarket ? session.PreMarketStart : session.RegularStart;
        var end = session.IncludesAfterHours ? session.AfterHoursEnd : session.RegularEnd;

        return timeOfDay >= start && timeOfDay <= end;
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;

        _cts.Cancel();
        if (_schedulerTask != null)
        {
            try
            {
                await _schedulerTask;
            }
            catch (OperationCanceledException) { }
        }
        var executions = _executionTasks.Values.ToArray();
        if (executions.Length > 0)
            await Task.WhenAll(executions).ConfigureAwait(false);
        _cts.Dispose();
        _historyHydrationGate.Dispose();
    }
}

/// <summary>
/// Interface for maintenance scheduler.
/// </summary>
public interface IMaintenanceScheduler
{
    Task<ScheduleDecision> CanRunNowAsync(MaintenanceType operation, ResourceRequirements requirements, CancellationToken ct = default);
    Task<ScheduleSlot?> FindNextWindowAsync(MaintenanceType operation, TimeSpan estimatedDuration, ResourceRequirements requirements, CancellationToken ct = default);
    Task<ScheduledJob> ScheduleAsync(MaintenanceJob job, ScheduleOptions options, CancellationToken ct = default);
    Task<OperationalState> GetStateAsync(CancellationToken ct = default);
    Task<JobExecutionStatus> ExecuteJobAsync(MaintenanceJob job, CancellationToken ct = default);
    IReadOnlyList<ScheduledJob> GetPendingJobs();
    IReadOnlyList<ScheduledJob> GetRunningJobs();
    JobExecutionStatus? GetJobStatus(string jobId);
    bool IsRealTimeCollectionActive();
    TimeSpan? GetTimeUntilCollectionEnds();
}

// Configuration types
public sealed record OperationalScheduleConfig(
    string Name = "Default",
    TradingSession[] TradingSessions = null!,
    MaintenanceWindow[] MaintenanceWindows = null!,
    string[] Holidays = null!,
    TimeZoneInfo PrimaryTimeZone = null!
)
{
    public OperationalScheduleConfig() : this("Default", Array.Empty<TradingSession>(), Array.Empty<MaintenanceWindow>(), Array.Empty<string>(), TimeZoneInfo.Utc) { }

    public static OperationalScheduleConfig Default => new(
        Name: "US_Equities_Schedule",
        TradingSessions: new[]
        {
            new TradingSession(
                Name: "US_Equities",
                ActiveDays: new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
                PreMarketStart: new TimeSpan(4, 0, 0),
                RegularStart: new TimeSpan(9, 30, 0),
                RegularEnd: new TimeSpan(16, 0, 0),
                AfterHoursEnd: new TimeSpan(20, 0, 0),
                IncludesPreMarket: true,
                IncludesAfterHours: true
            )
        },
        MaintenanceWindows: new[]
        {
            new MaintenanceWindow(
                Name: "overnight_maintenance",
                Start: new TimeSpan(21, 0, 0),
                End: new TimeSpan(3, 0, 0),
                Days: new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
                AllowedOperations: Array.Empty<MaintenanceType>(), // All allowed
                MaxConcurrentJobs: 8,
                Limits: new ResourceLimits(80, 70, 500)
            ),
            new MaintenanceWindow(
                Name: "weekend_maintenance",
                Start: TimeSpan.Zero,
                End: new TimeSpan(23, 59, 59),
                Days: new[] { DayOfWeek.Saturday, DayOfWeek.Sunday },
                AllowedOperations: Array.Empty<MaintenanceType>(),
                MaxConcurrentJobs: 16,
                Limits: new ResourceLimits(100, 90, 1000)
            ),
            new MaintenanceWindow(
                Name: "intraday_light",
                Start: new TimeSpan(12, 0, 0),
                End: new TimeSpan(13, 0, 0),
                Days: new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
                AllowedOperations: new[] { MaintenanceType.HealthCheck, MaintenanceType.QualityScoring },
                MaxConcurrentJobs: 2,
                Limits: new ResourceLimits(20, 10, 50)
            )
        },
        Holidays: Array.Empty<string>(),
        PrimaryTimeZone: TimeZoneInfo.FindSystemTimeZoneById("America/New_York")
    );
}

public sealed record TradingSession(
    string Name,
    DayOfWeek[] ActiveDays,
    TimeSpan PreMarketStart,
    TimeSpan RegularStart,
    TimeSpan RegularEnd,
    TimeSpan AfterHoursEnd,
    bool IncludesPreMarket = true,
    bool IncludesAfterHours = true
);

public sealed record MaintenanceWindow(
    string Name,
    TimeSpan Start,
    TimeSpan End,
    DayOfWeek[] Days,
    MaintenanceType[] AllowedOperations,
    int MaxConcurrentJobs = 4,
    ResourceLimits Limits = null!
);

public sealed record ResourceLimits(
    int MaxCpuPct = 80,
    int MaxMemoryPct = 70,
    int MaxDiskIoMbps = 500
);

public enum MaintenanceType : byte
{
    All,
    HealthCheck,
    IntegrityValidation,
    Backfill,
    Compaction,
    TierMigration,
    IndexRebuild,
    Archival,
    Backup,
    Reconciliation,
    QualityScoring
}

// Job types
public sealed record MaintenanceJob(
    string Id,
    MaintenanceType Type,
    JobPriority Priority,
    string Description,
    ResourceRequirements Requirements,
    TimeSpan EstimatedDuration,
    string[] TargetPaths,
    Dictionary<string, object>? Parameters = null,
    bool Interruptible = true,
    int MaxRetries = 3
);

public sealed record ResourceRequirements(
    int CpuCores = 1,
    long MemoryBytes = 1_073_741_824,
    long DiskIoMbps = 100,
    long NetworkIoMbps = 0,
    bool RequiresExclusiveLock = false,
    string[]? ExclusivePaths = null
);

public enum JobPriority : byte
{
    Critical,
    High,
    Normal,
    Low,
    Deferred
}

// Schedule types
public sealed record ScheduleDecision(
    bool Allowed,
    string Reason,
    MaintenanceWindow? CurrentWindow,
    TimeSpan? WaitTime,
    ResourceLimits? ApplicableLimits
);

public sealed record ScheduleSlot(
    DateTimeOffset Start,
    DateTimeOffset End,
    MaintenanceWindow Window,
    ResourceLimits Limits,
    int AvailableConcurrencySlots
);

public sealed record ScheduledJob(
    string Id,
    MaintenanceJob Job,
    DateTimeOffset ScheduledStart,
    JobStatus Status,
    DateTimeOffset CreatedAt
);

public sealed record ScheduleOptions(
    bool AllowImmediate = true,
    TimeSpan MaxWaitTime = default
);

public enum JobStatus : byte
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled,
    Blocked
}

public sealed record JobExecutionStatus(
    string JobId,
    MaintenanceType Type,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    JobStatus Status,
    int Progress,
    string Message,
    IReadOnlyList<string> Errors
)
{
    public VerifiedOperationOutcome? Outcome { get; init; }
    public bool AdmissionRetained { get; init; }
}

public sealed record OperationalState(
    bool IsRealTimeCollectionActive,
    TradingSession? CurrentSession,
    MaintenanceWindow? CurrentMaintenanceWindow,
    int RunningMaintenanceJobs,
    ScheduledJob[] PendingJobs,
    DateTimeOffset NextMaintenanceWindowStart
);

using System.Collections.Concurrent;
using System.Threading.Channels;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Meridian.Core.Pipeline;

namespace Meridian.Storage.Maintenance;

/// <summary>
/// Background service that schedules and executes archive maintenance tasks.
/// Integrates with FileMaintenanceService and TierMigrationService for actual operations.
/// </summary>
public sealed class ScheduledArchiveMaintenanceService : BackgroundService, IArchiveMaintenanceService
{
    private readonly ILogger<ScheduledArchiveMaintenanceService> _logger;
    private readonly ArchiveMaintenanceScheduleManager _scheduleManager;
    private readonly IFileMaintenanceService _fileMaintenanceService;
    private readonly ITierMigrationService _tierMigrationService;
    private readonly StorageOptions _storageOptions;
    private readonly Channel<MaintenanceExecution> _executionQueue;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runningExecutions = new();
    private readonly ConcurrentDictionary<string, byte> _explicitCancellations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _outstandingScheduleExecutions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ArchiveMaintenanceExecutionClaim> _executionClaims = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, MaintenanceExecution> _activeExecutions = new(StringComparer.Ordinal);
    private readonly string _leaseOwner = $"{Environment.ProcessId}:{Guid.NewGuid():N}";
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    private static readonly TimeSpan s_executionLeaseDuration = TimeSpan.FromMinutes(5);

    private volatile bool _isRunning;

    public event EventHandler<MaintenanceExecution>? ExecutionStarted;
    public event EventHandler<MaintenanceExecution>? ExecutionCompleted;
    public event EventHandler<MaintenanceExecution>? ExecutionFailed;

    public bool IsRunning => _isRunning;
    public int QueuedExecutions => _executionQueue.Reader.Count;
    public MaintenanceExecution? CurrentExecution => _activeExecutions.Values
        .OrderBy(execution => execution.StartedAt)
        .FirstOrDefault();

    public ScheduledArchiveMaintenanceService(
        ILogger<ScheduledArchiveMaintenanceService> logger,
        ArchiveMaintenanceScheduleManager scheduleManager,
        IFileMaintenanceService fileMaintenanceService,
        ITierMigrationService tierMigrationService,
        StorageOptions storageOptions)
    {
        _logger = logger;
        _scheduleManager = scheduleManager;
        _fileMaintenanceService = fileMaintenanceService;
        _tierMigrationService = tierMigrationService;
        _storageOptions = storageOptions;

        _executionQueue = EventPipelinePolicy.MaintenanceQueue.CreateChannel<MaintenanceExecution>(
            singleReader: true, singleWriter: false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _isRunning = true;
        _logger.LogInformation("Archive maintenance scheduler started");

        try
        {
            var schedulerTask = RunSchedulerLoopAsync(stoppingToken);
            var executorTask = RunExecutorLoopAsync(stoppingToken);
            var leaseHeartbeatTask = RunLeaseHeartbeatLoopAsync(stoppingToken);
            await Task.WhenAll(schedulerTask, executorTask, leaseHeartbeatTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal hosted-service shutdown.
        }
        finally
        {
            _executionQueue.Writer.TryComplete();
            await ReleaseQueuedClaimsForRestartAsync().ConfigureAwait(false);
            _isRunning = false;
            _logger.LogInformation("Archive maintenance scheduler stopped");
        }
    }

    private async Task RunSchedulerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PollDueSchedulesAsync(DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMinutes(1), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in maintenance scheduler loop");
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
        }
    }

    private async Task RunExecutorLoopAsync(CancellationToken ct)
    {
        await foreach (var execution in _executionQueue.Reader.ReadAllAsync(ct))
        {
            try
            {
                await RunMaintenanceExecutionAsync(execution, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing maintenance task {ExecutionId}", execution.ExecutionId);
            }
        }
    }

    private async Task RunLeaseHeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), ct).ConfigureAwait(false);
                await _scheduleManager
                    .RenewExecutionLeasesAsync(
                        _outstandingScheduleExecutions,
                        DateTimeOffset.UtcNow,
                        _leaseOwner,
                        s_executionLeaseDuration,
                        ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error renewing durable maintenance execution leases");
            }
        }
    }

    /// <summary>
    /// Claims and queues schedules due at the supplied instant. Exposed internally so focused
    /// scheduler tests can exercise repeated polls without waiting for the production interval.
    /// </summary>
    internal async Task PollDueSchedulesAsync(DateTimeOffset asOf, CancellationToken ct)
    {
        await _scheduleManager
            .RenewExecutionLeasesAsync(
                _outstandingScheduleExecutions,
                asOf,
                _leaseOwner,
                s_executionLeaseDuration,
                ct)
            .ConfigureAwait(false);

        foreach (var pendingScheduleId in _scheduleManager.GetPendingExecutionScheduleIds())
        {
            if (!_outstandingScheduleExecutions.TryAdd(pendingScheduleId, string.Empty))
                continue;

            try
            {
                var recovered = await _scheduleManager
                    .TryLeasePendingExecutionAsync(
                        pendingScheduleId,
                        asOf,
                        _leaseOwner,
                        s_executionLeaseDuration,
                        ct)
                    .ConfigureAwait(false);
                if (recovered is null)
                {
                    _outstandingScheduleExecutions.TryRemove(pendingScheduleId, out _);
                    continue;
                }

                if (recovered.Execution.State == ArchiveMaintenanceClaimState.Interrupted)
                {
                    await FinalizeInterruptedClaimAsync(recovered, ct).ConfigureAwait(false);
                    continue;
                }

                await QueueExecutionFromClaimAsync(recovered, ct).ConfigureAwait(false);
            }
            catch
            {
                _outstandingScheduleExecutions.TryRemove(pendingScheduleId, out _);
                throw;
            }
        }

        var dueSchedules = _scheduleManager.GetDueSchedules(asOf);
        foreach (var dueSchedule in dueSchedules)
        {
            if (!_outstandingScheduleExecutions.TryAdd(dueSchedule.ScheduleId, string.Empty))
            {
                _logger.LogDebug(
                    "Skipped due maintenance schedule {ScheduleId} because an execution is already queued or running",
                    dueSchedule.ScheduleId);
                continue;
            }

            try
            {
                var claimed = await _scheduleManager
                    .TryClaimDueScheduleAsync(
                        dueSchedule.ScheduleId,
                        asOf,
                        _leaseOwner,
                        s_executionLeaseDuration,
                        ct)
                    .ConfigureAwait(false);
                if (claimed is null)
                {
                    _outstandingScheduleExecutions.TryRemove(dueSchedule.ScheduleId, out _);
                    continue;
                }

                _logger.LogInformation(
                    "Maintenance schedule '{Name}' is due for execution",
                    claimed.Schedule.Name);

                await QueueExecutionFromClaimAsync(claimed, ct).ConfigureAwait(false);
            }
            catch
            {
                _outstandingScheduleExecutions.TryRemove(dueSchedule.ScheduleId, out _);
                throw;
            }
        }
    }

    private async Task<MaintenanceExecution> QueueExecutionFromClaimAsync(
        ArchiveMaintenanceClaim claimed,
        CancellationToken ct)
    {
        var schedule = claimed.Schedule;
        var durableClaim = claimed.Execution;
        var execution = new MaintenanceExecution
        {
            ExecutionId = durableClaim.ExecutionId,
            ScheduleId = schedule.ScheduleId,
            ScheduleName = durableClaim.ScheduleName,
            TaskType = durableClaim.TaskType,
            ManualTrigger = durableClaim.ManualTrigger,
            StartedAt = durableClaim.CreatedAt
        };

        _outstandingScheduleExecutions[schedule.ScheduleId] = execution.ExecutionId;
        _executionClaims[execution.ExecutionId] = durableClaim;

        var historyRecorded = false;
        try
        {
            await _scheduleManager.ExecutionHistory.RecordExecutionAsync(execution, ct).ConfigureAwait(false);
            historyRecorded = true;

            await _executionQueue.Writer.WriteAsync(execution, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _outstandingScheduleExecutions.TryRemove(schedule.ScheduleId, out _);
            _executionClaims.TryRemove(execution.ExecutionId, out _);
            var cancelledManualPublication =
                ex is OperationCanceledException && durableClaim.ManualTrigger;

            if (historyRecorded)
            {
                execution.Status = cancelledManualPublication
                    ? MaintenanceExecutionStatus.Cancelled
                    : MaintenanceExecutionStatus.Pending;
                execution.CompletedAt = cancelledManualPublication ? DateTimeOffset.UtcNow : null;
                execution.ErrorMessage = ex is OperationCanceledException
                    ? cancelledManualPublication
                        ? "Manual execution queueing was cancelled"
                        : "Execution queueing was cancelled; the durable claim remains pending"
                    : "Execution could not be queued; the durable claim remains pending";

                try
                {
                    await _scheduleManager.ExecutionHistory
                        .UpdateExecutionAsync(execution, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception historyException)
                {
                    _logger.LogError(
                        historyException,
                        "Failed to finalize unqueued maintenance execution {ExecutionId}",
                        execution.ExecutionId);
                }
            }

            try
            {
                if (cancelledManualPublication)
                {
                    if (!historyRecorded)
                    {
                        execution.Status = MaintenanceExecutionStatus.Cancelled;
                        execution.CompletedAt = DateTimeOffset.UtcNow;
                        execution.ErrorMessage = "Manual execution queueing was cancelled";
                        await _scheduleManager.ExecutionHistory
                            .RecordExecutionAsync(execution, CancellationToken.None)
                            .ConfigureAwait(false);
                    }

                    await _scheduleManager
                        .UpdateScheduleAfterExecutionAsync(
                            schedule.ScheduleId,
                            execution,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                else
                {
                    await _scheduleManager
                        .ReleaseExecutionForRetryAsync(
                            schedule.ScheduleId,
                            execution.ExecutionId,
                            _leaseOwner,
                            execution.ErrorMessage ?? "Execution publication failed; retry is pending.",
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception releaseException)
            {
                _logger.LogError(
                    releaseException,
                    "Failed to release durable maintenance claim {ExecutionId} for retry",
                    execution.ExecutionId);
            }

            throw;
        }

        _logger.LogDebug(
            "Queued maintenance execution {ExecutionId} for schedule '{ScheduleName}'",
            execution.ExecutionId, schedule.Name);

        return execution;
    }

    private async Task FinalizeInterruptedClaimAsync(
        ArchiveMaintenanceClaim claimed,
        CancellationToken ct)
    {
        var retainedHistory = _scheduleManager.ExecutionHistory.GetExecution(
            claimed.Execution.ExecutionId);
        var hasTerminalHistory = retainedHistory is not null
            && string.Equals(
                retainedHistory.ScheduleId,
                claimed.Schedule.ScheduleId,
                StringComparison.Ordinal)
            && retainedHistory.CompletedAt.HasValue
            && IsTerminal(retainedHistory.Status);
        var execution = hasTerminalHistory
            ? retainedHistory!
            : new MaintenanceExecution
            {
                ExecutionId = claimed.Execution.ExecutionId,
                ScheduleId = claimed.Schedule.ScheduleId,
                ScheduleName = claimed.Execution.ScheduleName,
                TaskType = claimed.Execution.TaskType,
                ManualTrigger = claimed.Execution.ManualTrigger,
                StartedAt = claimed.Execution.CreatedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                Status = MaintenanceExecutionStatus.Failed,
                ErrorMessage = claimed.Execution.LastError
                    ?? "A prior process stopped during this maintenance execution; its outcome is ambiguous and it was not replayed."
            };

        try
        {
            if (!hasTerminalHistory)
            {
                await _scheduleManager.ExecutionHistory
                    .RecordExecutionAsync(execution, ct)
                    .ConfigureAwait(false);
            }
            await _scheduleManager
                .UpdateScheduleAfterExecutionAsync(
                    claimed.Schedule.ScheduleId,
                    execution,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (execution.Status is MaintenanceExecutionStatus.Completed or
                MaintenanceExecutionStatus.CompletedWithWarnings)
            {
                ExecutionCompleted?.Invoke(this, execution);
            }
            else
            {
                ExecutionFailed?.Invoke(this, execution);
            }
        }
        finally
        {
            ReleaseOutstandingSchedule(execution);
        }
    }

    private static bool IsTerminal(MaintenanceExecutionStatus status)
    {
        return status is MaintenanceExecutionStatus.Completed
            or MaintenanceExecutionStatus.CompletedWithWarnings
            or MaintenanceExecutionStatus.Failed
            or MaintenanceExecutionStatus.TimedOut
            or MaintenanceExecutionStatus.Cancelled;
    }

    private async Task RunMaintenanceExecutionAsync(MaintenanceExecution execution, CancellationToken ct)
    {
        var executionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _runningExecutions[execution.ExecutionId] = executionCts;
        _activeExecutions[execution.ExecutionId] = execution;

        try
        {
            if (execution.ScheduleId is not null)
            {
                var markedRunning = await _scheduleManager
                    .MarkExecutionRunningAsync(
                        execution.ScheduleId,
                        execution.ExecutionId,
                        DateTimeOffset.UtcNow,
                        _leaseOwner,
                        s_executionLeaseDuration,
                        ct)
                    .ConfigureAwait(false);
                if (!markedRunning)
                {
                    throw new InvalidOperationException(
                        $"Durable maintenance claim '{execution.ExecutionId}' is no longer executable.");
                }
            }

            execution.Status = MaintenanceExecutionStatus.Running;
            await _scheduleManager.ExecutionHistory.UpdateExecutionAsync(execution, ct).ConfigureAwait(false);

            ExecutionStarted?.Invoke(this, execution);

            _logger.LogInformation(
                "Starting maintenance execution {ExecutionId} ({TaskType})",
                execution.ExecutionId, execution.TaskType);

            _executionClaims.TryGetValue(execution.ExecutionId, out var durableClaim);
            var schedule = execution.ScheduleId != null
                ? _scheduleManager.GetSchedule(execution.ScheduleId)
                : null;

            var options = durableClaim?.Options ?? schedule?.Options ?? new MaintenanceTaskOptions();
            var retainedTargetPaths = durableClaim?.TargetPaths ?? schedule?.TargetPaths;
            var targetPaths = retainedTargetPaths is { Count: > 0 }
                ? retainedTargetPaths.ToArray()
                : new[] { _storageOptions.RootPath };

            // Set timeout
            var timeout = durableClaim?.MaxDuration ?? schedule?.MaxDuration ?? TimeSpan.FromHours(2);
            executionCts.CancelAfter(timeout);

            var result = await ExecuteMaintenanceTaskAsync(
                execution.TaskType,
                options,
                targetPaths,
                execution,
                executionCts.Token);

            execution.Result = result;
            execution.FilesProcessed = result.FilesProcessed;
            execution.IssuesFound = result.IssuesFound;
            execution.IssuesResolved = result.IssuesResolved;
            execution.BytesProcessed = result.TotalBytesScanned;
            execution.BytesSaved = result.BytesSaved;

            execution.Status = result.IssuesFound > result.IssuesResolved
                ? MaintenanceExecutionStatus.CompletedWithWarnings
                : MaintenanceExecutionStatus.Completed;

            execution.CompletedAt = DateTimeOffset.UtcNow;

            _logger.LogInformation(
                "Completed maintenance execution {ExecutionId} in {Duration}: {FilesProcessed} files, {IssuesFound} issues found, {IssuesResolved} resolved",
                execution.ExecutionId,
                execution.Duration,
                execution.FilesProcessed,
                execution.IssuesFound,
                execution.IssuesResolved);

        }
        catch (OperationCanceledException)
        {
            execution.Status = ct.IsCancellationRequested
                || _explicitCancellations.ContainsKey(execution.ExecutionId)
                ? MaintenanceExecutionStatus.Cancelled
                : MaintenanceExecutionStatus.TimedOut;
            execution.CompletedAt = DateTimeOffset.UtcNow;
            execution.ErrorMessage = execution.Status == MaintenanceExecutionStatus.Cancelled
                ? "Execution was cancelled"
                : "Execution timed out";

            _logger.LogWarning(
                "Maintenance execution {ExecutionId} {Status}",
                execution.ExecutionId, execution.Status);

        }
        catch (Exception ex)
        {
            execution.Status = MaintenanceExecutionStatus.Failed;
            execution.CompletedAt = DateTimeOffset.UtcNow;
            execution.ErrorMessage = ex.Message;
            execution.LogMessages.Add($"Error: {ex}");

            _logger.LogError(ex,
                "Maintenance execution {ExecutionId} failed",
                execution.ExecutionId);

        }
        finally
        {
            _runningExecutions.TryRemove(execution.ExecutionId, out _);
            _activeExecutions.TryRemove(execution.ExecutionId, out _);
            _explicitCancellations.TryRemove(execution.ExecutionId, out _);
            try
            {
                await _scheduleManager.ExecutionHistory
                    .UpdateExecutionAsync(execution, CancellationToken.None)
                    .ConfigureAwait(false);

                // Update schedule with execution results. Finalization is intentionally
                // non-cancellable: the execution has already reached a terminal state.
                if (execution.ScheduleId != null)
                {
                    await _scheduleManager
                        .UpdateScheduleAfterExecutionAsync(
                            execution.ScheduleId,
                            execution,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                _executionClaims.TryRemove(execution.ExecutionId, out _);
                ReleaseOutstandingSchedule(execution);
                executionCts.Dispose();
            }
        }

        if (execution.Status is MaintenanceExecutionStatus.Completed or
            MaintenanceExecutionStatus.CompletedWithWarnings)
        {
            ExecutionCompleted?.Invoke(this, execution);
        }
        else
        {
            ExecutionFailed?.Invoke(this, execution);
        }
    }

    private void ReleaseOutstandingSchedule(MaintenanceExecution execution)
    {
        if (execution.ScheduleId is null)
            return;

        if (_outstandingScheduleExecutions.TryGetValue(execution.ScheduleId, out var executionId)
            && string.Equals(executionId, execution.ExecutionId, StringComparison.Ordinal))
        {
            _outstandingScheduleExecutions.TryRemove(execution.ScheduleId, out _);
        }
    }

    private async Task ReleaseQueuedClaimsForRestartAsync()
    {
        foreach (var (scheduleId, executionId) in _outstandingScheduleExecutions.ToArray())
        {
            if (_runningExecutions.ContainsKey(executionId))
                continue;

            try
            {
                await _scheduleManager
                    .ReleaseExecutionForRetryAsync(
                        scheduleId,
                        executionId,
                        _leaseOwner,
                        "The host stopped before this queued execution started; the durable claim is pending restart recovery.",
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to release queued maintenance claim {ExecutionId} during shutdown",
                    executionId);
            }
            finally
            {
                _executionClaims.TryRemove(executionId, out _);
                _outstandingScheduleExecutions.TryRemove(scheduleId, out _);
            }
        }
    }

    private async Task<MaintenanceResult> ExecuteMaintenanceTaskAsync(
        MaintenanceTaskType taskType,
        MaintenanceTaskOptions options,
        string[] targetPaths,
        MaintenanceExecution execution,
        CancellationToken ct)
    {
        return taskType switch
        {
            MaintenanceTaskType.HealthCheck => await RunHealthCheckAsync(options, targetPaths, execution, ct),
            MaintenanceTaskType.Cleanup => await RunCleanupAsync(options, targetPaths, execution, ct),
            MaintenanceTaskType.Defragmentation => await RunDefragmentationAsync(options, execution, ct),
            MaintenanceTaskType.TierMigration => await RunTierMigrationAsync(options, execution, ct),
            MaintenanceTaskType.Compression => await RunCompressionAsync(options, targetPaths, execution, ct),
            MaintenanceTaskType.Repair => await RunRepairAsync(options, execution, ct),
            MaintenanceTaskType.FullMaintenance => await RunFullMaintenanceAsync(options, targetPaths, execution, ct),
            MaintenanceTaskType.IntegrityCheck => await RunIntegrityCheckAsync(options, targetPaths, execution, ct),
            MaintenanceTaskType.Archival => await RunArchivalAsync(options, execution, ct),
            MaintenanceTaskType.RetentionEnforcement => await RunRetentionEnforcementAsync(options, execution, ct),
            _ => throw new NotSupportedException($"Task type {taskType} is not supported")
        };
    }

    private async Task<MaintenanceResult> RunHealthCheckAsync(
        MaintenanceTaskOptions options,
        string[] targetPaths,
        MaintenanceExecution execution,
        CancellationToken ct)
    {
        var healthOptions = new HealthCheckOptions(
            ValidateChecksums: options.ValidateChecksums,
            CheckSequenceContinuity: options.CheckSequenceContinuity,
            ValidateSchemas: true,
            CheckFilePermissions: options.CheckFilePermissions,
            IdentifyCorruption: options.IdentifyCorruption,
            CheckManifestConsistency: false,
            Paths: targetPaths,
            ParallelChecks: options.ParallelOperations
        );

        var report = await _fileMaintenanceService.RunHealthCheckAsync(healthOptions, ct);

        execution.LogMessages.Add($"Health check completed: scanned {report.Summary.TotalFiles} files");
        execution.LogMessages.Add($"Found {report.Issues.Count} issues ({report.Summary.CorruptedFiles} corrupted, {report.Summary.OrphanedFiles} orphaned)");

        return new MaintenanceResult
        {
            Success = report.Summary.CorruptedFiles == 0,
            Summary = $"Health check: {report.Summary.HealthyFiles}/{report.Summary.TotalFiles} files healthy",
            TotalFiles = report.Summary.TotalFiles,
            FilesProcessed = report.Summary.TotalFiles,
            FilesSkipped = 0,
            FilesFailed = report.Summary.CorruptedFiles,
            TotalBytesScanned = report.Summary.TotalBytes,
            BytesSaved = 0,
            IssuesFound = report.Issues.Count,
            IssuesResolved = 0,
            Issues = report.Issues.Select(i => new MaintenanceIssue(
                i.Path,
                i.Type.ToString(),
                i.Details ?? i.RecommendedAction,
                i.Severity.ToString(),
                false,
                i.RecommendedAction
            )).ToList()
        };
    }

    private async Task<MaintenanceResult> RunCleanupAsync(
        MaintenanceTaskOptions options,
        string[] targetPaths,
        MaintenanceExecution execution,
        CancellationToken ct)
    {
        var orphanReport = await _fileMaintenanceService.FindOrphansAsync(ct);
        var deletedFiles = 0;
        long bytesRecovered = 0;

        if (!options.DryRun && options.DeleteOrphans)
        {
            foreach (var orphan in orphanReport.OrphanedFiles)
            {
                ct.ThrowIfCancellationRequested();

                // Only delete orphans older than threshold
                if (orphan.LastModified < DateTime.UtcNow.AddDays(-options.OrphanAgeDays))
                {
                    try
                    {
                        File.Delete(orphan.Path);
                        deletedFiles++;
                        bytesRecovered += orphan.SizeBytes;
                        execution.LogMessages.Add($"Deleted orphan: {orphan.Path}");
                    }
                    catch (Exception ex)
                    {
                        execution.LogMessages.Add($"Failed to delete {orphan.Path}: {ex.Message}");
                    }
                }
            }
        }

        // Clean up temporary files
        if (options.DeleteTemporaryFiles)
        {
            foreach (var path in targetPaths)
            {
                var tempFiles = Directory.EnumerateFiles(path, "*.tmp", SearchOption.AllDirectories);
                foreach (var tempFile in tempFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(tempFile);
                        if (info.LastWriteTimeUtc < DateTime.UtcNow.AddHours(-24))
                        {
                            bytesRecovered += info.Length;
                            if (!options.DryRun)
                            {
                                File.Delete(tempFile);
                            }
                            deletedFiles++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to delete temp file during cleanup: {FilePath}", tempFile);
                    }
                }
            }
        }

        return new MaintenanceResult
        {
            Success = true,
            Summary = $"Cleanup: deleted {deletedFiles} files, recovered {bytesRecovered / 1024.0 / 1024.0:F2} MB",
            TotalFiles = orphanReport.OrphanedFiles.Count,
            FilesProcessed = deletedFiles,
            FilesSkipped = orphanReport.OrphanedFiles.Count - deletedFiles,
            FilesFailed = 0,
            TotalBytesScanned = orphanReport.TotalOrphanedBytes,
            BytesSaved = bytesRecovered,
            IssuesFound = orphanReport.OrphanedFiles.Count,
            IssuesResolved = deletedFiles
        };
    }

    private async Task<MaintenanceResult> RunDefragmentationAsync(
        MaintenanceTaskOptions options,
        MaintenanceExecution execution,
        CancellationToken ct)
    {
        var defragOptions = new DefragOptions(
            MinFileSizeBytes: options.MinFileSizeBytes,
            MaxFilesPerMerge: options.MaxFilesPerMerge,
            MaxFileAge: TimeSpan.FromDays(options.FileAgeDaysThreshold),
            DryRun: options.DryRun
        );

        var result = await _fileMaintenanceService.DefragmentAsync(defragOptions, ct);

        execution.LogMessages.Add($"Defragmentation: processed {result.FilesProcessed} files, created {result.FilesCreated} merged files");
        execution.LogMessages.Add($"Compression improvement: {result.CompressionImprovement:F1}%");

        return new MaintenanceResult
        {
            Success = true,
            Summary = $"Defrag: merged {result.FilesProcessed} files into {result.FilesCreated}, saved {(result.BytesBefore - result.BytesAfter) / 1024.0 / 1024.0:F2} MB",
            TotalFiles = result.FilesProcessed,
            FilesProcessed = result.FilesProcessed,
            FilesSkipped = 0,
            FilesFailed = 0,
            TotalBytesScanned = result.BytesBefore,
            BytesSaved = result.BytesBefore - result.BytesAfter,
            IssuesFound = 0,
            IssuesResolved = 0,
            Metrics = new Dictionary<string, object>
            {
                ["filesCreated"] = result.FilesCreated,
                ["compressionImprovement"] = result.CompressionImprovement
            }
        };
    }

    private async Task<MaintenanceResult> RunTierMigrationAsync(
        MaintenanceTaskOptions options,
        MaintenanceExecution execution,
        CancellationToken ct)
    {
        if (options.RunOnlyDuringMarketClosedHours && !IsMarketClosed(options))
        {
            execution.LogMessages.Add("Tier migration skipped because market is currently open for configured hours");
            return new MaintenanceResult
            {
                Success = true,
                Summary = "Tier migration skipped during market hours",
                TotalFiles = 0,
                FilesProcessed = 0,
                IssuesFound = 0,
                IssuesResolved = 0
            };
        }

        // Get migration plan
        var plan = await _tierMigrationService.PlanMigrationAsync(TimeSpan.FromDays(365), ct);

        if (plan.Actions.Count == 0)
        {
            execution.LogMessages.Add("No files eligible for tier migration");
            return new MaintenanceResult
            {
                Success = true,
                Summary = "No files eligible for tier migration",
                TotalFiles = 0,
                FilesProcessed = 0,
                IssuesFound = 0,
                IssuesResolved = 0
            };
        }

        var maxFiles = Math.Max(1, options.MaxMigrationsPerRun);
        var maxBytes = options.MaxMigrationBytesPerRun ?? long.MaxValue;
        var selectedActions = plan.Actions
            .OrderByDescending(a => a.FileAge)
            .Take(maxFiles)
            .ToList();

        var incrementalActions = new List<PlannedMigrationAction>(selectedActions.Count);
        long selectedBytes = 0;

        foreach (var action in selectedActions)
        {
            if (incrementalActions.Count > 0 && selectedBytes + action.SizeBytes > maxBytes)
                break;

            incrementalActions.Add(action);
            selectedBytes += action.SizeBytes;
        }

        var totalMigrated = 0;
        var totalFailed = 0;
        long bytesSaved = 0;
        long bytesProcessed = 0;

        var failureErrors = new ConcurrentBag<string>();

        var migrationOptions = new MigrationOptions(
            DeleteSource: options.DeleteSourceAfterMigration,
            VerifyChecksum: options.VerifyAfterMigration,
            ParallelFiles: options.ParallelOperations
        );

        await Parallel.ForEachAsync(
            incrementalActions,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, options.ParallelOperations),
                CancellationToken = ct
            },
            async (action, token) =>
            {
                if (options.DryRun)
                {
                    lock (execution.LogMessages)
                    {
                        execution.LogMessages.Add($"[DRY RUN] Would migrate {action.SourcePath} to {action.TargetTier}");
                    }
                    Interlocked.Increment(ref totalMigrated);
                    return;
                }

                var result = await _tierMigrationService.MigrateAsync(
                    action.SourcePath,
                    action.TargetTier,
                    migrationOptions,
                    token);

                Interlocked.Add(ref bytesProcessed, result.BytesProcessed);

                if (result.Success)
                {
                    Interlocked.Increment(ref totalMigrated);
                    Interlocked.Add(ref bytesSaved, result.BytesSaved);
                    return;
                }

                Interlocked.Increment(ref totalFailed);
                foreach (var error in result.Errors)
                {
                    failureErrors.Add(error);
                }
            });

        execution.LogMessages.Add(
            $"Incremental tier migration processed {incrementalActions.Count} of {plan.Actions.Count} planned actions " +
            $"(limit: {maxFiles} files, {maxBytes / 1024.0 / 1024.0:F0} MB)");

        if (!failureErrors.IsEmpty)
        {
            execution.LogMessages.AddRange(failureErrors.Take(100));
        }

        return new MaintenanceResult
        {
            Success = totalFailed == 0,
            Summary = $"Tier migration: migrated {totalMigrated} files, saved {bytesSaved / 1024.0 / 1024.0:F2} MB",
            TotalFiles = incrementalActions.Count,
            FilesProcessed = totalMigrated,
            FilesSkipped = 0,
            FilesFailed = totalFailed,
            TotalBytesScanned = bytesProcessed,
            BytesSaved = bytesSaved,
            IssuesFound = totalFailed,
            IssuesResolved = 0
        };
    }

    private static bool IsMarketClosed(MaintenanceTaskOptions options)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var tz = TimeZoneInfo.FindSystemTimeZoneById(options.MarketTimeZoneId);
        var marketNow = TimeZoneInfo.ConvertTime(nowUtc, tz);

        if (marketNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return true;

        var tod = marketNow.TimeOfDay;
        var isOpen = tod >= options.MarketOpenTime && tod < options.MarketCloseTime;
        return !isOpen;
    }

    private async Task<MaintenanceResult> RunCompressionAsync(
        MaintenanceTaskOptions options,
        string[] targetPaths,
        MaintenanceExecution execution,
        CancellationToken ct)
    {
        // This would use CompressionProfileManager for actual compression
        // For now, use repair with recompress strategy
        var repairOptions = new RepairOptions(
            Strategy: RepairStrategy.RecompressOptimal,
            DryRun: options.DryRun,
            BackupBeforeRepair: options.BackupBeforeRepair,
            BackupPath: options.BackupPath
        );

        var result = await _fileMaintenanceService.RepairAsync(repairOptions, ct);

        return new MaintenanceResult
        {
            Success = result.Errors.Count == 0,
            Summary = $"Compression: processed {result.FilesProcessed} files, recompressed {result.FilesRepaired}",
            TotalFiles = result.FilesProcessed,
            FilesProcessed = result.FilesProcessed,
            FilesSkipped = result.FilesProcessed - result.FilesRepaired,
            FilesFailed = result.Errors.Count,
            TotalBytesScanned = 0,
            BytesSaved = 0,
            IssuesFound = result.FilesProcessed - result.FilesRepaired,
            IssuesResolved = result.FilesRepaired
        };
    }

    private async Task<MaintenanceResult> RunRepairAsync(
        MaintenanceTaskOptions options,
        MaintenanceExecution execution,
        CancellationToken ct)
    {
        var strategy = options.TruncateCorrupted
            ? RepairStrategy.TruncateCorrupted
            : RepairStrategy.RebuildIndex;

        var repairOptions = new RepairOptions(
            Strategy: strategy,
            DryRun: options.DryRun,
            BackupBeforeRepair: options.BackupBeforeRepair,
            BackupPath: options.BackupPath
        );

        var result = await _fileMaintenanceService.RepairAsync(repairOptions, ct);

        execution.LogMessages.Add($"Repair: processed {result.FilesProcessed} files, repaired {result.FilesRepaired}");
        if (result.Errors.Count > 0)
        {
            execution.LogMessages.AddRange(result.Errors.Take(10).Select(e => $"Error: {e}"));
        }

        return new MaintenanceResult
        {
            Success = result.Errors.Count == 0,
            Summary = $"Repair: repaired {result.FilesRepaired}/{result.FilesProcessed} files",
            TotalFiles = result.FilesProcessed,
            FilesProcessed = result.FilesProcessed,
            FilesSkipped = 0,
            FilesFailed = result.Errors.Count,
            TotalBytesScanned = 0,
            BytesSaved = 0,
            IssuesFound = result.FilesProcessed,
            IssuesResolved = result.FilesRepaired
        };
    }

    private async Task<MaintenanceResult> RunFullMaintenanceAsync(
        MaintenanceTaskOptions options,
        string[] targetPaths,
        MaintenanceExecution execution,
        CancellationToken ct)
    {
        var results = new List<MaintenanceResult>();

        // Run health check first
        execution.LogMessages.Add("Phase 1: Running health check...");
        results.Add(await RunHealthCheckAsync(options, targetPaths, execution, ct));

        // Run cleanup
        execution.LogMessages.Add("Phase 2: Running cleanup...");
        results.Add(await RunCleanupAsync(options, targetPaths, execution, ct));

        // Run defragmentation
        execution.LogMessages.Add("Phase 3: Running defragmentation...");
        results.Add(await RunDefragmentationAsync(options, execution, ct));

        // Run tier migration
        execution.LogMessages.Add("Phase 4: Running tier migration...");
        results.Add(await RunTierMigrationAsync(options, execution, ct));

        // Aggregate results
        return new MaintenanceResult
        {
            Success = results.All(r => r.Success),
            Summary = "Full maintenance completed: " + string.Join(", ", results.Select(r => r.Summary.Split(':').Last().Trim())),
            TotalFiles = results.Sum(r => r.TotalFiles),
            FilesProcessed = results.Sum(r => r.FilesProcessed),
            FilesSkipped = results.Sum(r => r.FilesSkipped),
            FilesFailed = results.Sum(r => r.FilesFailed),
            TotalBytesScanned = results.Sum(r => r.TotalBytesScanned),
            BytesSaved = results.Sum(r => r.BytesSaved),
            IssuesFound = results.Sum(r => r.IssuesFound),
            IssuesResolved = results.Sum(r => r.IssuesResolved),
            Issues = results.SelectMany(r => r.Issues).ToList()
        };
    }

    private async Task<MaintenanceResult> RunIntegrityCheckAsync(
        MaintenanceTaskOptions options,
        string[] targetPaths,
        MaintenanceExecution execution,
        CancellationToken ct)
    {
        var healthOptions = new HealthCheckOptions(
            ValidateChecksums: true,
            CheckSequenceContinuity: true,
            ValidateSchemas: true,
            CheckFilePermissions: false,
            IdentifyCorruption: true,
            CheckManifestConsistency: true,
            Paths: targetPaths,
            ParallelChecks: options.ParallelOperations
        );

        var report = await _fileMaintenanceService.RunHealthCheckAsync(healthOptions, ct);

        var checksumIssues = report.Issues.Count(i => i.Type == IssueType.ChecksumMismatch);

        return new MaintenanceResult
        {
            Success = checksumIssues == 0,
            Summary = $"Integrity check: {report.Summary.HealthyFiles}/{report.Summary.TotalFiles} files valid, {checksumIssues} checksum failures",
            TotalFiles = report.Summary.TotalFiles,
            FilesProcessed = report.Summary.TotalFiles,
            FilesSkipped = 0,
            FilesFailed = checksumIssues,
            TotalBytesScanned = report.Summary.TotalBytes,
            BytesSaved = 0,
            IssuesFound = report.Issues.Count,
            IssuesResolved = 0
        };
    }

    private Task<MaintenanceResult> RunArchivalAsync(
        MaintenanceTaskOptions options,
        MaintenanceExecution execution,
        CancellationToken ct)
    {
        // Archival is essentially tier migration to cold/archive tier
        return RunTierMigrationAsync(options, execution, ct);
    }

    private Task<MaintenanceResult> RunRetentionEnforcementAsync(
        MaintenanceTaskOptions options,
        MaintenanceExecution execution,
        CancellationToken ct)
    {
        var retentionDays = options.OverrideRetentionDays ?? _storageOptions.RetentionDays ?? 365;
        var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);
        var deletedFiles = 0;
        long bytesRecovered = 0;

        var allFiles = Directory.EnumerateFiles(_storageOptions.RootPath, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) ||
                       f.EndsWith(".jsonl.gz", StringComparison.OrdinalIgnoreCase))
            .Select(f => new FileInfo(f))
            .Where(f => f.LastWriteTimeUtc < cutoffDate);

        foreach (var file in allFiles)
        {
            ct.ThrowIfCancellationRequested();

            // Skip critical data if configured
            if (options.SkipCriticalData && file.FullName.Contains("critical", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!options.DryRun)
            {
                try
                {
                    bytesRecovered += file.Length;
                    file.Delete();
                    deletedFiles++;
                    execution.LogMessages.Add($"Deleted expired: {file.FullName}");
                }
                catch (Exception ex)
                {
                    execution.LogMessages.Add($"Failed to delete {file.FullName}: {ex.Message}");
                }
            }
            else
            {
                execution.LogMessages.Add($"[DRY RUN] Would delete: {file.FullName}");
                deletedFiles++;
                bytesRecovered += file.Length;
            }
        }

        return Task.FromResult(new MaintenanceResult
        {
            Success = true,
            Summary = $"Retention enforcement: deleted {deletedFiles} files older than {retentionDays} days, recovered {bytesRecovered / 1024.0 / 1024.0:F2} MB",
            TotalFiles = deletedFiles,
            FilesProcessed = deletedFiles,
            FilesSkipped = 0,
            FilesFailed = 0,
            TotalBytesScanned = bytesRecovered,
            BytesSaved = bytesRecovered,
            IssuesFound = 0,
            IssuesResolved = 0
        });
    }

    // IArchiveMaintenanceService implementation

    public async Task<MaintenanceExecution> ExecuteMaintenanceAsync(
        MaintenanceTaskType taskType,
        MaintenanceTaskOptions? options = null,
        string[]? targetPaths = null,
        CancellationToken ct = default)
    {
        var execution = new MaintenanceExecution
        {
            TaskType = taskType,
            ManualTrigger = true
        };

        await _scheduleManager.ExecutionHistory.RecordExecutionAsync(execution, ct).ConfigureAwait(false);

        var executionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _runningExecutions[execution.ExecutionId] = executionCts;
        _activeExecutions[execution.ExecutionId] = execution;

        try
        {
            execution.Status = MaintenanceExecutionStatus.Running;
            await _scheduleManager.ExecutionHistory.UpdateExecutionAsync(execution, ct).ConfigureAwait(false);
            ExecutionStarted?.Invoke(this, execution);

            var effectiveTargetPaths = targetPaths is { Length: > 0 }
                ? targetPaths
                : new[] { _storageOptions.RootPath };
            var result = await ExecuteMaintenanceTaskAsync(
                taskType,
                options ?? new MaintenanceTaskOptions(),
                effectiveTargetPaths,
                execution,
                executionCts.Token);

            execution.Result = result;
            execution.FilesProcessed = result.FilesProcessed;
            execution.IssuesFound = result.IssuesFound;
            execution.IssuesResolved = result.IssuesResolved;
            execution.BytesProcessed = result.TotalBytesScanned;
            execution.BytesSaved = result.BytesSaved;

            execution.Status = result.Success
                ? MaintenanceExecutionStatus.Completed
                : MaintenanceExecutionStatus.CompletedWithWarnings;

            execution.CompletedAt = DateTimeOffset.UtcNow;

        }
        catch (OperationCanceledException)
        {
            execution.Status = MaintenanceExecutionStatus.Cancelled;
            execution.CompletedAt = DateTimeOffset.UtcNow;
            execution.ErrorMessage = "Execution was cancelled";
        }
        catch (Exception ex)
        {
            execution.Status = MaintenanceExecutionStatus.Failed;
            execution.CompletedAt = DateTimeOffset.UtcNow;
            execution.ErrorMessage = ex.Message;
        }
        finally
        {
            _runningExecutions.TryRemove(execution.ExecutionId, out _);
            _activeExecutions.TryRemove(execution.ExecutionId, out _);
            _explicitCancellations.TryRemove(execution.ExecutionId, out _);
            try
            {
                await _scheduleManager.ExecutionHistory
                    .UpdateExecutionAsync(execution, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                executionCts.Dispose();
            }
        }

        if (execution.Status is MaintenanceExecutionStatus.Completed or
            MaintenanceExecutionStatus.CompletedWithWarnings)
        {
            ExecutionCompleted?.Invoke(this, execution);
        }
        else
        {
            ExecutionFailed?.Invoke(this, execution);
        }

        return execution;
    }

    public async Task<MaintenanceExecution> TriggerScheduleAsync(string scheduleId, CancellationToken ct = default)
    {
        if (!_outstandingScheduleExecutions.TryAdd(scheduleId, string.Empty))
        {
            throw new InvalidOperationException(
                $"Schedule '{scheduleId}' already has an execution queued or running");
        }

        MaintenanceExecution execution;
        try
        {
            var claimed = await _scheduleManager
                .TryClaimManualScheduleAsync(
                    scheduleId,
                    DateTimeOffset.UtcNow,
                    _leaseOwner,
                    s_executionLeaseDuration,
                    ct)
                .ConfigureAwait(false);
            if (claimed is null)
            {
                throw new InvalidOperationException(
                    $"Schedule '{scheduleId}' already has an execution queued or running");
            }

            execution = await QueueExecutionFromClaimAsync(claimed, ct).ConfigureAwait(false);
        }
        catch
        {
            _outstandingScheduleExecutions.TryRemove(scheduleId, out _);
            throw;
        }

        _logger.LogInformation(
            "Manually triggered maintenance schedule '{Name}' (ID: {ScheduleId})",
            execution.ScheduleName, scheduleId);

        return execution;
    }

    public Task<bool> CancelExecutionAsync(string executionId)
    {
        if (_runningExecutions.TryGetValue(executionId, out var cts))
        {
            try
            {
                _explicitCancellations[executionId] = 0;
                cts.Cancel();
                _logger.LogInformation("Cancelled maintenance execution {ExecutionId}", executionId);
                return Task.FromResult(true);
            }
            catch (ObjectDisposedException)
            {
                _explicitCancellations.TryRemove(executionId, out _);
            }
        }

        return Task.FromResult(false);
    }

    public MaintenanceServiceStatus GetStatus()
    {
        var summary = _scheduleManager.GetStatusSummary();
        var executionsToday = _scheduleManager.ExecutionHistory
            .GetExecutionsByTimeRange(DateTimeOffset.UtcNow.Date, DateTimeOffset.UtcNow)
            .Count;

        return new MaintenanceServiceStatus(
            IsRunning: _isRunning,
            QueuedExecutions: QueuedExecutions,
            CurrentExecution: CurrentExecution,
            NextScheduledExecution: summary.NextDueSchedule,
            ActiveSchedules: summary.EnabledSchedules,
            TotalExecutionsToday: executionsToday,
            Uptime: DateTimeOffset.UtcNow - _startTime
        );
    }
}

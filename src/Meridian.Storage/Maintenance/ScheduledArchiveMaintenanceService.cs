using System.Collections.Concurrent;
using System.Threading.Channels;
using Meridian.Application.Pipeline;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;

    private MaintenanceExecution? _currentExecution;
    private bool _isRunning;

    public event EventHandler<MaintenanceExecution>? ExecutionStarted;
    public event EventHandler<MaintenanceExecution>? ExecutionCompleted;
    public event EventHandler<MaintenanceExecution>? ExecutionFailed;

    public bool IsRunning => _isRunning;
    public int QueuedExecutions => _executionQueue.Reader.Count;
    public MaintenanceExecution? CurrentExecution => _currentExecution;

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

        // Start the scheduler task and executor task
        var schedulerTask = RunSchedulerLoopAsync(stoppingToken);
        var executorTask = RunExecutorLoopAsync(stoppingToken);

        await Task.WhenAll(schedulerTask, executorTask);

        _isRunning = false;
        _logger.LogInformation("Archive maintenance scheduler stopped");
    }

    private async Task RunSchedulerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Check for due schedules every minute
                await Task.Delay(TimeSpan.FromMinutes(1), ct);

                var dueSchedules = _scheduleManager.GetDueSchedules(DateTimeOffset.UtcNow);
                foreach (var schedule in dueSchedules)
                {
                    _logger.LogInformation(
                        "Maintenance schedule '{Name}' is due for execution",
                        schedule.Name);

                    await QueueExecutionFromScheduleAsync(schedule, ct);
                }
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

    private async Task QueueExecutionFromScheduleAsync(ArchiveMaintenanceSchedule schedule, CancellationToken ct)
    {
        var execution = new MaintenanceExecution
        {
            ScheduleId = schedule.ScheduleId,
            ScheduleName = schedule.Name,
            TaskType = schedule.TaskType,
            ManualTrigger = false
        };

        await _scheduleManager.ExecutionHistory.RecordExecutionAsync(execution, ct).ConfigureAwait(false);

        await _executionQueue.Writer.WriteAsync(execution, ct);

        _logger.LogDebug(
            "Queued maintenance execution {ExecutionId} for schedule '{ScheduleName}'",
            execution.ExecutionId, schedule.Name);
    }

    private async Task RunMaintenanceExecutionAsync(MaintenanceExecution execution, CancellationToken ct)
    {
        var executionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _runningExecutions[execution.ExecutionId] = executionCts;
        _currentExecution = execution;

        execution.Status = MaintenanceExecutionStatus.Running;
        await _scheduleManager.ExecutionHistory.UpdateExecutionAsync(execution, ct).ConfigureAwait(false);

        ExecutionStarted?.Invoke(this, execution);

        _logger.LogInformation(
            "Starting maintenance execution {ExecutionId} ({TaskType})",
            execution.ExecutionId, execution.TaskType);

        try
        {
            // Get schedule for options
            var schedule = execution.ScheduleId != null
                ? _scheduleManager.GetSchedule(execution.ScheduleId)
                : null;

            var options = schedule?.Options ?? new MaintenanceTaskOptions();
            var targetPaths = schedule?.TargetPaths.ToArray() ?? new[] { _storageOptions.RootPath };

            // Set timeout
            var timeout = schedule?.MaxDuration ?? TimeSpan.FromHours(2);
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

            ExecutionCompleted?.Invoke(this, execution);
        }
        catch (OperationCanceledException)
        {
            execution.Status = ct.IsCancellationRequested
                ? MaintenanceExecutionStatus.Cancelled
                : MaintenanceExecutionStatus.TimedOut;
            execution.CompletedAt = DateTimeOffset.UtcNow;
            execution.ErrorMessage = execution.Status == MaintenanceExecutionStatus.Cancelled
                ? "Execution was cancelled"
                : "Execution timed out";

            _logger.LogWarning(
                "Maintenance execution {ExecutionId} {Status}",
                execution.ExecutionId, execution.Status);

            ExecutionFailed?.Invoke(this, execution);
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

            ExecutionFailed?.Invoke(this, execution);
        }
        finally
        {
            _runningExecutions.TryRemove(execution.ExecutionId, out _);
            _currentExecution = null;
            await _scheduleManager.ExecutionHistory.UpdateExecutionAsync(execution, ct).ConfigureAwait(false);

            // Update schedule with execution results
            if (execution.ScheduleId != null)
            {
                await _scheduleManager.UpdateScheduleAfterExecutionAsync(execution.ScheduleId, execution, ct).ConfigureAwait(false);
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
            PreserveOriginals: options.DryRun,
            MaxFileAge: TimeSpan.FromDays(options.FileAgeDaysThreshold)
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
        _currentExecution = execution;

        execution.Status = MaintenanceExecutionStatus.Running;
        await _scheduleManager.ExecutionHistory.UpdateExecutionAsync(execution, ct).ConfigureAwait(false);

        ExecutionStarted?.Invoke(this, execution);

        try
        {
            var result = await ExecuteMaintenanceTaskAsync(
                taskType,
                options ?? new MaintenanceTaskOptions(),
                targetPaths ?? new[] { _storageOptions.RootPath },
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

            ExecutionCompleted?.Invoke(this, execution);
        }
        catch (Exception ex)
        {
            execution.Status = MaintenanceExecutionStatus.Failed;
            execution.CompletedAt = DateTimeOffset.UtcNow;
            execution.ErrorMessage = ex.Message;

            ExecutionFailed?.Invoke(this, execution);
        }
        finally
        {
            _runningExecutions.TryRemove(execution.ExecutionId, out _);
            _currentExecution = null;
            await _scheduleManager.ExecutionHistory.UpdateExecutionAsync(execution, ct).ConfigureAwait(false);
        }

        return execution;
    }

    public async Task<MaintenanceExecution> TriggerScheduleAsync(string scheduleId, CancellationToken ct = default)
    {
        var schedule = _scheduleManager.GetSchedule(scheduleId)
            ?? throw new KeyNotFoundException($"Schedule '{scheduleId}' not found");

        var execution = new MaintenanceExecution
        {
            ScheduleId = schedule.ScheduleId,
            ScheduleName = schedule.Name,
            TaskType = schedule.TaskType,
            ManualTrigger = true
        };

        await _scheduleManager.ExecutionHistory.RecordExecutionAsync(execution, ct).ConfigureAwait(false);

        await _executionQueue.Writer.WriteAsync(execution, ct);

        _logger.LogInformation(
            "Manually triggered maintenance schedule '{Name}' (ID: {ScheduleId})",
            schedule.Name, scheduleId);

        return execution;
    }

    public Task<bool> CancelExecutionAsync(string executionId)
    {
        if (_runningExecutions.TryRemove(executionId, out var cts))
        {
            cts.Cancel();
            _logger.LogInformation("Cancelled maintenance execution {ExecutionId}", executionId);
            return Task.FromResult(true);
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
            CurrentExecution: _currentExecution,
            NextScheduledExecution: summary.NextDueSchedule,
            ActiveSchedules: summary.EnabledSchedules,
            TotalExecutionsToday: executionsToday,
            Uptime: DateTimeOffset.UtcNow - _startTime
        );
    }
}

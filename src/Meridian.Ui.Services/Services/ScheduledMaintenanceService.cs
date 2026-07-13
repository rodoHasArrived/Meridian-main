using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for scheduling and executing archive maintenance tasks.
/// Supports recurring maintenance jobs like verification, optimization, and cleanup.
/// </summary>
public sealed class ScheduledMaintenanceService
{
    private static readonly Lazy<ScheduledMaintenanceService> _instance = new(() => new ScheduledMaintenanceService());
    private readonly NotificationService _notificationService;
    private readonly List<MaintenanceTask> _tasks = new();
    private readonly List<MaintenanceExecutionLog> _executionLog = new();
    private readonly Dictionary<string, CancellationTokenSource> _runningTasks = new();
    private Timer? _schedulerTimer;
    private const int MaxLogEntries = 100;

    public static ScheduledMaintenanceService Instance => _instance.Value;

    private ScheduledMaintenanceService()
    {
        _notificationService = NotificationService.Instance;
        InitializeDefaultTasks();
    }

    /// <summary>
    /// Gets all configured maintenance tasks.
    /// </summary>
    public IReadOnlyList<MaintenanceTask> Tasks => _tasks.AsReadOnly();

    /// <summary>
    /// Gets the maintenance execution log.
    /// </summary>
    public IReadOnlyList<MaintenanceExecutionLog> ExecutionLog => _executionLog.AsReadOnly();

    /// <summary>
    /// Gets whether the scheduler is running.
    /// </summary>
    public bool IsSchedulerRunning => _schedulerTimer != null;

    /// <summary>
    /// Starts the maintenance scheduler.
    /// </summary>
    public void StartScheduler()
    {
        _schedulerTimer?.Dispose();
        _schedulerTimer = new Timer(
            async _ => await CheckAndExecuteScheduledTasksAsync(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromMinutes(1)); // Check every minute

        SchedulerStarted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Stops the maintenance scheduler.
    /// </summary>
    public void StopScheduler()
    {
        _schedulerTimer?.Dispose();
        _schedulerTimer = null;

        // Cancel all running tasks
        foreach (var cts in _runningTasks.Values)
        {
            cts.Cancel();
        }
        _runningTasks.Clear();

        SchedulerStopped?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Adds a new maintenance task.
    /// </summary>
    public void AddTask(MaintenanceTask task)
    {
        if (string.IsNullOrEmpty(task.Id))
        {
            task.Id = Guid.NewGuid().ToString();
        }

        _tasks.Add(task);
        TaskAdded?.Invoke(this, task);
    }

    /// <summary>
    /// Removes a maintenance task.
    /// </summary>
    public bool RemoveTask(string taskId)
    {
        var task = _tasks.FirstOrDefault(t => t.Id == taskId);
        if (task != null)
        {
            _tasks.Remove(task);
            TaskRemoved?.Invoke(this, task);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Updates an existing maintenance task.
    /// </summary>
    public bool UpdateTask(MaintenanceTask updatedTask)
    {
        var index = _tasks.FindIndex(t => t.Id == updatedTask.Id);
        if (index >= 0)
        {
            _tasks[index] = updatedTask;
            TaskUpdated?.Invoke(this, updatedTask);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Enables or disables a maintenance task.
    /// </summary>
    public void SetTaskEnabled(string taskId, bool enabled)
    {
        var task = _tasks.FirstOrDefault(t => t.Id == taskId);
        if (task != null)
        {
            task.IsEnabled = enabled;
            TaskUpdated?.Invoke(this, task);
        }
    }

    /// <summary>
    /// Runs a maintenance task immediately.
    /// </summary>
    public async Task<MaintenanceResult> RunTaskNowAsync(string taskId, bool dryRun = false, CancellationToken ct = default)
    {
        var task = _tasks.FirstOrDefault(t => t.Id == taskId);
        if (task == null)
        {
            return new MaintenanceResult
            {
                TaskId = taskId,
                Success = false,
                Message = "Task not found"
            };
        }

        return await ExecuteTaskAsync(task, dryRun);
    }

    /// <summary>
    /// Cancels a running maintenance task.
    /// </summary>
    public bool CancelTask(string taskId)
    {
        if (_runningTasks.TryGetValue(taskId, out var cts))
        {
            cts.Cancel();
            _runningTasks.Remove(taskId);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Gets the next scheduled run time for a task.
    /// </summary>
    public DateTime? GetNextRunTime(string taskId)
    {
        var task = _tasks.FirstOrDefault(t => t.Id == taskId);
        return task?.GetNextRunTime();
    }

    /// <summary>
    /// Gets upcoming maintenance tasks.
    /// </summary>
    public IReadOnlyList<(MaintenanceTask Task, DateTime NextRun)> GetUpcomingTasks(int count = 5)
    {
        return _tasks
            .Where(t => t.IsEnabled)
            .Select(t => (Task: t, NextRun: t.GetNextRunTime()))
            .Where(x => x.NextRun.HasValue)
            .OrderBy(x => x.NextRun!.Value)
            .Take(count)
            .Select(x => (x.Task, x.NextRun!.Value))
            .ToList();
    }

    private void InitializeDefaultTasks()
    {
        // Add default maintenance tasks
        _tasks.Add(new MaintenanceTask
        {
            Id = "daily-verification",
            Name = "Daily Verification",
            Description = "Verify integrity of recent data files (last 7 days)",
            TaskType = MaintenanceTaskType.Verification,
            Schedule = new MaintenanceTimingConfig
            {
                ScheduleType = ScheduleType.Daily,
                TimeOfDay = new TimeSpan(3, 0, 0) // 3 AM
            },
            Scope = MaintenanceScope.Last7Days,
            IsEnabled = true
        });

        _tasks.Add(new MaintenanceTask
        {
            Id = "weekly-optimization",
            Name = "Weekly Optimization",
            Description = "Optimize storage by compressing warm tier files",
            TaskType = MaintenanceTaskType.Optimization,
            Schedule = new MaintenanceTimingConfig
            {
                ScheduleType = ScheduleType.Weekly,
                DayOfWeek = DayOfWeek.Sunday,
                TimeOfDay = new TimeSpan(4, 0, 0) // 4 AM Sunday
            },
            Scope = MaintenanceScope.WarmTier,
            IsEnabled = true
        });

        _tasks.Add(new MaintenanceTask
        {
            Id = "monthly-audit",
            Name = "Monthly Full Audit",
            Description = "Complete archive verification and integrity audit",
            TaskType = MaintenanceTaskType.FullAudit,
            Schedule = new MaintenanceTimingConfig
            {
                ScheduleType = ScheduleType.Monthly,
                DayOfMonth = 1,
                TimeOfDay = new TimeSpan(2, 0, 0) // 2 AM on 1st of month
            },
            Scope = MaintenanceScope.All,
            IsEnabled = true
        });

        _tasks.Add(new MaintenanceTask
        {
            Id = "daily-cleanup",
            Name = "Daily Cleanup",
            Description = "Remove expired files according to retention policy",
            TaskType = MaintenanceTaskType.Cleanup,
            Schedule = new MaintenanceTimingConfig
            {
                ScheduleType = ScheduleType.Daily,
                TimeOfDay = new TimeSpan(5, 0, 0) // 5 AM
            },
            Scope = MaintenanceScope.All,
            IsEnabled = false // Disabled by default for safety
        });
    }

    private async Task CheckAndExecuteScheduledTasksAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        foreach (var task in _tasks.Where(t => t.IsEnabled && !_runningTasks.ContainsKey(t.Id)))
        {
            if (task.ShouldRunNow(now))
            {
                _ = ExecuteTaskAsync(task, dryRun: false);
            }
        }

        await Task.CompletedTask;
    }

    private async Task<MaintenanceResult> ExecuteTaskAsync(MaintenanceTask task, bool dryRun, CancellationToken ct = default)
    {
        var result = new MaintenanceResult
        {
            TaskId = task.Id,
            TaskName = task.Name,
            StartTime = DateTime.UtcNow,
            IsDryRun = dryRun
        };

        // Mark task as running
        var cts = new CancellationTokenSource();
        _runningTasks[task.Id] = cts;
        task.IsRunning = true;
        task.LastRunStart = result.StartTime;

        TaskStarted?.Invoke(this, task);

        await _notificationService.NotifyScheduledJobAsync(task.Name, started: true);

        try
        {
            // Execute based on task type
            result = task.TaskType switch
            {
                MaintenanceTaskType.Verification => await ExecuteVerificationAsync(task, dryRun, cts.Token),
                MaintenanceTaskType.Optimization => await ExecuteOptimizationAsync(task, dryRun, cts.Token),
                MaintenanceTaskType.Cleanup => await ExecuteCleanupAsync(task, dryRun, cts.Token),
                MaintenanceTaskType.FullAudit => await ExecuteFullAuditAsync(task, dryRun, cts.Token),
                MaintenanceTaskType.Compression => await ExecuteCompressionAsync(task, dryRun, cts.Token),
                MaintenanceTaskType.Deduplication => await ExecuteDeduplicationAsync(task, dryRun, cts.Token),
                _ => new MaintenanceResult { Success = false, Message = "Unknown task type" }
            };
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.Message = "Task was cancelled";
            result.WasCancelled = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Task failed: {ex.Message}";
            result.Error = ex.ToString();
        }
        finally
        {
            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime.Value - result.StartTime;

            // Update task status
            task.IsRunning = false;
            task.LastRunEnd = result.EndTime;
            task.LastRunSuccess = result.Success;
            task.LastRunMessage = result.Message;

            _runningTasks.Remove(task.Id);

            // Log execution
            LogExecution(result);

            TaskCompleted?.Invoke(this, (task, result));

            await _notificationService.NotifyScheduledJobAsync(
                task.Name,
                started: false,
                success: result.Success);
        }

        return result;
    }

    // Executors delegate to the real archive maintenance API (/api/admin/maintenance/run,
    // backed by ScheduledArchiveMaintenanceService in Meridian.Storage). Task types without a
    // server-side implementation report an honest not-executed result instead of fabricating
    // success metrics.

    private Task<MaintenanceResult> ExecuteVerificationAsync(MaintenanceTask task, bool dryRun, CancellationToken ct)
        => ExecuteArchiveMaintenanceAsync(task, dryRun, serverTaskType: "HealthCheck", ct);

    private Task<MaintenanceResult> ExecuteOptimizationAsync(MaintenanceTask task, bool dryRun, CancellationToken ct)
        => ExecuteArchiveMaintenanceAsync(task, dryRun, serverTaskType: "Compression", ct);

    private Task<MaintenanceResult> ExecuteCleanupAsync(MaintenanceTask task, bool dryRun, CancellationToken ct)
        => ExecuteArchiveMaintenanceAsync(task, dryRun, serverTaskType: "Cleanup", ct);

    private Task<MaintenanceResult> ExecuteFullAuditAsync(MaintenanceTask task, bool dryRun, CancellationToken ct)
        => ExecuteArchiveMaintenanceAsync(task, dryRun, serverTaskType: "IntegrityCheck", ct);

    private Task<MaintenanceResult> ExecuteCompressionAsync(MaintenanceTask task, bool dryRun, CancellationToken ct)
        => ExecuteArchiveMaintenanceAsync(task, dryRun, serverTaskType: "Compression", ct);

    private Task<MaintenanceResult> ExecuteDeduplicationAsync(MaintenanceTask task, bool dryRun, CancellationToken ct)
        => Task.FromResult(BuildNotImplementedResult(
            task,
            dryRun,
            "Deduplication has no server-side implementation. No operation was executed."));

    /// <summary>
    /// Runs a maintenance task through the real archive maintenance API and translates the
    /// execution record into a <see cref="MaintenanceResult"/> with genuine metrics. Never
    /// fabricates counts, durations, or savings: when the API is unreachable or reports failure,
    /// the result honestly reports failure with the underlying error.
    /// </summary>
    private async Task<MaintenanceResult> ExecuteArchiveMaintenanceAsync(
        MaintenanceTask task,
        bool dryRun,
        string serverTaskType,
        CancellationToken ct)
    {
        var result = new MaintenanceResult
        {
            TaskId = task.Id,
            TaskName = task.Name,
            StartTime = DateTime.UtcNow,
            IsDryRun = dryRun
        };

        if (dryRun)
        {
            // The archive maintenance run endpoint does not expose a dry-run mode; report that
            // honestly instead of inventing a preview.
            Log.Warning(
                "Maintenance task {TaskId} ({ServerTaskType}) requested a dry run, which the archive maintenance API does not support; no operation was executed.",
                task.Id,
                serverTaskType);
            result.Success = false;
            result.Message = "Dry run is not supported by the archive maintenance API. No operation was executed.";
            return result;
        }

        var response = await ApiClientService.Instance.PostWithResponseAsync<ArchiveMaintenanceExecutionDto>(
            "/api/admin/maintenance/run",
            new { taskType = serverTaskType },
            ct);

        if (!response.Success || response.Data is null)
        {
            Log.Warning(
                "Maintenance task {TaskId} ({ServerTaskType}) failed: archive maintenance API returned status {StatusCode} with error {Error}.",
                task.Id,
                serverTaskType,
                response.StatusCode,
                response.ErrorMessage);
            result.Success = false;
            result.Message = "Archive maintenance API request failed. No maintenance result is available.";
            result.Error = response.ErrorMessage;
            return result;
        }

        var execution = response.Data;
        var status = ResolveExecutionStatus(execution.Status);
        var completed =
            string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "CompletedWithWarnings", StringComparison.OrdinalIgnoreCase);

        result.Success = completed;
        result.Message = !string.IsNullOrWhiteSpace(execution.Result?.Summary)
            ? execution.Result!.Summary!
            : completed
                ? $"Archive maintenance ({serverTaskType}) completed."
                : $"Archive maintenance ({serverTaskType}) ended with status {status ?? "unknown"}.";
        result.Error = execution.ErrorMessage;
        result.FilesProcessed = execution.FilesProcessed;
        result.FilesFailed = execution.Result?.FilesFailed ?? 0;
        result.FilesSuccessful = Math.Max(0, execution.FilesProcessed - result.FilesFailed);
        result.BytesSaved = execution.BytesSaved;

        if (!completed)
        {
            Log.Warning(
                "Maintenance task {TaskId} ({ServerTaskType}) ended with status {Status}: {Error}",
                task.Id,
                serverTaskType,
                status,
                execution.ErrorMessage);
        }

        return result;
    }

    /// <summary>
    /// Normalizes the execution status, which the host may serialize as either an enum name or
    /// a numeric value depending on its JSON options.
    /// </summary>
    private static string? ResolveExecutionStatus(System.Text.Json.JsonElement status) => status.ValueKind switch
    {
        System.Text.Json.JsonValueKind.String => status.GetString(),
        System.Text.Json.JsonValueKind.Number when status.TryGetInt32(out var value) => value switch
        {
            0 => "Pending",
            1 => "Running",
            2 => "Completed",
            3 => "CompletedWithWarnings",
            4 => "Failed",
            5 => "Cancelled",
            6 => "TimedOut",
            _ => value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        },
        _ => null
    };

    private static MaintenanceResult BuildNotImplementedResult(MaintenanceTask task, bool dryRun, string message)
    {
        Log.Warning(
            "Maintenance task {TaskId} of type {TaskType} is not implemented; reporting not-executed instead of fabricating a result.",
            task.Id,
            task.TaskType);

        return new MaintenanceResult
        {
            TaskId = task.Id,
            TaskName = task.Name,
            StartTime = DateTime.UtcNow,
            IsDryRun = dryRun,
            Success = false,
            Message = message
        };
    }

    /// <summary>
    /// Client-side projection of the archive maintenance execution record returned by
    /// /api/admin/maintenance/run (Meridian.Storage.Maintenance.MaintenanceExecution).
    /// Status is kept as a raw JSON element so the payload deserializes regardless of the
    /// host's enum serialization settings (string names or numeric values).
    /// </summary>
    private sealed class ArchiveMaintenanceExecutionDto
    {
        public string? ExecutionId { get; set; }
        public System.Text.Json.JsonElement Status { get; set; }
        public int FilesProcessed { get; set; }
        public int IssuesFound { get; set; }
        public int IssuesResolved { get; set; }
        public long BytesProcessed { get; set; }
        public long BytesSaved { get; set; }
        public string? ErrorMessage { get; set; }
        public ArchiveMaintenanceResultDto? Result { get; set; }
    }

    private sealed class ArchiveMaintenanceResultDto
    {
        public bool Success { get; set; }
        public string? Summary { get; set; }
        public int FilesProcessed { get; set; }
        public int FilesFailed { get; set; }
        public int FilesSkipped { get; set; }
    }

    private void LogExecution(MaintenanceResult result)
    {
        _executionLog.Insert(0, new MaintenanceExecutionLog
        {
            TaskId = result.TaskId,
            TaskName = result.TaskName,
            StartTime = result.StartTime,
            EndTime = result.EndTime,
            Duration = result.Duration,
            Success = result.Success,
            Message = result.Message,
            IsDryRun = result.IsDryRun,
            FilesProcessed = result.FilesProcessed,
            BytesSaved = result.BytesSaved
        });

        // Trim log
        while (_executionLog.Count > MaxLogEntries)
        {
            _executionLog.RemoveAt(_executionLog.Count - 1);
        }
    }

    /// <summary>
    /// Event raised when the scheduler starts.
    /// </summary>
    public event EventHandler? SchedulerStarted;

    /// <summary>
    /// Event raised when the scheduler stops.
    /// </summary>
    public event EventHandler? SchedulerStopped;

    /// <summary>
    /// Event raised when a task is added.
    /// </summary>
    public event EventHandler<MaintenanceTask>? TaskAdded;

    /// <summary>
    /// Event raised when a task is removed.
    /// </summary>
    public event EventHandler<MaintenanceTask>? TaskRemoved;

    /// <summary>
    /// Event raised when a task is updated.
    /// </summary>
    public event EventHandler<MaintenanceTask>? TaskUpdated;

    /// <summary>
    /// Event raised when a task starts execution.
    /// </summary>
    public event EventHandler<MaintenanceTask>? TaskStarted;

    /// <summary>
    /// Event raised when a task completes execution.
    /// </summary>
    public event EventHandler<(MaintenanceTask Task, MaintenanceResult Result)>? TaskCompleted;
}

/// <summary>
/// Represents a scheduled maintenance task.
/// </summary>
public sealed class MaintenanceTask
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public MaintenanceTaskType TaskType { get; set; }
    public MaintenanceTimingConfig Schedule { get; set; } = new();
    public MaintenanceScope Scope { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsRunning { get; set; }

    // Last run information
    public DateTime? LastRunStart { get; set; }
    public DateTime? LastRunEnd { get; set; }
    public bool? LastRunSuccess { get; set; }
    public string? LastRunMessage { get; set; }

    /// <summary>
    /// Checks if the task should run at the given time.
    /// </summary>
    public bool ShouldRunNow(DateTime now)
    {
        if (!IsEnabled || IsRunning)
            return false;

        var nextRun = GetNextRunTime();
        if (!nextRun.HasValue)
            return false;

        // Check if we're within the execution window (within 1 minute of scheduled time)
        return now >= nextRun.Value && now < nextRun.Value.AddMinutes(1);
    }

    /// <summary>
    /// Gets the next scheduled run time.
    /// </summary>
    public DateTime? GetNextRunTime()
    {
        var now = DateTime.UtcNow;
        var today = now.Date;

        return Schedule.ScheduleType switch
        {
            ScheduleType.Daily => GetNextDailyRun(now, today),
            ScheduleType.Weekly => GetNextWeeklyRun(now, today),
            ScheduleType.Monthly => GetNextMonthlyRun(now, today),
            ScheduleType.Hourly => GetNextHourlyRun(now),
            _ => null
        };
    }

    private DateTime GetNextDailyRun(DateTime now, DateTime today)
    {
        var scheduledTime = today + Schedule.TimeOfDay;
        return now > scheduledTime ? scheduledTime.AddDays(1) : scheduledTime;
    }

    private DateTime GetNextWeeklyRun(DateTime now, DateTime today)
    {
        var daysUntilTarget = ((int)Schedule.DayOfWeek - (int)today.DayOfWeek + 7) % 7;
        if (daysUntilTarget == 0 && now.TimeOfDay > Schedule.TimeOfDay)
        {
            daysUntilTarget = 7;
        }
        return today.AddDays(daysUntilTarget) + Schedule.TimeOfDay;
    }

    private DateTime GetNextMonthlyRun(DateTime now, DateTime today)
    {
        var targetDay = Math.Min(Schedule.DayOfMonth, DateTime.DaysInMonth(today.Year, today.Month));
        var scheduledTime = new DateTime(today.Year, today.Month, targetDay) + Schedule.TimeOfDay;

        if (now > scheduledTime)
        {
            var nextMonth = today.AddMonths(1);
            targetDay = Math.Min(Schedule.DayOfMonth, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month));
            scheduledTime = new DateTime(nextMonth.Year, nextMonth.Month, targetDay) + Schedule.TimeOfDay;
        }

        return scheduledTime;
    }

    private DateTime GetNextHourlyRun(DateTime now)
    {
        var nextHour = now.Date.AddHours(now.Hour + 1);
        return nextHour.AddMinutes(Schedule.MinuteOfHour);
    }
}

/// <summary>
/// Maintenance task schedule configuration.
/// </summary>
public sealed class MaintenanceTimingConfig
{
    public ScheduleType ScheduleType { get; set; }
    public TimeSpan TimeOfDay { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public int DayOfMonth { get; set; } = 1;
    public int MinuteOfHour { get; set; }
}

/// <summary>
/// Types of maintenance tasks.
/// </summary>
public enum MaintenanceTaskType : byte
{
    Verification,
    Optimization,
    Cleanup,
    FullAudit,
    Compression,
    Deduplication
}

/// <summary>
/// Schedule types for maintenance tasks.
/// </summary>
public enum ScheduleType : byte
{
    Hourly,
    Daily,
    Weekly,
    Monthly
}

/// <summary>
/// Scope of maintenance operations.
/// </summary>
public enum MaintenanceScope : byte
{
    All,
    HotTier,
    WarmTier,
    ColdTier,
    Last7Days,
    Last30Days,
    Custom
}

/// <summary>
/// Result of a maintenance task execution.
/// </summary>
public sealed class MaintenanceResult
{
    public string TaskId { get; set; } = string.Empty;
    public string TaskName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan? Duration { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Error { get; set; }
    public bool IsDryRun { get; set; }
    public bool WasCancelled { get; set; }
    public int FilesProcessed { get; set; }
    public int FilesSuccessful { get; set; }
    public int FilesFailed { get; set; }
    public long BytesSaved { get; set; }
}

/// <summary>
/// Log entry for maintenance task executions.
/// </summary>
public sealed class MaintenanceExecutionLog
{
    public string TaskId { get; set; } = string.Empty;
    public string TaskName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan? Duration { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool IsDryRun { get; set; }
    public int FilesProcessed { get; set; }
    public long BytesSaved { get; set; }

    /// <summary>
    /// Gets a formatted duration string.
    /// </summary>
    public string DurationText
    {
        get
        {
            if (!Duration.HasValue)
                return "N/A";
            var d = Duration.Value;
            if (d.TotalHours >= 1)
                return $"{(int)d.TotalHours}h {d.Minutes}m";
            if (d.TotalMinutes >= 1)
                return $"{d.Minutes}m {d.Seconds}s";
            return $"{d.Seconds}s";
        }
    }
}

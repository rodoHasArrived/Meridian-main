using System.Collections.Concurrent;
using System.Text.Json;
using Meridian.Core.Scheduling;
using Microsoft.Extensions.Logging;

namespace Meridian.Storage.Maintenance;

/// <summary>
/// Manages archive maintenance schedules with file-based persistence.
/// Thread-safe implementation for concurrent access.
/// </summary>
public sealed class ArchiveMaintenanceScheduleManager : IArchiveMaintenanceScheduleManager
{
    private readonly ILogger<ArchiveMaintenanceScheduleManager> _logger;
    private readonly string _schedulesPath;
    private readonly ConcurrentDictionary<string, ArchiveMaintenanceSchedule> _schedules = new();
    private readonly MaintenanceExecutionHistory _executionHistory;
    private readonly SemaphoreSlim _persistLock = new(1, 1);

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public event EventHandler<ArchiveMaintenanceSchedule>? ScheduleCreated;
    public event EventHandler<ArchiveMaintenanceSchedule>? ScheduleUpdated;
    public event EventHandler<string>? ScheduleDeleted;

    public MaintenanceExecutionHistory ExecutionHistory => _executionHistory;

    public ArchiveMaintenanceScheduleManager(
        ILogger<ArchiveMaintenanceScheduleManager> logger,
        string dataRoot,
        MaintenanceExecutionHistory? executionHistory = null)
    {
        _logger = logger;
        _schedulesPath = Path.Combine(dataRoot, ".maintenance", "schedules.json");
        _executionHistory = executionHistory ?? new MaintenanceExecutionHistory(dataRoot);

        Directory.CreateDirectory(Path.GetDirectoryName(_schedulesPath)!);
        LoadSchedules();
    }

    public IReadOnlyList<ArchiveMaintenanceSchedule> GetAllSchedules()
    {
        return _schedules.Values
            .OrderBy(s => s.Name)
            .ToList();
    }

    public ArchiveMaintenanceSchedule? GetSchedule(string scheduleId)
    {
        return _schedules.TryGetValue(scheduleId, out var schedule) ? schedule : null;
    }

    public async Task<ArchiveMaintenanceSchedule> CreateScheduleAsync(
        ArchiveMaintenanceSchedule schedule,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (string.IsNullOrWhiteSpace(schedule.Name))
            throw new ArgumentException("Schedule name is required", nameof(schedule));

        if (!CronExpressionParser.IsValid(schedule.CronExpression))
            throw new ArgumentException($"Invalid cron expression: {schedule.CronExpression}", nameof(schedule));

        // Calculate next execution
        schedule.NextExecutionAt = schedule.CalculateNextExecution();

        if (!_schedules.TryAdd(schedule.ScheduleId, schedule))
            throw new InvalidOperationException($"Schedule with ID '{schedule.ScheduleId}' already exists");

        await PersistSchedulesAsync(ct);

        _logger.LogInformation(
            "Created maintenance schedule '{Name}' (ID: {ScheduleId}) with cron '{Cron}', next execution: {NextExecution}",
            schedule.Name, schedule.ScheduleId, schedule.CronExpression, schedule.NextExecutionAt);

        ScheduleCreated?.Invoke(this, schedule);
        return schedule;
    }

    public async Task<ArchiveMaintenanceSchedule> CreateFromPresetAsync(
        string presetName,
        string name,
        CancellationToken ct = default)
    {
        var schedule = presetName.ToLowerInvariant() switch
        {
            "daily-health" or "health" => MaintenanceSchedulePresets.DailyHealthCheck(name),
            "weekly-full" or "full" => MaintenanceSchedulePresets.WeeklyFullMaintenance(name),
            "daily-tier" or "tier" => MaintenanceSchedulePresets.DailyTierMigration(name),
            "monthly-compression" or "compression" => MaintenanceSchedulePresets.MonthlyCompression(name),
            "daily-retention" or "retention" => MaintenanceSchedulePresets.DailyRetentionEnforcement(name),
            _ => throw new ArgumentException($"Unknown preset: {presetName}", nameof(presetName))
        };

        return await CreateScheduleAsync(schedule, ct);
    }

    public async Task<ArchiveMaintenanceSchedule> UpdateScheduleAsync(
        ArchiveMaintenanceSchedule schedule,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (!_schedules.ContainsKey(schedule.ScheduleId))
            throw new KeyNotFoundException($"Schedule '{schedule.ScheduleId}' not found");

        if (!CronExpressionParser.IsValid(schedule.CronExpression))
            throw new ArgumentException($"Invalid cron expression: {schedule.CronExpression}", nameof(schedule));

        schedule.ModifiedAt = DateTimeOffset.UtcNow;
        schedule.NextExecutionAt = schedule.CalculateNextExecution();

        _schedules[schedule.ScheduleId] = schedule;

        await PersistSchedulesAsync(ct);

        _logger.LogInformation(
            "Updated maintenance schedule '{Name}' (ID: {ScheduleId})",
            schedule.Name, schedule.ScheduleId);

        ScheduleUpdated?.Invoke(this, schedule);
        return schedule;
    }

    public async Task<bool> DeleteScheduleAsync(string scheduleId, CancellationToken ct = default)
    {
        if (!_schedules.TryRemove(scheduleId, out var schedule))
            return false;

        await PersistSchedulesAsync(ct);

        _logger.LogInformation(
            "Deleted maintenance schedule '{Name}' (ID: {ScheduleId})",
            schedule.Name, scheduleId);

        ScheduleDeleted?.Invoke(this, scheduleId);
        return true;
    }

    public async Task<bool> SetScheduleEnabledAsync(string scheduleId, bool enabled, CancellationToken ct = default)
    {
        if (!_schedules.TryGetValue(scheduleId, out var schedule))
            return false;

        schedule.Enabled = enabled;
        schedule.ModifiedAt = DateTimeOffset.UtcNow;

        if (enabled)
        {
            schedule.NextExecutionAt = schedule.CalculateNextExecution();
        }

        await PersistSchedulesAsync(ct);

        _logger.LogInformation(
            "Maintenance schedule '{Name}' (ID: {ScheduleId}) {Action}",
            schedule.Name, scheduleId, enabled ? "enabled" : "disabled");

        ScheduleUpdated?.Invoke(this, schedule);
        return true;
    }

    public IReadOnlyList<ArchiveMaintenanceSchedule> GetDueSchedules(DateTimeOffset asOf)
    {
        return _schedules.Values
            .Where(s => s.Enabled &&
                        s.NextExecutionAt.HasValue &&
                        s.NextExecutionAt.Value <= asOf)
            .OrderBy(s => s.Priority)
            .ThenBy(s => s.NextExecutionAt)
            .ToList();
    }

    public MaintenanceScheduleSummary GetStatusSummary()
    {
        var schedules = _schedules.Values.ToList();
        var byTaskType = schedules
            .GroupBy(s => s.TaskType)
            .ToDictionary(g => g.Key, g => g.Count());

        var nextDue = schedules
            .Where(s => s.Enabled && s.NextExecutionAt.HasValue)
            .OrderBy(s => s.NextExecutionAt)
            .FirstOrDefault();

        return new MaintenanceScheduleSummary(
            TotalSchedules: schedules.Count,
            EnabledSchedules: schedules.Count(s => s.Enabled),
            DisabledSchedules: schedules.Count(s => !s.Enabled),
            ByTaskType: byTaskType,
            NextDueSchedule: nextDue?.NextExecutionAt,
            NextDueScheduleName: nextDue?.Name
        );
    }

    public void UpdateScheduleAfterExecution(string scheduleId, MaintenanceExecution execution)
    {
        if (!_schedules.TryGetValue(scheduleId, out var schedule))
            return;

        schedule.LastExecutedAt = execution.StartedAt;
        schedule.LastExecutionId = execution.ExecutionId;
        schedule.LastExecutionStatus = execution.Status;
        schedule.ExecutionCount++;

        if (execution.Status == MaintenanceExecutionStatus.Completed ||
            execution.Status == MaintenanceExecutionStatus.CompletedWithWarnings)
        {
            schedule.SuccessfulExecutions++;
        }
        else if (execution.Status == MaintenanceExecutionStatus.Failed ||
                 execution.Status == MaintenanceExecutionStatus.TimedOut)
        {
            schedule.FailedExecutions++;
        }

        // Calculate next execution from the time it ran
        schedule.NextExecutionAt = schedule.CalculateNextExecution(execution.StartedAt);

        _ = PersistSchedulesAsync(CancellationToken.None);
    }

    private void LoadSchedules()
    {
        if (!File.Exists(_schedulesPath))
        {
            _logger.LogDebug("No existing maintenance schedules found at {Path}", _schedulesPath);
            return;
        }

        try
        {
            var json = File.ReadAllText(_schedulesPath);
            var schedules = JsonSerializer.Deserialize<List<ArchiveMaintenanceSchedule>>(json, s_jsonOptions);

            if (schedules != null)
            {
                foreach (var schedule in schedules)
                {
                    // Recalculate next execution if it's in the past
                    if (schedule.Enabled &&
                        (!schedule.NextExecutionAt.HasValue || schedule.NextExecutionAt < DateTimeOffset.UtcNow))
                    {
                        schedule.NextExecutionAt = schedule.CalculateNextExecution();
                    }

                    _schedules[schedule.ScheduleId] = schedule;
                }
            }

            _logger.LogInformation("Loaded {Count} maintenance schedules from {Path}", _schedules.Count, _schedulesPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load maintenance schedules from {Path}", _schedulesPath);
        }
    }

    private async Task PersistSchedulesAsync(CancellationToken ct)
    {
        await _persistLock.WaitAsync(ct);
        try
        {
            var schedules = _schedules.Values.ToList();
            var json = JsonSerializer.Serialize(schedules, s_jsonOptions);

            var tempPath = _schedulesPath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json, ct);
            File.Move(tempPath, _schedulesPath, overwrite: true);

            _logger.LogDebug("Persisted {Count} maintenance schedules to {Path}", schedules.Count, _schedulesPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist maintenance schedules");
            throw;
        }
        finally
        {
            _persistLock.Release();
        }
    }
}

/// <summary>
/// Tracks maintenance execution history with file-based persistence.
/// </summary>
public sealed class MaintenanceExecutionHistory : IMaintenanceExecutionHistory
{
    private readonly string _historyPath;
    private readonly ConcurrentDictionary<string, MaintenanceExecution> _executions = new();
    private readonly SemaphoreSlim _persistLock = new(1, 1);
    private const int MaxInMemoryExecutions = 1000;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public MaintenanceExecutionHistory(string dataRoot)
    {
        _historyPath = Path.Combine(dataRoot, ".maintenance", "history.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_historyPath)!);
        LoadHistory();
    }

    public void RecordExecution(MaintenanceExecution execution)
    {
        _executions[execution.ExecutionId] = execution;

        // Trim old entries if we exceed the limit
        if (_executions.Count > MaxInMemoryExecutions)
        {
            var oldestKeys = _executions
                .OrderBy(kvp => kvp.Value.StartedAt)
                .Take(_executions.Count - MaxInMemoryExecutions)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in oldestKeys)
            {
                _executions.TryRemove(key, out _);
            }
        }

        _ = PersistHistoryAsync(CancellationToken.None);
    }

    public void UpdateExecution(MaintenanceExecution execution)
    {
        _executions[execution.ExecutionId] = execution;
        _ = PersistHistoryAsync(CancellationToken.None);
    }

    public MaintenanceExecution? GetExecution(string executionId)
    {
        return _executions.TryGetValue(executionId, out var execution) ? execution : null;
    }

    public IReadOnlyList<MaintenanceExecution> GetRecentExecutions(int limit = 50)
    {
        return _executions.Values
            .OrderByDescending(e => e.StartedAt)
            .Take(limit)
            .ToList();
    }

    public IReadOnlyList<MaintenanceExecution> GetExecutionsForSchedule(string scheduleId, int limit = 50)
    {
        return _executions.Values
            .Where(e => e.ScheduleId == scheduleId)
            .OrderByDescending(e => e.StartedAt)
            .Take(limit)
            .ToList();
    }

    public IReadOnlyList<MaintenanceExecution> GetFailedExecutions(int limit = 50)
    {
        return _executions.Values
            .Where(e => e.Status == MaintenanceExecutionStatus.Failed ||
                       e.Status == MaintenanceExecutionStatus.TimedOut)
            .OrderByDescending(e => e.StartedAt)
            .Take(limit)
            .ToList();
    }

    public IReadOnlyList<MaintenanceExecution> GetExecutionsByTimeRange(DateTimeOffset from, DateTimeOffset to)
    {
        return _executions.Values
            .Where(e => e.StartedAt >= from && e.StartedAt <= to)
            .OrderByDescending(e => e.StartedAt)
            .ToList();
    }

    public ScheduleExecutionSummary GetScheduleSummary(string scheduleId, int recentCount = 10)
    {
        var executions = _executions.Values
            .Where(e => e.ScheduleId == scheduleId)
            .OrderByDescending(e => e.StartedAt)
            .ToList();

        var successful = executions.Count(e =>
            e.Status == MaintenanceExecutionStatus.Completed ||
            e.Status == MaintenanceExecutionStatus.CompletedWithWarnings);

        var failed = executions.Count(e =>
            e.Status == MaintenanceExecutionStatus.Failed ||
            e.Status == MaintenanceExecutionStatus.TimedOut);

        var completed = executions.Where(e => e.Duration.HasValue).ToList();
        var avgDuration = completed.Count > 0
            ? TimeSpan.FromTicks((long)completed.Average(e => e.Duration!.Value.Ticks))
            : TimeSpan.Zero;

        var lastExecution = executions.FirstOrDefault();

        return new ScheduleExecutionSummary(
            ScheduleId: scheduleId,
            ScheduleName: lastExecution?.ScheduleName ?? "Unknown",
            TotalExecutions: executions.Count,
            SuccessfulExecutions: successful,
            FailedExecutions: failed,
            SuccessRate: executions.Count > 0 ? (double)successful / executions.Count * 100 : 0,
            AverageDuration: avgDuration,
            LastExecutionAt: lastExecution?.StartedAt,
            LastStatus: lastExecution?.Status,
            NextScheduledAt: null,
            RecentExecutions: executions.Take(recentCount).ToList()
        );
    }

    public MaintenanceStatistics GetStatistics(TimeSpan? period = null)
    {
        var cutoff = period.HasValue
            ? DateTimeOffset.UtcNow - period.Value
            : DateTimeOffset.MinValue;

        var executions = _executions.Values
            .Where(e => e.StartedAt >= cutoff)
            .ToList();

        var last24h = executions.Count(e => e.StartedAt >= DateTimeOffset.UtcNow.AddHours(-24));
        var last7d = executions.Count(e => e.StartedAt >= DateTimeOffset.UtcNow.AddDays(-7));

        var successful = executions.Count(e =>
            e.Status == MaintenanceExecutionStatus.Completed ||
            e.Status == MaintenanceExecutionStatus.CompletedWithWarnings);

        var failed = executions.Count(e =>
            e.Status == MaintenanceExecutionStatus.Failed ||
            e.Status == MaintenanceExecutionStatus.TimedOut);

        var completed = executions.Where(e => e.Duration.HasValue).ToList();
        var avgDuration = completed.Count > 0
            ? TimeSpan.FromTicks((long)completed.Average(e => e.Duration!.Value.Ticks))
            : TimeSpan.Zero;

        return new MaintenanceStatistics(
            GeneratedAt: DateTimeOffset.UtcNow,
            TotalSchedules: 0, // Will be filled by caller
            EnabledSchedules: 0,
            DisabledSchedules: 0,
            TotalExecutions: executions.Count,
            SuccessfulExecutions: successful,
            FailedExecutions: failed,
            ExecutionsLast24Hours: last24h,
            ExecutionsLast7Days: last7d,
            TotalBytesProcessed: executions.Sum(e => e.BytesProcessed),
            TotalBytesSaved: executions.Sum(e => e.BytesSaved),
            TotalIssuesFound: executions.Sum(e => e.IssuesFound),
            TotalIssuesResolved: executions.Sum(e => e.IssuesResolved),
            AverageExecutionDuration: avgDuration,
            LastExecutionAt: executions.OrderByDescending(e => e.StartedAt).FirstOrDefault()?.StartedAt,
            NextScheduledExecution: null
        );
    }

    public async Task<int> CleanupOldRecordsAsync(int maxAgeDays = 90, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-maxAgeDays);
        var toRemove = _executions
            .Where(kvp => kvp.Value.StartedAt < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in toRemove)
        {
            _executions.TryRemove(key, out _);
        }

        if (toRemove.Count > 0)
        {
            await PersistHistoryAsync(ct);
        }

        return toRemove.Count;
    }

    private void LoadHistory()
    {
        if (!File.Exists(_historyPath))
            return;

        try
        {
            var json = File.ReadAllText(_historyPath);
            var executions = JsonSerializer.Deserialize<List<MaintenanceExecution>>(json, s_jsonOptions);

            if (executions != null)
            {
                // Load only the most recent executions
                foreach (var execution in executions.OrderByDescending(e => e.StartedAt).Take(MaxInMemoryExecutions))
                {
                    _executions[execution.ExecutionId] = execution;
                }
            }
        }
        catch
        {
            // Ignore load errors, start fresh
        }
    }

    private async Task PersistHistoryAsync(CancellationToken ct)
    {
        await _persistLock.WaitAsync(ct);
        try
        {
            var executions = _executions.Values
                .OrderByDescending(e => e.StartedAt)
                .Take(MaxInMemoryExecutions)
                .ToList();

            var json = JsonSerializer.Serialize(executions, s_jsonOptions);

            var tempPath = _historyPath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json, ct);
            File.Move(tempPath, _historyPath, overwrite: true);
        }
        finally
        {
            _persistLock.Release();
        }
    }
}

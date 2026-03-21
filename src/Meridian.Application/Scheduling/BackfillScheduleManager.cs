using System.Collections.Concurrent;
using System.Text.Json;
using Meridian.Core.Scheduling;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Scheduling;

/// <summary>
/// Manages backfill schedules including CRUD operations, persistence, and status tracking.
/// Thread-safe for concurrent access.
/// </summary>
public sealed class BackfillScheduleManager
{
    private readonly ConcurrentDictionary<string, BackfillSchedule> _schedules = new();
    private readonly SemaphoreSlim _persistLock = new(1, 1);
    private readonly ILogger<BackfillScheduleManager> _logger;
    private readonly string _schedulesDirectory;
    private readonly BackfillExecutionHistory _executionHistory;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _isLoaded;

    /// <summary>
    /// Event raised when a schedule is created.
    /// </summary>
    public event EventHandler<BackfillSchedule>? ScheduleCreated;

    /// <summary>
    /// Event raised when a schedule is updated.
    /// </summary>
    public event EventHandler<BackfillSchedule>? ScheduleUpdated;

    /// <summary>
    /// Event raised when a schedule is deleted.
    /// </summary>
    public event EventHandler<string>? ScheduleDeleted;

    /// <summary>
    /// Event raised when a schedule is due for execution.
    /// </summary>
#pragma warning disable CS0067 // Event will be raised when schedule timer is implemented
    public event EventHandler<BackfillSchedule>? ScheduleDue;
#pragma warning restore CS0067

    public BackfillScheduleManager(
        ILogger<BackfillScheduleManager> logger,
        string dataRoot,
        BackfillExecutionHistory? executionHistory = null)
    {
        _logger = logger;
        _schedulesDirectory = Path.Combine(dataRoot, "_backfill_schedules");
        _executionHistory = executionHistory ?? new BackfillExecutionHistory();
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    /// <summary>
    /// Execution history for viewing past runs.
    /// </summary>
    public BackfillExecutionHistory ExecutionHistory => _executionHistory;

    /// <summary>
    /// Load all schedules from disk.
    /// </summary>
    public async Task LoadSchedulesAsync(CancellationToken ct = default)
    {
        if (_isLoaded)
            return;

        try
        {
            await _persistLock.WaitAsync(ct);

            if (!Directory.Exists(_schedulesDirectory))
            {
                Directory.CreateDirectory(_schedulesDirectory);
                _isLoaded = true;
                return;
            }

            var files = Directory.GetFiles(_schedulesDirectory, "schedule_*.json");
            var loadedCount = 0;

            foreach (var file in files)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file, ct);
                    var schedule = JsonSerializer.Deserialize<BackfillSchedule>(json, _jsonOptions);

                    if (schedule != null)
                    {
                        // Recalculate next execution time
                        schedule.NextExecutionAt = schedule.CalculateNextExecution();
                        _schedules[schedule.ScheduleId] = schedule;
                        loadedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load schedule from {File}", file);
                }
            }

            _logger.LogInformation("Loaded {Count} backfill schedules", loadedCount);
            _isLoaded = true;
        }
        finally
        {
            _persistLock.Release();
        }
    }

    /// <summary>
    /// Create a new schedule.
    /// </summary>
    public async Task<BackfillSchedule> CreateScheduleAsync(
        BackfillSchedule schedule,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (!CronExpressionParser.IsValid(schedule.CronExpression))
            throw new ArgumentException($"Invalid cron expression: {schedule.CronExpression}");

        if (string.IsNullOrWhiteSpace(schedule.Name))
            throw new ArgumentException("Schedule name is required");

        // Calculate next execution
        schedule.NextExecutionAt = schedule.CalculateNextExecution();

        _schedules[schedule.ScheduleId] = schedule;
        await PersistScheduleAsync(schedule, ct);

        _logger.LogInformation(
            "Created schedule {ScheduleId}: {Name}, next execution: {NextExecution}",
            schedule.ScheduleId, schedule.Name, schedule.NextExecutionAt);

        ScheduleCreated?.Invoke(this, schedule);
        return schedule;
    }

    /// <summary>
    /// Create a schedule from a preset.
    /// </summary>
    public async Task<BackfillSchedule> CreateFromPresetAsync(
        string presetName,
        string scheduleName,
        IEnumerable<string>? symbols = null,
        CancellationToken ct = default)
    {
        var schedule = presetName.ToLowerInvariant() switch
        {
            "daily" or "dailygapfill" => BackfillSchedulePresets.DailyGapFill(scheduleName, symbols),
            "weekly" or "weeklyfullbackfill" => BackfillSchedulePresets.WeeklyFullBackfill(scheduleName, symbols),
            "eod" or "endofday" => BackfillSchedulePresets.EndOfDayUpdate(scheduleName, symbols),
            "monthly" or "monthlydeepbackfill" => BackfillSchedulePresets.MonthlyDeepBackfill(scheduleName, symbols),
            _ => throw new ArgumentException($"Unknown preset: {presetName}")
        };

        return await CreateScheduleAsync(schedule, ct);
    }

    /// <summary>
    /// Update an existing schedule.
    /// </summary>
    public async Task<BackfillSchedule> UpdateScheduleAsync(
        BackfillSchedule schedule,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (!_schedules.ContainsKey(schedule.ScheduleId))
            throw new KeyNotFoundException($"Schedule not found: {schedule.ScheduleId}");

        if (!CronExpressionParser.IsValid(schedule.CronExpression))
            throw new ArgumentException($"Invalid cron expression: {schedule.CronExpression}");

        schedule.ModifiedAt = DateTimeOffset.UtcNow;
        schedule.NextExecutionAt = schedule.CalculateNextExecution();

        _schedules[schedule.ScheduleId] = schedule;
        await PersistScheduleAsync(schedule, ct);

        _logger.LogInformation(
            "Updated schedule {ScheduleId}: {Name}",
            schedule.ScheduleId, schedule.Name);

        ScheduleUpdated?.Invoke(this, schedule);
        return schedule;
    }

    /// <summary>
    /// Delete a schedule.
    /// </summary>
    public Task<bool> DeleteScheduleAsync(string scheduleId, CancellationToken ct = default)
    {
        if (!_schedules.TryRemove(scheduleId, out var removed))
            return Task.FromResult(false);

        var filePath = GetScheduleFilePath(scheduleId);
        if (File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete schedule file: {Path}", filePath);
            }
        }

        _logger.LogInformation("Deleted schedule {ScheduleId}: {Name}", scheduleId, removed.Name);
        ScheduleDeleted?.Invoke(this, scheduleId);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Get a schedule by ID.
    /// </summary>
    public BackfillSchedule? GetSchedule(string scheduleId)
    {
        return _schedules.TryGetValue(scheduleId, out var schedule) ? schedule : null;
    }

    /// <summary>
    /// Get all schedules.
    /// </summary>
    public IReadOnlyList<BackfillSchedule> GetAllSchedules()
    {
        return _schedules.Values.OrderBy(s => s.Name).ToList();
    }

    /// <summary>
    /// Get enabled schedules.
    /// </summary>
    public IReadOnlyList<BackfillSchedule> GetEnabledSchedules()
    {
        return _schedules.Values
            .Where(s => s.Enabled)
            .OrderBy(s => s.NextExecutionAt)
            .ToList();
    }

    /// <summary>
    /// Get schedules due for execution.
    /// </summary>
    public IReadOnlyList<BackfillSchedule> GetDueSchedules(DateTimeOffset? asOf = null)
    {
        var now = asOf ?? DateTimeOffset.UtcNow;
        return _schedules.Values
            .Where(s => s.Enabled && s.NextExecutionAt.HasValue && s.NextExecutionAt.Value <= now)
            .OrderBy(s => s.NextExecutionAt)
            .ToList();
    }

    /// <summary>
    /// Get next schedule to execute.
    /// </summary>
    public BackfillSchedule? GetNextDueSchedule(DateTimeOffset? asOf = null)
    {
        return GetDueSchedules(asOf).FirstOrDefault();
    }

    /// <summary>
    /// Enable or disable a schedule.
    /// </summary>
    public async Task<bool> SetScheduleEnabledAsync(
        string scheduleId,
        bool enabled,
        CancellationToken ct = default)
    {
        if (!_schedules.TryGetValue(scheduleId, out var schedule))
            return false;

        schedule.Enabled = enabled;
        schedule.ModifiedAt = DateTimeOffset.UtcNow;

        if (enabled)
            schedule.NextExecutionAt = schedule.CalculateNextExecution();

        await PersistScheduleAsync(schedule, ct);

        _logger.LogInformation(
            "{Action} schedule {ScheduleId}: {Name}",
            enabled ? "Enabled" : "Disabled", scheduleId, schedule.Name);

        ScheduleUpdated?.Invoke(this, schedule);
        return true;
    }

    /// <summary>
    /// Record that a schedule has been executed.
    /// </summary>
    public async Task RecordExecutionAsync(
        BackfillSchedule schedule,
        BackfillExecutionLog execution,
        CancellationToken ct = default)
    {
        schedule.LastExecutedAt = DateTimeOffset.UtcNow;
        schedule.LastJobId = execution.JobId;
        schedule.ExecutionCount++;

        if (execution.Status == ExecutionStatus.Completed)
            schedule.SuccessfulExecutions++;
        else if (execution.Status == ExecutionStatus.Failed)
            schedule.FailedExecutions++;

        // Calculate next execution
        schedule.NextExecutionAt = schedule.CalculateNextExecution();

        await PersistScheduleAsync(schedule, ct);
        _executionHistory.AddExecution(execution);

        _logger.LogInformation(
            "Recorded execution for schedule {ScheduleId}: status={Status}, next={NextExecution}",
            schedule.ScheduleId, execution.Status, schedule.NextExecutionAt);
    }

    /// <summary>
    /// Trigger a manual execution of a schedule.
    /// </summary>
    public BackfillExecutionLog CreateManualExecution(BackfillSchedule schedule)
    {
        return new BackfillExecutionLog
        {
            ScheduleId = schedule.ScheduleId,
            ScheduleName = schedule.Name,
            Trigger = ExecutionTrigger.Manual,
            ScheduledAt = DateTimeOffset.UtcNow,
            FromDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-schedule.LookbackDays),
            ToDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            Symbols = new List<string>(schedule.Symbols)
        };
    }

    /// <summary>
    /// Get schedules by tag.
    /// </summary>
    public IReadOnlyList<BackfillSchedule> GetSchedulesByTag(string tag)
    {
        return _schedules.Values
            .Where(s => s.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .OrderBy(s => s.Name)
            .ToList();
    }

    /// <summary>
    /// Check if any schedules are currently running.
    /// </summary>
    public bool HasRunningSchedules()
    {
        return _executionHistory.GetRecentExecutions(10)
            .Any(e => e.Status == ExecutionStatus.Running);
    }

    /// <summary>
    /// Get schedule status summary.
    /// </summary>
    public ScheduleStatusSummary GetStatusSummary()
    {
        var schedules = _schedules.Values.ToList();
        var now = DateTimeOffset.UtcNow;

        return new ScheduleStatusSummary
        {
            TotalSchedules = schedules.Count,
            EnabledSchedules = schedules.Count(s => s.Enabled),
            DisabledSchedules = schedules.Count(s => !s.Enabled),
            SchedulesDueNow = schedules.Count(s => s.Enabled && s.NextExecutionAt <= now),
            NextScheduledExecution = schedules
                .Where(s => s.Enabled && s.NextExecutionAt.HasValue)
                .Select(s => s.NextExecutionAt!.Value)
                .DefaultIfEmpty(DateTimeOffset.MaxValue)
                .Min(),
            TotalExecutions = schedules.Sum(s => s.ExecutionCount),
            TotalSuccesses = schedules.Sum(s => s.SuccessfulExecutions),
            TotalFailures = schedules.Sum(s => s.FailedExecutions)
        };
    }

    private async Task PersistScheduleAsync(BackfillSchedule schedule, CancellationToken ct)
    {
        try
        {
            await _persistLock.WaitAsync(ct);

            if (!Directory.Exists(_schedulesDirectory))
                Directory.CreateDirectory(_schedulesDirectory);

            var filePath = GetScheduleFilePath(schedule.ScheduleId);
            var json = JsonSerializer.Serialize(schedule, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json, ct);
        }
        finally
        {
            _persistLock.Release();
        }
    }

    private string GetScheduleFilePath(string scheduleId)
    {
        return Path.Combine(_schedulesDirectory, $"schedule_{scheduleId}.json");
    }
}

/// <summary>
/// Summary of all schedule statuses.
/// </summary>
public sealed record ScheduleStatusSummary
{
    public int TotalSchedules { get; init; }
    public int EnabledSchedules { get; init; }
    public int DisabledSchedules { get; init; }
    public int SchedulesDueNow { get; init; }
    public DateTimeOffset NextScheduledExecution { get; init; }
    public int TotalExecutions { get; init; }
    public int TotalSuccesses { get; init; }
    public int TotalFailures { get; init; }
    public double OverallSuccessRate => TotalExecutions > 0
        ? (double)TotalSuccesses / TotalExecutions
        : 0;
}

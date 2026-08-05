using System.Collections.Concurrent;
using System.Text.Json;
using Meridian.Core.Scheduling;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Scheduling;

/// <summary>
/// Manages backfill schedules including CRUD operations, persistence, and status tracking.
/// Thread-safe for concurrent access.
/// </summary>
public sealed class BackfillScheduleManager
{
    private volatile ConcurrentDictionary<string, BackfillSchedule> _schedules = new();
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
        if (Volatile.Read(ref _isLoaded))
            return;

        await _persistLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_isLoaded)
                return;

            if (!Directory.Exists(_schedulesDirectory))
            {
                Directory.CreateDirectory(_schedulesDirectory);
                Volatile.Write(ref _isLoaded, true);
                return;
            }

            var files = Directory.GetFiles(_schedulesDirectory, "schedule_*.json");
            var loadedSchedules = new ConcurrentDictionary<string, BackfillSchedule>();

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var json = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                    var schedule = JsonSerializer.Deserialize<BackfillSchedule>(json, _jsonOptions);

                    if (schedule != null)
                    {
                        // Recalculate next execution time
                        schedule.NextExecutionAt = schedule.CalculateNextExecution();
                        loadedSchedules[schedule.ScheduleId] = schedule;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    ct.ThrowIfCancellationRequested();
                    _logger.LogWarning(ex, "Failed to load schedule from {File}", file);
                }
            }

            ct.ThrowIfCancellationRequested();
            _schedules = loadedSchedules;
            _logger.LogInformation("Loaded {Count} backfill schedules", loadedSchedules.Count);
            Volatile.Write(ref _isLoaded, true);
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

        var candidate = CloneSchedule(schedule);
        candidate.NextExecutionAt = candidate.CalculateNextExecution();

        await _persistLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await PersistScheduleUnderLockAsync(candidate, ct).ConfigureAwait(false);
            _schedules[candidate.ScheduleId] = candidate;
        }
        finally
        {
            _persistLock.Release();
        }

        _logger.LogInformation(
            "Created schedule {ScheduleId}: {Name}, next execution: {NextExecution}",
            candidate.ScheduleId, candidate.Name, candidate.NextExecutionAt);

        var created = CloneSchedule(candidate);
        ScheduleCreated?.Invoke(this, created);
        return created;
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

        if (!CronExpressionParser.IsValid(schedule.CronExpression))
            throw new ArgumentException($"Invalid cron expression: {schedule.CronExpression}");

        var candidate = CloneSchedule(schedule);
        candidate.ModifiedAt = DateTimeOffset.UtcNow;
        candidate.NextExecutionAt = candidate.CalculateNextExecution();

        await _persistLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_schedules.ContainsKey(candidate.ScheduleId))
                throw new KeyNotFoundException($"Schedule not found: {candidate.ScheduleId}");

            await PersistScheduleUnderLockAsync(candidate, ct).ConfigureAwait(false);
            _schedules[candidate.ScheduleId] = candidate;
        }
        finally
        {
            _persistLock.Release();
        }

        _logger.LogInformation(
            "Updated schedule {ScheduleId}: {Name}",
            candidate.ScheduleId, candidate.Name);

        var updated = CloneSchedule(candidate);
        ScheduleUpdated?.Invoke(this, updated);
        return updated;
    }

    /// <summary>
    /// Delete a schedule.
    /// </summary>
    public async Task<bool> DeleteScheduleAsync(string scheduleId, CancellationToken ct = default)
    {
        BackfillSchedule? removed = null;
        string? tombstonePath = null;

        await _persistLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_schedules.TryGetValue(scheduleId, out var retained) || retained is null)
                return false;
            removed = retained;

            ct.ThrowIfCancellationRequested();

            var filePath = GetScheduleFilePath(scheduleId);
            if (Directory.Exists(filePath))
            {
                throw new IOException(
                    $"Schedule file path is a directory and cannot be deleted: {filePath}");
            }

            if (File.Exists(filePath))
            {
                tombstonePath = GetDeletionTombstonePath(scheduleId);
                File.Move(filePath, tombstonePath);

                try
                {
                    await AtomicFileWriter.SyncDirectoryAsync(
                            _schedulesDirectory,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception syncException)
                {
                    try
                    {
                        File.Move(tombstonePath, filePath);
                        await AtomicFileWriter.SyncDirectoryAsync(
                                _schedulesDirectory,
                                CancellationToken.None)
                            .ConfigureAwait(false);
                        tombstonePath = null;
                    }
                    catch (Exception rollbackException)
                    {
                        throw new AggregateException(
                            "Failed to make the schedule deletion durable and failed to restore the schedule file.",
                            syncException,
                            rollbackException);
                    }

                    throw;
                }
            }

            _schedules.TryRemove(scheduleId, out _);
        }
        finally
        {
            _persistLock.Release();
        }

        if (tombstonePath is not null)
            await DeleteTombstoneBestEffortAsync(tombstonePath).ConfigureAwait(false);

        _logger.LogInformation("Deleted schedule {ScheduleId}: {Name}", scheduleId, removed!.Name);
        ScheduleDeleted?.Invoke(this, scheduleId);
        return true;
    }

    /// <summary>
    /// Get a schedule by ID.
    /// </summary>
    public BackfillSchedule? GetSchedule(string scheduleId)
    {
        return _schedules.TryGetValue(scheduleId, out var schedule)
            ? CloneSchedule(schedule)
            : null;
    }

    /// <summary>
    /// Get all schedules.
    /// </summary>
    public IReadOnlyList<BackfillSchedule> GetAllSchedules()
    {
        return _schedules.Values
            .OrderBy(s => s.Name)
            .Select(CloneSchedule)
            .ToList();
    }

    /// <summary>
    /// Get enabled schedules.
    /// </summary>
    public IReadOnlyList<BackfillSchedule> GetEnabledSchedules()
    {
        return _schedules.Values
            .Where(s => s.Enabled)
            .OrderBy(s => s.NextExecutionAt)
            .Select(CloneSchedule)
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
            .Select(CloneSchedule)
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
        BackfillSchedule candidate = null!;

        await _persistLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_schedules.TryGetValue(scheduleId, out var existing))
                return false;

            candidate = CloneSchedule(existing);
            candidate.Enabled = enabled;
            candidate.ModifiedAt = DateTimeOffset.UtcNow;

            if (enabled)
                candidate.NextExecutionAt = candidate.CalculateNextExecution();

            await PersistScheduleUnderLockAsync(candidate, ct).ConfigureAwait(false);
            _schedules[scheduleId] = candidate;
        }
        finally
        {
            _persistLock.Release();
        }

        _logger.LogInformation(
            "{Action} schedule {ScheduleId}: {Name}",
            enabled ? "Enabled" : "Disabled", scheduleId, candidate.Name);

        ScheduleUpdated?.Invoke(this, CloneSchedule(candidate));
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
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(execution);

        BackfillSchedule? candidate = null;

        await _persistLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_schedules.TryGetValue(schedule.ScheduleId, out var existing))
            {
                candidate = CloneSchedule(existing);

                candidate.LastExecutedAt = DateTimeOffset.UtcNow;
                candidate.LastJobId = execution.JobId;
                candidate.ExecutionCount++;

                if (execution.Status == ExecutionStatus.Completed)
                    candidate.SuccessfulExecutions++;
                else if (execution.Status == ExecutionStatus.Failed)
                    candidate.FailedExecutions++;

                candidate.NextExecutionAt = candidate.CalculateNextExecution();

                await PersistScheduleUnderLockAsync(candidate, ct).ConfigureAwait(false);
                _schedules[candidate.ScheduleId] = candidate;
            }

            _executionHistory.AddExecution(execution);
        }
        finally
        {
            _persistLock.Release();
        }

        if (candidate is null)
        {
            _logger.LogInformation(
                "Recorded execution for deleted schedule {ScheduleId}: status={Status}; schedule state was not restored",
                schedule.ScheduleId, execution.Status);
            return;
        }

        _logger.LogInformation(
            "Recorded execution for schedule {ScheduleId}: status={Status}, next={NextExecution}",
            candidate.ScheduleId, execution.Status, candidate.NextExecutionAt);
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
            .Select(CloneSchedule)
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

    private async Task PersistScheduleUnderLockAsync(
        BackfillSchedule schedule,
        CancellationToken ct)
    {
        if (!Directory.Exists(_schedulesDirectory))
            Directory.CreateDirectory(_schedulesDirectory);

        var filePath = GetScheduleFilePath(schedule.ScheduleId);
        var json = JsonSerializer.Serialize(schedule, _jsonOptions);
        await AtomicFileWriter.WriteAsync(filePath, json, ct).ConfigureAwait(false);
    }

    private BackfillSchedule CloneSchedule(BackfillSchedule schedule)
    {
        var json = JsonSerializer.Serialize(schedule, _jsonOptions);
        return JsonSerializer.Deserialize<BackfillSchedule>(json, _jsonOptions)
            ?? throw new JsonException("Backfill schedule serialization produced a null value.");
    }

    private string GetScheduleFilePath(string scheduleId)
    {
        return Path.Combine(_schedulesDirectory, $"schedule_{scheduleId}.json");
    }

    private string GetDeletionTombstonePath(string scheduleId)
    {
        return Path.Combine(
            _schedulesDirectory,
            $"deleted_{scheduleId}_{Guid.NewGuid():N}.schedule-tombstone");
    }

    private async Task DeleteTombstoneBestEffortAsync(string tombstonePath)
    {
        try
        {
            File.Delete(tombstonePath);
            await AtomicFileWriter.SyncDirectoryAsync(
                    _schedulesDirectory,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Deleted backfill schedule remains as ignored tombstone {Path}",
                tombstonePath);
        }
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

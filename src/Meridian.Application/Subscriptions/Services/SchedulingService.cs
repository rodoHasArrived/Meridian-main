using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Meridian.Application.Subscriptions.Models;
using Meridian.Application.UI;
using Meridian.Storage.Archival;
using Serilog;

namespace Meridian.Application.Subscriptions.Services;

/// <summary>
/// Service for scheduling symbol subscription enable/disable by time/date.
/// </summary>
public sealed class SchedulingService : IAsyncDisposable
{
    private readonly ConfigStore _configStore;
    private readonly string _schedulesPath;
    private readonly ILogger _log;
    private readonly ConcurrentDictionary<string, SubscriptionSchedule> _schedules = new();
    private readonly ConcurrentDictionary<string, ScheduleExecutionStatus> _executionStatus = new();
    private readonly Timer _timer;
    private readonly object _gate = new();
    private bool _disposed;

    public SchedulingService(ConfigStore configStore, string? schedulesPath = null, ILogger? log = null)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _schedulesPath = schedulesPath ?? Path.Combine(
            Path.GetDirectoryName(configStore.ConfigPath) ?? ".",
            "schedules.json");
        _log = log ?? LoggingSetup.ForContext<SchedulingService>();

        // Check every minute
        _timer = new Timer(CheckSchedules, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Initialize the service and load existing schedules.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await LoadSchedulesAsync(ct);
        _log.Information("Scheduling service initialized with {Count} schedules", _schedules.Count);
    }

    /// <summary>
    /// Get all schedules.
    /// </summary>
    public IReadOnlyList<SubscriptionSchedule> GetAllSchedules()
    {
        return _schedules.Values.ToList();
    }

    /// <summary>
    /// Get a specific schedule.
    /// </summary>
    public SubscriptionSchedule? GetSchedule(string scheduleId)
    {
        return _schedules.TryGetValue(scheduleId, out var schedule) ? schedule : null;
    }

    /// <summary>
    /// Get execution status for a schedule.
    /// </summary>
    public ScheduleExecutionStatus? GetExecutionStatus(string scheduleId)
    {
        return _executionStatus.TryGetValue(scheduleId, out var status) ? status : null;
    }

    /// <summary>
    /// Get all execution statuses.
    /// </summary>
    public IReadOnlyList<ScheduleExecutionStatus> GetAllExecutionStatuses()
    {
        return _executionStatus.Values.ToList();
    }

    /// <summary>
    /// Create a new schedule.
    /// </summary>
    public async Task<SubscriptionSchedule> CreateScheduleAsync(
        CreateScheduleRequest request,
        CancellationToken ct = default)
    {
        var schedule = new SubscriptionSchedule(
            Id: $"sched_{Guid.NewGuid():N}"[..16],
            Name: request.Name,
            Symbols: request.Symbols,
            Action: request.Action,
            Timing: request.Timing,
            Description: request.Description
        );

        _schedules[schedule.Id] = schedule;
        await SaveSchedulesAsync(ct);

        _log.Information("Created schedule {ScheduleId}: {Name}", schedule.Id, schedule.Name);
        return schedule;
    }

    /// <summary>
    /// Update an existing schedule.
    /// </summary>
    public async Task<SubscriptionSchedule?> UpdateScheduleAsync(
        string scheduleId,
        CreateScheduleRequest request,
        CancellationToken ct = default)
    {
        if (!_schedules.TryGetValue(scheduleId, out var existing))
            return null;

        var updated = existing with
        {
            Name = request.Name,
            Symbols = request.Symbols,
            Action = request.Action,
            Timing = request.Timing,
            Description = request.Description
        };

        _schedules[scheduleId] = updated;
        await SaveSchedulesAsync(ct);

        _log.Information("Updated schedule {ScheduleId}", scheduleId);
        return updated;
    }

    /// <summary>
    /// Enable or disable a schedule.
    /// </summary>
    public async Task<bool> SetScheduleEnabledAsync(
        string scheduleId,
        bool enabled,
        CancellationToken ct = default)
    {
        if (!_schedules.TryGetValue(scheduleId, out var existing))
            return false;

        var updated = existing with { IsEnabled = enabled };
        _schedules[scheduleId] = updated;
        await SaveSchedulesAsync(ct);

        _log.Information("Schedule {ScheduleId} {Action}", scheduleId, enabled ? "enabled" : "disabled");
        return true;
    }

    /// <summary>
    /// Delete a schedule.
    /// </summary>
    public async Task<bool> DeleteScheduleAsync(string scheduleId, CancellationToken ct = default)
    {
        if (!_schedules.TryRemove(scheduleId, out _))
            return false;

        _executionStatus.TryRemove(scheduleId, out _);
        await SaveSchedulesAsync(ct);

        _log.Information("Deleted schedule {ScheduleId}", scheduleId);
        return true;
    }

    /// <summary>
    /// Manually execute a schedule now.
    /// </summary>
    public async Task<ScheduleExecutionStatus> ExecuteNowAsync(
        string scheduleId,
        CancellationToken ct = default)
    {
        if (!_schedules.TryGetValue(scheduleId, out var schedule))
        {
            return new ScheduleExecutionStatus(
                scheduleId,
                DateTimeOffset.UtcNow,
                null,
                false,
                "Schedule not found"
            );
        }

        return await ExecuteScheduleAsync(schedule, ct);
    }

    private void CheckSchedules(object? state)
    {
        if (_disposed)
            return;

        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;

            foreach (var schedule in _schedules.Values.Where(s => s.IsEnabled))
            {
                try
                {
                    if (ShouldExecute(schedule, now))
                    {
                        _ = ExecuteScheduleAsync(schedule, CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error checking schedule {ScheduleId}", schedule.Id);
                }
            }
        }
    }

    private bool ShouldExecute(SubscriptionSchedule schedule, DateTimeOffset now)
    {
        var timing = schedule.Timing;

        // Check end date
        if (timing.EndDate.HasValue && DateOnly.FromDateTime(now.UtcDateTime) > timing.EndDate.Value)
            return false;

        // Parse time
        if (!TimeOnly.TryParse(timing.TimeUtc, out var scheduledTime))
            return false;

        var currentTime = TimeOnly.FromDateTime(now.UtcDateTime);
        var currentDate = DateOnly.FromDateTime(now.UtcDateTime);

        // Check if we're within the minute of scheduled time
        var timeDiff = Math.Abs((currentTime.ToTimeSpan() - scheduledTime.ToTimeSpan()).TotalMinutes);
        if (timeDiff > 1)
            return false;

        // Check last execution to avoid double-execution
        if (_executionStatus.TryGetValue(schedule.Id, out var lastStatus))
        {
            var lastRunDate = DateOnly.FromDateTime(lastStatus.LastRun.UtcDateTime);
            var lastRunTime = TimeOnly.FromDateTime(lastStatus.LastRun.UtcDateTime);

            if (lastRunDate == currentDate &&
                Math.Abs((lastRunTime.ToTimeSpan() - scheduledTime.ToTimeSpan()).TotalMinutes) < 2)
            {
                return false; // Already ran today at this time
            }
        }

        return timing.Type switch
        {
            ScheduleType.OneTime => timing.Date == currentDate,
            ScheduleType.Daily => true,
            ScheduleType.Weekly => timing.DaysOfWeek?.Contains((int)now.DayOfWeek) ?? false,
            _ => false
        };
    }

    private async Task<ScheduleExecutionStatus> ExecuteScheduleAsync(
        SubscriptionSchedule schedule,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var affected = 0;
        string? error = null;

        try
        {
            var cfg = _configStore.Load();
            var symbols = (cfg.Symbols ?? Array.Empty<SymbolConfig>()).ToList();
            var scheduleSymbolSet = new HashSet<string>(schedule.Symbols, StringComparer.OrdinalIgnoreCase);

            if (schedule.Action == ScheduleAction.Enable)
            {
                // Enable: set SubscribeTrades and SubscribeDepth to true
                for (var i = 0; i < symbols.Count; i++)
                {
                    if (scheduleSymbolSet.Contains(symbols[i].Symbol))
                    {
                        symbols[i] = symbols[i] with
                        {
                            SubscribeTrades = true,
                            SubscribeDepth = true
                        };
                        affected++;
                    }
                }

                // Add missing symbols
                foreach (var symbolName in schedule.Symbols)
                {
                    if (!symbols.Any(s => s.Symbol.Equals(symbolName, StringComparison.OrdinalIgnoreCase)))
                    {
                        symbols.Add(new SymbolConfig(symbolName));
                        affected++;
                    }
                }
            }
            else // Disable
            {
                // Disable: set SubscribeTrades and SubscribeDepth to false
                for (var i = 0; i < symbols.Count; i++)
                {
                    if (scheduleSymbolSet.Contains(symbols[i].Symbol))
                    {
                        symbols[i] = symbols[i] with
                        {
                            SubscribeTrades = false,
                            SubscribeDepth = false
                        };
                        affected++;
                    }
                }
            }

            var next = cfg with { Symbols = symbols.ToArray() };
            await _configStore.SaveAsync(next);

            _log.Information(
                "Executed schedule {ScheduleId}: {Action} {Count} symbols",
                schedule.Id, schedule.Action, affected);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _log.Error(ex, "Failed to execute schedule {ScheduleId}", schedule.Id);
        }

        var nextRun = CalculateNextRun(schedule, now);
        var status = new ScheduleExecutionStatus(
            schedule.Id,
            now,
            nextRun,
            error is null,
            error,
            affected
        );

        _executionStatus[schedule.Id] = status;
        return status;
    }

    private static DateTimeOffset? CalculateNextRun(SubscriptionSchedule schedule, DateTimeOffset from)
    {
        var timing = schedule.Timing;
        if (!TimeOnly.TryParse(timing.TimeUtc, out var scheduledTime))
            return null;

        var currentDate = DateOnly.FromDateTime(from.UtcDateTime);

        return timing.Type switch
        {
            ScheduleType.OneTime => timing.Date > currentDate
                ? new DateTimeOffset(timing.Date.Value.ToDateTime(scheduledTime), TimeSpan.Zero)
                : null,
            ScheduleType.Daily => new DateTimeOffset(
                currentDate.AddDays(1).ToDateTime(scheduledTime), TimeSpan.Zero),
            ScheduleType.Weekly => CalculateNextWeeklyRun(timing, currentDate, scheduledTime),
            _ => null
        };
    }

    private static DateTimeOffset? CalculateNextWeeklyRun(
        ScheduleTiming timing,
        DateOnly currentDate,
        TimeOnly scheduledTime)
    {
        if (timing.DaysOfWeek is null || timing.DaysOfWeek.Length == 0)
            return null;

        var sortedDays = timing.DaysOfWeek.OrderBy(d => d).ToList();
        var currentDayOfWeek = (int)currentDate.DayOfWeek;

        // Find next day this week or next week
        foreach (var day in sortedDays)
        {
            if (day > currentDayOfWeek)
            {
                var daysToAdd = day - currentDayOfWeek;
                var nextDate = currentDate.AddDays(daysToAdd);
                if (timing.EndDate.HasValue && nextDate > timing.EndDate.Value)
                    return null;
                return new DateTimeOffset(nextDate.ToDateTime(scheduledTime), TimeSpan.Zero);
            }
        }

        // Next week
        var firstDayNextWeek = sortedDays[0];
        var daysUntilNext = 7 - currentDayOfWeek + firstDayNextWeek;
        var nextWeekDate = currentDate.AddDays(daysUntilNext);

        if (timing.EndDate.HasValue && nextWeekDate > timing.EndDate.Value)
            return null;

        return new DateTimeOffset(nextWeekDate.ToDateTime(scheduledTime), TimeSpan.Zero);
    }

    private async Task LoadSchedulesAsync(CancellationToken ct)
    {
        if (!File.Exists(_schedulesPath))
            return;

        try
        {
            var json = await File.ReadAllTextAsync(_schedulesPath, ct);
            var schedules = JsonSerializer.Deserialize<List<SubscriptionSchedule>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            foreach (var schedule in schedules ?? Enumerable.Empty<SubscriptionSchedule>())
            {
                _schedules[schedule.Id] = schedule;
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to load schedules from {Path}", _schedulesPath);
        }
    }

    private async Task SaveSchedulesAsync(CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(_schedules.Values.ToList(), new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            await AtomicFileWriter.WriteAsync(_schedulesPath, json, ct);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to save schedules to {Path}", _schedulesPath);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await _timer.DisposeAsync();
    }
}

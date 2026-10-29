using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using Meridian.Core.Config;
using Meridian.Core.Logging;
using Meridian.Core.Subscriptions.Models;
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
    private volatile IReadOnlyDictionary<string, SubscriptionSchedule> _schedules = new Dictionary<string, SubscriptionSchedule>();
    private readonly ConcurrentDictionary<string, ScheduleExecutionStatus> _executionStatus = new();
    private readonly Timer _timer;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _mutationLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, Task<ScheduleExecutionStatus>> _running = new();
    private readonly Func<SubscriptionSchedule, CancellationToken, Task<int>> _applySchedule;
    private Task? _disposeTask;
    private bool _disposed;

    public SchedulingService(ConfigStore configStore, string? schedulesPath = null, ILogger? log = null)
        : this(configStore, schedulesPath, log, null)
    {
    }

    internal SchedulingService(
        ConfigStore configStore,
        string? schedulesPath,
        ILogger? log,
        Func<SubscriptionSchedule, CancellationToken, Task<int>>? applySchedule)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _schedulesPath = schedulesPath ?? Path.Combine(
            Path.GetDirectoryName(configStore.ConfigPath) ?? ".",
            "schedules.json");
        _log = log ?? LoggingSetup.ForContext<SchedulingService>();

        _applySchedule = applySchedule ?? ApplyScheduleAsync;

        // Check every minute
        _timer = new Timer(CheckSchedules, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Initialize the service and load existing schedules.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _mutationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (_gate)
                ObjectDisposedException.ThrowIf(_disposed, this);
            await LoadSchedulesAsync(ct).ConfigureAwait(false);
            _log.Information("Scheduling service initialized with {Count} schedules", _schedules.Count);
        }
        finally
        {
            _mutationLock.Release();
        }
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

        await MutateSchedulesAsync(snapshot =>
        {
            snapshot[schedule.Id] = schedule;
            return (true, schedule);
        }, ct).ConfigureAwait(false);

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
        var updated = await MutateSchedulesAsync<SubscriptionSchedule?>(snapshot =>
        {
            if (!snapshot.TryGetValue(scheduleId, out var existing))
                return (false, null);

            var replacement = existing with
            {
                Name = request.Name,
                Symbols = request.Symbols,
                Action = request.Action,
                Timing = request.Timing,
                Description = request.Description
            };
            snapshot[scheduleId] = replacement;
            return (true, replacement);
        }, ct).ConfigureAwait(false);
        if (updated is null)
            return null;

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
        var found = await MutateSchedulesAsync(snapshot =>
        {
            if (!snapshot.TryGetValue(scheduleId, out var existing))
                return (false, false);
            snapshot[scheduleId] = existing with { IsEnabled = enabled };
            return (true, true);
        }, ct).ConfigureAwait(false);
        if (!found)
            return false;

        _log.Information("Schedule {ScheduleId} {Action}", scheduleId, enabled ? "enabled" : "disabled");
        return true;
    }

    /// <summary>
    /// Delete a schedule.
    /// </summary>
    public async Task<bool> DeleteScheduleAsync(string scheduleId, CancellationToken ct = default)
    {
        var removed = await MutateSchedulesAsync(snapshot =>
        {
            var found = snapshot.Remove(scheduleId);
            return (found, found);
        }, ct).ConfigureAwait(false);
        if (!removed)
            return false;

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
        ct.ThrowIfCancellationRequested();
        lock (_gate)
            ObjectDisposedException.ThrowIf(_disposed, this);
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

        return await StartExecution(schedule, ct).ConfigureAwait(false);
    }

    private void CheckSchedules(object? state) => CheckSchedules(DateTimeOffset.UtcNow);

    internal void CheckSchedules(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            foreach (var schedule in _schedules.Values.Where(s => s.IsEnabled))
            {
                try
                {
                    if (!_running.ContainsKey(schedule.Id) && ShouldExecute(schedule, now))
                        _ = StartExecution(schedule, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error checking schedule {ScheduleId}", schedule.Id);
                }
            }
        }
    }

    private Task<ScheduleExecutionStatus> StartExecution(SubscriptionSchedule schedule, CancellationToken ct)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ct.ThrowIfCancellationRequested();
            if (_running.TryGetValue(schedule.Id, out var running))
                return running.WaitAsync(ct);

            var completion = new TaskCompletionSource<ScheduleExecutionStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            _running.Add(schedule.Id, completion.Task);
            _ = CompleteExecutionAsync(schedule, ct, completion);
            return completion.Task;
        }
    }

    private async Task CompleteExecutionAsync(
        SubscriptionSchedule schedule,
        CancellationToken ct,
        TaskCompletionSource<ScheduleExecutionStatus> completion)
    {
        // Admission is published before any user or configuration-store work can start.
        await Task.Yield();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        try
        {
            var status = await ExecuteScheduleAsync(schedule, linked.Token).ConfigureAwait(false);
            lock (_gate)
            {
                _running.Remove(schedule.Id);
                completion.TrySetResult(status);
            }
        }
        catch (OperationCanceledException ex) when (linked.IsCancellationRequested)
        {
            lock (_gate)
            {
                _running.Remove(schedule.Id);
                completion.TrySetCanceled(ex.CancellationToken);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Unexpected schedule execution failure for {ScheduleId}", schedule.Id);
            lock (_gate)
            {
                _running.Remove(schedule.Id);
                completion.TrySetException(ex);
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
            ct.ThrowIfCancellationRequested();
            affected = await _applySchedule(schedule, ct).ConfigureAwait(false);

            _log.Information(
                "Executed schedule {ScheduleId}: {Action} {Count} symbols",
                schedule.Id, schedule.Action, affected);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
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

        lock (_gate)
        {
            if (_schedules.ContainsKey(schedule.Id))
                _executionStatus[schedule.Id] = status;
        }
        return status;
    }

    private async Task<int> ApplyScheduleAsync(SubscriptionSchedule schedule, CancellationToken ct)
    {
        var scheduleSymbolSet = new HashSet<string>(schedule.Symbols, StringComparer.OrdinalIgnoreCase);

        return await _configStore.UpdateAsync(cfg =>
        {
            var symbols = (cfg.Symbols ?? Array.Empty<SymbolConfig>()).ToList();
            var affected = 0;

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

            return (cfg with { Symbols = symbols.ToArray() }, affected);
        }, ct);
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

            var snapshot = (schedules ?? [])
                .GroupBy(schedule => schedule.Id)
                .ToDictionary(group => group.Key, group => group.Last());
            lock (_gate)
                _schedules = snapshot;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var quarantinePath = CorruptStoreQuarantine.PreserveOrThrow(_schedulesPath, ex);
            _log.Error(ex, "Failed to load schedules from {Path}; unreadable file preserved at {QuarantinePath}",
                _schedulesPath, quarantinePath);
        }
    }

    private async Task<T> MutateSchedulesAsync<T>(
        Func<Dictionary<string, SubscriptionSchedule>, (bool Changed, T Result)> mutate,
        CancellationToken ct)
    {
        await _mutationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (_gate)
                ObjectDisposedException.ThrowIf(_disposed, this);
            var snapshot = _schedules.ToDictionary(pair => pair.Key, pair => pair.Value);
            var (changed, result) = mutate(snapshot);
            if (!changed)
                return result;

            await SaveSchedulesAsync(snapshot.Values, ct).ConfigureAwait(false);
            // Once publication commits, cancellation must not leave memory on the old snapshot.
            lock (_gate)
            {
                _schedules = snapshot;
                foreach (var id in _executionStatus.Keys.Where(id => !snapshot.ContainsKey(id)))
                    _executionStatus.TryRemove(id, out _);
            }
            return result;
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    private async Task SaveSchedulesAsync(IEnumerable<SubscriptionSchedule> schedules, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(schedules.ToList(), new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            await AtomicFileWriter.WriteAsync(_schedulesPath, json, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to save schedules to {Path}", _schedulesPath);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposeTask is null)
            {
                _disposed = true;
                _disposeTask = DisposeCoreAsync(_running.Values.ToArray());
            }
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync(Task<ScheduleExecutionStatus>[] running)
    {
        await Task.Yield();
        await _timer.DisposeAsync().ConfigureAwait(false);
        await _lifetime.CancelAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(running).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Owned executions have observed shutdown; all tracked tasks have settled.
        }
        // Join an already-admitted persistence operation before releasing lifetime resources.
        await _mutationLock.WaitAsync().ConfigureAwait(false);
        _mutationLock.Release();
        _lifetime.Dispose();
    }
}

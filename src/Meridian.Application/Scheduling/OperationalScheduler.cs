using Meridian.Infrastructure.Contracts;

namespace Meridian.Application.Scheduling;

/// <summary>
/// Concrete implementation of <see cref="IOperationalScheduler"/> that provides trading-hours-aware
/// scheduling of maintenance, backfill, and other operational tasks.
/// Coordinates with the trading calendar to schedule heavy operations during off-hours
/// and defers non-critical work to maintenance windows.
/// </summary>
[ImplementsAdr("ADR-001", "Trading-hours-aware operational scheduling")]
public sealed class OperationalScheduler : IOperationalScheduler
{
    private readonly ITradingCalendarProvider _calendarProvider;
    private readonly List<MaintenanceWindow> _maintenanceWindows = new();
    private readonly object _windowLock = new();

    /// <summary>
    /// Operations that should be deferred during trading hours.
    /// </summary>
    private static readonly HashSet<OperationType> TradingHoursSensitiveOps = new()
    {
        OperationType.Maintenance,
        OperationType.IntegrityCheck,
        OperationType.IndexRebuild,
        OperationType.CacheRefresh
    };

    /// <summary>
    /// Operations that are always allowed regardless of trading hours.
    /// </summary>
    private static readonly HashSet<OperationType> AlwaysAllowedOps = new()
    {
        OperationType.HealthCheck,
        OperationType.CredentialRefresh
    };

    /// <summary>
    /// Default maintenance windows when none are configured.
    /// </summary>
    private static readonly MaintenanceWindow[] DefaultWindows =
    {
        // Weeknight maintenance: 1 AM - 4 AM ET (6 AM - 9 AM UTC)
        new("weeknight-maintenance",
            StartTime: DateTimeOffset.UtcNow.Date.AddHours(6),
            EndTime: DateTimeOffset.UtcNow.Date.AddHours(9),
            AllowedOperations: null, // All operations
            Priority: 10),

        // Weekend maintenance: Saturday 1 AM - Sunday 11 PM ET
        new("weekend-maintenance",
            StartTime: DateTimeOffset.UtcNow.Date.AddHours(6),
            EndTime: DateTimeOffset.UtcNow.Date.AddHours(6).AddDays(1),
            AllowedOperations: null,
            Priority: 5)
    };

    public OperationalScheduler(ITradingCalendarProvider calendarProvider)
    {
        _calendarProvider = calendarProvider ?? throw new ArgumentNullException(nameof(calendarProvider));
    }

    /// <inheritdoc />
    public bool IsWithinTradingHours
    {
        get
        {
            var now = DateOnly.FromDateTime(DateTime.UtcNow);
            if (!_calendarProvider.IsTradingDay(now))
                return false;

            var sessions = _calendarProvider.GetTradingSessions(now);
            var nowOffset = DateTimeOffset.UtcNow;
            return sessions.Any(s => nowOffset >= s.OpenTime && nowOffset < s.CloseTime);
        }
    }

    /// <inheritdoc />
    public bool IsWithinMaintenanceWindow
    {
        get
        {
            var current = GetCurrentMaintenanceWindow();
            return current != null;
        }
    }

    /// <inheritdoc />
    public Task<ScheduleDecision> CanExecuteAsync(
        OperationType operationType,
        ResourceRequirements? requirements = null,
        CancellationToken ct = default)
    {
        // Always-allowed operations can run anytime
        if (AlwaysAllowedOps.Contains(operationType))
            return Task.FromResult(ScheduleDecision.Allowed);

        // Check if we're in trading hours
        if (IsWithinTradingHours && TradingHoursSensitiveOps.Contains(operationType))
        {
            var session = GetCurrentTradingSession();
            var timeUntilClose = session != null
                ? session.CloseTime - DateTimeOffset.UtcNow
                : TimeSpan.FromHours(1);

            return Task.FromResult(ScheduleDecision.Denied(
                $"Operation {operationType} deferred: market is open ({session?.Market ?? "US"})",
                suggestedDelay: timeUntilClose));
        }

        // Check if we're in a maintenance window (preferred for heavy operations)
        if (TradingHoursSensitiveOps.Contains(operationType))
        {
            var mw = GetCurrentMaintenanceWindow();
            if (mw != null)
            {
                // In a maintenance window - check if operation is allowed
                if (mw.AllowedOperations == null || mw.AllowedOperations.Contains(operationType))
                    return Task.FromResult(ScheduleDecision.Allowed);
            }
        }

        // Check resource requirements
        if (requirements != null)
        {
            if (requirements.RequiresNetwork && IsWithinTradingHours)
            {
                // Network-heavy operations during trading may impact streaming
                return Task.FromResult(ScheduleDecision.Denied(
                    "Network-intensive operations deferred during trading hours",
                    suggestedDelay: TimeSpan.FromMinutes(30)));
            }

            if (requirements.RequiresCpuIntensive && requirements.RequiresIoIntensive && IsWithinTradingHours)
            {
                return Task.FromResult(ScheduleDecision.Denied(
                    "CPU+I/O intensive operations deferred during trading hours",
                    suggestedDelay: TimeSpan.FromMinutes(30)));
            }
        }

        // Backfill can run outside trading hours even without a maintenance window
        if (operationType == OperationType.Backfill && !IsWithinTradingHours)
            return Task.FromResult(ScheduleDecision.Allowed);

        // Reporting can run anytime outside trading hours
        if (operationType == OperationType.Reporting && !IsWithinTradingHours)
            return Task.FromResult(ScheduleDecision.Allowed);

        // Default: allow if not in trading hours
        if (!IsWithinTradingHours)
            return Task.FromResult(ScheduleDecision.Allowed);

        // During trading hours, non-sensitive operations are allowed
        return Task.FromResult(ScheduleDecision.Allowed);
    }

    /// <inheritdoc />
    public Task<ScheduleSlot?> FindNextAvailableSlotAsync(
        OperationType operationType,
        TimeSpan minDuration,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var searchLimit = now.AddDays(7); // Search up to 7 days ahead

        // Always-allowed operations: next available is now
        if (AlwaysAllowedOps.Contains(operationType))
        {
            return Task.FromResult<ScheduleSlot?>(new ScheduleSlot(now, now + minDuration, "immediate"));
        }

        // For trading-hours-sensitive operations, find non-trading-hour slot
        if (TradingHoursSensitiveOps.Contains(operationType))
        {
            return Task.FromResult(FindNextNonTradingSlot(now, minDuration, searchLimit));
        }

        // For backfill, prefer post-market hours
        if (operationType == OperationType.Backfill)
        {
            return Task.FromResult(FindNextPostMarketSlot(now, minDuration, searchLimit));
        }

        // Default: next available non-trading slot
        return Task.FromResult(FindNextNonTradingSlot(now, minDuration, searchLimit));
    }

    /// <inheritdoc />
    public TradingSession? GetCurrentTradingSession()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (!_calendarProvider.IsTradingDay(today))
            return null;

        var sessions = _calendarProvider.GetTradingSessions(today);
        var now = DateTimeOffset.UtcNow;
        return sessions.FirstOrDefault(s => now >= s.OpenTime && now < s.CloseTime);
    }

    /// <inheritdoc />
    public TradingSession? GetNextTradingSession()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTimeOffset.UtcNow;

        // Check remaining sessions today
        if (_calendarProvider.IsTradingDay(today))
        {
            var todaySessions = _calendarProvider.GetTradingSessions(today);
            var nextToday = todaySessions.FirstOrDefault(s => s.OpenTime > now);
            if (nextToday != null)
                return nextToday;
        }

        // Check next trading day
        var nextDay = _calendarProvider.GetNextTradingDay(today);
        var nextSessions = _calendarProvider.GetTradingSessions(nextDay);
        return nextSessions.FirstOrDefault();
    }

    /// <inheritdoc />
    public MaintenanceWindow? GetCurrentMaintenanceWindow()
    {
        var now = DateTimeOffset.UtcNow;
        lock (_windowLock)
        {
            return _maintenanceWindows
                .Where(w => now >= w.StartTime && now < w.EndTime)
                .OrderByDescending(w => w.Priority)
                .FirstOrDefault();
        }
    }

    /// <inheritdoc />
    public MaintenanceWindow? GetNextMaintenanceWindow()
    {
        var now = DateTimeOffset.UtcNow;
        lock (_windowLock)
        {
            return _maintenanceWindows
                .Where(w => w.StartTime > now)
                .OrderBy(w => w.StartTime)
                .FirstOrDefault();
        }
    }

    /// <inheritdoc />
    public void RegisterMaintenanceWindow(MaintenanceWindow window)
    {
        lock (_windowLock)
        {
            // Remove any existing window with the same name
            _maintenanceWindows.RemoveAll(w => w.Name == window.Name);
            _maintenanceWindows.Add(window);
        }
    }

    /// <inheritdoc />
    public void RemoveMaintenanceWindow(string windowName)
    {
        lock (_windowLock)
        {
            _maintenanceWindows.RemoveAll(w => w.Name == windowName);
        }
    }

    private ScheduleSlot? FindNextNonTradingSlot(DateTimeOffset from, TimeSpan minDuration, DateTimeOffset searchLimit)
    {
        var cursor = from;

        while (cursor < searchLimit)
        {
            var date = DateOnly.FromDateTime(cursor.UtcDateTime);

            if (!_calendarProvider.IsTradingDay(date))
            {
                // Non-trading day: entire day is available
                var dayStart = cursor.Date == from.Date ? cursor : new DateTimeOffset(cursor.Date, TimeSpan.Zero);
                var dayEnd = new DateTimeOffset(cursor.Date.AddDays(1), TimeSpan.Zero);

                if (dayEnd - dayStart >= minDuration)
                {
                    return new ScheduleSlot(dayStart, dayStart + minDuration, "non-trading-day");
                }
            }
            else
            {
                // Trading day: find gaps between sessions
                var sessions = _calendarProvider.GetTradingSessions(date);
                if (sessions.Count == 0)
                {
                    var dayStart = new DateTimeOffset(cursor.Date, TimeSpan.Zero);
                    return new ScheduleSlot(dayStart, dayStart + minDuration, "no-sessions");
                }

                // Before first session
                var firstSession = sessions[0];
                if (cursor < firstSession.OpenTime)
                {
                    var gap = firstSession.OpenTime - cursor;
                    if (gap >= minDuration)
                    {
                        return new ScheduleSlot(cursor, cursor + minDuration, "pre-market");
                    }
                }

                // After last session
                var lastSession = sessions[^1];
                if (cursor >= lastSession.CloseTime || DateTimeOffset.UtcNow >= lastSession.CloseTime)
                {
                    var postMarketStart = lastSession.CloseTime > cursor ? lastSession.CloseTime : cursor;
                    var midnight = new DateTimeOffset(cursor.Date.AddDays(1), TimeSpan.Zero);
                    var gap = midnight - postMarketStart;
                    if (gap >= minDuration)
                    {
                        return new ScheduleSlot(postMarketStart, postMarketStart + minDuration, "post-market");
                    }
                }
            }

            // Move to next day
            cursor = new DateTimeOffset(cursor.Date.AddDays(1), TimeSpan.Zero);
        }

        return null;
    }

    private ScheduleSlot? FindNextPostMarketSlot(DateTimeOffset from, TimeSpan minDuration, DateTimeOffset searchLimit)
    {
        var cursor = from;

        while (cursor < searchLimit)
        {
            var date = DateOnly.FromDateTime(cursor.UtcDateTime);

            if (_calendarProvider.IsTradingDay(date))
            {
                var sessions = _calendarProvider.GetTradingSessions(date);
                if (sessions.Count > 0)
                {
                    var lastSession = sessions[^1];
                    var postMarketStart = lastSession.CloseTime;

                    if (postMarketStart > cursor || DateTimeOffset.UtcNow >= lastSession.CloseTime)
                    {
                        var actualStart = postMarketStart > cursor ? postMarketStart : cursor;
                        return new ScheduleSlot(actualStart, actualStart + minDuration, "post-market");
                    }
                }
            }

            cursor = new DateTimeOffset(cursor.Date.AddDays(1), TimeSpan.Zero);
        }

        return null;
    }
}

using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

public static class ReconciliationSlaCalculator
{
    private const int MaxCalendarDays = 36600;
    public static ReconciliationSlaComputationResult Compute(
        ReconciliationBreakQueueItem item,
        ReconciliationSlaPolicy policy,
        DateTimeOffset asOfUtc,
        IReconciliationSlaCalendarResolver? calendarResolver = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.BusinessDayEnd <= policy.BusinessDayStart || policy.DueBusinessHours < 0
            || policy.DueBusinessHours > MaxCalendarDays * 24 || policy.WarningBusinessHours < 0
            || policy.WarningBusinessHours > policy.DueBusinessHours)
            throw new ArgumentException("SLA policies require a positive same-day business window and 0 <= warning hours <= due hours.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(policy.TimeZoneId);
        var calendar = (calendarResolver ?? WeekdayReconciliationSlaCalendarResolver.Instance).Resolve(policy);
        var occurrenceStart = item.Lineage?.OccurrenceFirstObservedAt ?? item.DetectedAt;

        if (policy.PauseAwaitingEvidence &&
            item.LifecycleState == ReconciliationCaseLifecycleState.AwaitingEvidence &&
            (item.EvidenceLinks?.Count > 0 || item.Comments?.Any(c => c.Visibility == ReconciliationCaseCommentVisibility.CloseEvidence) == true))
        {
            return new ReconciliationSlaComputationResult(
                policy.PolicyId,
                item.SlaDueAt ?? AddBusinessHours(occurrenceStart, policy, policy.DueBusinessHours, zone, calendar),
                item.SlaWarningAt ?? AddBusinessHours(occurrenceStart, policy, policy.WarningBusinessHours, zone, calendar),
                item.SlaBreachedAt,
                ReconciliationCaseSlaState.Paused,
                BuildAgeBand(item.BusinessAgeHours),
                item.BusinessAgeHours);
        }

        if ((policy.StopOnResolved && item.LifecycleState is ReconciliationCaseLifecycleState.Resolved or ReconciliationCaseLifecycleState.Superseded) ||
            (policy.StopOnSignedOff && item.LifecycleState == ReconciliationCaseLifecycleState.SignedOff))
        {
            var due = item.SlaDueAt ?? AddBusinessHours(occurrenceStart, policy, policy.DueBusinessHours, zone, calendar);
            return new ReconciliationSlaComputationResult(policy.PolicyId, due, item.SlaWarningAt ?? due, item.SlaBreachedAt, ReconciliationCaseSlaState.Stopped, BuildAgeBand(item.BusinessAgeHours), item.BusinessAgeHours);
        }

        var dueAt = AddBusinessHours(occurrenceStart, policy, policy.DueBusinessHours, zone, calendar);
        var warningAt = AddBusinessHours(occurrenceStart, policy, policy.WarningBusinessHours, zone, calendar);
        var businessAge = CountBusinessHours(occurrenceStart, asOfUtc, policy, zone, calendar);
        var state = asOfUtc >= dueAt
            ? ReconciliationCaseSlaState.Breached
            : asOfUtc >= warningAt ? ReconciliationCaseSlaState.Warning : ReconciliationCaseSlaState.OnTrack;

        return new ReconciliationSlaComputationResult(
            policy.PolicyId,
            dueAt,
            warningAt,
            state == ReconciliationCaseSlaState.Breached ? asOfUtc : item.SlaBreachedAt,
            state,
            BuildAgeBand(businessAge),
            businessAge);
    }

    public static ReconciliationSlaPolicy DefaultPolicyFor(ReconciliationBreakQueueItem item)
    {
        var priority = item.Priority;
        var dueHours = item.Severity switch
        {
            ReconciliationBreakSeverity.Critical => 4,
            ReconciliationBreakSeverity.High => 8,
            ReconciliationBreakSeverity.Medium => 24,
            _ => 40
        };
        if (priority == ReconciliationCasePriority.Critical)
        {
            dueHours = Math.Min(dueHours, 4);
        }

        return new ReconciliationSlaPolicy(
            PolicyId: $"default-{item.Severity.ToString().ToLowerInvariant()}-{priority.ToString().ToLowerInvariant()}",
            FundId: item.FundAccountId,
            AccountId: item.ExternalAccountId,
            BreakType: item.Category.ToString(),
            Severity: item.Severity,
            Priority: priority,
            TimeZoneId: "UTC",
            BusinessDayStart: new TimeOnly(9, 0),
            BusinessDayEnd: new TimeOnly(17, 0),
            DueBusinessHours: dueHours,
            WarningBusinessHours: Math.Max(1, dueHours / 2),
            StopOnResolved: true,
            StopOnSignedOff: false,
            PauseAwaitingEvidence: true);
    }

    private static DateTimeOffset AddBusinessHours(DateTimeOffset startUtc, ReconciliationSlaPolicy policy,
        int hours, TimeZoneInfo zone, IReconciliationSlaCalendar calendar)
    {
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(startUtc, zone).DateTime);
        long remaining = (long)hours * TimeSpan.TicksPerHour;
        for (var walked = 0; walked < MaxCalendarDays; walked++)
        {
            var businessDay = calendar.IsBusinessDay(date);
            if (remaining == 0)
                return startUtc.ToUniversalTime();
            if (businessDay)
            {
                var (open, close) = BusinessWindow(date, policy, zone);
                var segmentStart = open > startUtc ? open : startUtc;
                var available = Math.Max(0, (close - segmentStart).Ticks);
                if (available >= remaining)
                    return segmentStart.AddTicks(remaining);
                remaining -= available;
            }
            date = NextDate(date);
        }
        throw new InvalidOperationException("SLA deadline exceeds the calendar traversal limit.");
    }

    private static double CountBusinessHours(DateTimeOffset fromUtc, DateTimeOffset toUtc,
        ReconciliationSlaPolicy policy, TimeZoneInfo zone, IReconciliationSlaCalendar calendar)
    {
        if (toUtc <= fromUtc)
            return 0;
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(fromUtc, zone).DateTime);
        var endDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(toUtc, zone).DateTime);
        long ticks = 0;
        for (var walked = 0; walked < MaxCalendarDays; walked++)
        {
            if (calendar.IsBusinessDay(date))
            {
                var (open, close) = BusinessWindow(date, policy, zone);
                var start = open > fromUtc ? open : fromUtc;
                var end = close < toUtc ? close : toUtc;
                ticks += Math.Max(0, (end - start).Ticks);
            }
            if (date >= endDate)
                return (double)ticks / TimeSpan.TicksPerHour;
            date = NextDate(date);
        }
        throw new InvalidOperationException("SLA age exceeds the calendar traversal limit.");
    }

    private static (DateTimeOffset Open, DateTimeOffset Close) BusinessWindow(DateOnly date,
        ReconciliationSlaPolicy policy, TimeZoneInfo zone)
    {
        DateTimeOffset Boundary(TimeOnly time)
        {
            var local = date.ToDateTime(time, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
                throw new InvalidOperationException("SLA business-window boundary requires an explicit daylight-saving policy decision.");
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
        }
        return (Boundary(policy.BusinessDayStart), Boundary(policy.BusinessDayEnd));
    }

    private static DateOnly NextDate(DateOnly date) => date == DateOnly.MaxValue
        ? throw new InvalidOperationException("SLA calendar exceeds the supported date range.") : date.AddDays(1);

    private static string BuildAgeBand(double businessAgeHours)
        => businessAgeHours switch
        {
            < 4 => "0-4h",
            < 8 => "4-8h",
            < 24 => "8-24h",
            < 40 => "1-2d",
            _ => "2d+"
        };
}

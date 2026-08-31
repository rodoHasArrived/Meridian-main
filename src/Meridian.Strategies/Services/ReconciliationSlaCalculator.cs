using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

public static class ReconciliationSlaCalculator
{
    public static ReconciliationSlaComputationResult Compute(
        ReconciliationBreakQueueItem item,
        ReconciliationSlaPolicy policy,
        DateTimeOffset asOfUtc)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(policy);

        if (policy.PauseAwaitingEvidence &&
            item.LifecycleState == ReconciliationCaseLifecycleState.AwaitingEvidence &&
            (item.EvidenceLinks?.Count > 0 || item.Comments?.Any(c => c.Visibility == ReconciliationCaseCommentVisibility.CloseEvidence) == true))
        {
            return new ReconciliationSlaComputationResult(
                policy.PolicyId,
                item.SlaDueAt ?? AddBusinessHours(item.DetectedAt, policy, policy.DueBusinessHours),
                item.SlaWarningAt ?? AddBusinessHours(item.DetectedAt, policy, policy.WarningBusinessHours),
                item.SlaBreachedAt,
                ReconciliationCaseSlaState.Paused,
                BuildAgeBand(item.BusinessAgeHours),
                item.BusinessAgeHours);
        }

        if ((policy.StopOnResolved && item.LifecycleState is ReconciliationCaseLifecycleState.Resolved or ReconciliationCaseLifecycleState.Superseded) ||
            (policy.StopOnSignedOff && item.LifecycleState == ReconciliationCaseLifecycleState.SignedOff))
        {
            var due = item.SlaDueAt ?? AddBusinessHours(item.DetectedAt, policy, policy.DueBusinessHours);
            return new ReconciliationSlaComputationResult(policy.PolicyId, due, item.SlaWarningAt ?? due, item.SlaBreachedAt, ReconciliationCaseSlaState.Stopped, BuildAgeBand(item.BusinessAgeHours), item.BusinessAgeHours);
        }

        var dueAt = AddBusinessHours(item.DetectedAt, policy, policy.DueBusinessHours);
        var warningAt = AddBusinessHours(item.DetectedAt, policy, policy.WarningBusinessHours);
        var businessAge = CountBusinessHours(item.DetectedAt, asOfUtc, policy);
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

    private static DateTimeOffset AddBusinessHours(DateTimeOffset startUtc, ReconciliationSlaPolicy policy, int hours)
    {
        var zone = ResolveZone(policy.TimeZoneId);
        var cursor = TimeZoneInfo.ConvertTime(startUtc, zone);
        var remaining = Math.Max(0, hours);

        while (remaining > 0)
        {
            cursor = NormalizeToBusinessTime(cursor, policy);
            var close = new DateTimeOffset(DateOnly.FromDateTime(cursor.Date).ToDateTime(policy.BusinessDayEnd), cursor.Offset);
            var available = Math.Max(0, (close - cursor).TotalHours);
            if (available >= remaining)
            {
                cursor = cursor.AddHours(remaining);
                remaining = 0;
            }
            else
            {
                remaining -= (int)Math.Ceiling(available);
                cursor = NextBusinessStart(cursor.AddDays(1), policy);
            }
        }

        return TimeZoneInfo.ConvertTime(cursor, TimeZoneInfo.Utc);
    }

    private static double CountBusinessHours(DateTimeOffset fromUtc, DateTimeOffset toUtc, ReconciliationSlaPolicy policy)
    {
        if (toUtc <= fromUtc)
        {
            return 0;
        }

        var zone = ResolveZone(policy.TimeZoneId);
        var cursor = TimeZoneInfo.ConvertTime(fromUtc, zone);
        var end = TimeZoneInfo.ConvertTime(toUtc, zone);
        double hours = 0;
        while (cursor < end)
        {
            cursor = NormalizeToBusinessTime(cursor, policy);
            if (cursor >= end)
            {
                break;
            }

            var close = new DateTimeOffset(DateOnly.FromDateTime(cursor.Date).ToDateTime(policy.BusinessDayEnd), cursor.Offset);
            var segmentEnd = close < end ? close : end;
            hours += Math.Max(0, (segmentEnd - cursor).TotalHours);
            cursor = NextBusinessStart(cursor.AddDays(1), policy);
        }

        return hours;
    }

    private static DateTimeOffset NormalizeToBusinessTime(DateTimeOffset value, ReconciliationSlaPolicy policy)
    {
        if (value.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return NextBusinessStart(value, policy);
        }

        var start = new DateTimeOffset(DateOnly.FromDateTime(value.Date).ToDateTime(policy.BusinessDayStart), value.Offset);
        var end = new DateTimeOffset(DateOnly.FromDateTime(value.Date).ToDateTime(policy.BusinessDayEnd), value.Offset);
        if (value < start)
        {
            return start;
        }

        return value >= end ? NextBusinessStart(value.AddDays(1), policy) : value;
    }

    private static DateTimeOffset NextBusinessStart(DateTimeOffset value, ReconciliationSlaPolicy policy)
    {
        var date = DateOnly.FromDateTime(value.Date);
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            date = date.AddDays(1);
        }

        return new DateTimeOffset(date.ToDateTime(policy.BusinessDayStart), value.Offset);
    }

    private static TimeZoneInfo ResolveZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

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

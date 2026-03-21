namespace Meridian.Application.Subscriptions.Models;

/// <summary>
/// Schedule for enabling/disabling symbol subscriptions by time/date.
/// </summary>
public sealed record SubscriptionSchedule(
    string Id,

    string Name,

    string[] Symbols,

    ScheduleAction Action,

    ScheduleTiming Timing,

    bool IsEnabled = true,

    string? Description = null
);

/// <summary>
/// Action to perform when schedule triggers.
/// </summary>
public enum ScheduleAction : byte
{
    Enable,

    Disable
}

/// <summary>
/// Timing configuration for a subscription schedule.
/// </summary>
public sealed record ScheduleTiming(
    ScheduleType Type,

    string TimeUtc,

    DateOnly? Date = null,

    int[]? DaysOfWeek = null,

    DateOnly? EndDate = null,

    string Timezone = "UTC"
);

/// <summary>
/// Type of schedule frequency.
/// </summary>
public enum ScheduleType : byte
{
    OneTime,

    Daily,

    Weekly,

    Custom
}

/// <summary>
/// Status of a schedule execution.
/// </summary>
public sealed record ScheduleExecutionStatus(
    string ScheduleId,
    DateTimeOffset LastRun,
    DateTimeOffset? NextRun,
    bool LastRunSuccess,
    string? LastError = null,
    int SymbolsAffected = 0
);

/// <summary>
/// Request to create or update a schedule.
/// </summary>
public sealed record CreateScheduleRequest(
    string Name,
    string[] Symbols,
    ScheduleAction Action,
    ScheduleTiming Timing,
    string? Description = null
);

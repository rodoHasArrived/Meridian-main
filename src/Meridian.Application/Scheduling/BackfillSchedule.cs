using System.Text.Json.Serialization;
using Meridian.Core.Scheduling;
using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.Application.Scheduling;

/// <summary>
/// Represents a scheduled backfill configuration with cron-like scheduling.
/// Supports daily, weekly, and custom cron expressions for automatic gap-fill.
/// </summary>
public sealed class BackfillSchedule
{
    /// <summary>
    /// Unique identifier for this schedule.
    /// </summary>
    public string ScheduleId { get; init; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>
    /// Human-readable name for the schedule.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of what this schedule does.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Whether this schedule is active.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Cron expression for scheduling (standard 5-field cron format).
    /// Examples: "0 2 * * *" (daily at 2am), "0 3 * * 0" (weekly on Sunday at 3am)
    /// </summary>
    public string CronExpression { get; set; } = "0 2 * * *";

    /// <summary>
    /// Timezone for cron expression evaluation.
    /// </summary>
    public string TimeZoneId { get; set; } = "UTC";

    /// <summary>
    /// Type of backfill operation.
    /// </summary>
    public ScheduledBackfillType BackfillType { get; set; } = ScheduledBackfillType.GapFill;

    /// <summary>
    /// Symbols to include in backfill. Empty = use all configured symbols.
    /// </summary>
    public List<string> Symbols { get; init; } = new();

    /// <summary>
    /// Lookback period for gap detection (e.g., 30 days).
    /// </summary>
    public int LookbackDays { get; set; } = 30;

    /// <summary>
    /// Data granularity for backfill.
    /// </summary>
    public DataGranularity Granularity { get; set; } = DataGranularity.Daily;

    /// <summary>
    /// Preferred providers in priority order. Empty = use default priority.
    /// </summary>
    public List<string> PreferredProviders { get; init; } = new();

    /// <summary>
    /// Priority level for jobs created by this schedule.
    /// </summary>
    public BackfillPriority Priority { get; set; } = BackfillPriority.Normal;

    /// <summary>
    /// Maximum retries for failed requests.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Maximum concurrent requests for jobs from this schedule.
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 3;

    /// <summary>
    /// Whether to skip dates that already have data.
    /// </summary>
    public bool SkipExistingData { get; set; } = true;

    /// <summary>
    /// Auto-pause when rate limited.
    /// </summary>
    public bool AutoPauseOnRateLimit { get; set; } = true;

    /// <summary>
    /// When the schedule was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the schedule was last modified.
    /// </summary>
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the schedule last executed.
    /// </summary>
    public DateTimeOffset? LastExecutedAt { get; set; }

    /// <summary>
    /// When the schedule is next scheduled to execute.
    /// </summary>
    public DateTimeOffset? NextExecutionAt { get; set; }

    /// <summary>
    /// ID of the last job created by this schedule.
    /// </summary>
    public string? LastJobId { get; set; }

    /// <summary>
    /// Total number of times this schedule has executed.
    /// </summary>
    public int ExecutionCount { get; set; }

    /// <summary>
    /// Number of successful executions.
    /// </summary>
    public int SuccessfulExecutions { get; set; }

    /// <summary>
    /// Number of failed executions.
    /// </summary>
    public int FailedExecutions { get; set; }

    /// <summary>
    /// Tags for categorization and filtering.
    /// </summary>
    public List<string> Tags { get; init; } = new();

    /// <summary>
    /// Gets the timezone info for cron evaluation.
    /// </summary>
    [JsonIgnore]
    public TimeZoneInfo TimeZone => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    /// <summary>
    /// Calculate the next execution time based on the cron expression.
    /// </summary>
    public DateTimeOffset? CalculateNextExecution(DateTimeOffset? from = null)
    {
        var baseTime = from ?? DateTimeOffset.UtcNow;
        return CronExpressionParser.GetNextOccurrence(CronExpression, TimeZone, baseTime);
    }

    /// <summary>
    /// Create a BackfillJob from this schedule.
    /// </summary>
    public BackfillJob CreateJob(DateOnly? fromDate = null, DateOnly? toDate = null)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = fromDate ?? today.AddDays(-LookbackDays);
        var to = toDate ?? today.AddDays(-1); // Yesterday (complete data)

        return new BackfillJob
        {
            Name = $"Scheduled: {Name} ({DateTime.UtcNow:yyyy-MM-dd HH:mm})",
            Symbols = Symbols.Count > 0 ? new List<string>(Symbols) : new List<string>(),
            FromDate = from,
            ToDate = to,
            Granularity = Granularity,
            PreferredProviders = new List<string>(PreferredProviders),
            Options = new BackfillJobOptions
            {
                MaxConcurrentRequests = MaxConcurrentRequests,
                MaxRetries = MaxRetries,
                SkipExistingData = SkipExistingData,
                FillGapsOnly = BackfillType == ScheduledBackfillType.GapFill,
                AutoPauseOnRateLimit = AutoPauseOnRateLimit,
                Priority = (int)Priority
            }
        };
    }
}

/// <summary>
/// Type of scheduled backfill operation.
/// </summary>
public enum ScheduledBackfillType : byte
{
    /// <summary>
    /// Only fill gaps in existing data.
    /// </summary>
    GapFill,

    /// <summary>
    /// Full backfill of the lookback period (may overwrite existing).
    /// </summary>
    FullBackfill,

    /// <summary>
    /// Rolling window - always fetch last N days.
    /// </summary>
    RollingWindow,

    /// <summary>
    /// End-of-day update - fetch only yesterday's data.
    /// </summary>
    EndOfDay
}

/// <summary>
/// Preset schedule templates for common use cases.
/// </summary>
public static class BackfillSchedulePresets
{
    /// <summary>
    /// Daily gap-fill at 2 AM UTC.
    /// </summary>
    public static BackfillSchedule DailyGapFill(string name, IEnumerable<string>? symbols = null) => new()
    {
        Name = name,
        Description = "Daily gap-fill to ensure complete historical data",
        CronExpression = "0 2 * * *",
        BackfillType = ScheduledBackfillType.GapFill,
        LookbackDays = 7,
        Symbols = symbols?.ToList() ?? new List<string>(),
        Priority = BackfillPriority.Normal
    };

    /// <summary>
    /// Weekly full backfill on Sunday at 3 AM UTC.
    /// </summary>
    public static BackfillSchedule WeeklyFullBackfill(string name, IEnumerable<string>? symbols = null) => new()
    {
        Name = name,
        Description = "Weekly full backfill to refresh historical data",
        CronExpression = "0 3 * * 0",
        BackfillType = ScheduledBackfillType.FullBackfill,
        LookbackDays = 30,
        Symbols = symbols?.ToList() ?? new List<string>(),
        Priority = BackfillPriority.Low
    };

    /// <summary>
    /// End-of-day update weekdays at 6 PM EST (11 PM UTC).
    /// </summary>
    public static BackfillSchedule EndOfDayUpdate(string name, IEnumerable<string>? symbols = null) => new()
    {
        Name = name,
        Description = "End-of-day update after market close",
        CronExpression = "0 23 * * 1-5",
        BackfillType = ScheduledBackfillType.EndOfDay,
        LookbackDays = 1,
        Symbols = symbols?.ToList() ?? new List<string>(),
        Priority = BackfillPriority.High
    };

    /// <summary>
    /// Monthly deep historical backfill on first Sunday at 1 AM UTC.
    /// </summary>
    public static BackfillSchedule MonthlyDeepBackfill(string name, IEnumerable<string>? symbols = null) => new()
    {
        Name = name,
        Description = "Monthly deep backfill to ensure data integrity",
        CronExpression = "0 1 1-7 * 0",
        BackfillType = ScheduledBackfillType.FullBackfill,
        LookbackDays = 365,
        Symbols = symbols?.ToList() ?? new List<string>(),
        Priority = BackfillPriority.Deferred
    };
}

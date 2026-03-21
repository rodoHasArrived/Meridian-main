namespace Meridian.Core.Scheduling;

/// <summary>
/// Lightweight cron expression parser supporting standard 5-field format.
/// Format: minute hour day-of-month month day-of-week
/// Examples:
///   "0 2 * * *"     - Daily at 2:00 AM
///   "0 3 * * 0"     - Every Sunday at 3:00 AM
///   "30 6 * * 1-5"  - Weekdays at 6:30 AM
///   "0 0 1 * *"     - First day of each month at midnight
///   "*/15 * * * *"  - Every 15 minutes
/// </summary>
public static class CronExpressionParser
{
    /// <summary>
    /// Calculate the next occurrence of a cron expression.
    /// </summary>
    /// <param name="cronExpression">5-field cron expression.</param>
    /// <param name="timeZone">Timezone for evaluation.</param>
    /// <param name="from">Start time for calculation.</param>
    /// <returns>Next occurrence or null if expression is invalid.</returns>
    public static DateTimeOffset? GetNextOccurrence(string cronExpression, TimeZoneInfo timeZone, DateTimeOffset from)
    {
        if (!TryParse(cronExpression, out var schedule))
            return null;

        return schedule.GetNextOccurrence(from, timeZone);
    }

    /// <summary>
    /// Validate a cron expression.
    /// </summary>
    public static bool IsValid(string cronExpression)
    {
        return TryParse(cronExpression, out _);
    }

    /// <summary>
    /// Get a human-readable description of a cron expression.
    /// </summary>
    public static string GetDescription(string cronExpression)
    {
        if (!TryParse(cronExpression, out var schedule))
            return "Invalid cron expression";

        return schedule.GetDescription();
    }

    /// <summary>
    /// Parse a cron expression into a schedule.
    /// </summary>
    public static bool TryParse(string cronExpression, out CronSchedule schedule)
    {
        schedule = new CronSchedule();

        if (string.IsNullOrWhiteSpace(cronExpression))
            return false;

        var parts = cronExpression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
            return false;

        try
        {
            schedule.Minutes = ParseField(parts[0], 0, 59);
            schedule.Hours = ParseField(parts[1], 0, 23);
            schedule.DaysOfMonth = ParseField(parts[2], 1, 31);
            schedule.Months = ParseField(parts[3], 1, 12);
            schedule.DaysOfWeek = ParseField(parts[4], 0, 6);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static HashSet<int> ParseField(string field, int min, int max)
    {
        var values = new HashSet<int>();

        foreach (var part in field.Split(','))
        {
            if (part == "*")
            {
                for (var i = min; i <= max; i++)
                    values.Add(i);
            }
            else if (part.Contains('/'))
            {
                var stepParts = part.Split('/');
                var range = stepParts[0];
                var step = int.Parse(stepParts[1]);

                int start, end;
                if (range == "*")
                {
                    start = min;
                    end = max;
                }
                else if (range.Contains('-'))
                {
                    var rangeParts = range.Split('-');
                    start = int.Parse(rangeParts[0]);
                    end = int.Parse(rangeParts[1]);

                    // Validate range bounds
                    if (start < min || start > max || end < min || end > max)
                        throw new ArgumentOutOfRangeException(nameof(field), $"Range {start}-{end} is outside valid bounds [{min}-{max}]");
                }
                else
                {
                    start = int.Parse(range);
                    end = max;

                    // Validate start value
                    if (start < min || start > max)
                        throw new ArgumentOutOfRangeException(nameof(field), $"Value {start} is outside valid bounds [{min}-{max}]");
                }

                for (var i = start; i <= end; i += step)
                    values.Add(i);
            }
            else if (part.Contains('-'))
            {
                var rangeParts = part.Split('-');
                var start = int.Parse(rangeParts[0]);
                var end = int.Parse(rangeParts[1]);

                // Validate range bounds
                if (start < min || start > max || end < min || end > max)
                    throw new ArgumentOutOfRangeException(nameof(field), $"Range {start}-{end} is outside valid bounds [{min}-{max}]");

                for (var i = start; i <= end; i++)
                    values.Add(i);
            }
            else
            {
                var value = int.Parse(part);

                // Validate single value is within bounds
                if (value < min || value > max)
                    throw new ArgumentOutOfRangeException(nameof(field), $"Value {value} is outside valid bounds [{min}-{max}]");

                values.Add(value);
            }
        }

        return values;
    }
}

/// <summary>
/// Parsed cron schedule that can calculate next occurrences.
/// </summary>
public sealed class CronSchedule
{
    public HashSet<int> Minutes { get; set; } = new();
    public HashSet<int> Hours { get; set; } = new();
    public HashSet<int> DaysOfMonth { get; set; } = new();
    public HashSet<int> Months { get; set; } = new();
    public HashSet<int> DaysOfWeek { get; set; } = new();

    /// <summary>
    /// Calculate the next occurrence after the given time.
    /// </summary>
    public DateTimeOffset GetNextOccurrence(DateTimeOffset from, TimeZoneInfo timeZone)
    {
        // Convert to the target timezone
        var localFrom = TimeZoneInfo.ConvertTime(from, timeZone);
        var current = new DateTime(
            localFrom.Year, localFrom.Month, localFrom.Day,
            localFrom.Hour, localFrom.Minute, 0, DateTimeKind.Unspecified);

        // Start from the next minute
        current = current.AddMinutes(1);

        // Search for next occurrence (max 4 years)
        var maxIterations = 365 * 4 * 24 * 60;
        for (var i = 0; i < maxIterations; i++)
        {
            if (Matches(current))
            {
                // Convert back to DateTimeOffset with the correct offset
                var offset = timeZone.GetUtcOffset(current);
                return new DateTimeOffset(current, offset);
            }

            // Advance to next candidate
            current = AdvanceToNextCandidate(current);
        }

        // Fallback: return next day at the first scheduled time
        return from.AddDays(1);
    }

    private bool Matches(DateTime dt)
    {
        return Minutes.Contains(dt.Minute) &&
               Hours.Contains(dt.Hour) &&
               DaysOfMonth.Contains(dt.Day) &&
               Months.Contains(dt.Month) &&
               DaysOfWeek.Contains((int)dt.DayOfWeek);
    }

    private DateTime AdvanceToNextCandidate(DateTime current)
    {
        // Try to skip to the next valid minute
        var nextMinute = Minutes.Where(m => m > current.Minute).DefaultIfEmpty(-1).First();
        if (nextMinute >= 0)
            return new DateTime(current.Year, current.Month, current.Day, current.Hour, nextMinute, 0);

        // Reset minute to first valid, advance hour
        var firstMinute = Minutes.Min();
        var nextHour = Hours.Where(h => h > current.Hour).DefaultIfEmpty(-1).First();
        if (nextHour >= 0)
            return new DateTime(current.Year, current.Month, current.Day, nextHour, firstMinute, 0);

        // Reset hour and minute, advance day
        var firstHour = Hours.Min();
        return new DateTime(current.Year, current.Month, current.Day, firstHour, firstMinute, 0).AddDays(1);
    }

    /// <summary>
    /// Get a human-readable description of this schedule.
    /// </summary>
    public string GetDescription()
    {
        var parts = new List<string>();

        // Time description
        if (Minutes.Count == 1 && Hours.Count == 1)
        {
            parts.Add($"at {Hours.First():D2}:{Minutes.First():D2}");
        }
        else if (Minutes.Count == 60 && Hours.Count == 24)
        {
            parts.Add("every minute");
        }
        else if (Minutes.Count == 1 && Hours.Count == 24)
        {
            parts.Add($"every hour at minute {Minutes.First()}");
        }
        else
        {
            parts.Add($"at minutes {string.Join(",", Minutes.OrderBy(x => x))} of hours {string.Join(",", Hours.OrderBy(x => x))}");
        }

        // Day of week description
        if (DaysOfWeek.Count < 7)
        {
            var dayNames = DaysOfWeek.OrderBy(d => d)
                .Select(d => ((DayOfWeek)d).ToString()[..3])
                .ToList();
            parts.Add($"on {string.Join(", ", dayNames)}");
        }

        // Day of month description
        if (DaysOfMonth.Count < 31)
        {
            parts.Add($"on days {string.Join(",", DaysOfMonth.OrderBy(x => x))} of the month");
        }

        // Month description
        if (Months.Count < 12)
        {
            parts.Add($"in months {string.Join(",", Months.OrderBy(x => x))}");
        }

        return string.Join(" ", parts);
    }
}

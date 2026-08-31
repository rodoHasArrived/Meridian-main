namespace Meridian.Core.Scheduling;

/// <summary>
/// Lightweight cron expression parser supporting five-field POSIX cron plus the explicit
/// <c>d#n</c> ordinal-day extension.
/// Format: minute hour day-of-month month day-of-week
/// Examples:
///   "0 2 * * *"     - Daily at 2:00 AM
///   "0 3 * * 0"     - Every Sunday at 3:00 AM
///   "0 1 * * 0#1"   - First Sunday of each month at 1:00 AM
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
    /// <returns>
    /// Next occurrence, or <see langword="null"/> when the expression is invalid or has no
    /// occurrence within the supported calendar horizon.
    /// </returns>
    public static DateTimeOffset? GetNextOccurrence(string cronExpression, TimeZoneInfo timeZone, DateTimeOffset from)
    {
        if (!TryParse(cronExpression, out var schedule))
            return null;

        return schedule.GetNextOccurrenceOrNull(from, timeZone);
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
            (schedule.DaysOfWeek, schedule.DayOfWeekOrdinal) = ParseDayOfWeekField(parts[4]);
            // Vixie-cron semantics: a day field whose expression starts with '*'
            // is treated as unrestricted when combining day-of-month with day-of-week.
            schedule.DayOfMonthIsWildcard = parts[2].StartsWith('*');
            schedule.DayOfWeekIsWildcard = parts[4].StartsWith('*');
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
                if (stepParts.Length != 2)
                    throw new FormatException($"Invalid step expression '{part}'.");

                var range = stepParts[0];
                var step = int.Parse(stepParts[1]);
                if (step <= 0)
                    throw new ArgumentOutOfRangeException(nameof(field), "Cron step must be greater than zero.");

                int start, end;
                if (range == "*")
                {
                    start = min;
                    end = max;
                }
                else if (range.Contains('-'))
                {
                    var rangeParts = range.Split('-');
                    if (rangeParts.Length != 2)
                        throw new FormatException($"Invalid range expression '{range}'.");

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

                if (start > end)
                    throw new ArgumentException($"Range {start}-{end} must be ascending.", nameof(field));

                for (var i = start; i <= end;)
                {
                    values.Add(i);
                    if (step > end - i)
                        break;

                    i += step;
                }
            }
            else if (part.Contains('-'))
            {
                var rangeParts = part.Split('-');
                if (rangeParts.Length != 2)
                    throw new FormatException($"Invalid range expression '{part}'.");

                var start = int.Parse(rangeParts[0]);
                var end = int.Parse(rangeParts[1]);

                // Validate range bounds
                if (start < min || start > max || end < min || end > max)
                    throw new ArgumentOutOfRangeException(nameof(field), $"Range {start}-{end} is outside valid bounds [{min}-{max}]");

                if (start > end)
                    throw new ArgumentException($"Range {start}-{end} must be ascending.", nameof(field));

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

        if (values.Count == 0)
            throw new ArgumentException("Cron fields must contain at least one value.", nameof(field));

        return values;
    }

    private static (HashSet<int> DaysOfWeek, int? Ordinal) ParseDayOfWeekField(string field)
    {
        if (!field.Contains('#'))
            return (ParseField(field, 0, 6), null);

        // Meridian's explicit ordinal extension intentionally accepts one day and one occurrence.
        // Keeping this narrow avoids ambiguous combinations with POSIX day-of-month/day-of-week OR
        // semantics while making presets such as "first Sunday" directly representable.
        var parts = field.Split('#');
        if (parts.Length != 2
            || parts[0].Contains(',')
            || parts[0].Contains('-')
            || parts[0].Contains('/'))
        {
            throw new FormatException($"Invalid ordinal day-of-week expression '{field}'.");
        }

        var dayOfWeek = int.Parse(parts[0]);
        var ordinal = int.Parse(parts[1]);
        if (dayOfWeek is < 0 or > 6)
            throw new ArgumentOutOfRangeException(nameof(field), "Ordinal day of week must be between 0 and 6.");
        if (ordinal is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(field), "Day-of-week ordinal must be between 1 and 5.");

        return (new HashSet<int> { dayOfWeek }, ordinal);
    }
}

/// <summary>
/// Parsed cron schedule that can calculate next occurrences.
/// </summary>
public sealed class CronSchedule
{
    // Eight years covers the longest gap between February 29 occurrences across a non-leap
    // century while keeping malformed or impossible schedules absolutely bounded.
    private const int SearchHorizonYears = 8;

    public HashSet<int> Minutes { get; set; } = new();
    public HashSet<int> Hours { get; set; } = new();
    public HashSet<int> DaysOfMonth { get; set; } = new();
    public HashSet<int> Months { get; set; } = new();
    public HashSet<int> DaysOfWeek { get; set; } = new();

    /// <summary>True when the day-of-month field was written as a wildcard ("*" or "*/n").</summary>
    public bool DayOfMonthIsWildcard { get; set; } = true;

    /// <summary>True when the day-of-week field was written as a wildcard ("*" or "*/n").</summary>
    public bool DayOfWeekIsWildcard { get; set; } = true;

    /// <summary>
    /// Optional one-based occurrence of the selected day of week within a month. For example,
    /// <c>0#1</c> means the first Sunday. This is an explicit Meridian extension to five-field cron.
    /// </summary>
    public int? DayOfWeekOrdinal { get; set; }

    /// <summary>
    /// Calculate the next occurrence after the given time. This legacy non-null API is retained
    /// for binary compatibility and throws when the schedule has no occurrence in the supported
    /// calendar horizon.
    /// </summary>
    public DateTimeOffset GetNextOccurrence(DateTimeOffset from, TimeZoneInfo timeZone)
    {
        return GetNextOccurrenceOrNull(from, timeZone)
            ?? throw new InvalidOperationException(
                $"The cron schedule has no occurrence within the next {SearchHorizonYears} calendar years.");
    }

    /// <summary>
    /// Calculate the next occurrence after the given time, or return <see langword="null"/> when
    /// no occurrence exists within the supported calendar horizon.
    /// </summary>
    public DateTimeOffset? GetNextOccurrenceOrNull(DateTimeOffset from, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        if (!HasUsableFields())
            return null;

        var localFrom = TimeZoneInfo.ConvertTime(from, timeZone);
        var localMinute = new DateTime(
            localFrom.Year, localFrom.Month, localFrom.Day,
            localFrom.Hour, localFrom.Minute, 0, DateTimeKind.Unspecified);
        var searchHorizon = GetSearchHorizon(localMinute);
        var ambiguousOccurrence = GetNextAmbiguousOccurrence(localMinute, timeZone, from);
        if (ambiguousOccurrence.HasValue)
            return ambiguousOccurrence;

        if (!TryAddMinutes(localMinute, 1, out var current))
            return null;

        while (current <= searchHorizon)
        {
            if (!Months.Contains(current.Month))
            {
                if (!TryAdvanceToNextMonth(current, out current))
                    return null;
                continue;
            }

            if (!MatchesDay(current))
            {
                if (!TryAdvanceToNextDay(current, out current))
                    return null;
                continue;
            }

            if (!Hours.Contains(current.Hour))
            {
                if (!TryAdvanceToNextHour(current, out current))
                    return null;
                continue;
            }

            if (!Minutes.Contains(current.Minute))
            {
                if (!TryAdvanceToNextMinute(current, out current))
                    return null;
                continue;
            }

            if (TryCreateOccurrence(current, timeZone, from, out var occurrence))
                return occurrence;

            if (!TryAdvanceToNextMinute(current, out current))
                return null;
        }

        return null;
    }

    private bool HasUsableFields()
    {
        return Minutes is { Count: > 0 }
            && Minutes.All(static value => value is >= 0 and <= 59)
            && Hours is { Count: > 0 }
            && Hours.All(static value => value is >= 0 and <= 23)
            && DaysOfMonth is { Count: > 0 }
            && DaysOfMonth.All(static value => value is >= 1 and <= 31)
            && Months is { Count: > 0 }
            && Months.All(static value => value is >= 1 and <= 12)
            && DaysOfWeek is { Count: > 0 }
            && DaysOfWeek.All(static value => value is >= 0 and <= 6)
            && (DayOfWeekOrdinal is null or >= 1 and <= 5)
            && (!DayOfWeekOrdinal.HasValue || DaysOfWeek.Count == 1);
    }

    private bool MatchesDay(DateTime dt)
    {
        var dayOfMonthMatches = DaysOfMonth.Contains(dt.Day);
        var dayOfWeekMatches = DaysOfWeek.Contains((int)dt.DayOfWeek)
            && (!DayOfWeekOrdinal.HasValue
                || ((dt.Day - 1) / 7) + 1 == DayOfWeekOrdinal.Value);

        // POSIX cron: when BOTH day fields are restricted, a day matches if EITHER
        // field matches (OR). When at least one is a wildcard, both must match (the
        // wildcard side always matches, so this reduces to the restricted side).
        return DayOfMonthIsWildcard || DayOfWeekIsWildcard
            ? dayOfMonthMatches && dayOfWeekMatches
            : dayOfMonthMatches || dayOfWeekMatches;
    }

    private bool MatchesCandidate(DateTime value)
    {
        return Minutes.Contains(value.Minute)
            && Hours.Contains(value.Hour)
            && Months.Contains(value.Month)
            && MatchesDay(value);
    }

    private bool TryAdvanceToNextMonth(DateTime current, out DateTime next)
    {
        var nextMonth = Months.Where(month => month > current.Month).DefaultIfEmpty(-1).Min();
        var year = current.Year;
        if (nextMonth < 0)
        {
            if (year == DateTime.MaxValue.Year)
            {
                next = default;
                return false;
            }

            year++;
            nextMonth = Months.Min();
        }

        next = new DateTime(year, nextMonth, 1, Hours.Min(), Minutes.Min(), 0, DateTimeKind.Unspecified);
        return true;
    }

    private bool TryAdvanceToNextMinute(DateTime current, out DateTime next)
    {
        var nextMinute = Minutes.Where(minute => minute > current.Minute).DefaultIfEmpty(-1).Min();
        if (nextMinute >= 0)
        {
            next = new DateTime(
                current.Year,
                current.Month,
                current.Day,
                current.Hour,
                nextMinute,
                0,
                DateTimeKind.Unspecified);
            return true;
        }

        return TryAdvanceToNextHour(current, out next);
    }

    private bool TryAdvanceToNextHour(DateTime current, out DateTime next)
    {
        var nextHour = Hours.Where(hour => hour > current.Hour).DefaultIfEmpty(-1).Min();
        if (nextHour >= 0)
        {
            next = new DateTime(
                current.Year,
                current.Month,
                current.Day,
                nextHour,
                Minutes.Min(),
                0,
                DateTimeKind.Unspecified);
            return true;
        }

        return TryAdvanceToNextDay(current, out next);
    }

    private bool TryAdvanceToNextDay(DateTime current, out DateTime next)
    {
        if (current.Date == DateTime.MaxValue.Date)
        {
            next = default;
            return false;
        }

        var nextDay = current.Date.AddDays(1);
        next = new DateTime(
            nextDay.Year,
            nextDay.Month,
            nextDay.Day,
            Hours.Min(),
            Minutes.Min(),
            0,
            DateTimeKind.Unspecified);
        return true;
    }

    private static DateTime GetSearchHorizon(DateTime from)
    {
        return from.Year > DateTime.MaxValue.Year - SearchHorizonYears
            ? DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Unspecified)
            : from.AddYears(SearchHorizonYears);
    }

    private DateTimeOffset? GetNextAmbiguousOccurrence(
        DateTime localMinute,
        TimeZoneInfo timeZone,
        DateTimeOffset after)
    {
        if (!timeZone.IsAmbiguousTime(localMinute))
            return null;

        var start = localMinute;
        while (start > DateTime.MinValue)
        {
            var previous = start.AddMinutes(-1);
            if (!timeZone.IsAmbiguousTime(previous))
                break;
            start = previous;
        }

        var end = localMinute;
        while (end < DateTime.MaxValue.AddMinutes(-1))
        {
            var next = end.AddMinutes(1);
            if (!timeZone.IsAmbiguousTime(next))
                break;
            end = next;
        }

        DateTimeOffset? earliest = null;
        for (var candidate = start; candidate <= end; candidate = candidate.AddMinutes(1))
        {
            if (MatchesCandidate(candidate)
                && TryCreateOccurrence(candidate, timeZone, after, out var occurrence)
                && (!earliest.HasValue || occurrence < earliest.Value))
            {
                earliest = occurrence;
            }

            if (candidate == end)
                break;
        }

        return earliest;
    }

    private static bool TryAddMinutes(DateTime value, int minutes, out DateTime result)
    {
        if (value > DateTime.MaxValue.AddMinutes(-minutes))
        {
            result = default;
            return false;
        }

        result = value.AddMinutes(minutes);
        return true;
    }

    private static bool TryCreateOccurrence(
        DateTime localTime,
        TimeZoneInfo timeZone,
        DateTimeOffset after,
        out DateTimeOffset occurrence)
    {
        if (timeZone.IsInvalidTime(localTime))
        {
            occurrence = default;
            return false;
        }

        try
        {
            if (timeZone.IsAmbiguousTime(localTime))
            {
                var futureCandidates = timeZone.GetAmbiguousTimeOffsets(localTime)
                    .Select(offset => new DateTimeOffset(localTime, offset))
                    .Where(candidate => candidate > after)
                    .OrderBy(static candidate => candidate.UtcDateTime)
                    .ToArray();
                if (futureCandidates.Length == 0)
                {
                    occurrence = default;
                    return false;
                }

                occurrence = futureCandidates[0];
                return true;
            }

            occurrence = new DateTimeOffset(localTime, timeZone.GetUtcOffset(localTime));
            return occurrence > after;
        }
        catch (ArgumentException)
        {
            occurrence = default;
            return false;
        }
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

        string? dayOfWeekDescription = null;
        if (DayOfWeekOrdinal.HasValue)
        {
            var ordinalNames = new[] { "first", "second", "third", "fourth", "fifth" };
            var dayName = ((DayOfWeek)DaysOfWeek.Single()).ToString();
            dayOfWeekDescription =
                $"on the {ordinalNames[DayOfWeekOrdinal.Value - 1]} {dayName} of the month";
        }
        else if (DaysOfWeek.Count < 7)
        {
            var dayNames = DaysOfWeek.OrderBy(d => d)
                .Select(d => ((DayOfWeek)d).ToString()[..3])
                .ToList();
            dayOfWeekDescription = $"on {string.Join(", ", dayNames)}";
        }

        var dayOfMonthDescription = DaysOfMonth.Count < 31
            ? $"on days {string.Join(",", DaysOfMonth.OrderBy(x => x))} of the month"
            : null;
        if (!DayOfMonthIsWildcard
            && !DayOfWeekIsWildcard
            && dayOfWeekDescription is not null
            && dayOfMonthDescription is not null)
        {
            parts.Add($"{dayOfWeekDescription} or {dayOfMonthDescription}");
        }
        else
        {
            if (dayOfWeekDescription is not null)
                parts.Add(dayOfWeekDescription);
            if (dayOfMonthDescription is not null)
                parts.Add(dayOfMonthDescription);
        }

        // Month description
        if (Months.Count < 12)
        {
            parts.Add($"in months {string.Join(",", Months.OrderBy(x => x))}");
        }

        return string.Join(" ", parts);
    }
}

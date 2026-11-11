namespace Meridian.Ledger;

/// <summary>
/// Projects recurring ledger report schedules into concrete export occurrences.
/// </summary>
public static class LedgerReportSchedulePlanner
{
    public static IReadOnlyList<LedgerReportScheduledExport> Project(
        LedgerReportSchedule schedule,
        int occurrenceCount)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (occurrenceCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(occurrenceCount), occurrenceCount, "Occurrence count must be positive.");

        var exports = new List<LedgerReportScheduledExport>(occurrenceCount);
        var periodStart = schedule.FirstPeriodStart;

        while (exports.Count < occurrenceCount)
        {
            if (schedule.EndsOn.HasValue && periodStart > schedule.EndsOn.Value)
                break;

            var period = PeriodStartingOn(schedule.Frequency, periodStart);
            if (schedule.EndsOn.HasValue && period.PeriodEnd > schedule.EndsOn.Value)
                break;

            exports.Add(BuildExport(schedule, exports.Count + 1, period.PeriodStart, period.PeriodEnd));
            periodStart = period.NextPeriodStart;
        }

        return exports;
    }

    /// <summary>
    /// Returns the reporting period of the given cadence that starts on <paramref name="periodStart"/>.
    /// This is the single definition of period boundaries and period identifiers; both
    /// <see cref="Project"/> and the governed reporting scheduler advance through it.
    /// </summary>
    public static LedgerReportPeriod PeriodStartingOn(
        LedgerReportScheduleFrequency frequency,
        DateOnly periodStart)
    {
        var nextPeriodStart = AddPeriod(periodStart, frequency);
        return new LedgerReportPeriod(
            PeriodId(frequency, periodStart),
            periodStart,
            nextPeriodStart.AddDays(-1),
            nextPeriodStart);
    }

    private static LedgerReportScheduledExport BuildExport(
        LedgerReportSchedule schedule,
        int sequence,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        var periodId = PeriodId(schedule.Frequency, periodStart);
        var reportId = $"{schedule.ScheduleId}:{periodId}:{sequence:0000}";
        var asOf = EndOfDayUtc(periodEnd);
        var dueAtUtc = new DateTimeOffset(
            periodEnd.AddDays(schedule.DueDaysAfterPeriodEnd).ToDateTime(TimeOnly.MinValue),
            TimeSpan.Zero);

        return new LedgerReportScheduledExport(
            schedule,
            reportId,
            periodId,
            periodStart,
            periodEnd,
            asOf,
            dueAtUtc,
            schedule.Formats,
            schedule.Recipients);
    }

    private static DateOnly AddPeriod(DateOnly start, LedgerReportScheduleFrequency frequency)
        => frequency switch
        {
            LedgerReportScheduleFrequency.Monthly => start.AddMonths(1),
            LedgerReportScheduleFrequency.Quarterly => start.AddMonths(3),
            LedgerReportScheduleFrequency.Annually => start.AddYears(1),
            _ => throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Unsupported report schedule frequency."),
        };

    private static string PeriodId(LedgerReportScheduleFrequency frequency, DateOnly periodStart)
        => frequency switch
        {
            LedgerReportScheduleFrequency.Monthly => $"{periodStart:yyyy-MM}",
            LedgerReportScheduleFrequency.Quarterly => $"{periodStart.Year}-Q{((periodStart.Month - 1) / 3) + 1}",
            LedgerReportScheduleFrequency.Annually => $"{periodStart:yyyy}",
            _ => throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Unsupported report schedule frequency."),
        };

    private static DateTimeOffset EndOfDayUtc(DateOnly date)
        => new(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
}

/// <summary>One period-bounded reporting window produced by <see cref="LedgerReportSchedulePlanner"/>.</summary>
public readonly record struct LedgerReportPeriod(
    string PeriodId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly NextPeriodStart);

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

            var nextPeriodStart = AddPeriod(periodStart, schedule.Frequency);
            var periodEnd = nextPeriodStart.AddDays(-1);
            if (schedule.EndsOn.HasValue && periodEnd > schedule.EndsOn.Value)
                break;

            exports.Add(BuildExport(schedule, exports.Count + 1, periodStart, periodEnd));
            periodStart = nextPeriodStart;
        }

        return exports;
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

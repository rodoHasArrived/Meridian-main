namespace Meridian.Ledger;

/// <summary>
/// One scheduled export occurrence for a period-bounded ledger report.
/// </summary>
public sealed record LedgerReportScheduledExport(
    LedgerReportSchedule Schedule,
    string ReportId,
    string PeriodId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateTimeOffset AsOf,
    DateTimeOffset DueAtUtc,
    IReadOnlyList<LedgerReportExportFormat> Formats,
    IReadOnlyList<string> Recipients)
{
    public LedgerReportPackRequest ToReportPackRequest(
        string generatedBy,
        DateTimeOffset generatedAtUtc,
        LockedAccountingPeriod? lockedPeriod = null)
    {
        var periodStart = new DateTimeOffset(PeriodStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var periodEnd = EndOfDayUtc(PeriodEnd);

        return new LedgerReportPackRequest(
            ReportId,
            Schedule.FundId,
            PeriodId,
            periodStart,
            periodEnd,
            AsOf,
            Schedule.BaseCurrency,
            generatedBy,
            generatedAtUtc,
            lockedPeriod,
            lineDimensions: Schedule.LineDimensions);
    }

    private static DateTimeOffset EndOfDayUtc(DateOnly date)
        => new(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
}

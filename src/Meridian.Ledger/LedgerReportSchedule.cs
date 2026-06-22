namespace Meridian.Ledger;

/// <summary>
/// Governed schedule definition for recurring financial report exports.
/// </summary>
public sealed record LedgerReportSchedule
{
    public LedgerReportSchedule(
        string scheduleId,
        string fundId,
        string reportName,
        LedgerReportScheduleFrequency frequency,
        DateOnly firstPeriodStart,
        int dueDaysAfterPeriodEnd,
        string baseCurrency,
        IReadOnlyList<LedgerReportExportFormat> formats,
        IReadOnlyList<string> recipients,
        string createdBy,
        DateTimeOffset createdAtUtc,
        DateOnly? endsOn = null,
        LedgerLineDimensionSet? lineDimensions = null)
    {
        if (string.IsNullOrWhiteSpace(scheduleId))
            throw new ArgumentException("Schedule identifier must not be null or whitespace.", nameof(scheduleId));
        if (string.IsNullOrWhiteSpace(fundId))
            throw new ArgumentException("Fund identifier must not be null or whitespace.", nameof(fundId));
        if (string.IsNullOrWhiteSpace(reportName))
            throw new ArgumentException("Report name must not be null or whitespace.", nameof(reportName));
        if (dueDaysAfterPeriodEnd < 0)
            throw new ArgumentOutOfRangeException(nameof(dueDaysAfterPeriodEnd), dueDaysAfterPeriodEnd, "Due-day offset cannot be negative.");
        if (string.IsNullOrWhiteSpace(baseCurrency))
            throw new ArgumentException("Base currency must not be null or whitespace.", nameof(baseCurrency));
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(recipients);
        if (formats.Count == 0)
            throw new ArgumentException("At least one export format is required.", nameof(formats));
        if (recipients.Count == 0)
            throw new ArgumentException("At least one report recipient is required.", nameof(recipients));
        if (string.IsNullOrWhiteSpace(createdBy))
            throw new ArgumentException("Schedule creator must not be null or whitespace.", nameof(createdBy));
        if (endsOn.HasValue && endsOn.Value < firstPeriodStart)
            throw new ArgumentException("Schedule end date must be greater than or equal to the first period start.", nameof(endsOn));

        ScheduleId = scheduleId.Trim();
        FundId = fundId.Trim();
        ReportName = reportName.Trim();
        Frequency = frequency;
        FirstPeriodStart = firstPeriodStart;
        DueDaysAfterPeriodEnd = dueDaysAfterPeriodEnd;
        BaseCurrency = baseCurrency.Trim().ToUpperInvariant();
        Formats = formats.Distinct().ToArray();
        Recipients = recipients
            .Select(static recipient => recipient.Trim())
            .Where(static recipient => recipient.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (Recipients.Count == 0)
            throw new ArgumentException("At least one non-empty report recipient is required.", nameof(recipients));

        CreatedBy = createdBy.Trim();
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        EndsOn = endsOn;
        LineDimensions = lineDimensions;
    }

    public string ScheduleId { get; }

    public string FundId { get; }

    public string ReportName { get; }

    public LedgerReportScheduleFrequency Frequency { get; }

    public DateOnly FirstPeriodStart { get; }

    public int DueDaysAfterPeriodEnd { get; }

    public string BaseCurrency { get; }

    public IReadOnlyList<LedgerReportExportFormat> Formats { get; }

    public IReadOnlyList<string> Recipients { get; }

    public string CreatedBy { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateOnly? EndsOn { get; }

    public LedgerLineDimensionSet? LineDimensions { get; }
}

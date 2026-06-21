namespace Meridian.Ledger;

/// <summary>
/// Metadata required to produce a governed ledger report pack for a closed accounting period.
/// </summary>
public sealed record LedgerReportPackRequest
{
    public LedgerReportPackRequest(
        string reportId,
        string fundId,
        string periodId,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        DateTimeOffset asOf,
        string baseCurrency,
        string generatedBy,
        DateTimeOffset generatedAtUtc,
        LockedAccountingPeriod? lockedPeriod = null,
        string? sourceRunId = null,
        string? sourceSessionId = null,
        IReadOnlyList<string>? reconciliationEvidenceLinks = null,
        IReadOnlyList<string>? approvalEvidenceLinks = null,
        LedgerLineDimensionSet? lineDimensions = null)
    {
        if (string.IsNullOrWhiteSpace(reportId))
            throw new ArgumentException("Report identifier must not be null or whitespace.", nameof(reportId));
        if (string.IsNullOrWhiteSpace(fundId))
            throw new ArgumentException("Fund identifier must not be null or whitespace.", nameof(fundId));
        if (string.IsNullOrWhiteSpace(periodId))
            throw new ArgumentException("Period identifier must not be null or whitespace.", nameof(periodId));
        if (periodEnd < periodStart)
            throw new ArgumentException("Period end must be greater than or equal to period start.", nameof(periodEnd));
        if (asOf < periodStart || asOf > periodEnd)
            throw new ArgumentException("Report as-of timestamp must fall inside the accounting period.", nameof(asOf));
        if (string.IsNullOrWhiteSpace(baseCurrency))
            throw new ArgumentException("Base currency must not be null or whitespace.", nameof(baseCurrency));
        if (string.IsNullOrWhiteSpace(generatedBy))
            throw new ArgumentException("Generator identity must not be null or whitespace.", nameof(generatedBy));

        if (lockedPeriod is not null)
        {
            if (!string.Equals(lockedPeriod.PeriodId, periodId.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Locked period identifier must match the report period.", nameof(lockedPeriod));
            if (lockedPeriod.StartsAtInclusive != periodStart || lockedPeriod.EndsAtInclusive != periodEnd)
                throw new ArgumentException("Locked period range must match the report period.", nameof(lockedPeriod));
        }

        ReportId = reportId.Trim();
        FundId = fundId.Trim();
        PeriodId = periodId.Trim();
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        AsOf = asOf;
        BaseCurrency = baseCurrency.Trim().ToUpperInvariant();
        GeneratedBy = generatedBy.Trim();
        GeneratedAtUtc = generatedAtUtc.ToUniversalTime();
        LockedPeriod = lockedPeriod;
        SourceRunId = string.IsNullOrWhiteSpace(sourceRunId) ? null : sourceRunId.Trim();
        SourceSessionId = string.IsNullOrWhiteSpace(sourceSessionId) ? null : sourceSessionId.Trim();
        ReconciliationEvidenceLinks = NormalizeEvidenceLinks(reconciliationEvidenceLinks);
        ApprovalEvidenceLinks = NormalizeEvidenceLinks(approvalEvidenceLinks);
        LineDimensions = lineDimensions;
    }

    public string ReportId { get; }

    public string FundId { get; }

    public string PeriodId { get; }

    public DateTimeOffset PeriodStart { get; }

    public DateTimeOffset PeriodEnd { get; }

    public DateTimeOffset AsOf { get; }

    public string BaseCurrency { get; }

    public string GeneratedBy { get; }

    public DateTimeOffset GeneratedAtUtc { get; }

    public LockedAccountingPeriod? LockedPeriod { get; }

    public string? SourceRunId { get; }

    public string? SourceSessionId { get; }

    public IReadOnlyList<string> ReconciliationEvidenceLinks { get; }

    public IReadOnlyList<string> ApprovalEvidenceLinks { get; }

    public LedgerLineDimensionSet? LineDimensions { get; }

    private static IReadOnlyList<string> NormalizeEvidenceLinks(IReadOnlyList<string>? links)
        => links?
            .Select(static link => link.Trim())
            .Where(static link => link.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
}

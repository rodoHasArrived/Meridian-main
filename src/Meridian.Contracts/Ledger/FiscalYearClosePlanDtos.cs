namespace Meridian.Contracts.Ledger;

/// <summary>
/// Read-only fiscal-year close plan for one ledger book (W12-YEND-001 slice 1). It is derived only
/// from retained hard-close snapshots and never posts, reopens or changes period state. When any
/// blocker is present the plan is not ready, carries no balances and no plan hash, so an incomplete
/// year can never be read as a closeable or empty one.
/// </summary>
public sealed record FiscalYearClosePlanDto(
    Guid LedgerBookId,
    int FiscalYear,
    DateOnly? FiscalYearStart,
    DateOnly? FiscalYearEnd,
    bool IsReady,
    IReadOnlyList<FiscalYearCloseBlockerDto> Blockers,
    IReadOnlyList<FiscalYearClosePeriodEvidenceDto> Periods,
    IReadOnlyList<FiscalYearCloseBalanceDto> PreClosingTrialBalance,
    IReadOnlyList<FiscalYearCloseBalanceDto> PostedClosingTransfers,
    IReadOnlyList<FiscalYearCloseBalanceDto> ProposedClosingTransfers,
    IReadOnlyList<FiscalYearCloseBalanceDto> PostClosingTrialBalance,
    IReadOnlyList<FiscalYearCloseBalanceDto> NextYearOpeningBalances,
    decimal ReportedNetIncome,
    decimal OpeningEquity,
    decimal PreClosingEquity,
    decimal PostClosingEquity,
    string? PlanHashSha256);

/// <summary>One named reason the fiscal year cannot be closed yet.</summary>
public sealed record FiscalYearCloseBlockerDto(string Code, string Message, Guid? PeriodId = null);

/// <summary>A constituent period and the retained close snapshot the plan relied on.</summary>
public sealed record FiscalYearClosePeriodEvidenceDto(
    Guid PeriodId,
    int PeriodNo,
    string Label,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    long PeriodVersion,
    long? SnapshotVersion,
    string? SnapshotHashSha256);

/// <summary>One account and dimensional scope in a close-plan section, with normal-balance sign.</summary>
public sealed record FiscalYearCloseBalanceDto(
    string AccountName,
    string AccountType,
    string? Symbol,
    string? FinancialAccountId,
    LedgerDimensionSetDto? Dimensions,
    decimal DebitTotal,
    decimal CreditTotal,
    decimal Balance);

/// <summary>Stable blocker codes for the fiscal-year close plan.</summary>
public static class FiscalYearCloseBlockerCodes
{
    public const string NoPeriods = "no-periods";
    public const string CoverageGap = "coverage-gap";
    public const string PeriodNotHardClosed = "period-not-hard-closed";
    public const string PriorYearNotClosed = "prior-year-not-closed";
    public const string SnapshotMissing = "snapshot-missing";
    public const string SnapshotStale = "snapshot-stale";
    public const string SnapshotIntegrity = "snapshot-integrity";
    public const string FinalSnapshotSuperseded = "final-snapshot-superseded";
    public const string UnknownJournalPeriod = "unknown-journal-period";
    public const string LaterYearJournal = "later-year-journal";
    public const string Unbalanced = "unbalanced";
    public const string IncomeNotPreserved = "income-not-preserved";
}

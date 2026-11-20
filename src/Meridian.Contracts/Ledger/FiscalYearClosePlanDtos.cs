using System.Text.Json.Serialization;

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
    string? PlanHashSha256)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FiscalYearCloseBookEvidenceDto? Book { get; init; }

    /// <summary>Whether the authenticated reader can authorize the current plan.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CanFinalizeFiscalYearClose { get; init; }
}

/// <summary>The retained book settings bound into the reviewed fiscal plan.</summary>
public sealed record FiscalYearCloseBookEvidenceDto(
    string FunctionalCurrency,
    AccountingBasisKindDto AccountingBasis,
    string AccountingPolicyId,
    string AccountingPolicyVersion,
    Guid OwningEntityId,
    DateTimeOffset BookUpdatedAt);

public sealed record FinalizeFiscalYearCloseRequestDto(
    Guid CommandId,
    string ExpectedPlanHashSha256,
    string Reason,
    Guid? SupersedesReceiptId = null);

/// <summary>An immutable fiscal close and next-year opening representation for a continuous book.</summary>
public sealed record FiscalYearCloseReceiptDto(
    Guid ReceiptId,
    Guid? SupersedesReceiptId,
    DateTimeOffset RecordedAt,
    string AuthorizedBy,
    string ControllerRole,
    string Reason,
    FiscalYearClosePlanDto Plan,
    string FunctionalCurrency,
    AccountingBasisKindDto AccountingBasis,
    string AccountingPolicyId,
    string AccountingPolicyVersion,
    IReadOnlyList<Guid> ClosingJournalEntryIds);

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

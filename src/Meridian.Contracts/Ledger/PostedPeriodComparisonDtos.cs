namespace Meridian.Contracts.Ledger;

public sealed record PostedPeriodComparisonRangeDto(
    Guid PeriodId, string Label, DateOnly PeriodStart, DateOnly PeriodEnd,
    DateOnly RangeStart, DateOnly RangeEnd, bool IsPartial, string Status);

public sealed record PostedPeriodComparisonLineDto(
    string AccountKey, string AccountName, string AccountType, string? Symbol, string? FinancialAccountId,
    decimal CurrentDebit, decimal CurrentCredit, decimal CurrentBalance,
    decimal PriorDebit, decimal PriorCredit, decimal PriorBalance, decimal Change, decimal? PercentChange,
    LedgerDimensionSetDto? Dimensions);

/// <summary>Recomputed posted-journal population, with explicit cutoffs; never a strategy-run simulation.</summary>
public sealed record PostedPeriodComparisonDto(
    string Status, string? Reason, DateTimeOffset GeneratedAt, DateTimeOffset AsOf,
    Guid LedgerBookId, string FundProfileId, string BookName, AccountingBasisKindDto AccountingBasis,
    string Currency, string AccountingPolicyId, string AccountingPolicyVersion,
    PostedPeriodComparisonRangeDto Current, PostedPeriodComparisonRangeDto? Baseline,
    IReadOnlyList<PostedPeriodComparisonLineDto> Lines, IReadOnlyList<Guid> CurrentJournalIds,
    IReadOnlyList<Guid> BaselineJournalIds, string? PopulationHash, LedgerDimensionSetDto? Dimensions);

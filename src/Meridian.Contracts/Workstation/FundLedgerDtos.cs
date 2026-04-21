namespace Meridian.Contracts.Workstation;

/// <summary>
/// Fund-level ledger drill-in scope.
/// </summary>
public enum FundLedgerScope : byte
{
    Consolidated,
    Entity,
    Sleeve,
    Vehicle
}

/// <summary>
/// Query for governance-first fund ledger views.
/// </summary>
public sealed record FundLedgerQuery(
    string FundProfileId,
    DateTimeOffset? AsOf = null,
    FundLedgerScope ScopeKind = FundLedgerScope.Consolidated,
    string? ScopeId = null);

/// <summary>
/// Trial-balance row for a fund ledger view.
/// </summary>
public sealed record FundTrialBalanceLine(
    string AccountName,
    string AccountType,
    string? Symbol,
    string? FinancialAccountId,
    decimal Balance,
    int EntryCount);

/// <summary>
/// Journal row for a fund ledger view.
/// </summary>
public sealed record FundJournalLine(
    Guid JournalEntryId,
    DateTimeOffset Timestamp,
    string Description,
    decimal TotalDebits,
    decimal TotalCredits,
    int LineCount,
    IReadOnlyList<string>? FinancialAccountIds = null);

/// <summary>
/// Aggregated ledger totals for a ledger scope or slice.
/// </summary>
public sealed record FundLedgerTotalsDto(
    int JournalEntryCount,
    int LedgerEntryCount,
    decimal AssetBalance,
    decimal LiabilityBalance,
    decimal EquityBalance,
    decimal RevenueBalance,
    decimal ExpenseBalance);

/// <summary>
/// Slice-level ledger projection that supports governance drill-in by scope/group.
/// </summary>
public sealed record FundLedgerSliceDto(
    string SliceKey,
    FundLedgerScope ScopeKind,
    string? ScopeId,
    string DisplayName,
    FundLedgerTotalsDto Totals,
    IReadOnlyList<FundTrialBalanceLine> TrialBalance,
    IReadOnlyList<FundJournalLine> Journal,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>
/// Governance-facing fund ledger summary.
/// </summary>
public sealed record FundLedgerSummary(
    string FundProfileId,
    string FundDisplayName,
    FundLedgerScope ScopeKind,
    string? ScopeId,
    DateTimeOffset AsOf,
    int JournalEntryCount,
    int LedgerEntryCount,
    decimal AssetBalance,
    decimal LiabilityBalance,
    decimal EquityBalance,
    decimal RevenueBalance,
    decimal ExpenseBalance,
    IReadOnlyList<FundTrialBalanceLine> TrialBalance,
    IReadOnlyList<FundJournalLine> Journal,
    int EntityCount,
    int SleeveCount,
    int VehicleCount,
    FundLedgerTotalsDto? ConsolidatedTotals = null,
    IReadOnlyList<FundLedgerSliceDto>? LedgerSlices = null);

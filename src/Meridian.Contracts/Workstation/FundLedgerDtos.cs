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
/// <remarks>
/// Selection semantics:
/// <list type="bullet">
/// <item><description><c>SelectedLedgerIds</c> is null/empty: full fund consolidation for the requested scope.</description></item>
/// <item><description><c>SelectedLedgerIds</c> has values: consolidation constrained to those run/ledger IDs.</description></item>
/// <item><description>Unknown IDs produce an empty result set with no matching ledgers.</description></item>
/// </list>
/// </remarks>
public sealed record FundLedgerQuery(
    string FundProfileId,
    DateTimeOffset? AsOf = null,
    FundLedgerScope ScopeKind = FundLedgerScope.Consolidated,
    string? ScopeId = null,
    IReadOnlyList<string>? SelectedLedgerIds = null);

/// <summary>
/// Trial-balance row for a fund ledger view.
/// </summary>
public sealed record FundTrialBalanceLine(
    string AccountName,
    string AccountType,
    string? Symbol,
    string? FinancialAccountId,
    decimal Balance,
    int EntryCount,
    WorkstationSecurityReference? Security = null);

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
/// Slice-level ledger projection that supports governance drill-in by logical ledger key.
/// </summary>
public sealed record FundLedgerSliceDto(
    string SliceKey,
    string LedgerKey,
    string LedgerGroupId,
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

/// <summary>
/// Balance row captured in a reconciliation snapshot.
/// </summary>
public sealed record FundLedgerSnapshotBalanceLine(
    string AccountName,
    string AccountType,
    string? Symbol,
    string? FinancialAccountId,
    decimal Balance,
    WorkstationSecurityReference? Security = null);

/// <summary>
/// Point-in-time ledger snapshot used by reconciliation views.
/// </summary>
public sealed record FundLedgerDimensionSnapshot(
    DateTimeOffset Timestamp,
    int JournalEntryCount,
    int LedgerEntryCount,
    IReadOnlyList<FundLedgerSnapshotBalanceLine> Balances);

/// <summary>
/// Reconciliation snapshot of one fund ledger book, including consolidated totals and per-dimension breakdowns.
/// </summary>
public sealed record FundLedgerReconciliationSnapshot(
    string FundProfileId,
    DateTimeOffset AsOf,
    FundLedgerDimensionSnapshot Consolidated,
    IReadOnlyDictionary<string, FundLedgerDimensionSnapshot> Entities,
    IReadOnlyDictionary<string, FundLedgerDimensionSnapshot> Sleeves,
    IReadOnlyDictionary<string, FundLedgerDimensionSnapshot> Vehicles);

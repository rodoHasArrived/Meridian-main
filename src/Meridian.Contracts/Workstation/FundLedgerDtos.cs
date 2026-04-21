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
    int VehicleCount);

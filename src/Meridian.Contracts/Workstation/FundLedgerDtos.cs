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
/// <item><description>Unknown IDs produce an empty result set (no matching ledgers).</description></item>
/// </list>
/// </remarks>
public sealed record FundLedgerQuery(
    string FundProfileId,
    DateTimeOffset? AsOf = null,
    FundLedgerScope ScopeKind = FundLedgerScope.Consolidated,
    string? ScopeId = null)
{
    public IReadOnlyList<string>? SelectedLedgerIds { get; init; }

    public FundLedgerQuery(
        string FundProfileId,
        DateTimeOffset? AsOf,
        FundLedgerScope ScopeKind,
        string? ScopeId,
        IReadOnlyList<string>? SelectedLedgerIds)
        : this(FundProfileId, AsOf, ScopeKind, ScopeId)
    {
        this.SelectedLedgerIds = SelectedLedgerIds;
    }
}
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

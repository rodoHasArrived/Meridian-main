using Meridian.Contracts.FundStructure;

namespace Meridian.Contracts.Workstation;

/// <summary>
/// Tab targets within the governance-first fund operations workspace.
/// </summary>
public enum FundOperationsTab : byte
{
    Overview = 0,
    Accounts = 1,
    Banking = 2,
    Portfolio = 3,
    CashFinancing = 4,
    Journal = 5,
    TrialBalance = 6,
    Reconciliation = 7,
    AuditTrail = 8,
    ReportPack = 9
}

/// <summary>
/// Normalized navigation context that allows fund operations surfaces to open from a fund,
/// account, or run entry point without duplicating page implementations.
/// </summary>
public sealed record FundOperationsNavigationContext(
    FundOperationsTab Tab = FundOperationsTab.Overview,
    string? FundProfileId = null,
    Guid? AccountId = null,
    string? RunId = null);

/// <summary>
/// Summary cards shown at the top of the governance fund operations workspace.
/// </summary>
public sealed record FundWorkspaceSummary(
    string FundProfileId,
    string FundDisplayName,
    string BaseCurrency,
    DateTimeOffset AsOf,
    int TotalAccounts,
    int BankAccountCount,
    int BrokerageAccountCount,
    int CustodyAccountCount,
    decimal TotalCash,
    decimal GrossExposure,
    decimal NetExposure,
    decimal TotalEquity,
    decimal FinancingCost,
    decimal PendingSettlement,
    int OpenReconciliationBreaks,
    int ReconciliationRuns,
    int JournalEntryCount,
    int TrialBalanceLineCount,
    int SecurityResolvedCount = 0,
    int SecurityMissingCount = 0,
    int SecurityCoverageIssues = 0);

/// <summary>
/// Account-first row used by governance banking and account tabs.
/// </summary>
public sealed record FundAccountSummary(
    Guid AccountId,
    AccountTypeDto AccountType,
    string AccountCode,
    string DisplayName,
    string BaseCurrency,
    string? Institution,
    bool IsActive,
    decimal CashBalance,
    decimal SecuritiesMarketValue,
    decimal NetAssetValue,
    DateOnly? LastSnapshotDate,
    int ReconciliationRuns,
    int OpenBreaks,
    string? PortfolioId,
    string? LedgerReference,
    string? BankName,
    string? AccountNumberMasked,
    Guid? EntityId = null,
    Guid? SleeveId = null,
    Guid? VehicleId = null,
    string? StrategyId = null,
    string? RunId = null,
    string StructureLabel = "Unassigned",
    string WorkflowLabel = "Manual");

/// <summary>
/// Banking-focused projection for statement and balance review tabs.
/// </summary>
public sealed record BankAccountSnapshot(
    Guid AccountId,
    string DisplayName,
    string AccountCode,
    string BankName,
    string Currency,
    decimal CurrentBalance,
    decimal PendingSettlement,
    DateOnly? LastStatementDate,
    int StatementLineCount,
    string? LatestTransactionType,
    decimal? LatestTransactionAmount);

/// <summary>
/// Fund-level position row aggregated across strategy-run portfolio summaries.
/// </summary>
public sealed record FundPortfolioPosition(
    string Symbol,
    long NetQuantity,
    decimal WeightedAverageCostBasis,
    decimal RealizedPnl,
    decimal UnrealizedPnl,
    int ContributingRuns,
    int LinkedAccounts,
    Guid? SecurityId = null,
    string? SecurityDisplayName = null,
    string? AssetClass = null,
    string? SecuritySubType = null,
    string? PrimaryIdentifier = null,
    bool HasSecurityCoverage = false,
    string CoverageLabel = "Unresolved",
    IReadOnlyList<string>? ContributingRunIds = null,
    int SecurityResolvedContributions = 0,
    int SecurityMissingContributions = 0);

/// <summary>
/// Shared cash and financing rollup for governance and trading capital-control surfaces.
/// </summary>
public sealed record CashFinancingSummary(
    string Currency,
    decimal TotalCash,
    decimal PendingSettlement,
    decimal FinancingCost,
    decimal MarginBalance,
    decimal RealizedPnl,
    decimal UnrealizedPnl,
    decimal LongMarketValue,
    decimal ShortMarketValue,
    decimal GrossExposure,
    decimal NetExposure,
    decimal TotalEquity,
    IReadOnlyList<string> Highlights);

/// <summary>
/// Reconciliation row shown in governance operator queues.
/// </summary>
public sealed record FundReconciliationItem(
    Guid ReconciliationRunId,
    Guid AccountId,
    string AccountDisplayName,
    DateOnly AsOfDate,
    string Status,
    int TotalChecks,
    int TotalMatched,
    int TotalBreaks,
    decimal BreakAmountTotal,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt,
    string ScopeLabel = "Account",
    string? StrategyName = null,
    string? RunId = null,
    int SecurityIssueCount = 0,
    bool HasSecurityCoverageIssues = false,
    string CoverageLabel = "n/a");

/// <summary>
/// Summary of recent reconciliation posture for a fund.
/// </summary>
public sealed record ReconciliationSummary(
    int RunCount,
    int OpenBreakCount,
    decimal BreakAmountTotal,
    IReadOnlyList<FundReconciliationItem> RecentRuns,
    int SecurityCoverageIssueCount = 0);

/// <summary>
/// Lightweight audit row combining journal and reconciliation activity.
/// </summary>
public sealed record FundAuditEntry(
    DateTimeOffset Timestamp,
    string Category,
    string Description,
    string Reference);

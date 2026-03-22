namespace Meridian.Contracts.Workstation;

/// <summary>
/// Shared workstation-facing mode for a strategy run.
/// </summary>
public enum StrategyRunMode : byte
{
    Backtest,
    Paper,
    Live
}

/// <summary>
/// Shared workstation-facing execution engine for a strategy run.
/// </summary>
public enum StrategyRunEngine : byte
{
    Unknown,
    MeridianNative,
    Lean,
    BrokerPaper,
    BrokerLive
}

/// <summary>
/// Normalized status used by workstation surfaces across backtest, paper, and live runs.
/// </summary>
public enum StrategyRunStatus : byte
{
    Pending,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled,
    Stopped
}

/// <summary>
/// Promotion-oriented readiness signal derived from the current run state.
/// </summary>
public enum StrategyRunPromotionState : byte
{
    None,
    RequiresCompletion,
    CandidateForPaper,
    CandidateForLive,
    LiveManaged
}

/// <summary>
/// Shared execution summary used by workstation drill-ins and governance surfaces.
/// </summary>
public sealed record StrategyRunExecutionSummary(
    int FillCount,
    int TotalTrades,
    int WinningTrades,
    int LosingTrades,
    decimal TotalCommissions,
    decimal TotalMarginInterest,
    decimal TotalShortRebates,
    bool HasPortfolio,
    bool HasLedger,
    bool HasAuditTrail,
    string? AuditReference);

/// <summary>
/// Shared promotion summary used by research, trading, and governance workflows.
/// </summary>
public sealed record StrategyRunPromotionSummary(
    StrategyRunPromotionState State,
    StrategyRunMode? SuggestedNextMode,
    bool RequiresReview,
    string Reason);

/// <summary>
/// Shared governance summary used by audit and control surfaces.
/// </summary>
public sealed record StrategyRunGovernanceSummary(
    DateTimeOffset LastUpdatedAt,
    bool HasParameters,
    bool HasPortfolio,
    bool HasLedger,
    bool HasAuditTrail,
    string? AuditReference,
    string? DatasetReference,
    string? FeedReference);

/// <summary>
/// Summary row shown in a run browser or recent-run list.
/// </summary>
public sealed record StrategyRunSummary(
    string RunId,
    string StrategyId,
    string StrategyName,
    StrategyRunMode Mode,
    StrategyRunEngine Engine,
    StrategyRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? DatasetReference,
    string? FeedReference,
    string? PortfolioId,
    string? LedgerReference,
    decimal? NetPnl,
    decimal? TotalReturn,
    decimal? FinalEquity,
    int FillCount,
    DateTimeOffset LastUpdatedAt,
    string? AuditReference = null,
    StrategyRunExecutionSummary? Execution = null,
    StrategyRunPromotionSummary? Promotion = null,
    StrategyRunGovernanceSummary? Governance = null);

/// <summary>
/// Expanded detail for a single run, including derived portfolio and ledger views.
/// </summary>
public sealed record StrategyRunDetail(
    StrategyRunSummary Summary,
    IReadOnlyDictionary<string, string> Parameters,
    PortfolioSummary? Portfolio,
    LedgerSummary? Ledger,
    StrategyRunExecutionSummary? Execution = null,
    StrategyRunPromotionSummary? Promotion = null,
    StrategyRunGovernanceSummary? Governance = null);

/// <summary>
/// Shared portfolio rollup for workstation research and trading surfaces.
/// </summary>
public sealed record PortfolioSummary(
    string PortfolioId,
    string RunId,
    DateTimeOffset AsOf,
    decimal Cash,
    decimal LongMarketValue,
    decimal ShortMarketValue,
    decimal GrossExposure,
    decimal NetExposure,
    decimal TotalEquity,
    decimal RealizedPnl,
    decimal UnrealizedPnl,
    decimal Commissions,
    decimal Financing,
    IReadOnlyList<PortfolioPositionSummary> Positions);

/// <summary>
/// Shared position row for workstation portfolio views.
/// </summary>
public sealed record PortfolioPositionSummary(
    string Symbol,
    long Quantity,
    decimal AverageCostBasis,
    decimal RealizedPnl,
    decimal UnrealizedPnl,
    bool IsShort);

/// <summary>
/// Shared ledger rollup for workstation governance and audit surfaces.
/// </summary>
public sealed record LedgerSummary(
    string LedgerReference,
    string RunId,
    DateTimeOffset AsOf,
    int JournalEntryCount,
    int LedgerEntryCount,
    decimal AssetBalance,
    decimal LiabilityBalance,
    decimal EquityBalance,
    decimal RevenueBalance,
    decimal ExpenseBalance,
    IReadOnlyList<LedgerTrialBalanceLine> TrialBalance,
    IReadOnlyList<LedgerJournalLine> Journal);

/// <summary>
/// Shared trial-balance row for workstation ledger views.
/// </summary>
public sealed record LedgerTrialBalanceLine(
    string AccountName,
    string AccountType,
    string? Symbol,
    string? FinancialAccountId,
    decimal Balance,
    int EntryCount);

/// <summary>
/// Shared journal row for workstation audit surfaces.
/// </summary>
public sealed record LedgerJournalLine(
    Guid JournalEntryId,
    DateTimeOffset Timestamp,
    string Description,
    decimal TotalDebits,
    decimal TotalCredits,
    int LineCount);

/// <summary>
/// Comparison row used when reviewing multiple runs side by side.
/// </summary>
public sealed record StrategyRunComparison(
    string RunId,
    string StrategyName,
    StrategyRunMode Mode,
    StrategyRunEngine Engine,
    StrategyRunStatus Status,
    decimal? NetPnl,
    decimal? TotalReturn,
    decimal? FinalEquity,
    decimal? MaxDrawdown,
    double? SharpeRatio,
    int FillCount,
    DateTimeOffset LastUpdatedAt,
    StrategyRunPromotionState PromotionState = StrategyRunPromotionState.None,
    bool HasLedger = false,
    bool HasAuditTrail = false);

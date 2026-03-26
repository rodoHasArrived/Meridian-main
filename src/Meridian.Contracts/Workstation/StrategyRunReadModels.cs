using Meridian.Contracts.SecurityMaster;

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
/// Lightweight Security Master reference used by workstation portfolio and ledger surfaces.
/// <para><see cref="SubType"/> is the most specific classification available at query time
/// (e.g. "CommonShare", "Bond", "OptionContract"). It is derived from the security's asset
/// class and is null when the asset class does not map to a unique sub-type.</para>
/// </summary>
public sealed record WorkstationSecurityReference(
    Guid SecurityId,
    string DisplayName,
    string AssetClass,
    string Currency,
    SecurityStatusDto Status,
    string? PrimaryIdentifier,
    string? SubType = null);

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
    IReadOnlyList<PortfolioPositionSummary> Positions,
    int SecurityResolvedCount = 0,
    int SecurityMissingCount = 0);

/// <summary>
/// Shared position row for workstation portfolio views.
/// </summary>
public sealed record PortfolioPositionSummary(
    string Symbol,
    long Quantity,
    decimal AverageCostBasis,
    decimal RealizedPnl,
    decimal UnrealizedPnl,
    bool IsShort,
    WorkstationSecurityReference? Security = null);

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
    IReadOnlyList<LedgerJournalLine> Journal,
    int SecurityResolvedCount = 0,
    int SecurityMissingCount = 0);

/// <summary>
/// Shared trial-balance row for workstation ledger views.
/// </summary>
public sealed record LedgerTrialBalanceLine(
    string AccountName,
    string AccountType,
    string? Symbol,
    string? FinancialAccountId,
    decimal Balance,
    int EntryCount,
    WorkstationSecurityReference? Security = null);

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

// ---------------------------------------------------------------------------
// Track C drill-in models
// ---------------------------------------------------------------------------

/// <summary>A single point on the portfolio equity curve.</summary>
public sealed record EquityCurvePoint(
    DateOnly Date,
    decimal TotalEquity,
    decimal Cash,
    decimal DailyReturn,
    decimal DrawdownFromPeak,
    decimal DrawdownFromPeakPercent);

/// <summary>Full equity curve with summary drawdown statistics for one run.</summary>
public sealed record EquityCurveSummary(
    string RunId,
    decimal InitialEquity,
    decimal FinalEquity,
    decimal MaxDrawdown,
    decimal MaxDrawdownPercent,
    int MaxDrawdownRecoveryDays,
    double SharpeRatio,
    double SortinoRatio,
    IReadOnlyList<EquityCurvePoint> Points);

/// <summary>A single executed fill from a strategy run.</summary>
public sealed record RunFillEntry(
    Guid FillId,
    Guid OrderId,
    string Symbol,
    long FilledQuantity,
    decimal FillPrice,
    decimal Commission,
    DateTimeOffset FilledAt,
    string? AccountId);

/// <summary>Trade-level fill list for one run.</summary>
public sealed record RunFillSummary(
    string RunId,
    int TotalFills,
    decimal TotalCommissions,
    IReadOnlyList<RunFillEntry> Fills);

/// <summary>Per-symbol P&amp;L attribution for one run.</summary>
public sealed record SymbolAttributionEntry(
    string Symbol,
    decimal RealizedPnl,
    decimal UnrealizedPnl,
    decimal TotalPnl,
    int TradeCount,
    decimal Commissions,
    decimal MarginInterestAllocated);

/// <summary>Complete attribution breakdown for one run.</summary>
public sealed record RunAttributionSummary(
    string RunId,
    decimal TotalRealizedPnl,
    decimal TotalUnrealizedPnl,
    decimal TotalCommissions,
    IReadOnlyList<SymbolAttributionEntry> BySymbol);

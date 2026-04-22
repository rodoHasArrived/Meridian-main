using System.Text.Json.Serialization;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.Workstation;

/// <summary>
/// Shared workstation-facing mode for a strategy run.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StrategyRunMode>))]
public enum StrategyRunMode : byte
{
    Backtest,
    Paper,
    Live
}

/// <summary>
/// Shared workstation-facing execution engine for a strategy run.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StrategyRunEngine>))]
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
[JsonConverter(typeof(JsonStringEnumConverter<StrategyRunStatus>))]
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
[JsonConverter(typeof(JsonStringEnumConverter<StrategyRunPromotionState>))]
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
    StrategyRunGovernanceSummary? Governance = null,
    string? FundProfileId = null,
    string? FundDisplayName = null,
    string? ParentRunId = null);

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
/// Security Master coverage state associated with a workstation security reference.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<WorkstationSecurityCoverageStatus>))]
public enum WorkstationSecurityCoverageStatus : byte
{
    Resolved,
    Partial,
    Missing,
    Unavailable
}

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
    string? SubType = null,
    WorkstationSecurityCoverageStatus CoverageStatus = WorkstationSecurityCoverageStatus.Resolved,
    string? MatchedIdentifierKind = null,
    string? MatchedIdentifierValue = null,
    string? MatchedProvider = null,
    string? ResolutionReason = null,
    string? LookupPath = null,
    string? LookupSource = null,
    bool IsInferredMatch = false);

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

/// <summary>Filter options used for workstation run history retrieval.</summary>
public sealed record StrategyRunHistoryQuery(
    IReadOnlyList<StrategyRunMode>? Modes = null,
    StrategyRunStatus? Status = null,
    string? StrategyId = null,
    int Limit = 50);

/// <summary>Merged run timeline entry used by research and trading surfaces.</summary>
public sealed record StrategyRunTimelineEntry(
    string RunId,
    string StrategyId,
    string StrategyName,
    StrategyRunMode Mode,
    StrategyRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset LastUpdatedAt,
    decimal? NetPnl,
    decimal? TotalReturn,
    int FillCount);
/// <summary>
/// Normalized cross-mode run comparison DTO that includes the full set of
/// <c>BacktestMetrics</c> fields plus equity curve data and parentage chain info.
/// Returned by <c>GET /api/strategies/runs/compare?ids=a,b</c>.
/// </summary>
public sealed record RunComparisonDto(
    string RunId,
    string? ParentRunId,
    string StrategyName,
    StrategyRunMode Mode,
    StrategyRunEngine Engine,
    StrategyRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    decimal? NetPnl,
    decimal? TotalReturn,
    decimal? AnnualizedReturn,
    decimal? FinalEquity,
    double? SharpeRatio,
    double? SortinoRatio,
    double? CalmarRatio,
    decimal? MaxDrawdown,
    decimal? MaxDrawdownPercent,
    int MaxDrawdownRecoveryDays,
    double? ProfitFactor,
    double? WinRate,
    int TotalTrades,
    int WinningTrades,
    int LosingTrades,
    int FillCount,
    decimal TotalCommissions,
    decimal TotalMarginInterest,
    decimal TotalShortRebates,
    double? Xirr,
    EquityCurveSummary? EquityCurve,
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
    StrategyRunMode Mode,
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
    StrategyRunMode Mode,
    decimal TotalRealizedPnl,
    decimal TotalUnrealizedPnl,
    decimal TotalCommissions,
    IReadOnlyList<SymbolAttributionEntry> BySymbol);

// ---------------------------------------------------------------------------
// Cash-flow projection models
// ---------------------------------------------------------------------------

/// <summary>A single historical cash-flow entry from a strategy run.</summary>
public sealed record CashFlowEntryDto(
    DateTimeOffset Timestamp,
    decimal Amount,
    string EventKind,
    string? Symbol,
    string Currency,
    string? AccountId,
    string? Description);

/// <summary>A single time-bucket within a projected cash ladder.</summary>
public sealed record CashLadderBucketDto(
    DateTimeOffset BucketStart,
    DateTimeOffset BucketEnd,
    decimal ProjectedInflows,
    decimal ProjectedOutflows,
    decimal NetFlow,
    string Currency,
    int EventCount);

/// <summary>Time-bucketed forward view of projected cash flows for one run.</summary>
public sealed record RunCashLadder(
    DateTimeOffset AsOf,
    string Currency,
    int BucketDays,
    decimal TotalProjectedInflows,
    decimal TotalProjectedOutflows,
    decimal NetPosition,
    IReadOnlyList<CashLadderBucketDto> Buckets);

/// <summary>
/// Cash-flow projection summary for a strategy run.
/// <para>Includes both the raw historical entries and a time-bucketed cash ladder
/// computed by the F# <c>CashLadder.build</c> module.</para>
/// </summary>
public sealed record RunCashFlowSummary(
    string RunId,
    DateTimeOffset AsOf,
    string Currency,
    int TotalEntries,
    decimal TotalInflows,
    decimal TotalOutflows,
    decimal NetCashFlow,
    IReadOnlyList<CashFlowEntryDto> Entries,
    RunCashLadder Ladder);

/// <summary>
/// Lightweight run identity used to connect research, trading, and governance flows.
/// </summary>
public sealed record StrategyRunContinuityLink(
    string RunId,
    string StrategyId,
    string StrategyName,
    StrategyRunMode Mode,
    StrategyRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    StrategyRunPromotionState PromotionState,
    string? FundProfileId = null,
    string? FundDisplayName = null);

/// <summary>
/// Parent/child run linkage used by shared continuity drill-ins.
/// </summary>
public sealed record StrategyRunContinuityLineage(
    string? ParentRunId,
    StrategyRunContinuityLink? ParentRun,
    IReadOnlyList<StrategyRunContinuityLink> ChildRuns);

/// <summary>
/// Compact cash-flow digest attached to a continuity drill-in.
/// </summary>
public sealed record StrategyRunCashFlowDigest(
    DateTimeOffset AsOf,
    string Currency,
    int TotalEntries,
    decimal TotalInflows,
    decimal TotalOutflows,
    decimal NetCashFlow,
    decimal ProjectedNetPosition,
    int BucketCount,
    DateTimeOffset? NextBucketStart = null,
    DateTimeOffset? NextBucketEnd = null,
    decimal? NextBucketNetFlow = null);

/// <summary>
/// Machine-readable continuity warning for shared run-centered workflows.
/// </summary>
public sealed record StrategyRunContinuityWarning(
    string Code,
    string Message);

/// <summary>
/// Continuity posture across run, portfolio, ledger, cash-flow, and reconciliation seams.
/// </summary>
public sealed record StrategyRunContinuityStatus(
    bool HasPortfolio,
    bool HasLedger,
    bool HasCashFlow,
    bool HasReconciliation,
    int AsOfDriftMinutes,
    int OpenReconciliationBreaks,
    int SecurityCoverageIssueCount,
    bool HasWarnings,
    IReadOnlyList<StrategyRunContinuityWarning> Warnings);

/// <summary>
/// Shared continuity drill-in that bundles the run-centered seams used across workspaces.
/// </summary>
public sealed record StrategyRunContinuityDetail(
    StrategyRunDetail Run,
    StrategyRunContinuityLineage Lineage,
    StrategyRunCashFlowDigest? CashFlow,
    ReconciliationRunSummary? Reconciliation,
    StrategyRunContinuityStatus ContinuityStatus);

// ---------------------------------------------------------------------------
// Lot-level tracking read models
// ---------------------------------------------------------------------------

/// <summary>
/// Workstation-facing summary of a single open lot for a strategy run.
/// </summary>
public sealed record OpenLotSummary(
    Guid LotId,
    string Symbol,
    long Quantity,
    decimal EntryPrice,
    DateTimeOffset OpenedAt,
    decimal CurrentUnrealizedPnl,
    bool IsLongTerm,
    string? AccountId = null);

/// <summary>
/// Workstation-facing summary of a closed lot for a strategy run.
/// </summary>
public sealed record ClosedLotSummary(
    Guid LotId,
    string Symbol,
    long Quantity,
    decimal EntryPrice,
    decimal ClosePrice,
    DateTimeOffset OpenedAt,
    DateTimeOffset ClosedAt,
    decimal RealizedPnl,
    bool IsLongTerm,
    string? AccountId = null);

/// <summary>
/// Lot history for a single strategy run, optionally filtered to one symbol.
/// </summary>
public sealed record RunLotSummary(
    string RunId,
    int TotalOpenLots,
    int TotalClosedLots,
    decimal TotalRealizedPnl,
    IReadOnlyList<OpenLotSummary> OpenLots,
    IReadOnlyList<ClosedLotSummary> ClosedLots);

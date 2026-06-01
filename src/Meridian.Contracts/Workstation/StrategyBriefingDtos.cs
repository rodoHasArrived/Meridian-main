namespace Meridian.Contracts.Workstation;

/// <summary>
/// Drill-in links attached to strategy run cards and comparisons.
/// </summary>
public sealed record StrategyRunDrillInLinks(
    string EquityCurve,
    string Fills,
    string Attribution,
    string? Ledger,
    string CashFlows,
    string? Continuity);

/// <summary>
/// Strategy run card shown in the briefing and recent-run rail.
/// </summary>
public sealed record StrategyBriefingRun(
    string RunId,
    string StrategyName,
    StrategyRunMode Mode,
    StrategyRunStatus Status,
    string Dataset,
    string WindowLabel,
    string ReturnLabel,
    string SharpeLabel,
    string LastUpdatedLabel,
    string Notes,
    StrategyRunPromotionState? PromotionState,
    decimal? NetPnl,
    decimal? TotalReturn,
    decimal? FinalEquity,
    StrategyRunDrillInLinks DrillIn);

/// <summary>
/// Single mode participating in a saved strategy comparison package.
/// </summary>
public sealed record StrategySavedComparisonMode(
    string RunId,
    StrategyRunMode Mode,
    StrategyRunStatus Status,
    decimal? NetPnl,
    decimal? TotalReturn,
    StrategyRunDrillInLinks DrillIn);

/// <summary>
/// Saved strategy comparison summary surfaced from the briefing home.
/// </summary>
public sealed record StrategySavedComparison(
    string ComparisonId,
    string StrategyName,
    string ModeSummary,
    string Summary,
    string? AnchorRunId,
    IReadOnlyList<StrategySavedComparisonMode> Modes);

/// <summary>
/// Actionable alert shown on the strategy briefing surface.
/// </summary>
public sealed record StrategyBriefingAlert(
    string AlertId,
    string Title,
    string Summary,
    string Tone,
    string? RunId = null,
    string? ActionLabel = null);

/// <summary>
/// "What changed" entry summarizing recent run or workflow movement.
/// </summary>
public sealed record StrategyWhatChangedItem(
    string ChangeId,
    string Title,
    string Summary,
    string Category,
    DateTimeOffset Timestamp,
    string RelativeTime,
    string? RunId = null);

/// <summary>
/// Workspace-level summary used to drive the Strategy shell header.
/// </summary>
public sealed record StrategyBriefingWorkspaceSummary(
    int TotalRuns,
    int ActiveRuns,
    int PromotionCandidates,
    int PositivePnlRuns,
    string? LatestRunId,
    string? LatestStrategyName,
    bool HasLedgerCoverage,
    bool HasPortfolioCoverage,
    string Summary);

/// <summary>
/// Typed Strategy briefing payload shared by workstation surfaces.
/// </summary>
public sealed record StrategyBriefingDto(
    StrategyBriefingWorkspaceSummary Workspace,
    InsightFeed InsightFeed,
    IReadOnlyList<WorkstationWatchlist> Watchlists,
    IReadOnlyList<StrategyBriefingRun> RecentRuns,
    IReadOnlyList<StrategySavedComparison> SavedComparisons,
    IReadOnlyList<StrategyBriefingAlert> Alerts,
    IReadOnlyList<StrategyWhatChangedItem> WhatChanged);

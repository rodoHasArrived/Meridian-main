namespace Meridian.Contracts.Workstation;

/// <summary>
/// Shared insight tile used by Market Briefing style surfaces.
/// </summary>
public sealed record InsightWidget(
    string WidgetId,
    string Title,
    string Subtitle,
    string Headline,
    string Tone,
    string Summary,
    string? RunId = null,
    string? DrillInRoute = null);

/// <summary>
/// Personalized insight feed shown at the top of the Research workspace.
/// </summary>
public sealed record InsightFeed(
    string FeedId,
    string Title,
    string Summary,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<InsightWidget> Widgets);

/// <summary>
/// Shared workstation watchlist summary.
/// </summary>
public sealed record WorkstationWatchlist(
    string WatchlistId,
    string Name,
    IReadOnlyList<string> Symbols,
    int SymbolCount,
    bool IsPinned,
    int SortOrder,
    string? AccentColor = null,
    string? Summary = null);

/// <summary>
/// Drill-in links attached to research run cards and comparisons.
/// </summary>
public sealed record ResearchRunDrillInLinks(
    string EquityCurve,
    string Fills,
    string Attribution,
    string? Ledger,
    string CashFlows,
    string? Continuity);

/// <summary>
/// Research run card shown in the briefing and recent-run rail.
/// </summary>
public sealed record ResearchBriefingRun(
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
    ResearchRunDrillInLinks DrillIn);

/// <summary>
/// Single mode participating in a saved comparison package.
/// </summary>
public sealed record ResearchSavedComparisonMode(
    string RunId,
    StrategyRunMode Mode,
    StrategyRunStatus Status,
    decimal? NetPnl,
    decimal? TotalReturn,
    ResearchRunDrillInLinks DrillIn);

/// <summary>
/// Saved comparison summary surfaced from the briefing home.
/// </summary>
public sealed record ResearchSavedComparison(
    string ComparisonId,
    string StrategyName,
    string ModeSummary,
    string Summary,
    string? AnchorRunId,
    IReadOnlyList<ResearchSavedComparisonMode> Modes);

/// <summary>
/// Actionable alert shown on the research briefing surface.
/// </summary>
public sealed record ResearchBriefingAlert(
    string AlertId,
    string Title,
    string Summary,
    string Tone,
    string? RunId = null,
    string? ActionLabel = null);

/// <summary>
/// "What changed" entry summarizing recent run or workflow movement.
/// </summary>
public sealed record ResearchWhatChangedItem(
    string ChangeId,
    string Title,
    string Summary,
    string Category,
    DateTimeOffset Timestamp,
    string RelativeTime,
    string? RunId = null);

/// <summary>
/// Workspace-level summary used to drive the Research shell header.
/// </summary>
public sealed record ResearchBriefingWorkspaceSummary(
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
/// Typed Research workspace briefing payload shared across workstation surfaces.
/// </summary>
public sealed record ResearchBriefingDto(
    ResearchBriefingWorkspaceSummary Workspace,
    InsightFeed InsightFeed,
    IReadOnlyList<WorkstationWatchlist> Watchlists,
    IReadOnlyList<ResearchBriefingRun> RecentRuns,
    IReadOnlyList<ResearchSavedComparison> SavedComparisons,
    IReadOnlyList<ResearchBriefingAlert> Alerts,
    IReadOnlyList<ResearchWhatChangedItem> WhatChanged);

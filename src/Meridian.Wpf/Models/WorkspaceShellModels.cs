using System.Windows.Media;

namespace Meridian.Wpf.Models;

// ── Trading Workspace ───────────────────────────────────────────────────────

/// <summary>
/// Display item for a single active position shown in the Trading workspace shell.
/// </summary>
public sealed class TradingActivePositionItem
{
    public string Symbol { get; set; } = string.Empty;
    public string StrategyName { get; set; } = string.Empty;
    public string QuantityLabel { get; set; } = string.Empty;
    public string UnrealizedPnlFormatted { get; set; } = string.Empty;
    public Brush UnrealizedPnlBrush { get; set; } = Brushes.Transparent;
    public string RealizedPnlFormatted { get; set; } = string.Empty;
    public Brush RealizedPnlBrush { get; set; } = Brushes.Transparent;
    public Brush ModeBadgeBackground { get; set; } = Brushes.Transparent;
    public string ModeLabel { get; set; } = string.Empty;
}

/// <summary>
/// Aggregated summary for the Trading workspace landing page.
/// </summary>
public sealed class TradingWorkspaceSummary
{
    public int PaperRunCount { get; set; }
    public int LiveRunCount { get; set; }
    public string TotalEquityFormatted { get; set; } = "—";
    public string MaxDrawdownFormatted { get; set; } = "—";
    public string PositionLimitLabel { get; set; } = "—";
    public string OrderRateLabel { get; set; } = "—";
    public IReadOnlyList<TradingActivePositionItem> ActivePositions { get; set; } = [];
}

// ── Research Workspace ──────────────────────────────────────────────────────

/// <summary>
/// Display item for a recent strategy run shown in the Research workspace shell.
/// </summary>
public sealed class ResearchRunSummaryItem
{
    public string RunId { get; set; } = string.Empty;
    public string StrategyName { get; set; } = string.Empty;
    public string NetPnlFormatted { get; set; } = string.Empty;
    public Brush NetPnlBrush { get; set; } = Brushes.Transparent;
    public string TotalReturnFormatted { get; set; } = string.Empty;
    public Brush StatusBadgeBackground { get; set; } = Brushes.Transparent;
    public string StatusLabel { get; set; } = string.Empty;
}

/// <summary>
/// Display item for a strategy run that is a candidate for paper or live promotion.
/// </summary>
public sealed class ResearchPromotionCandidateItem
{
    public string RunId { get; set; } = string.Empty;
    public string StrategyName { get; set; } = string.Empty;
    public string PromotionReason { get; set; } = string.Empty;
    public string NextModeLabel { get; set; } = string.Empty;
}

/// <summary>
/// Aggregated summary for the Research workspace landing page.
/// </summary>
public sealed class ResearchWorkspaceSummary
{
    public int TotalRuns { get; set; }
    public int PromotedCount { get; set; }
    public int PendingReviewCount { get; set; }
    public IReadOnlyList<ResearchRunSummaryItem> RecentRuns { get; set; } = [];
    public IReadOnlyList<ResearchPromotionCandidateItem> PromotionCandidates { get; set; } = [];
}

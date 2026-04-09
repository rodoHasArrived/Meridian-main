using System.Windows.Media;
using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.Models;

/// <summary>
/// Shared selected-run context that can flow between research and trading workspaces.
/// </summary>
public sealed class ActiveRunContext
{
    public string RunId { get; set; } = string.Empty;
    public string StrategyId { get; set; } = string.Empty;
    public string StrategyName { get; set; } = string.Empty;
    public string ModeLabel { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public string FundScopeLabel { get; set; } = "Global";
    public bool CanPromoteToPaper { get; set; }
    public string PromotionLabel { get; set; } = "Review Promotion";
    public string TradingHandoffLabel { get; set; } = "Open in Trading Cockpit";
    public string PortfolioPreview { get; set; } = "No portfolio preview available.";
    public string LedgerPreview { get; set; } = "No ledger preview available.";
    public string RiskSummary { get; set; } = "No active risk posture.";
    public PortfolioSummary? Portfolio { get; set; }
    public LedgerSummary? Ledger { get; set; }
}

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
    public ActiveRunContext? ActiveRunContext { get; set; }
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
    public ActiveRunContext? ActiveRunContext { get; set; }
}

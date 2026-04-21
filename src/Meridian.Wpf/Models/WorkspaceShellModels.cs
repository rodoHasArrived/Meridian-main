using System.Windows.Media;

namespace Meridian.Wpf.Models;

// ── Trading Workspace ───────────────────────────────────────────────────────

/// <summary>
/// Visual tone for promotion, audit, and validation status shown in the Trading shell.
/// </summary>
public enum TradingWorkspaceStatusTone : byte
{
    Info,
    Success,
    Warning
}

/// <summary>
/// Compact status item used by the Trading workspace promotion and control card.
/// </summary>
public sealed class TradingWorkspaceStatusItem
{
    public string Label { get; set; } = "Status unavailable";
    public string Detail { get; set; } = "Status detail unavailable.";
    public TradingWorkspaceStatusTone Tone { get; set; } = TradingWorkspaceStatusTone.Info;
}

/// <summary>
/// Active run context shared by the workstation shell pages.
/// </summary>
public sealed class ActiveRunContext
{
    public string RunId { get; set; } = string.Empty;
    public string StrategyName { get; set; } = string.Empty;
    public string ModeLabel { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public string FundScopeLabel { get; set; } = "Global scope";
    public string? ParentRunId { get; set; }
    public string PortfolioPreview { get; set; } = "Portfolio preview unavailable.";
    public string LedgerPreview { get; set; } = "Ledger preview unavailable.";
    public string RiskSummary { get; set; } = "Risk summary unavailable.";
    public TradingWorkspaceStatusItem PromotionStatus { get; set; } = new();
    public TradingWorkspaceStatusItem AuditStatus { get; set; } = new();
    public TradingWorkspaceStatusItem ValidationStatus { get; set; } = new();
    public string PromotionStatusLabel => PromotionStatus.Label;
    public string PromotionStatusDetail => PromotionStatus.Detail;
    public string AuditStatusLabel => AuditStatus.Label;
    public string AuditStatusDetail => AuditStatus.Detail;
    public string ValidationStatusLabel => ValidationStatus.Label;
    public string ValidationStatusDetail => ValidationStatus.Detail;
    public bool CanPromoteToPaper { get; set; }
}

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
    public TradingWorkspaceStatusItem PromotionStatus { get; set; } = new();
    public TradingWorkspaceStatusItem AuditStatus { get; set; } = new();
    public TradingWorkspaceStatusItem ValidationStatus { get; set; } = new();
    public string PromotionStatusLabel => PromotionStatus.Label;
    public string PromotionStatusDetail => PromotionStatus.Detail;
    public string AuditStatusLabel => AuditStatus.Label;
    public string AuditStatusDetail => AuditStatus.Detail;
    public string ValidationStatusLabel => ValidationStatus.Label;
    public string ValidationStatusDetail => ValidationStatus.Detail;
    public ActiveRunContext? ActiveRunContext { get; set; }
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

using System.Windows;
using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.Models;

public enum ResearchDeskHeroTone : byte
{
    Info,
    Success,
    Warning
}

internal enum ResearchWorkspaceShellActionKind : byte
{
    None,
    Navigate,
    Dock,
    ResetLayout,
    OpenRunStudio
}

internal readonly record struct ResearchDeskHeroState(
    string FocusLabel,
    string Summary,
    string Detail,
    string BadgeText,
    ResearchDeskHeroTone BadgeTone,
    string HandoffTitle,
    string HandoffDetail,
    string PrimaryActionId,
    string PrimaryActionLabel,
    string SecondaryActionId,
    string SecondaryActionLabel,
    string TargetLabel);

internal readonly record struct ResearchWorkspaceShellActionRequest(
    string ActionId,
    ResearchWorkspaceShellActionKind Kind,
    string? PageTag,
    PaneDropAction Action,
    object? Parameter);

internal sealed class ResearchWorkspaceShellPresentationState
{
    public string TotalRunsText { get; init; } = "-";

    public string PromotedText { get; init; } = "-";

    public string PendingReviewText { get; init; } = "-";

    public string PromotionCountBadgeText { get; init; } = "0";

    public IReadOnlyList<ResearchRunSummaryItem> RecentRuns { get; init; } =
        Array.Empty<ResearchRunSummaryItem>();

    public IReadOnlyList<ResearchPromotionCandidateItem> PromotionCandidates { get; init; } =
        Array.Empty<ResearchPromotionCandidateItem>();

    public string ActiveRunNameText { get; init; } = "No selected run";

    public string ActiveRunMetaText { get; init; } =
        "Start a backtest or choose a run from history.";

    public string ScenarioStrategyText { get; init; } = "No strategy selected";

    public string ScenarioCoverageText { get; init; } = "No research session restored.";

    public string RunStatusText { get; init; } = "Awaiting run selection";

    public string RunPerformanceText { get; init; } =
        "Compare runs, equity, and fills from a selected strategy run.";

    public string RunCompareText { get; init; } =
        "Use the bottom history rail to select a run and load detail panels.";

    public string PortfolioPreviewText { get; init; } =
        "Portfolio inspector opens here once a run is selected.";

    public string LedgerPreviewText { get; init; } =
        "Accounting impact preview opens here once a run is selected.";

    public string RiskPreviewText { get; init; } =
        "Risk and audit preview becomes available after a completed run is selected.";

    public string BriefingSummaryText { get; init; } =
        "Pinned research context, watchlists, saved comparisons, and workflow alerts.";

    public string BriefingGeneratedText { get; init; } = "Updated just now";

    public IReadOnlyList<InsightWidget> BriefingInsights { get; init; } =
        Array.Empty<InsightWidget>();

    public IReadOnlyList<WorkstationWatchlist> BriefingWatchlists { get; init; } =
        Array.Empty<WorkstationWatchlist>();

    public IReadOnlyList<ResearchWhatChangedItem> BriefingWhatChanged { get; init; } =
        Array.Empty<ResearchWhatChangedItem>();

    public IReadOnlyList<ResearchBriefingAlert> BriefingAlerts { get; init; } =
        Array.Empty<ResearchBriefingAlert>();

    public IReadOnlyList<ResearchSavedComparison> BriefingComparisons { get; init; } =
        Array.Empty<ResearchSavedComparison>();

    public WorkspaceShellContext ShellContext { get; init; } = new();

    public WorkspaceCommandGroup CommandGroup { get; init; } = new();

    public WorkspaceWorkflowSummary Workflow { get; init; } =
        ResearchWorkspaceShellPresentationDefaults.Workflow;

    public ResearchDeskHeroState DeskHero { get; init; } =
        ResearchWorkspaceShellPresentationDefaults.DeskHero;

    public ActiveRunContext? ActiveRunContext { get; init; }
}

internal static class ResearchWorkspaceShellPresentationDefaults
{
    public static WorkspaceWorkflowSummary Workflow { get; } =
        new(
            WorkspaceId: "research",
            WorkspaceTitle: "Research",
            StatusLabel: "Ready for a new research cycle",
            StatusDetail: "No live workflow summary is available, so the shell is using deterministic fallback guidance.",
            StatusTone: "Info",
            NextAction: new WorkflowNextAction(
                Label: "Start Backtest",
                Detail: "Launch a new simulation from the research workspace.",
                TargetPageTag: "Backtest",
                Tone: "Primary"),
            PrimaryBlocker: new WorkflowBlockerSummary(
                Code: "fallback",
                Label: "Workflow summary unavailable",
                Detail: "Fallback guidance keeps one stable next action visible while shared workflow data refreshes.",
                Tone: "Info",
                IsBlocking: false),
            Evidence: Array.Empty<WorkflowEvidenceBadge>());

    public static ResearchDeskHeroState DeskHero { get; } =
        new(
            FocusLabel: "New cycle",
            Summary: "Research queue is empty.",
            Detail: "Start a backtest and stage a watchlist to seed comparisons, alerts, and the promotion pipeline.",
            BadgeText: "Setup",
            BadgeTone: ResearchDeskHeroTone.Info,
            HandoffTitle: "Launch the first run",
            HandoffDetail: "Use Backtest to record the first scenario, then keep symbols staged in Watchlists for follow-on analysis.",
            PrimaryActionId: "Backtest",
            PrimaryActionLabel: "Start Backtest",
            SecondaryActionId: "Watchlist",
            SecondaryActionLabel: "Open Watchlists",
            TargetLabel: "Target page: Backtest");
}

using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.Models;

internal readonly record struct TradingPortfolioNavigationTarget(
    string PageTag,
    PaneDropAction Action,
    string? RunId);

internal readonly record struct TradingStatusCardPresentation(
    string SummaryText,
    string BadgeText,
    TradingWorkspaceStatusTone BadgeTone,
    TradingWorkspaceStatusItem PromotionStatus,
    TradingWorkspaceStatusItem AuditStatus,
    TradingWorkspaceStatusItem ValidationStatus);

internal readonly record struct TradingDeskHeroState(
    string FocusLabel,
    string Summary,
    string Detail,
    string BadgeText,
    TradingWorkspaceStatusTone BadgeTone,
    string HandoffTitle,
    string HandoffDetail,
    string PrimaryActionId,
    string PrimaryActionLabel,
    string SecondaryActionId,
    string SecondaryActionLabel,
    string TargetLabel);

internal readonly record struct TradingWorkspaceShellActionRequest(
    string ActionId,
    string? PageTag,
    PaneDropAction Action,
    object? Parameter,
    bool UseAppNavigation,
    bool RequestContextSelection,
    string? StatusMessage);

internal sealed class TradingWorkspaceShellPresentationState
{
    public string ActiveFundText { get; init; } = "No operating context selected";

    public string ActiveFundDetailText { get; init; } =
        "Runs, allocations, and accounting posture scope to the active operating context.";

    public string PaperRunsText { get; init; } = "-";

    public string LiveRunsText { get; init; } = "-";

    public string TotalEquityText { get; init; } = "-";

    public string DrawdownText { get; init; } = "-";

    public string PositionLimitText { get; init; } = "-";

    public string OrderRateText { get; init; } = "-";

    public string CapitalCashText { get; init; } = "-";

    public string CapitalGrossExposureText { get; init; } = "-";

    public string CapitalNetExposureText { get; init; } = "-";

    public string CapitalFinancingText { get; init; } = "-";

    public string CapitalControlsDetailText { get; init; } =
        "Select an operating context to unlock capital, financing, and reconciliation posture.";

    public string TradingActiveRunText { get; init; } = "No active trading run";

    public string TradingActiveRunMetaText { get; init; } =
        "Use Research to promote a run, or open a live/paper panel below.";

    public string WatchlistStatusText { get; init; } =
        "Watchlists and active strategies populate once paper or live runs are started.";

    public string MarketCoreText { get; init; } =
        "Live data, order book, portfolio, and accounting consequences are ready to dock below.";

    public string RiskRailText { get; init; } =
        "Risk, reconciliation, and audit surfaces become specific once an active run is selected.";

    public string DeskActionStatusText { get; init; } =
        "Desk actions update here after a pause, stop, flatten, or alert acknowledgement.";

    public IReadOnlyList<TradingActivePositionItem> ActivePositions { get; init; } =
        Array.Empty<TradingActivePositionItem>();

    public WorkspaceShellContext ShellContext { get; init; } = new();

    public WorkspaceCommandGroup CommandGroup { get; init; } = new();

    public TradingStatusCardPresentation StatusCard { get; init; } =
        TradingWorkspaceShellPresentationDefaults.StatusCard;

    public TradingDeskHeroState DeskHero { get; init; } =
        TradingWorkspaceShellPresentationDefaults.DeskHero;

    public WorkflowNextAction? WorkflowNextAction { get; init; }

    public ActiveRunContext? ActiveRunContext { get; init; }
}

internal static class TradingWorkspaceShellPresentationDefaults
{
    public static TradingStatusCardPresentation StatusCard { get; } =
        new(
            "Role-first workflow guidance appears here for the active desk state.",
            "Info",
            TradingWorkspaceStatusTone.Info,
            new TradingWorkspaceStatusItem
            {
                Label = "Context required",
                Detail = "Trading review starts once an operating context and run handoff are both visible.",
                Tone = TradingWorkspaceStatusTone.Info
            },
            new TradingWorkspaceStatusItem
            {
                Label = "No operating context selected",
                Detail = "Paper review, live posture, and governance consequences scope to the active context.",
                Tone = TradingWorkspaceStatusTone.Info
            },
            new TradingWorkspaceStatusItem
            {
                Label = "Choose Context",
                Detail = "Open the correct existing page instead of leaving the desk state implicit.",
                Tone = TradingWorkspaceStatusTone.Info
            });

    public static TradingDeskHeroState DeskHero { get; } =
        new(
            FocusLabel: "Context handoff",
            Summary: "Choose the active desk scope before relying on trading posture.",
            Detail: "Replay evidence, controls, blotter posture, and governance-linked handoffs stay anchored to the active operating context.",
            BadgeText: "Info",
            BadgeTone: TradingWorkspaceStatusTone.Info,
            HandoffTitle: "Choose context",
            HandoffDetail: "Keep the scope explicit before moving into blotter, risk, or audit-first review.",
            PrimaryActionId: "SwitchContext",
            PrimaryActionLabel: "Switch Context",
            SecondaryActionId: "StrategyRuns",
            SecondaryActionLabel: "Run Browser",
            TargetLabel: "Target page: Context selector");
}

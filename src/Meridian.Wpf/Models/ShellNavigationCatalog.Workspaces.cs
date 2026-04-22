using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Models;

public static partial class ShellNavigationCatalog
{
    public static readonly IReadOnlyList<WorkspaceShellDescriptor> Workspaces =
    [
        new(
            Id: "research",
            Title: "Research",
            Description: "Configure backtests, review run evidence, and monitor model behavior.",
            Summary: "Monitor simulations, compare outcomes, and promote vetted strategies.",
            HomePageTag: "ResearchShell",
            TileSummary: "Runs · Compare · Promote"),
        new(
            Id: "trading",
            Title: "Trading",
            Description: "Monitor live markets, review orders, and manage trading risk.",
            Summary: "Monitor execution, reconcile positions, and review run-level risk.",
            HomePageTag: "TradingShell",
            TileSummary: "Live · Orders · Risk"),
        new(
            Id: "data-operations",
            Title: "Data Operations",
            Description: "Configure providers, monitor collection jobs, and export trusted datasets.",
            Summary: "Monitor ingestion, reconcile data quality issues, and export analysis packages.",
            HomePageTag: "DataOperationsShell",
            TileSummary: "Providers · Storage · Jobs"),
        new(
            Id: "governance",
            Title: "Governance",
            Description: "Review fund controls, reconcile books, and monitor audit readiness.",
            Summary: "Review ledgers, reconcile breaks, and configure operational safeguards.",
            HomePageTag: "GovernanceShell",
            TileSummary: "Ledger · Audit · Controls")
    ];

    private static readonly WorkspaceShellDefinition ResearchWorkspaceShellDefinition =
        new(
            WorkspaceId: "research",
            LayoutId: "research-backtest-studio",
            DisplayName: "Backtest Studio",
            DefaultPanes:
            [
                Pane("Backtest", PaneDropAction.Replace),
                Pane("StrategyRuns", PaneDropAction.SplitLeft),
                Pane("RunDetail", PaneDropAction.SplitRight, WorkspacePaneParameterBinding.ActiveRunId, fallbackPageTag: "Charts", fallbackAction: PaneDropAction.SplitRight),
                Pane("RunPortfolio", PaneDropAction.SplitBelow, WorkspacePaneParameterBinding.ActiveRunId, fallbackPageTag: "LeanIntegration", fallbackAction: PaneDropAction.SplitBelow)
            ],
            PresetPanes: new Dictionary<string, IReadOnlyList<WorkspacePaneDefinition>>(StringComparer.OrdinalIgnoreCase)
            {
                ["research-compare"] =
                [
                    Pane("Backtest", PaneDropAction.Replace),
                    Pane("StrategyRuns", PaneDropAction.SplitLeft),
                    Pane("RunDetail", PaneDropAction.SplitRight, WorkspacePaneParameterBinding.ActiveRunId, openWithoutBoundParameter: true),
                    Pane("RunPortfolio", PaneDropAction.SplitBelow, WorkspacePaneParameterBinding.ActiveRunId, openWithoutBoundParameter: true),
                    Pane("RunLedger", PaneDropAction.OpenTab, WorkspacePaneParameterBinding.ActiveRunId, openWithoutBoundParameter: true)
                ]
            },
            ContextlessPanes: Array.Empty<WorkspacePaneDefinition>(),
            StateProviderType: typeof(ResearchWorkspaceShellStateProvider),
            ViewModelType: typeof(ResearchWorkspaceShellViewModel));

    private static readonly WorkspaceShellDefinition TradingWorkspaceShellDefinition =
        new(
            WorkspaceId: "trading",
            LayoutId: "trading-cockpit",
            DisplayName: "Trading Cockpit",
            DefaultPanes:
            [
                Pane("LiveData", PaneDropAction.Replace),
                Pane("RunPortfolio", PaneDropAction.SplitLeft),
                Pane("PositionBlotter", PaneDropAction.SplitRight),
                Pane("RunRisk", PaneDropAction.SplitBelow),
                Pane("RunLedger", PaneDropAction.OpenTab, WorkspacePaneParameterBinding.ActiveRunId, fallbackPageTag: "OrderBook", fallbackAction: PaneDropAction.OpenTab),
                Pane("FundTrialBalance", PaneDropAction.OpenTab, WorkspacePaneParameterBinding.ActiveRunId)
            ],
            PresetPanes: new Dictionary<string, IReadOnlyList<WorkspacePaneDefinition>>(StringComparer.OrdinalIgnoreCase)
            {
                ["__workbench__"] =
                [
                    Pane("LiveData", PaneDropAction.Replace),
                    Pane("RunPortfolio", PaneDropAction.SplitLeft),
                    Pane("PositionBlotter", PaneDropAction.SplitRight),
                    Pane("RunRisk", PaneDropAction.SplitBelow),
                    Pane("RunLedger", PaneDropAction.OpenTab, WorkspacePaneParameterBinding.ActiveRunId, openWithoutBoundParameter: true),
                    Pane("FundTrialBalance", PaneDropAction.OpenTab, WorkspacePaneParameterBinding.ActiveRunId, openWithoutBoundParameter: true)
                ]
            },
            ContextlessPanes: Array.Empty<WorkspacePaneDefinition>(),
            StateProviderType: typeof(TradingWorkspaceShellStateProvider),
            ViewModelType: typeof(TradingWorkspaceShellViewModel));

    private static readonly WorkspaceShellDefinition DataOperationsWorkspaceShellDefinition =
        new(
            WorkspaceId: "data-operations",
            LayoutId: "data-operations-workspace",
            DisplayName: "Data Operations Workspace",
            DefaultPanes:
            [
                Pane("Provider", PaneDropAction.Replace),
                Pane("Backfill", PaneDropAction.SplitRight),
                Pane("Storage", PaneDropAction.SplitBelow),
                Pane("CollectionSessions", PaneDropAction.OpenTab)
            ],
            PresetPanes: new Dictionary<string, IReadOnlyList<WorkspacePaneDefinition>>(StringComparer.OrdinalIgnoreCase),
            ContextlessPanes: Array.Empty<WorkspacePaneDefinition>(),
            StateProviderType: typeof(DataOperationsWorkspaceShellStateProvider),
            ViewModelType: typeof(DataOperationsWorkspaceShellViewModel));

    private static readonly WorkspaceShellDefinition GovernanceWorkspaceShellDefinition =
        new(
            WorkspaceId: "governance",
            LayoutId: "governance-workspace",
            DisplayName: "Governance Workspace",
            DefaultPanes:
            [
                Pane("FundLedger", PaneDropAction.Replace),
                Pane("FundReconciliation", PaneDropAction.SplitRight),
                Pane("NotificationCenter", PaneDropAction.SplitBelow),
                Pane("FundAuditTrail", PaneDropAction.OpenTab)
            ],
            PresetPanes: new Dictionary<string, IReadOnlyList<WorkspacePaneDefinition>>(StringComparer.OrdinalIgnoreCase)
            {
                ["reconciliation-workbench"] =
                [
                    Pane("FundReconciliation", PaneDropAction.Replace),
                    Pane("FundTrialBalance", PaneDropAction.SplitLeft),
                    Pane("NotificationCenter", PaneDropAction.SplitBelow),
                    Pane("FundAuditTrail", PaneDropAction.OpenTab)
                ],
                ["accounting-review"] =
                [
                    Pane("FundLedger", PaneDropAction.Replace),
                    Pane("FundTrialBalance", PaneDropAction.SplitRight),
                    Pane("FundCashFinancing", PaneDropAction.SplitBelow),
                    Pane("FundAuditTrail", PaneDropAction.OpenTab)
                ]
            },
            ContextlessPanes:
            [
                Pane("Diagnostics", PaneDropAction.Replace),
                Pane("NotificationCenter", PaneDropAction.SplitRight),
                Pane("SystemHealth", PaneDropAction.SplitBelow)
            ],
            StateProviderType: typeof(GovernanceWorkspaceShellStateProvider),
            ViewModelType: typeof(GovernanceWorkspaceShellViewModel));
}

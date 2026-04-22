using Meridian.Wpf.Copy;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Models;

public static partial class ShellNavigationCatalog
{
    public static readonly IReadOnlyList<WorkspaceShellDescriptor> Workspaces =
    [
        new(
            Id: WorkspaceCopyCatalog.Research.Descriptor.Id,
            Title: WorkspaceCopyCatalog.Research.Descriptor.Title,
            Description: WorkspaceCopyCatalog.Research.Descriptor.Description,
            Summary: WorkspaceCopyCatalog.Research.Descriptor.Summary,
            HomePageTag: "ResearchShell",
            TileSummary: WorkspaceCopyCatalog.Research.Descriptor.TileSummary),
        new(
            Id: WorkspaceCopyCatalog.Trading.Descriptor.Id,
            Title: WorkspaceCopyCatalog.Trading.Descriptor.Title,
            Description: WorkspaceCopyCatalog.Trading.Descriptor.Description,
            Summary: WorkspaceCopyCatalog.Trading.Descriptor.Summary,
            HomePageTag: "TradingShell",
            TileSummary: WorkspaceCopyCatalog.Trading.Descriptor.TileSummary),
        new(
            Id: WorkspaceCopyCatalog.DataOperations.Descriptor.Id,
            Title: WorkspaceCopyCatalog.DataOperations.Descriptor.Title,
            Description: WorkspaceCopyCatalog.DataOperations.Descriptor.Description,
            Summary: WorkspaceCopyCatalog.DataOperations.Descriptor.Summary,
            HomePageTag: "DataOperationsShell",
            TileSummary: WorkspaceCopyCatalog.DataOperations.Descriptor.TileSummary),
        new(
            Id: WorkspaceCopyCatalog.Governance.Descriptor.Id,
            Title: WorkspaceCopyCatalog.Governance.Descriptor.Title,
            Description: WorkspaceCopyCatalog.Governance.Descriptor.Description,
            Summary: WorkspaceCopyCatalog.Governance.Descriptor.Summary,
            HomePageTag: "GovernanceShell",
            TileSummary: WorkspaceCopyCatalog.Governance.Descriptor.TileSummary)
    ];

    private static readonly WorkspaceShellDefinition ResearchWorkspaceShellDefinition =
        new(
            WorkspaceId: WorkspaceCopyCatalog.Research.WorkspaceId,
            LayoutId: "research-backtest-studio",
            DisplayName: WorkspaceCopyCatalog.Research.Descriptor.ShellDisplayName,
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
            WorkspaceId: WorkspaceCopyCatalog.Trading.WorkspaceId,
            LayoutId: "trading-cockpit",
            DisplayName: WorkspaceCopyCatalog.Trading.Descriptor.ShellDisplayName,
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
            WorkspaceId: WorkspaceCopyCatalog.DataOperations.WorkspaceId,
            LayoutId: "data-operations-workspace",
            DisplayName: WorkspaceCopyCatalog.DataOperations.Descriptor.ShellDisplayName,
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
            WorkspaceId: WorkspaceCopyCatalog.Governance.WorkspaceId,
            LayoutId: "governance-workspace",
            DisplayName: WorkspaceCopyCatalog.Governance.Descriptor.ShellDisplayName,
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

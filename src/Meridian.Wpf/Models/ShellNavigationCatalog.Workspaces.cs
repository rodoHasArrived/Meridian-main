using Meridian.Wpf.Copy;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Models;

public static partial class ShellNavigationCatalog
{
    public static IReadOnlyList<WorkspaceShellDescriptor> Workspaces
        => WorkspaceCapabilities.Select(static capability => capability.Workspace).ToArray();

    private static IReadOnlyList<WorkspaceCapabilityDescriptor> BuildWorkspaceCapabilities()
        =>
        [
            BuildCapability(WorkspaceCopyCatalog.Trading.Descriptor, "TradingShell", TradingWorkspaceShellDefinition, TradingPages),
            BuildCapability(WorkspaceCopyCatalog.Portfolio.Descriptor, "PortfolioShell", PortfolioWorkspaceShellDefinition, PortfolioPages),
            BuildCapability(WorkspaceCopyCatalog.Accounting.Descriptor, "AccountingShell", AccountingWorkspaceShellDefinition, AccountingPages),
            BuildCapability(WorkspaceCopyCatalog.Reporting.Descriptor, "ReportingShell", ReportingWorkspaceShellDefinition, ReportingPages),
            BuildCapability(WorkspaceCopyCatalog.Strategy.Descriptor, "StrategyShell", StrategyWorkspaceShellDefinition, StrategyPages),
            BuildCapability(WorkspaceCopyCatalog.Data.Descriptor, "DataShell", DataWorkspaceShellDefinition, DataPages),
            BuildCapability(WorkspaceCopyCatalog.Settings.Descriptor, "SettingsShell", SettingsWorkspaceShellDefinition, SettingsPages)
        ];

    private static WorkspaceCapabilityDescriptor BuildCapability(
        WorkspaceDescriptorCopy copy,
        string homePageTag,
        WorkspaceShellDefinition shellDefinition,
        IReadOnlyList<ShellPageDescriptor> pages)
        => new(
            new WorkspaceShellDescriptor(
                Id: copy.Id,
                Title: copy.Title,
                Description: copy.Description,
                Summary: copy.Summary,
                HomePageTag: homePageTag,
                TileSummary: copy.TileSummary),
            shellDefinition,
            pages);

    private static readonly WorkspaceShellDefinition TradingWorkspaceShellDefinition =
        new(
            WorkspaceId: WorkspaceCopyCatalog.Trading.WorkspaceId,
            LayoutId: "trading-cockpit",
            DisplayName: WorkspaceCopyCatalog.Trading.Descriptor.ShellDisplayName,
            DefaultPanes:
            [
                Pane("LiveData", PaneDropAction.Replace),
                Pane("OrderBook", PaneDropAction.SplitLeft),
                Pane("PositionBlotter", PaneDropAction.SplitRight),
                Pane("RunRisk", PaneDropAction.SplitBelow),
                Pane("TradingHours", PaneDropAction.OpenTab)
            ],
            PresetPanes: new Dictionary<string, IReadOnlyList<WorkspacePaneDefinition>>(StringComparer.OrdinalIgnoreCase)
            {
                ["__workbench__"] =
                [
                    Pane("LiveData", PaneDropAction.Replace),
                    Pane("OrderBook", PaneDropAction.SplitLeft),
                    Pane("PositionBlotter", PaneDropAction.SplitRight),
                    Pane("RunRisk", PaneDropAction.SplitBelow),
                    Pane("TradingHours", PaneDropAction.OpenTab, openWithoutBoundParameter: true)
                ]
            },
            ContextlessPanes: Array.Empty<WorkspacePaneDefinition>(),
            StateProviderType: typeof(TradingWorkspaceShellStateProvider),
            ViewModelType: typeof(TradingWorkspaceShellViewModel));

    private static readonly WorkspaceShellDefinition PortfolioWorkspaceShellDefinition =
        new(
            WorkspaceId: WorkspaceCopyCatalog.Portfolio.WorkspaceId,
            LayoutId: "portfolio-review",
            DisplayName: WorkspaceCopyCatalog.Portfolio.Descriptor.ShellDisplayName,
            DefaultPanes:
            [
                Pane("AccountPortfolio", PaneDropAction.Replace),
                Pane("AggregatePortfolio", PaneDropAction.SplitLeft),
                Pane("FundPortfolio", PaneDropAction.SplitRight),
                Pane("FundAccounts", PaneDropAction.SplitBelow)
            ],
            PresetPanes: new Dictionary<string, IReadOnlyList<WorkspacePaneDefinition>>(StringComparer.OrdinalIgnoreCase),
            ContextlessPanes: Array.Empty<WorkspacePaneDefinition>(),
            StateProviderType: typeof(PortfolioWorkspaceShellStateProvider),
            ViewModelType: typeof(PortfolioWorkspaceShellViewModel));

    private static readonly WorkspaceShellDefinition AccountingWorkspaceShellDefinition =
        new(
            WorkspaceId: WorkspaceCopyCatalog.Accounting.WorkspaceId,
            LayoutId: "accounting-workspace",
            DisplayName: WorkspaceCopyCatalog.Accounting.Descriptor.ShellDisplayName,
            DefaultPanes:
            [
                Pane("FundLedger", PaneDropAction.Replace),
                Pane("FundReconciliation", PaneDropAction.SplitRight),
                Pane("FundTrialBalance", PaneDropAction.SplitBelow),
                Pane("FundAuditTrail", PaneDropAction.OpenTab)
            ],
            PresetPanes: new Dictionary<string, IReadOnlyList<WorkspacePaneDefinition>>(StringComparer.OrdinalIgnoreCase)
            {
                ["reconciliation-workbench"] =
                [
                    Pane("FundReconciliation", PaneDropAction.Replace),
                    Pane("FundTrialBalance", PaneDropAction.SplitLeft),
                    Pane("FundLedger", PaneDropAction.SplitBelow),
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
                Pane("FundLedger", PaneDropAction.Replace),
                Pane("FundTrialBalance", PaneDropAction.SplitRight),
                Pane("FundAuditTrail", PaneDropAction.SplitBelow)
            ],
            StateProviderType: typeof(AccountingWorkspaceShellStateProvider),
            ViewModelType: typeof(AccountingWorkspaceShellViewModel));

    private static readonly WorkspaceShellDefinition ReportingWorkspaceShellDefinition =
        new(
            WorkspaceId: WorkspaceCopyCatalog.Reporting.WorkspaceId,
            LayoutId: "reporting-workspace",
            DisplayName: WorkspaceCopyCatalog.Reporting.Descriptor.ShellDisplayName,
            DefaultPanes:
            [
                Pane("FundReportPack", PaneDropAction.Replace),
                Pane("Dashboard", PaneDropAction.SplitLeft),
                Pane("AnalysisExport", PaneDropAction.SplitRight),
                Pane("ExportPresets", PaneDropAction.OpenTab)
            ],
            PresetPanes: new Dictionary<string, IReadOnlyList<WorkspacePaneDefinition>>(StringComparer.OrdinalIgnoreCase),
            ContextlessPanes: Array.Empty<WorkspacePaneDefinition>(),
            StateProviderType: typeof(ReportingWorkspaceShellStateProvider),
            ViewModelType: typeof(ReportingWorkspaceShellViewModel));

    private static readonly WorkspaceShellDefinition StrategyWorkspaceShellDefinition =
        new(
            WorkspaceId: WorkspaceCopyCatalog.Strategy.WorkspaceId,
            LayoutId: "strategy-studio",
            DisplayName: WorkspaceCopyCatalog.Strategy.Descriptor.ShellDisplayName,
            DefaultPanes:
            [
                Pane("Backtest", PaneDropAction.Replace),
                Pane("StrategyRuns", PaneDropAction.SplitLeft),
                Pane("RunDetail", PaneDropAction.SplitRight, WorkspacePaneParameterBinding.ActiveRunId, fallbackPageTag: "Charts", fallbackAction: PaneDropAction.SplitRight),
                Pane("RunMat", PaneDropAction.SplitBelow)
            ],
            PresetPanes: new Dictionary<string, IReadOnlyList<WorkspacePaneDefinition>>(StringComparer.OrdinalIgnoreCase)
            {
                ["strategy-compare"] =
                [
                    Pane("Backtest", PaneDropAction.Replace),
                    Pane("StrategyRuns", PaneDropAction.SplitLeft),
                    Pane("RunDetail", PaneDropAction.SplitRight, WorkspacePaneParameterBinding.ActiveRunId, openWithoutBoundParameter: true),
                    Pane("Charts", PaneDropAction.SplitBelow, WorkspacePaneParameterBinding.ActiveRunId, openWithoutBoundParameter: true)
                ]
            },
            ContextlessPanes: Array.Empty<WorkspacePaneDefinition>(),
            StateProviderType: typeof(ResearchWorkspaceShellStateProvider),
            ViewModelType: typeof(ResearchWorkspaceShellViewModel));

    private static readonly WorkspaceShellDefinition DataWorkspaceShellDefinition =
        new(
            WorkspaceId: WorkspaceCopyCatalog.Data.WorkspaceId,
            LayoutId: "data-workspace",
            DisplayName: WorkspaceCopyCatalog.Data.Descriptor.ShellDisplayName,
            DefaultPanes:
            [
                Pane("Provider", PaneDropAction.Replace),
                Pane("Backfill", PaneDropAction.SplitRight),
                Pane("Storage", PaneDropAction.SplitBelow),
                Pane("DataQuality", PaneDropAction.OpenTab)
            ],
            PresetPanes: new Dictionary<string, IReadOnlyList<WorkspacePaneDefinition>>(StringComparer.OrdinalIgnoreCase),
            ContextlessPanes: Array.Empty<WorkspacePaneDefinition>(),
            StateProviderType: typeof(DataOperationsWorkspaceShellStateProvider),
            ViewModelType: typeof(DataOperationsWorkspaceShellViewModel));

    private static readonly WorkspaceShellDefinition SettingsWorkspaceShellDefinition =
        new(
            WorkspaceId: WorkspaceCopyCatalog.Settings.WorkspaceId,
            LayoutId: "settings-workspace",
            DisplayName: WorkspaceCopyCatalog.Settings.Descriptor.ShellDisplayName,
            DefaultPanes:
            [
                Pane("Settings", PaneDropAction.Replace),
                Pane("Diagnostics", PaneDropAction.SplitRight),
                Pane("SystemHealth", PaneDropAction.SplitBelow),
                Pane("NotificationCenter", PaneDropAction.OpenTab)
            ],
            PresetPanes: new Dictionary<string, IReadOnlyList<WorkspacePaneDefinition>>(StringComparer.OrdinalIgnoreCase),
            ContextlessPanes: Array.Empty<WorkspacePaneDefinition>(),
            StateProviderType: typeof(SettingsWorkspaceShellStateProvider),
            ViewModelType: typeof(SettingsWorkspaceShellViewModel));
}

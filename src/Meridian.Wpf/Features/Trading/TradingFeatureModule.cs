using Meridian.Ui.Services;
using Meridian.Wpf.Copy;
using Meridian.Wpf.Models;
using Meridian.Wpf.Shell.Services;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Features.Trading;

public sealed class TradingFeatureModule : IDesktopFeatureModule
{
    private static readonly IReadOnlyList<ShellPageDescriptor> Pages =
    [
        ShellPageRegistryBuilder.Page<TradingWorkspaceShellPage>("TradingShell", "Trading Workspace", "Monitor markets, review orders, and track risk across active runs.", "trading", "Desk", "\uE945", 0, ShellNavigationVisibilityTier.Primary, ["trading", "home", "workspace", "monitor"], ["LiveData", "OrderBook", "PositionBlotter", "RunRisk", "PortfolioShell"], ["TradingWorkspace"]),
        ShellPageRegistryBuilder.Page<LiveDataViewerPage>("LiveData", "Live data", "Monitor streaming quotes, venue state, and feed health.", "trading", "Market Feed", "\uE9D2", 10, ShellNavigationVisibilityTier.Primary, ["streaming", "market", "feed", "monitor"], ["OrderBook", "PositionBlotter"], ["LiveDataViewer"]),
        ShellPageRegistryBuilder.Page<OrderBookPage>("OrderBook", "Order book", "Review depth and quote quality before sending orders.", "trading", "Market Feed", "\uE8FD", 20, ShellNavigationVisibilityTier.Primary, ["order book", "depth", "quotes"], ["LiveData", "PositionBlotter"]),
        ShellPageRegistryBuilder.Page<PositionBlotterPage>("PositionBlotter", "Position blotter", "Review open positions and realized or unrealized P&L.", "trading", "Execution", "\uE8FD", 30, ShellNavigationVisibilityTier.Primary, ["positions", "blotter", "pnl", "review"], ["AccountPortfolio", "RunRisk", "RunLedger", "FundPortfolio"], ["Blotter"]),
        ShellPageRegistryBuilder.Page<RunRiskPage>("RunRisk", "Run risk", "Monitor risk metrics, limits, and drawdown alerts.", "trading", "Execution", "\uE7BA", 40, ShellNavigationVisibilityTier.Primary, ["risk", "limits", "monitor"], ["PositionBlotter", "RunLedger", "AccountingShell"]),
        ShellPageRegistryBuilder.Page<TradingHoursPage>("TradingHours", "Trading hours", "Review market sessions and schedule coverage before execution.", "trading", "Execution", "\uE823", 50, ShellNavigationVisibilityTier.Secondary, ["calendar", "hours", "sessions", "review"], ["LiveData", "OrderBook"])
    ];

    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddWorkspaceScoped<TradingWorkspaceShellPresentationService>();
        services.AddTransient<TradingWorkspaceShellStateProvider>();
        services.AddTransient<TradingWorkspaceShellViewModel>();
        services.AddTransient<TradingWorkspaceShellPage>();
    }

    public IReadOnlyList<ShellPageDescriptor> DescribePages() => Pages;

    public IReadOnlyList<FeatureCapabilityDescriptor> DeclareCapabilities() =>
    [
        new("desktop.trading.workspace", "Trading workspace", "Trading shell, market feed, order book, position blotter, and run-risk screens.", true, true),
        new("desktop.trading.hours", "Trading hours", "Market-session briefing and schedule coverage review in the Trading workspace.", true, false)
    ];

    public WorkspaceCapabilityDescriptor DescribeWorkspace()
        => ShellNavigationCatalog.BuildCapability(
            WorkspaceCopyCatalog.Trading.Descriptor,
            "TradingShell",
            new WorkspaceShellDefinition(
                WorkspaceId: WorkspaceCopyCatalog.Trading.WorkspaceId,
                LayoutId: "trading-cockpit",
                DisplayName: WorkspaceCopyCatalog.Trading.Descriptor.ShellDisplayName,
                DefaultPanes:
                [
                    ShellPageRegistryBuilder.Pane("LiveData", PaneDropAction.Replace),
                    ShellPageRegistryBuilder.Pane("OrderBook", PaneDropAction.SplitLeft),
                    ShellPageRegistryBuilder.Pane("PositionBlotter", PaneDropAction.SplitRight),
                    ShellPageRegistryBuilder.Pane("RunRisk", PaneDropAction.SplitBelow),
                    ShellPageRegistryBuilder.Pane("TradingHours", PaneDropAction.OpenTab)
                ],
                PresetPanes: new Dictionary<string, IReadOnlyList<WorkspacePaneDefinition>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["__workbench__"] =
                    [
                        ShellPageRegistryBuilder.Pane("LiveData", PaneDropAction.Replace),
                        ShellPageRegistryBuilder.Pane("OrderBook", PaneDropAction.SplitLeft),
                        ShellPageRegistryBuilder.Pane("PositionBlotter", PaneDropAction.SplitRight),
                        ShellPageRegistryBuilder.Pane("RunRisk", PaneDropAction.SplitBelow),
                        ShellPageRegistryBuilder.Pane("TradingHours", PaneDropAction.OpenTab, openWithoutBoundParameter: true)
                    ]
                },
                ContextlessPanes: Array.Empty<WorkspacePaneDefinition>(),
                StateProviderType: typeof(TradingWorkspaceShellStateProvider),
                ViewModelType: typeof(TradingWorkspaceShellViewModel)),
            Pages);
}

using Meridian.Wpf.Views;

namespace Meridian.Wpf.Models;

public static partial class ShellNavigationCatalog
{
    private static readonly ShellPageDescriptor[] ResearchPages =
    [
        Page<ResearchWorkspaceShellPage>("ResearchShell", "Research Workspace", "Monitor research priorities, review watchlists, and open run workflows.", "research", "Launchpad", "\uEC35", 0, ShellNavigationVisibilityTier.Primary, ["research", "home", "workspace", "monitor"], ["StrategyRuns", "Backtest", "RunPortfolio", "TradingShell"], ["ResearchWorkspace"]),
        Page<BacktestPage>("Backtest", "Backtest", "Configure simulation inputs and launch backtest runs.", "research", "Studio", "\uEC35", 10, ShellNavigationVisibilityTier.Primary, ["simulation", "backtest", "configure", "scenario"], ["StrategyRuns", "RunDetail", "RunPortfolio"], ["BacktestStudio"]),
        Page<StrategyRunsPage>("StrategyRuns", "Strategy runs", "Review active and completed runs, then compare outcomes.", "research", "Studio", "\uE8FD", 20, ShellNavigationVisibilityTier.Primary, ["runs", "review", "compare", "history"], ["RunDetail", "RunPortfolio", "RunLedger", "TradingShell"], ["RunBrowser"]),
        Page<ChartingPage>("Charts", "Charts", "Review price and signal charts for selected strategies.", "research", "Studio", "\uE9D9", 30, ShellNavigationVisibilityTier.Primary, ["charts", "charting", "analysis", "monitor"], ["Backtest", "StrategyRuns", "AdvancedAnalytics"], ["Charting"]),
        Page<RunMatPage>("RunMat", "Run scripts", "Configure and run research scripts for custom analysis.", "research", "Studio", "\uE943", 40, ShellNavigationVisibilityTier.Primary, ["automation", "script", "run scripts", "tooling"], ["QuantScript", "StrategyRuns"], ["ResearchAutomation"]),
        Page<WatchlistPage>("Watchlist", "Watchlist", "Monitor candidate symbols and route ideas to research or trading.", "research", "Desk", "\uE8D4", 50, ShellNavigationVisibilityTier.Primary, ["symbols", "watchlist", "monitoring"], ["TradingShell", "LiveData", "OrderBook"]),
        Page<DashboardPage>("Dashboard", "Research operations", "Monitor research holdings, quality exceptions, maturities, and export readiness.", "research", "Operations", "\uE71D", 60, ShellNavigationVisibilityTier.Secondary, ["overview", "portfolio", "holdings", "quality"], ["ResearchShell", "DataQuality", "SecurityMaster"], hideFromDefaultPalette: true),
        Page<BatchBacktestPage>("BatchBacktest", "Batch backtest", "Monitor queued simulation batches and review grouped results.", "research", "Studio", "\uE768", 70, ShellNavigationVisibilityTier.Secondary, ["batch", "queue", "backtest"], ["Backtest", "StrategyRuns"]),
        Page<RunDetailPage>("RunDetail", "Run detail", "Review diagnostics and execution details for a selected run.", "research", "Inspectors", "\uE8A5", 80, ShellNavigationVisibilityTier.Secondary, ["detail", "diagnostics", "review"], ["RunPortfolio", "RunLedger", "RunCashFlow"], ["SimulationDetail"]),
        Page<RunPortfolioPage>("RunPortfolio", "Run portfolio", "Review holdings, exposures, and weights for the selected run.", "research", "Inspectors", "\uE821", 90, ShellNavigationVisibilityTier.Secondary, ["portfolio", "positions", "review"], ["RunLedger", "RunCashFlow", "GovernanceShell"], ["PortfolioInspector"]),
        Page<RunCashFlowPage>("RunCashFlow", "Run cash flow", "Review simulated cash, financing, and funding impacts.", "research", "Inspectors", "\uEAFD", 100, ShellNavigationVisibilityTier.Secondary, ["cash flow", "cash", "financing"], ["RunPortfolio", "RunLedger"]),
        Page<AdvancedAnalyticsPage>("AdvancedAnalytics", "Advanced analytics", "Review advanced metrics and model diagnostics.", "research", "Analysis", "\uE9D9", 110, ShellNavigationVisibilityTier.Secondary, ["metrics", "analytics", "review"], ["Charts", "StrategyRuns"]),
        Page<QuantScriptPage>("QuantScript", "Quant script", "Configure and run quantitative scripts for strategy research.", "research", "Analysis", "\uE943", 120, ShellNavigationVisibilityTier.Secondary, ["code", "quant", "script", "configure"], ["RunMat", "Backtest"]),
        Page<LeanIntegrationPage>("LeanIntegration", "Lean integration", "Configure Lean connectivity and review handoff status.", "research", "Analysis", "\uE71B", 130, ShellNavigationVisibilityTier.Overflow, ["lean", "integration", "configure", "engine"], ["Backtest", "StrategyRuns"]),
        Page<EventReplayPage>("EventReplay", "Event replay", "Replay event streams to review sequence and signal behavior.", "research", "Analysis", "\uE768", 140, ShellNavigationVisibilityTier.Overflow, ["replay", "events", "review"], ["StrategyRuns", "Charts"])
    ];
}

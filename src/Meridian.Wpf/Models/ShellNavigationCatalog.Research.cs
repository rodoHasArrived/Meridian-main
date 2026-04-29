using Meridian.Wpf.Views;

namespace Meridian.Wpf.Models;

public static partial class ShellNavigationCatalog
{
    private static readonly ShellPageDescriptor[] StrategyPages =
    [
        Page<ResearchWorkspaceShellPage>("StrategyShell", "Strategy Workspace", "Monitor strategy priorities, review watchlists, and open run workflows.", "strategy", "Launchpad", "\uEC35", 0, ShellNavigationVisibilityTier.Primary, ["strategy", "research", "home", "workspace", "monitor"], ["StrategyRuns", "Backtest", "RunDetail", "TradingShell"], ["ResearchShell", "ResearchWorkspace"]),
        Page<BacktestPage>("Backtest", "Backtest", "Configure simulation inputs and launch backtest runs.", "strategy", "Studio", "\uEC35", 10, ShellNavigationVisibilityTier.Primary, ["simulation", "backtest", "configure", "scenario"], ["StrategyRuns", "RunDetail", "RunPortfolio"], ["BacktestStudio"]),
        Page<StrategyRunsPage>("StrategyRuns", "Strategy runs", "Review active and completed runs, then compare outcomes.", "strategy", "Studio", "\uE8FD", 20, ShellNavigationVisibilityTier.Primary, ["runs", "review", "compare", "history"], ["RunDetail", "RunPortfolio", "RunLedger", "TradingShell"], ["RunBrowser"]),
        Page<RunDetailPage>("RunDetail", "Run detail", "Review diagnostics and execution details for a selected run.", "strategy", "Inspectors", "\uE8A5", 30, ShellNavigationVisibilityTier.Primary, ["detail", "diagnostics", "review"], ["RunPortfolio", "RunLedger", "RunCashFlow"], ["SimulationDetail"]),
        Page<ChartingPage>("Charts", "Charts", "Review price and signal charts for selected strategies.", "strategy", "Analysis", "\uE9D9", 40, ShellNavigationVisibilityTier.Primary, ["charts", "charting", "analysis", "monitor"], ["Backtest", "StrategyRuns", "AdvancedAnalytics"], ["Charting"]),
        Page<RunMatPage>("RunMat", "Run scripts", "Configure and run strategy scripts for custom analysis.", "strategy", "Studio", "\uE943", 50, ShellNavigationVisibilityTier.Primary, ["automation", "script", "run scripts", "tooling"], ["QuantScript", "StrategyRuns"], ["ResearchAutomation"]),
        Page<BatchBacktestPage>("BatchBacktest", "Batch backtest", "Monitor queued simulation batches and review grouped results.", "strategy", "Studio", "\uE768", 60, ShellNavigationVisibilityTier.Secondary, ["batch", "queue", "backtest"], ["Backtest", "StrategyRuns"]),
        Page<AdvancedAnalyticsPage>("AdvancedAnalytics", "Advanced analytics", "Review advanced metrics and model diagnostics.", "strategy", "Analysis", "\uE9D9", 70, ShellNavigationVisibilityTier.Secondary, ["metrics", "analytics", "review"], ["Charts", "StrategyRuns"]),
        Page<QuantScriptPage>("QuantScript", "Quant script", "Configure and run quantitative scripts for strategy research.", "strategy", "Analysis", "\uE943", 80, ShellNavigationVisibilityTier.Secondary, ["code", "quant", "script", "configure"], ["RunMat", "Backtest"]),
        Page<LeanIntegrationPage>("LeanIntegration", "Lean integration", "Configure Lean connectivity and review handoff status.", "strategy", "Analysis", "\uE71B", 90, ShellNavigationVisibilityTier.Overflow, ["lean", "integration", "configure", "engine"], ["Backtest", "StrategyRuns"]),
        Page<EventReplayPage>("EventReplay", "Event replay", "Replay event streams to review sequence and signal behavior.", "strategy", "Analysis", "\uE768", 100, ShellNavigationVisibilityTier.Overflow, ["replay", "events", "review"], ["StrategyRuns", "Charts"]),
        Page<WatchlistPage>("Watchlist", "Watchlist", "Monitor candidate symbols and route ideas to strategy or trading.", "strategy", "Desk", "\uE8D4", 110, ShellNavigationVisibilityTier.Overflow, ["symbols", "watchlist", "monitoring"], ["TradingShell", "LiveData", "OrderBook"])
    ];
}

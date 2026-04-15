using Meridian.Wpf.Views;

namespace Meridian.Wpf.Models;

public static partial class ShellNavigationCatalog
{
    private static readonly ShellPageDescriptor[] ResearchPages =
    [
        Page<ResearchWorkspaceShellPage>("ResearchShell", "Research Workspace", "Backtest studio shell with active run context, compare lanes, and promotion rails.", "research", "Launchpad", "\uEC35", 0, ShellNavigationVisibilityTier.Primary, ["research", "workspace", "studio", "launchpad"], ["StrategyRuns", "Backtest", "RunPortfolio", "TradingShell"], ["ResearchWorkspace"]),
        Page<BacktestPage>("Backtest", "Backtest", "Configure and launch new simulations from the studio workbench.", "research", "Studio", "\uEC35", 10, ShellNavigationVisibilityTier.Primary, ["simulation", "backtesting", "scenario"], ["StrategyRuns", "RunDetail", "RunPortfolio"], ["BacktestStudio"]),
        Page<StrategyRunsPage>("StrategyRuns", "Strategy Runs", "Browse completed and active runs, compare outcomes, and drill into evidence.", "research", "Studio", "\uE8FD", 20, ShellNavigationVisibilityTier.Primary, ["runs", "compare", "history"], ["RunDetail", "RunPortfolio", "RunLedger", "TradingShell"], ["RunBrowser"]),
        Page<ChartingPage>("Charts", "Charts", "Inspect overlays, annotations, and investigation views for strategy behavior.", "research", "Studio", "\uE9D9", 30, ShellNavigationVisibilityTier.Primary, ["charting", "analysis", "visual"], ["Backtest", "StrategyRuns", "AdvancedAnalytics"], ["Charting"]),
        Page<RunMatPage>("RunMat", "Run Mat", "Prototype research scripts and run external analytics utilities inside the workstation.", "research", "Studio", "\uE943", 40, ShellNavigationVisibilityTier.Primary, ["automation", "script", "tooling"], ["QuantScript", "StrategyRuns"], ["ResearchAutomation"]),
        Page<WatchlistPage>("Watchlist", "Watchlist", "Stage symbols, shortlist ideas, and hand candidates into research or trading flows.", "research", "Desk", "\uE8D4", 50, ShellNavigationVisibilityTier.Primary, ["symbols", "monitoring"], ["TradingShell", "LiveData", "OrderBook"]),
        Page<DashboardPage>("Dashboard", "Dashboard", "Legacy global overview with live posture, alerts, and action shortcuts.", "research", "Legacy", "\uE71D", 60, ShellNavigationVisibilityTier.Secondary, ["overview", "status"], ["ResearchShell", "NotificationCenter"], hideFromDefaultPalette: true),
        Page<BatchBacktestPage>("BatchBacktest", "Batch Backtest", "Supervise multi-run research jobs and inspect grouped outcomes.", "research", "Studio", "\uE768", 70, ShellNavigationVisibilityTier.Secondary, ["batch", "queue"], ["Backtest", "StrategyRuns"]),
        Page<RunDetailPage>("RunDetail", "Run Detail", "Inspect run diagnostics, execution state, and evidence for a selected simulation.", "research", "Inspectors", "\uE8A5", 80, ShellNavigationVisibilityTier.Secondary, ["detail", "diagnostics"], ["RunPortfolio", "RunLedger", "RunCashFlow"], ["SimulationDetail"]),
        Page<RunPortfolioPage>("RunPortfolio", "Run Portfolio", "Review holdings, exposures, and position context for the selected run.", "research", "Inspectors", "\uE821", 90, ShellNavigationVisibilityTier.Secondary, ["portfolio", "positions"], ["RunLedger", "RunCashFlow", "GovernanceShell"], ["PortfolioInspector"]),
        Page<RunCashFlowPage>("RunCashFlow", "Run Cash Flow", "Inspect simulated cash movement, financing, and funding impact.", "research", "Inspectors", "\uEAFD", 100, ShellNavigationVisibilityTier.Secondary, ["cash", "financing"], ["RunPortfolio", "RunLedger"]),
        Page<AdvancedAnalyticsPage>("AdvancedAnalytics", "Advanced Analytics", "Open deeper analytics surfaces and higher-order metrics for research review.", "research", "Analysis", "\uE9D9", 110, ShellNavigationVisibilityTier.Secondary, ["metrics", "analytics"], ["Charts", "StrategyRuns"]),
        Page<QuantScriptPage>("QuantScript", "Quant Script", "Prototype research logic and calculation workflows inside the desktop shell.", "research", "Analysis", "\uE943", 120, ShellNavigationVisibilityTier.Secondary, ["code", "quant", "script"], ["RunMat", "Backtest"]),
        Page<LeanIntegrationPage>("LeanIntegration", "Lean Integration", "Coordinate Lean connectivity and research engine handoff checks.", "research", "Analysis", "\uE71B", 130, ShellNavigationVisibilityTier.Overflow, ["lean", "engine", "integration"], ["Backtest", "StrategyRuns"]),
        Page<EventReplayPage>("EventReplay", "Event Replay", "Replay captured event streams to inspect sequencing and investigation results.", "research", "Analysis", "\uE768", 140, ShellNavigationVisibilityTier.Overflow, ["replay", "events"], ["StrategyRuns", "Charts"])
    ];
}

using Meridian.Wpf.Views;

namespace Meridian.Wpf.Models;

public static partial class ShellNavigationCatalog
{
    private static readonly ShellPageDescriptor[] TradingPages =
    [
        Page<TradingWorkspaceShellPage>("TradingShell", "Trading Workspace", "Trading cockpit with paper/live context, positions, orders, and risk rails.", "trading", ShellNavigationSections.Overview, "\uE945", 0, ShellNavigationVisibilityTier.Primary, ["trading", "workspace", "cockpit"], ["RunPortfolio", "PositionBlotter", "RunRisk", "FundPortfolio", "GovernanceShell"], ["TradingWorkspace"]),
        Page<LiveDataViewerPage>("LiveData", "Live Data", "Monitor streaming market traffic, venue state, and flow health in real time.", "trading", ShellNavigationSections.Operations, "\uE9D2", 10, ShellNavigationVisibilityTier.Primary, ["streaming", "market", "feed"], ["OrderBook", "PositionBlotter"], ["LiveDataViewer"]),
        Page<OrderBookPage>("OrderBook", "Order Book", "Inspect market depth, quote quality, and execution context for active instruments.", "trading", ShellNavigationSections.Operations, "\uE8FD", 20, ShellNavigationVisibilityTier.Primary, ["depth", "quotes", "book"], ["LiveData", "PositionBlotter"]),
        Page<PositionBlotterPage>("PositionBlotter", "Position Blotter", "Review open positions, realized and unrealized P&L, and operator posture.", "trading", ShellNavigationSections.Operations, "\uE8FD", 30, ShellNavigationVisibilityTier.Primary, ["positions", "blotter", "pnl"], ["RunPortfolio", "RunRisk", "RunLedger", "FundPortfolio"], ["Blotter"]),
        Page<RunRiskPage>("RunRisk", "Run Risk", "Inspect run-level risk metrics, drawdown posture, and position-limit breaches.", "trading", ShellNavigationSections.Risk, "\uE7BA", 40, ShellNavigationVisibilityTier.Primary, ["risk", "limits"], ["RunPortfolio", "PositionBlotter", "RunLedger", "GovernanceShell"]),
        Page<TradingHoursPage>("TradingHours", "Trading Hours", "Check venue sessions, market hours, and schedule coverage before execution.", "trading", ShellNavigationSections.Operations, "\uE823", 50, ShellNavigationVisibilityTier.Secondary, ["calendar", "hours", "sessions"], ["LiveData", "OrderBook"]),
        Page<RunLedgerPage>("RunLedger", "Run Ledger", "Inspect run-specific postings and execution-linked financial movements.", "trading", ShellNavigationSections.Operations, "\uE82D", 60, ShellNavigationVisibilityTier.Secondary, ["ledger", "postings"], ["PositionBlotter", "RunRisk", "FundLedger"], ["LedgerInspector"]),
        Page<AccountPortfolioPage>("AccountPortfolio", "Account Portfolio", "Inspect account-scoped positions and allocations for trading review.", "trading", ShellNavigationSections.Operations, "\uE821", 70, ShellNavigationVisibilityTier.Secondary, ["account", "portfolio"], ["RunPortfolio", "AggregatePortfolio", "FundPortfolio"]),
        Page<AggregatePortfolioPage>("AggregatePortfolio", "Aggregate Portfolio", "Review aggregated book exposure across the desk.", "trading", ShellNavigationSections.Operations, "\uE821", 80, ShellNavigationVisibilityTier.Secondary, ["aggregate", "book"], ["RunPortfolio", "AccountPortfolio", "FundPortfolio"]),
        Page<DirectLendingPage>("DirectLending", "Direct Lending", "Operate lending-focused trading and portfolio workflows.", "trading", ShellNavigationSections.Operations, "\uE8C7", 90, ShellNavigationVisibilityTier.Overflow, ["lending", "credit"], ["TradingShell", "FundPortfolio"])
    ];
}

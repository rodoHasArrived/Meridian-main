using Meridian.Wpf.Views;

namespace Meridian.Wpf.Models;

public static partial class ShellNavigationCatalog
{
    private static readonly ShellPageDescriptor[] TradingPages =
    [
        Page<TradingWorkspaceShellPage>("TradingShell", "Trading home", "Monitor markets, review orders, and track risk across active runs.", "trading", "Desk", "\uE945", 0, ShellNavigationVisibilityTier.Primary, ["trading", "home", "workspace", "monitor"], ["RunPortfolio", "PositionBlotter", "RunRisk", "FundPortfolio", "GovernanceShell"], ["TradingWorkspace"]),
        Page<LiveDataViewerPage>("LiveData", "Live data", "Monitor streaming quotes, venue state, and feed health.", "trading", "Market Feed", "\uE9D2", 10, ShellNavigationVisibilityTier.Primary, ["streaming", "market", "feed", "monitor"], ["OrderBook", "PositionBlotter"], ["LiveDataViewer"]),
        Page<OrderBookPage>("OrderBook", "Order book", "Review depth and quote quality before sending orders.", "trading", "Market Feed", "\uE8FD", 20, ShellNavigationVisibilityTier.Primary, ["order book", "depth", "quotes"], ["LiveData", "PositionBlotter"]),
        Page<PositionBlotterPage>("PositionBlotter", "Position blotter", "Review open positions and realized or unrealized P&L.", "trading", "Execution", "\uE8FD", 30, ShellNavigationVisibilityTier.Primary, ["positions", "blotter", "pnl", "review"], ["RunPortfolio", "RunRisk", "RunLedger", "FundPortfolio"], ["Blotter"]),
        Page<RunRiskPage>("RunRisk", "Run risk", "Monitor risk metrics, limits, and drawdown alerts.", "trading", "Execution", "\uE7BA", 40, ShellNavigationVisibilityTier.Primary, ["risk", "limits", "monitor"], ["RunPortfolio", "PositionBlotter", "RunLedger", "GovernanceShell"]),
        Page<TradingHoursPage>("TradingHours", "Trading hours", "Review market sessions and schedule coverage before execution.", "trading", "Execution", "\uE823", 50, ShellNavigationVisibilityTier.Secondary, ["calendar", "hours", "sessions", "review"], ["LiveData", "OrderBook"]),
        Page<RunLedgerPage>("RunLedger", "Run ledger", "Review run postings and execution-linked cash movement.", "trading", "Inspectors", "\uE82D", 60, ShellNavigationVisibilityTier.Secondary, ["ledger", "postings", "review"], ["PositionBlotter", "RunRisk", "FundLedger"], ["LedgerInspector"]),
        Page<AccountPortfolioPage>("AccountPortfolio", "Account portfolio", "Review account positions and allocation changes.", "trading", "Inspectors", "\uE821", 70, ShellNavigationVisibilityTier.Secondary, ["account", "portfolio", "review"], ["RunPortfolio", "AggregatePortfolio", "FundPortfolio"]),
        Page<AggregatePortfolioPage>("AggregatePortfolio", "Aggregate portfolio", "Monitor desk-level exposure across all accounts.", "trading", "Inspectors", "\uE821", 80, ShellNavigationVisibilityTier.Secondary, ["aggregate", "portfolio", "monitor"], ["RunPortfolio", "AccountPortfolio", "FundPortfolio"]),
        Page<DirectLendingPage>("DirectLending", "Direct lending", "Review lending positions and configure credit workflows.", "trading", "Specialty", "\uE8C7", 90, ShellNavigationVisibilityTier.Overflow, ["lending", "credit", "configure"], ["TradingShell", "FundPortfolio"])
    ];
}

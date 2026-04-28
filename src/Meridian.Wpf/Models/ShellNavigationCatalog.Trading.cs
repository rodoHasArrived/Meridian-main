using Meridian.Wpf.Views;

namespace Meridian.Wpf.Models;

public static partial class ShellNavigationCatalog
{
    private static readonly ShellPageDescriptor[] TradingPages =
    [
        Page<TradingWorkspaceShellPage>("TradingShell", "Trading Workspace", "Monitor markets, review orders, and track risk across active runs.", "trading", "Desk", "\uE945", 0, ShellNavigationVisibilityTier.Primary, ["trading", "home", "workspace", "monitor"], ["LiveData", "OrderBook", "PositionBlotter", "RunRisk", "PortfolioShell"], ["TradingWorkspace"]),
        Page<LiveDataViewerPage>("LiveData", "Live data", "Monitor streaming quotes, venue state, and feed health.", "trading", "Market Feed", "\uE9D2", 10, ShellNavigationVisibilityTier.Primary, ["streaming", "market", "feed", "monitor"], ["OrderBook", "PositionBlotter"], ["LiveDataViewer"]),
        Page<OrderBookPage>("OrderBook", "Order book", "Review depth and quote quality before sending orders.", "trading", "Market Feed", "\uE8FD", 20, ShellNavigationVisibilityTier.Primary, ["order book", "depth", "quotes"], ["LiveData", "PositionBlotter"]),
        Page<PositionBlotterPage>("PositionBlotter", "Position blotter", "Review open positions and realized or unrealized P&L.", "trading", "Execution", "\uE8FD", 30, ShellNavigationVisibilityTier.Primary, ["positions", "blotter", "pnl", "review"], ["AccountPortfolio", "RunRisk", "RunLedger", "FundPortfolio"], ["Blotter"]),
        Page<RunRiskPage>("RunRisk", "Run risk", "Monitor risk metrics, limits, and drawdown alerts.", "trading", "Execution", "\uE7BA", 40, ShellNavigationVisibilityTier.Primary, ["risk", "limits", "monitor"], ["PositionBlotter", "RunLedger", "AccountingShell"]),
        Page<TradingHoursPage>("TradingHours", "Trading hours", "Review market sessions and schedule coverage before execution.", "trading", "Execution", "\uE823", 50, ShellNavigationVisibilityTier.Secondary, ["calendar", "hours", "sessions", "review"], ["LiveData", "OrderBook"])
    ];

    private static readonly ShellPageDescriptor[] PortfolioPages =
    [
        Page<WorkspaceCapabilityHomePage>("PortfolioShell", "Portfolio Workspace", "Review account, aggregate, fund, lending, and import workflows.", "portfolio", "Launchpad", "\uE821", 0, ShellNavigationVisibilityTier.Primary, ["portfolio", "home", "workspace", "accounts"], ["AccountPortfolio", "AggregatePortfolio", "FundPortfolio", "FundAccounts"]),
        Page<AccountPortfolioPage>("AccountPortfolio", "Account portfolio", "Review account positions and allocation changes.", "portfolio", "Accounts", "\uE821", 10, ShellNavigationVisibilityTier.Primary, ["account", "portfolio", "review"], ["AggregatePortfolio", "FundPortfolio", "PositionBlotter"]),
        Page<AggregatePortfolioPage>("AggregatePortfolio", "Aggregate portfolio", "Monitor exposure across all accounts.", "portfolio", "Accounts", "\uE821", 20, ShellNavigationVisibilityTier.Primary, ["aggregate", "portfolio", "monitor"], ["AccountPortfolio", "FundPortfolio", "RunPortfolio"]),
        Page<RunPortfolioPage>("RunPortfolio", "Run portfolio", "Review holdings, exposures, and weights for the selected run.", "portfolio", "Run Inspectors", "\uE821", 30, ShellNavigationVisibilityTier.Primary, ["portfolio", "positions", "review"], ["RunLedger", "RunCashFlow", "AccountingShell"], ["PortfolioInspector"]),
        Page<FundLedgerPage>("FundPortfolio", "Fund portfolio", "Inspect fund-scoped positions and exposure detail.", "portfolio", "Fund", "\uE821", 40, ShellNavigationVisibilityTier.Secondary, ["fund portfolio", "exposure"], ["FundAccounts", "FundCashFinancing", "AggregatePortfolio"]),
        Page<FundAccountsPage>("FundAccounts", "Fund accounts", "Inspect account balances, routing, and reconciliation readiness.", "portfolio", "Fund", "\uE8C7", 50, ShellNavigationVisibilityTier.Secondary, ["accounts", "balances"], ["FundPortfolio", "FundBanking", "FundReconciliation"]),
        Page<PortfolioImportPage>("PortfolioImport", "Portfolio import", "Import external portfolio snapshots for reconciliation.", "portfolio", "Import", "\uE8B5", 60, ShellNavigationVisibilityTier.Secondary, ["import", "portfolio", "reconcile"], ["AccountPortfolio", "FundPortfolio", "Symbols"]),
        Page<DirectLendingPage>("DirectLending", "Direct lending", "Review lending positions and configure credit workflows.", "portfolio", "Specialty", "\uE8C7", 70, ShellNavigationVisibilityTier.Overflow, ["lending", "credit", "configure"], ["FundPortfolio", "AccountingShell"])
    ];
}

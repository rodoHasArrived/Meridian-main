using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// The one catalog of workstation headline figures (UX Phase A). Each figure has a fixed label,
/// the population it counts and its comparison basis; the browser's <c>lib/kpi-registry.ts</c>
/// mirrors it and <c>tests/fixtures/kpi-definitions/kpi-definitions.json</c> holds both to the
/// same values. Served at <c>/api/workstation/kpi-definitions</c> for the WPF desktop.
/// </summary>
public static class WorkstationKpiDefinitions
{
    private const string LiveHoldings = "Live holdings · all accounts";
    private const string RetainedRecords = "Retained run records";
    private const string BrokerageSession = "Brokerage session · selected account";
    private const string RecentRuns = "Recent strategy runs";
    private const string ProvidersInUse = "Providers in use";
    private const string DataOperations = "Data operations";
    private const string AccountingWorkspace = "Accounting workspace";

    public static IReadOnlyList<WorkstationKpiDefinitionDto> All { get; } =
    [
        new("accounting.audit-ready", "Audit ready", AccountingWorkspace, null),
        new("accounting.kernel-critical-jumps", "Kernel jump alerts", AccountingWorkspace, null),
        new("accounting.open-breaks", "Open breaks", "Unresolved reconciliation breaks", "Your last visit"),
        new("accounting.security-gaps", "Security gaps", AccountingWorkspace, null),
        new("accounting.timing-drift", "Timing drift", AccountingWorkspace, null),
        new("data.backfills-running", "Backfills running", DataOperations, null),
        new("data.exports-ready", "Exports ready", DataOperations, null),
        new("data.kernel-critical-jumps", "Kernel jump alerts", DataOperations, null),
        new("data.ops-review", "Needs review", DataOperations, null),
        new("data.providers-healthy", "Providers healthy", ProvidersInUse, null),
        new("data.replacement-cost", "Est. replacement cost", DataOperations, null),
        new("portfolio.cash", "Cash", LiveHoldings, null),
        new("portfolio.open-positions", "Open positions", LiveHoldings, null),
        new("portfolio.retained-market-value", "Market value", RetainedRecords, null),
        new("portfolio.retained-records", "Retained positions", RetainedRecords, null),
        new("portfolio.total-exposure", "Total exposure", LiveHoldings, null),
        new("portfolio.unrealized-pnl", "Unrealized P&L", LiveHoldings, null),
        new("strategy.active-runs", "Active recent runs", RecentRuns, null),
        new("strategy.promotions", "Promotions", RecentRuns, null),
        new("strategy.queued", "Queued", RecentRuns, null),
        new("strategy.queued-runs", "Queued promotions", RecentRuns, null),
        new("strategy.review-runs", "Needs review", RecentRuns, null),
        new("strategy.winning-runs", "Positive P&L", RecentRuns, null),
        new("system.active-runs", "Active runs", "All strategy runs · system", null),
        new("system.providers-online", "Providers healthy", ProvidersInUse, null),
        new("system.storage-health", "Storage health", "Storage · system", null),
        new("trading.cash", "Cash", BrokerageSession, null),
        new("trading.fills", "Fills", BrokerageSession, null),
        new("trading.net-pnl", "Net P&L", BrokerageSession, "Session open"),
        new("trading.open-orders", "Open orders", "Open orders · selected account", null),
        new("trading.portfolio-value", "Portfolio value", BrokerageSession, null),
        new("trading.risk", "Risk", BrokerageSession, null)
    ];

    public static WorkstationKpiDefinitionsDto Catalog { get; } = new(All);
}

namespace Meridian.Contracts.Workstation;

/// <summary>
/// Shared root workspace metadata used by browser and desktop workstation surfaces.
/// </summary>
public sealed record WorkstationWorkspaceDefinition(
    string Key,
    string Label,
    string BrowserDescription,
    // Retain the public positional member name for contract and named-argument
    // compatibility. Its values are now restricted to product maturity.
    string BrowserStatus,
    string DesktopDescription,
    string DesktopSummary,
    string DesktopTileSummary,
    string DesktopShellDisplayName);

public static class WorkstationWorkspaceCatalog
{
    public const string TradingKey = "trading";
    public const string TradingLabel = "Trading";
    public const string TradingBrowserDescription = "Paper cockpit readiness, sessions, orders, positions, replay, and promotion evidence.";
    public const string TradingBrowserMaturity = "Available";
    [Obsolete("Use TradingBrowserMaturity.")]
    public const string TradingBrowserStatus = TradingBrowserMaturity;
    public const string TradingDesktopDescription = "Live readiness, order flow, execution, and risk-aware monitoring.";
    public const string TradingDesktopSummary = "Operate trading workflows with paper/live separation and audit reachability.";
    public const string TradingDesktopTileSummary = "Live | Orders | Risk";
    public const string TradingDesktopShellDisplayName = "Trading Desk";

    public const string PortfolioKey = "portfolio";
    public const string PortfolioLabel = "Portfolio";
    public const string PortfolioBrowserDescription = "Portfolio exposure, positions, attribution, fills, and run-level equity continuity.";
    public const string PortfolioBrowserMaturity = "Preview";
    [Obsolete("Use PortfolioBrowserMaturity.")]
    public const string PortfolioBrowserStatus = PortfolioBrowserMaturity;
    public const string PortfolioDesktopDescription = "Account, aggregate, fund, lending, and portfolio import review.";
    public const string PortfolioDesktopSummary = "Review account and fund exposure with import and lending workflows nearby.";
    public const string PortfolioDesktopTileSummary = "Accounts | Exposure | Import";
    public const string PortfolioDesktopShellDisplayName = "Portfolio Workspace";

    public const string AccountingKey = "accounting";
    public const string AccountingLabel = "Accounting";
    public const string AccountingBrowserDescription = "Ledger, cash-flow, reconciliation, Security Master coverage, and fund-account evidence.";
    public const string AccountingBrowserMaturity = "Available";
    [Obsolete("Use AccountingBrowserMaturity.")]
    public const string AccountingBrowserStatus = AccountingBrowserMaturity;
    public const string AccountingDesktopDescription = "Ledger, cash, banking, reconciliation, trial balance, and audit review.";
    public const string AccountingDesktopSummary = "Operate accounting, reconciliation, cash, banking, and audit evidence from one area.";
    public const string AccountingDesktopTileSummary = "Ledger | Cash | Audit";
    public const string AccountingDesktopShellDisplayName = "Accounting Workspace";

    public const string ReportingKey = "reporting";
    public const string ReportingLabel = "Reporting";
    public const string ReportingBrowserDescription = "Report packs, governed exports, loader scripts, data dictionaries, and approval posture.";
    public const string ReportingBrowserMaturity = "Available";
    [Obsolete("Use ReportingBrowserMaturity.")]
    public const string ReportingBrowserStatus = ReportingBrowserMaturity;
    public const string ReportingDesktopDescription = "Report packs, dashboards, analysis export, and reusable export presets.";
    public const string ReportingDesktopSummary = "Prepare reporting outputs and analysis handoffs from a focused workspace.";
    public const string ReportingDesktopTileSummary = "Packs | Dashboard | Export";
    public const string ReportingDesktopShellDisplayName = "Reporting Workspace";

    public const string StrategyKey = "strategy";
    public const string StrategyLabel = "Strategy";
    public const string StrategyBrowserDescription = "Backtest runs, comparisons, run diffing, and paper-promotion review.";
    public const string StrategyBrowserMaturity = "Available";
    [Obsolete("Use StrategyBrowserMaturity.")]
    public const string StrategyBrowserStatus = StrategyBrowserMaturity;
    public const string StrategyDesktopDescription = "Backtest studio, run comparison, charts, scripting, and replay flows.";
    public const string StrategyDesktopSummary = "Operate model exploration with docked run, portfolio, and promotion context.";
    public const string StrategyDesktopTileSummary = "Runs | Compare | Scripts";
    public const string StrategyDesktopShellDisplayName = "Strategy Studio";

    public const string DataKey = "data";
    public const string DataLabel = "Data";
    public const string DataBrowserDescription = "Provider posture, backfill queues, symbol readiness, and data-quality handoffs.";
    public const string DataBrowserMaturity = "Available";
    [Obsolete("Use DataBrowserMaturity.")]
    public const string DataBrowserStatus = DataBrowserMaturity;
    public const string DataDesktopDescription = "Providers, ingestion, storage, schedules, quality, and data products.";
    public const string DataDesktopSummary = "Operate collection queues, freshness checks, storage, and exports from one workspace.";
    public const string DataDesktopTileSummary = "Providers | Storage | Quality";
    public const string DataDesktopShellDisplayName = "Data Workspace";

    public const string SettingsKey = "settings";
    public const string SettingsLabel = "Settings";
    public const string SettingsBrowserDescription = "Operator session context, shell preferences, integrations, and workstation setup checks.";
    public const string SettingsBrowserMaturity = "Setup";
    [Obsolete("Use SettingsBrowserMaturity.")]
    public const string SettingsBrowserStatus = SettingsBrowserMaturity;
    public const string SettingsDesktopDescription = "Preferences, credentials, diagnostics, services, alerts, help, and setup.";
    public const string SettingsDesktopSummary = "Manage workstation configuration, support, notifications, diagnostics, and setup.";
    public const string SettingsDesktopTileSummary = "Prefs | Health | Help";
    public const string SettingsDesktopShellDisplayName = "Settings Workspace";

    public static IReadOnlyList<WorkstationWorkspaceDefinition> RootWorkspaces { get; } =
    [
        new(
            TradingKey,
            TradingLabel,
            TradingBrowserDescription,
            TradingBrowserMaturity,
            TradingDesktopDescription,
            TradingDesktopSummary,
            TradingDesktopTileSummary,
            TradingDesktopShellDisplayName),
        new(
            PortfolioKey,
            PortfolioLabel,
            PortfolioBrowserDescription,
            PortfolioBrowserMaturity,
            PortfolioDesktopDescription,
            PortfolioDesktopSummary,
            PortfolioDesktopTileSummary,
            PortfolioDesktopShellDisplayName),
        new(
            AccountingKey,
            AccountingLabel,
            AccountingBrowserDescription,
            AccountingBrowserMaturity,
            AccountingDesktopDescription,
            AccountingDesktopSummary,
            AccountingDesktopTileSummary,
            AccountingDesktopShellDisplayName),
        new(
            ReportingKey,
            ReportingLabel,
            ReportingBrowserDescription,
            ReportingBrowserMaturity,
            ReportingDesktopDescription,
            ReportingDesktopSummary,
            ReportingDesktopTileSummary,
            ReportingDesktopShellDisplayName),
        new(
            StrategyKey,
            StrategyLabel,
            StrategyBrowserDescription,
            StrategyBrowserMaturity,
            StrategyDesktopDescription,
            StrategyDesktopSummary,
            StrategyDesktopTileSummary,
            StrategyDesktopShellDisplayName),
        new(
            DataKey,
            DataLabel,
            DataBrowserDescription,
            DataBrowserMaturity,
            DataDesktopDescription,
            DataDesktopSummary,
            DataDesktopTileSummary,
            DataDesktopShellDisplayName),
        new(
            SettingsKey,
            SettingsLabel,
            SettingsBrowserDescription,
            SettingsBrowserMaturity,
            SettingsDesktopDescription,
            SettingsDesktopSummary,
            SettingsDesktopTileSummary,
            SettingsDesktopShellDisplayName)
    ];
}

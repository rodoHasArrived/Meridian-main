using System.Linq;

namespace Meridian.Wpf.Models;

/// <summary>
/// Static registry of workspace definitions for saved desktop templates.
/// Each workspace maps to a set of related pages from Pages.cs.
/// Page names must match the tags registered in NavigationService.RegisterAllPages().
///
/// Icon values are single-character Segoe MDL2 Assets glyph strings.
/// The corresponding Segoe MDL2 Unicode code points are declared as StaticResource
/// keys in Styles/IconResources.xaml (e.g. IconResearch, IconTrading …).
/// High-quality SVG source files for every icon live in Assets/Icons/*.svg.
/// </summary>
public static class WorkspaceRegistry
{
    /// <summary>
    /// All built-in workspace definitions, in canonical operator navigation order.
    /// </summary>
    public static readonly IReadOnlyList<WorkspaceDefinition> All = new[]
    {
        // ── Trading Workspace ───────────────────────────────────────────────────
        new WorkspaceDefinition(
            Id: "trading",
            Label: "Trading",
            Icon: "\uE945",   // Lightning   — SVG: Assets/Icons/trading.svg
            Pages: new WorkspacePageEntry[]
            {
                new("TradingShell",  "Trading Desk",  "\uE945"),
                new("LiveData",     "Live Data",    "\uE9D2"),  // LineChart
                new("OrderBook",    "Order Book",   "\uE8FD"),  // List
                new("PositionBlotter","Positions",   "\uE8A1"),  // List
                new("RunRisk",      "Run Risk",     "\uE7BA"),  // Shield
                new("TradingHours",  "Trading Hours","\uE823"),  // Clock
            }),

        // ── Portfolio Workspace ─────────────────────────────────────────────────
        new WorkspaceDefinition(
            Id: "portfolio",
            Label: "Portfolio",
            Icon: "\uE821",   // Briefcase   — SVG: Assets/Icons/portfolio.svg
            Pages: new WorkspacePageEntry[]
            {
                new("PortfolioShell", "Portfolio Workspace", "\uE821"),
                new("AccountPortfolio", "Account Portfolio", "\uE821"),
                new("AggregatePortfolio", "Aggregate Portfolio", "\uE821"),
                new("FundPortfolio", "Fund Portfolio", "\uE821"),
                new("FundAccounts", "Fund Accounts", "\uE8C7"),
                new("PortfolioImport", "Portfolio Import", "\uE8B5"),
            }),

        // ── Accounting Workspace ────────────────────────────────────────────────
        new WorkspaceDefinition(
            Id: "accounting",
            Label: "Accounting",
            Icon: "\uE8D7",   // Permissions — SVG: Assets/Icons/governance.svg
            Pages: new WorkspacePageEntry[]
            {
                new("AccountingShell", "Accounting Workspace", "\uE8D7"),
                new("FundLedger", "Fund Ledger", "\uE82D"),
                new("FundReconciliation", "Fund Reconciliation", "\uE9D9"),
                new("FundTrialBalance", "Trial Balance", "\uE9D9"),
                new("FundAuditTrail", "Audit Trail", "\uE8A5"),
                new("SecurityMaster", "Security Master", "\uE72E"),
            }),

        // ── Reporting Workspace ────────────────────────────────────────────────
        new WorkspaceDefinition(
            Id: "reporting",
            Label: "Reporting",
            Icon: "\uE9D9",   // BarChart
            Pages: new WorkspacePageEntry[]
            {
                new("ReportingShell", "Reporting Workspace", "\uE9D9"),
                new("FundReportPack", "Report Packs", "\uE8A5"),
                new("ReportRunStatus", "Report Runs", "\uE768"),
                new("Dashboard", "Dashboard", "\uE9D9"),
                new("AnalysisExport", "Analysis Export", "\uEDE1"),
                new("ExportPresets", "Export Presets", "\uE8FD"),
            }),

        // ── Strategy Workspace ──────────────────────────────────────────────────
        new WorkspaceDefinition(
            Id: "strategy",
            Label: "Strategy",
            Icon: "\uEC35",   // TestBeaker  — SVG: Assets/Icons/research.svg
            Pages: new WorkspacePageEntry[]
            {
                new("StrategyShell", "Strategy Workspace", "\uEC35"),
                new("Backtest", "Backtest", "\uEC35"),
                new("StrategyRuns", "Strategy Runs", "\uE768"),
                new("RunDetail", "Run Detail", "\uE8A5"),
                new("RunMat", "RunMat", "\uE8CF"),
                new("Charts", "Charting", "\uE9D9"),
                new("QuantScript", "Quant Script", "\uE943"),
            }),

        // ── Data Workspace ──────────────────────────────────────────────────────
        new WorkspaceDefinition(
            Id: "data",
            Label: "Data",
            Icon: "\uEE94",   // Database    — SVG: Assets/Icons/data-operations.svg
            Pages: new WorkspacePageEntry[]
            {
                new("DataShell",          "Data Workspace",       "\uEE94"),
                new("Provider",           "Provider Setup",        "\uEC05"),
                new("Symbols",            "Symbols",              "\uE8AB"), // Sort/Exchange
                new("Backfill",           "Backfill",             "\uE892"), // Rewind
                new("ProviderHealth",     "Provider Health",      "\uEB51"), // Heart
                new("DataSources",        "Data Sources",         "\uEC05"), // Wireless
                new("DataQuality",        "Data Quality",         "\uE73E"), // Checkmark
                new("DataCalendar",       "Data Calendar",        "\uE787"), // Calendar
                new("DataExport",         "Data Export",          "\uEDE1"), // Export
                new("Storage",            "Storage",              "\uEE94"), // Database
                new("EventReplay",        "Event Replay",         "\uE768"), // Play
                new("Schedules",          "Schedule Manager",     "\uE916"), // Timer
                new("CollectionSessions", "Collection Sessions",  "\uE8EF"), // VideoCapture
            }),

        // ── Settings Workspace ──────────────────────────────────────────────────
        new WorkspaceDefinition(
            Id: "settings",
            Label: "Settings",
            Icon: "\uE713",   // Settings
            Pages: new WorkspacePageEntry[]
            {
                new("SettingsShell",     "Settings Workspace", "\uE713"),
                new("Settings",          "Settings",           "\uE713"), // Settings
                new("Diagnostics",       "Diagnostics",        "\uE90F"), // Repair
                new("ServiceManager",    "Service Manager",    "\uECE7"), // Plug
                new("AdminMaintenance",  "Admin Maintenance",  "\uE74D"), // BulkEdit
                new("RetentionAssurance","Retention Assurance","\uE8A5"), // Document
                new("LeanIntegration",   "Lean Integration",   "\uE71B"), // Link
                new("SystemHealth",      "System Health",      "\uE9D9"), // BarChart
                new("Help",              "Help",               "\uE897"), // Help
                new("KeyboardShortcuts", "Keyboard Shortcuts", "\uE765"), // Keyboard
            }),
    };

    /// <summary>
    /// Gets a workspace by ID, or null if not found.
    /// </summary>
    public static WorkspaceDefinition? GetWorkspaceById(string id)
    {
        var canonicalId = WorkstationNavigationDefaults.NormalizeWorkspaceId(id);
        return All.FirstOrDefault(w => string.Equals(w.Id, canonicalId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets the default workspace.
    /// </summary>
    public static WorkspaceDefinition GetDefaultWorkspace()
    {
        return GetWorkspaceById(WorkstationNavigationDefaults.StrategyWorkspaceId) ?? All.First();
    }
}

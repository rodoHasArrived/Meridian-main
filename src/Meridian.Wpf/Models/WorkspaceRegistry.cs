using System.Linq;

namespace Meridian.Wpf.Models;

/// <summary>
/// Static registry of workspace definitions for the 4-workspace navigation shell.
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
    /// All workspace definitions, in order (Research, Trading, Data Ops, Governance).
    /// </summary>
    public static readonly IReadOnlyList<WorkspaceDefinition> All = new[]
    {
        // ── Research Workspace ──────────────────────────────────────────────────
        new WorkspaceDefinition(
            Id: "research",
            Label: "Research",
            Icon: "\uEC35",   // TestBeaker  — SVG: Assets/Icons/research.svg
            Pages: new WorkspacePageEntry[]
            {
                new("Dashboard",    "Dashboard",    "\uE71D"),  // Dashboard (tiles)
                new("LiveData",     "Live Data",    "\uE9D2"),  // LineChart
                new("OrderBook",    "Order Book",   "\uE8FD"),  // List
                new("Charts",       "Charting",     "\uE9D9"),  // Chart
                new("DataBrowser",  "Data Browser", "\uE721"),  // Search
                new("DataSampling", "Data Sampling","\uF1AD"),  // DataUsage
                new("SymbolStorage","Symbol Storage","\uEE94"), // Database
            }),

        // ── Trading Workspace ───────────────────────────────────────────────────
        new WorkspaceDefinition(
            Id: "trading",
            Label: "Trading",
            Icon: "\uE945",   // Lightning   — SVG: Assets/Icons/trading.svg
            Pages: new WorkspacePageEntry[]
            {
                new("Backtest",       "Backtest",        "\uEC35"), // TestBeaker
                new("StrategyRuns",   "Strategy Runs",   "\uE768"), // Play
                new("RunDetail",      "Run Detail",      "\uE8A5"), // Document
                new("RunLedger",      "Run Ledger",      "\uE82D"), // Library
                new("RunPortfolio",   "Run Portfolio",   "\uE821"), // Briefcase
                new("RunMat",         "RunMat",          "\uE8CF"), // Target
                new("TradingHours",   "Trading Hours",   "\uE823"), // Clock
            }),

        // ── Data Ops Workspace ──────────────────────────────────────────────────
        new WorkspaceDefinition(
            Id: "data-operations",
            Label: "Data Operations",
            Icon: "\uEE94",   // Database    — SVG: Assets/Icons/data-operations.svg
            Pages: new WorkspacePageEntry[]
            {
                new("Symbols",            "Symbols",              "\uE8AB"), // Sort/Exchange
                new("Backfill",           "Backfill",             "\uE892"), // Rewind
                new("ProviderHealth",     "Provider Health",      "\uEB51"), // Heart
                new("DataSources",        "Data Sources",         "\uEC05"), // Wireless
                new("DataQuality",        "Data Quality",         "\uE73E"), // Checkmark
                new("DataCalendar",       "Data Calendar",        "\uE787"), // Calendar
                new("DataExport",         "Data Export",          "\uEDE1"), // Export
                new("StorageOptimization","Storage Optimization", "\uE713"), // Settings
                new("Storage",            "Storage",              "\uEE94"), // Database
                new("ArchiveHealth",      "Archive Health",       "\uE8E3"), // Accept
                new("EventReplay",        "Event Replay",         "\uE768"), // Play
                new("IndexSubscription",  "Index Subscription",   "\uE8FD"), // List
                new("PortfolioImport",    "Portfolio Import",     "\uE8B5"), // Import
                new("Watchlist",          "Watchlist",            "\uE7B3"), // View
                new("Schedules",          "Schedule Manager",     "\uE916"), // Timer
                new("CollectionSessions", "Collection Sessions",  "\uE8EF"), // VideoCapture
            }),

        // ── Governance Workspace ────────────────────────────────────────────────
        new WorkspaceDefinition(
            Id: "governance",
            Label: "Governance",
            Icon: "\uE8D7",   // Permissions — SVG: Assets/Icons/governance.svg
            Pages: new WorkspacePageEntry[]
            {
                new("SecurityMaster",    "Security Master",    "\uE72E"), // Lock
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
        return All.FirstOrDefault(w => w.Id == id);
    }

    /// <summary>
    /// Gets the first workspace (Research by default).
    /// </summary>
    public static WorkspaceDefinition GetDefaultWorkspace()
    {
        return All.First();
    }
}

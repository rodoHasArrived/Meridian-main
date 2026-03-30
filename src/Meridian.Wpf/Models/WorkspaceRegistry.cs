using System.Linq;

namespace Meridian.Wpf.Models;

/// <summary>
/// Static registry of workspace definitions for the 4-workspace navigation shell.
/// Each workspace maps to a set of related pages from Pages.cs.
/// Page names must match the tags registered in NavigationService.RegisterAllPages().
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
            Icon: "🔬",
            Pages: new WorkspacePageEntry[]
            {
                new("Dashboard", "Dashboard", "📊"),
                new("LiveData", "Live Data", "📈"),
                new("OrderBook", "Order Book", "📋"),
                new("Charts", "Charting", "📉"),
                new("DataBrowser", "Data Browser", "🔍"),
                new("DataSampling", "Data Sampling", "🎲"),
                new("SymbolStorage", "Symbol Storage", "💾"),
            }),

        // ── Trading Workspace ───────────────────────────────────────────────────
        new WorkspaceDefinition(
            Id: "trading",
            Label: "Trading",
            Icon: "⚡",
            Pages: new WorkspacePageEntry[]
            {
                new("Backtest", "Backtest", "🧪"),
                new("StrategyRuns", "Strategy Runs", "🚀"),
                new("RunDetail", "Run Detail", "📑"),
                new("RunLedger", "Run Ledger", "📚"),
                new("RunPortfolio", "Run Portfolio", "💼"),
                new("RunMat", "RunMat", "🎯"),
                new("TradingHours", "Trading Hours", "🕐"),
            }),

        // ── Data Ops Workspace ──────────────────────────────────────────────────
        new WorkspaceDefinition(
            Id: "data-operations",
            Label: "Data Operations",
            Icon: "🗂",
            Pages: new WorkspacePageEntry[]
            {
                new("Symbols", "Symbols", "🔤"),
                new("Backfill", "Backfill", "⏮"),
                new("ProviderHealth", "Provider Health", "❤️"),
                new("DataSources", "Data Sources", "📡"),
                new("DataQuality", "Data Quality", "✓"),
                new("DataCalendar", "Data Calendar", "📅"),
                new("DataExport", "Data Export", "📤"),
                new("StorageOptimization", "Storage Optimization", "⚙️"),
                new("Storage", "Storage", "💿"),
                new("ArchiveHealth", "Archive Health", "🏥"),
                new("EventReplay", "Event Replay", "▶️"),
                new("IndexSubscription", "Index Subscription", "📑"),
                new("PortfolioImport", "Portfolio Import", "📥"),
                new("Watchlist", "Watchlist", "👁"),
                new("Schedules", "Schedule Manager", "⏰"),
                new("CollectionSessions", "Collection Sessions", "🎬"),
            }),

        // ── Governance Workspace ────────────────────────────────────────────────
        new WorkspaceDefinition(
            Id: "governance",
            Label: "Governance",
            Icon: "🛡",
            Pages: new WorkspacePageEntry[]
            {
                new("SecurityMaster", "Security Master", "🔐"),
                new("Settings", "Settings", "⚙️"),
                new("Diagnostics", "Diagnostics", "🔧"),
                new("ServiceManager", "Service Manager", "🔌"),
                new("AdminMaintenance", "Admin Maintenance", "🧹"),
                new("RetentionAssurance", "Retention Assurance", "📋"),
                new("LeanIntegration", "Lean Integration", "🔗"),
                new("SystemHealth", "System Health", "📊"),
                new("Help", "Help", "❓"),
                new("KeyboardShortcuts", "Keyboard Shortcuts", "⌨️"),
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

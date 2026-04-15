using System.Collections.ObjectModel;

namespace Meridian.Wpf.Models;

public static class ShellNavigationCatalog
{
    public static readonly IReadOnlyList<WorkspaceShellDescriptor> Workspaces =
    [
        new(
            Id: "research",
            Title: "Research",
            Description: "Backtest studio, run comparison, charts, and investigation flows.",
            Summary: "Operate model exploration with docked run, portfolio, and promotion context.",
            HomePageTag: "ResearchShell",
            TileSummary: "Runs · Compare · Promote"),
        new(
            Id: "trading",
            Title: "Trading",
            Description: "Live posture, order flow, execution, and risk-aware monitoring.",
            Summary: "Operate the trading cockpit with explicit paper/live separation and audit reachability.",
            HomePageTag: "TradingShell",
            TileSummary: "Live · Orders · Risk"),
        new(
            Id: "data-operations",
            Title: "Data Operations",
            Description: "Providers, ingestion, storage, schedules, exports, and blocker visibility.",
            Summary: "Operate the collection queue, freshness posture, and export pipeline from one workstation.",
            HomePageTag: "DataOperationsShell",
            TileSummary: "Providers · Storage · Jobs"),
        new(
            Id: "governance",
            Title: "Governance",
            Description: "Controls, diagnostics, fund operations, reconciliation, and trust-critical review.",
            Summary: "Operate accounts, ledger, reconciliation, and audit work from a single control surface.",
            HomePageTag: "GovernanceShell",
            TileSummary: "Ledger · Audit · Controls")
    ];

    public static readonly IReadOnlyList<ShellPageDescriptor> Pages =
    [
        // Research
        Page("ResearchShell", "Research Workspace", "Backtest studio shell with active run context, compare lanes, and promotion rails.", "research", "Launchpad", "\uEC35", 0, ShellNavigationVisibilityTier.Primary, ["research", "workspace", "studio", "launchpad"], ["StrategyRuns", "Backtest", "RunPortfolio", "TradingShell"]),
        Page("Backtest", "Backtest", "Configure and launch new simulations from the studio workbench.", "research", "Studio", "\uEC35", 10, ShellNavigationVisibilityTier.Primary, ["simulation", "backtesting", "scenario"], ["StrategyRuns", "RunDetail", "RunPortfolio"]),
        Page("StrategyRuns", "Strategy Runs", "Browse completed and active runs, compare outcomes, and drill into evidence.", "research", "Studio", "\uE8FD", 20, ShellNavigationVisibilityTier.Primary, ["runs", "compare", "history"], ["RunDetail", "RunPortfolio", "RunLedger", "TradingShell"]),
        Page("Charts", "Charts", "Inspect overlays, annotations, and investigation views for strategy behavior.", "research", "Studio", "\uE9D9", 30, ShellNavigationVisibilityTier.Primary, ["charting", "analysis", "visual"], ["Backtest", "StrategyRuns", "AdvancedAnalytics"]),
        Page("RunMat", "Run Mat", "Prototype research scripts and run external analytics utilities inside the workstation.", "research", "Studio", "\uE943", 40, ShellNavigationVisibilityTier.Primary, ["automation", "script", "tooling"], ["QuantScript", "StrategyRuns"]),
        Page("Watchlist", "Watchlist", "Stage symbols, shortlist ideas, and hand candidates into research or trading flows.", "research", "Desk", "\uE8D4", 50, ShellNavigationVisibilityTier.Primary, ["symbols", "monitoring"], ["TradingShell", "LiveData", "OrderBook"]),
        Page("Dashboard", "Dashboard", "Legacy global overview with live posture, alerts, and action shortcuts.", "research", "Legacy", "\uE71D", 60, ShellNavigationVisibilityTier.Secondary, ["overview", "status"], ["ResearchShell", "NotificationCenter"], hideFromDefaultPalette: true),
        Page("BatchBacktest", "Batch Backtest", "Supervise multi-run research jobs and inspect grouped outcomes.", "research", "Studio", "\uE768", 70, ShellNavigationVisibilityTier.Secondary, ["batch", "queue"], ["Backtest", "StrategyRuns"]),
        Page("RunDetail", "Run Detail", "Inspect run diagnostics, execution state, and evidence for a selected simulation.", "research", "Inspectors", "\uE8A5", 80, ShellNavigationVisibilityTier.Secondary, ["detail", "diagnostics"], ["RunPortfolio", "RunLedger", "RunCashFlow"]),
        Page("RunPortfolio", "Run Portfolio", "Review holdings, exposures, and position context for the selected run.", "research", "Inspectors", "\uE821", 90, ShellNavigationVisibilityTier.Secondary, ["portfolio", "positions"], ["RunLedger", "RunCashFlow", "GovernanceShell"]),
        Page("RunCashFlow", "Run Cash Flow", "Inspect simulated cash movement, financing, and funding impact.", "research", "Inspectors", "\uEAFD", 100, ShellNavigationVisibilityTier.Secondary, ["cash", "financing"], ["RunPortfolio", "RunLedger"]),
        Page("AdvancedAnalytics", "Advanced Analytics", "Open deeper analytics surfaces and higher-order metrics for research review.", "research", "Analysis", "\uE9D9", 110, ShellNavigationVisibilityTier.Secondary, ["metrics", "analytics"], ["Charts", "StrategyRuns"]),
        Page("QuantScript", "Quant Script", "Prototype research logic and calculation workflows inside the desktop shell.", "research", "Analysis", "\uE943", 120, ShellNavigationVisibilityTier.Secondary, ["code", "quant", "script"], ["RunMat", "Backtest"]),
        Page("LeanIntegration", "Lean Integration", "Coordinate Lean connectivity and research engine handoff checks.", "research", "Analysis", "\uE71B", 130, ShellNavigationVisibilityTier.Overflow, ["lean", "engine", "integration"], ["Backtest", "StrategyRuns"]),
        Page("EventReplay", "Event Replay", "Replay captured event streams to inspect sequencing and investigation results.", "research", "Analysis", "\uE768", 140, ShellNavigationVisibilityTier.Overflow, ["replay", "events"], ["StrategyRuns", "Charts"]),

        // Trading
        Page("TradingShell", "Trading Workspace", "Trading cockpit with paper/live context, positions, orders, and risk rails.", "trading", "Cockpit", "\uE945", 0, ShellNavigationVisibilityTier.Primary, ["trading", "workspace", "cockpit"], ["PositionBlotter", "RunRisk", "GovernanceShell"]),
        Page("LiveData", "Live Data", "Monitor streaming market traffic, venue state, and flow health in real time.", "trading", "Market Feed", "\uE9D2", 10, ShellNavigationVisibilityTier.Primary, ["streaming", "market", "feed"], ["OrderBook", "PositionBlotter"]),
        Page("OrderBook", "Order Book", "Inspect market depth, quote quality, and execution context for active instruments.", "trading", "Market Feed", "\uE8FD", 20, ShellNavigationVisibilityTier.Primary, ["depth", "quotes", "book"], ["LiveData", "PositionBlotter"]),
        Page("PositionBlotter", "Position Blotter", "Review open positions, realized and unrealized P&L, and operator posture.", "trading", "Execution", "\uE8FD", 30, ShellNavigationVisibilityTier.Primary, ["positions", "blotter", "pnl"], ["RunRisk", "RunLedger", "FundPortfolio"]),
        Page("RunRisk", "Run Risk", "Inspect run-level risk metrics, drawdown posture, and position-limit breaches.", "trading", "Execution", "\uE7BA", 40, ShellNavigationVisibilityTier.Primary, ["risk", "limits"], ["PositionBlotter", "RunLedger", "GovernanceShell"]),
        Page("TradingHours", "Trading Hours", "Check venue sessions, market hours, and schedule coverage before execution.", "trading", "Execution", "\uE823", 50, ShellNavigationVisibilityTier.Secondary, ["calendar", "hours", "sessions"], ["LiveData", "OrderBook"]),
        Page("RunLedger", "Run Ledger", "Inspect run-specific postings and execution-linked financial movements.", "trading", "Inspectors", "\uE82D", 60, ShellNavigationVisibilityTier.Secondary, ["ledger", "postings"], ["PositionBlotter", "RunRisk", "FundLedger"]),
        Page("AccountPortfolio", "Account Portfolio", "Inspect account-scoped positions and allocations for trading review.", "trading", "Inspectors", "\uE821", 70, ShellNavigationVisibilityTier.Secondary, ["account", "portfolio"], ["AggregatePortfolio", "FundPortfolio"]),
        Page("AggregatePortfolio", "Aggregate Portfolio", "Review aggregated book exposure across the desk.", "trading", "Inspectors", "\uE821", 80, ShellNavigationVisibilityTier.Secondary, ["aggregate", "book"], ["AccountPortfolio", "FundPortfolio"]),
        Page("DirectLending", "Direct Lending", "Operate lending-focused trading and portfolio workflows.", "trading", "Specialty", "\uE8C7", 90, ShellNavigationVisibilityTier.Overflow, ["lending", "credit"], ["TradingShell", "FundPortfolio"]),

        // Data Operations
        Page("DataOperationsShell", "Data Operations Workspace", "Queue-driven data workstation for providers, backfills, storage, and freshness review.", "data-operations", "Launchpad", "\uEE94", 0, ShellNavigationVisibilityTier.Primary, ["data operations", "workspace", "queue"], ["Provider", "Backfill", "Storage", "DataQuality"]),
        Page("Provider", "Providers", "Manage provider integrations, readiness, and operating posture.", "data-operations", "Operations Queue", "\uEC05", 10, ShellNavigationVisibilityTier.Primary, ["provider", "integrations"], ["ProviderHealth", "DataSources", "AddProviderWizard"]),
        Page("Backfill", "Backfill", "Run and supervise gap-filling jobs with freshness and blocker visibility.", "data-operations", "Operations Queue", "\uE896", 20, ShellNavigationVisibilityTier.Primary, ["historical", "fill", "jobs"], ["CollectionSessions", "DataQuality", "Storage"]),
        Page("Symbols", "Symbols", "Search, add, and curate the symbol catalog used across Meridian workflows.", "data-operations", "Catalog", "\uE8AB", 30, ShellNavigationVisibilityTier.Primary, ["symbol", "catalog", "search"], ["SymbolMapping", "SymbolStorage", "SecurityMaster"]),
        Page("Storage", "Storage", "Review storage posture, archive capacity, and persistence health.", "data-operations", "Platform", "\uEE94", 40, ShellNavigationVisibilityTier.Primary, ["storage", "archive", "capacity"], ["StorageOptimization", "ArchiveHealth"]),
        Page("DataExport", "Data Export", "Package and export datasets for downstream consumption.", "data-operations", "Packaging", "\uEDE1", 50, ShellNavigationVisibilityTier.Primary, ["export", "package"], ["ExportPresets", "AnalysisExport"]),
        Page("DataSources", "Data Sources", "Audit source connectivity, feed coverage, and readiness to collect.", "data-operations", "Operations Queue", "\uEC05", 60, ShellNavigationVisibilityTier.Secondary, ["sources", "connectivity"], ["Provider", "ProviderHealth"]),
        Page("SymbolMapping", "Symbol Mapping", "Align vendor symbols and canonical Meridian identifiers.", "data-operations", "Catalog", "\uE8AB", 70, ShellNavigationVisibilityTier.Secondary, ["mapping", "vendor"], ["Symbols", "SecurityMaster"]),
        Page("SymbolStorage", "Symbol Storage", "Inspect symbol persistence and storage layout.", "data-operations", "Catalog", "\uEE94", 80, ShellNavigationVisibilityTier.Secondary, ["symbol storage", "persistence"], ["Symbols", "Storage"]),
        Page("Schedules", "Schedules", "Coordinate scheduled collection, maintenance, and workstation jobs.", "data-operations", "Platform", "\uE916", 90, ShellNavigationVisibilityTier.Secondary, ["schedule", "jobs"], ["Backfill", "CollectionSessions"]),
        Page("CollectionSessions", "Collection Sessions", "Inspect recent ingest sessions and collector lifecycle state.", "data-operations", "Operations Queue", "\uE8EF", 100, ShellNavigationVisibilityTier.Secondary, ["collector", "sessions"], ["Backfill", "ProviderHealth"]),
        Page("PackageManager", "Package Manager", "Inspect datasets, packages, and installed workstation content.", "data-operations", "Packaging", "\uE8B7", 110, ShellNavigationVisibilityTier.Secondary, ["packages", "content"], ["DataExport", "AnalysisExport"]),
        Page("DataBrowser", "Data Browser", "Inspect stored datasets and navigate raw time-series samples.", "data-operations", "Inspectors", "\uE721", 120, ShellNavigationVisibilityTier.Secondary, ["browser", "dataset"], ["DataSampling", "TimeSeriesAlignment"]),
        Page("DataCalendar", "Data Calendar", "Review date coverage, market calendars, and scheduling gaps.", "data-operations", "Inspectors", "\uE787", 130, ShellNavigationVisibilityTier.Secondary, ["calendar", "coverage"], ["TradingHours", "Backfill"]),
        Page("DataSampling", "Data Sampling", "Validate sample quality and inspect stored slices before export.", "data-operations", "Inspectors", "\uF1AD", 140, ShellNavigationVisibilityTier.Overflow, ["sampling", "quality"], ["DataBrowser", "DataQuality"]),
        Page("TimeSeriesAlignment", "Time Series Alignment", "Compare feeds and reconcile timestamp alignment issues.", "data-operations", "Inspectors", "\uE81E", 150, ShellNavigationVisibilityTier.Overflow, ["alignment", "timeseries"], ["DataSampling", "DataQuality"]),
        Page("ExportPresets", "Export Presets", "Save and reuse export configurations across operator workflows.", "data-operations", "Packaging", "\uE70B", 160, ShellNavigationVisibilityTier.Overflow, ["presets", "export"], ["DataExport", "AnalysisExport"]),
        Page("AnalysisExport", "Analysis Export", "Generate analysis packages and handoff artifacts for downstream review.", "data-operations", "Packaging", "\uEDE1", 170, ShellNavigationVisibilityTier.Overflow, ["analysis", "handoff"], ["AnalysisExportWizard", "DataExport"]),
        Page("AnalysisExportWizard", "Analysis Export Wizard", "Guide operators through structured analysis export setup.", "data-operations", "Packaging", "\uE8B0", 180, ShellNavigationVisibilityTier.Overflow, ["wizard", "analysis"], ["AnalysisExport", "ExportPresets"]),
        Page("PortfolioImport", "Portfolio Import", "Bring external portfolio snapshots into Meridian for downstream workflows.", "data-operations", "Catalog", "\uE8B5", 190, ShellNavigationVisibilityTier.Overflow, ["import", "portfolio"], ["Symbols", "FundPortfolio"]),
        Page("IndexSubscription", "Index Subscription", "Manage derived index subscriptions and coverage dependencies.", "data-operations", "Catalog", "\uE8FD", 200, ShellNavigationVisibilityTier.Overflow, ["index", "subscription"], ["Symbols", "DataSources"]),
        Page("Options", "Options", "Inspect options and derivatives support surfaces.", "data-operations", "Catalog", "\uE7C5", 210, ShellNavigationVisibilityTier.Overflow, ["options", "derivatives"], ["Symbols", "SecurityMaster"]),
        Page("AddProviderWizard", "Add Provider Wizard", "Guided flow for introducing a new provider integration.", "data-operations", "Operations Queue", "\uE710", 220, ShellNavigationVisibilityTier.Overflow, ["provider wizard", "setup"], ["Provider", "CredentialManagement"]),

        // Governance
        Page("GovernanceShell", "Governance Workspace", "Trust-critical workbench for accounts, ledger, reconciliation, and audit review.", "governance", "Control Tower", "\uE8D7", 0, ShellNavigationVisibilityTier.Primary, ["governance", "workspace", "control"], ["FundLedger", "FundReconciliation", "SecurityMaster", "Diagnostics"]),
        Page("FundLedger", "Fund Operations", "Review journal, account, and governance operations posture in one workbench.", "governance", "Fund Ops", "\uE82D", 10, ShellNavigationVisibilityTier.Primary, ["fund ledger", "journal", "operations"], ["FundAccounts", "FundTrialBalance", "FundAuditTrail"]),
        Page("FundAccounts", "Fund Accounts", "Inspect account balances, routing, and reconciliation posture.", "governance", "Fund Ops", "\uE8C7", 20, ShellNavigationVisibilityTier.Primary, ["accounts", "balances"], ["FundLedger", "FundBanking", "FundReconciliation"]),
        Page("FundReconciliation", "Fund Reconciliation", "Inspect reconciliation runs, breaks, and exception detail.", "governance", "Fund Ops", "\uE7BA", 30, ShellNavigationVisibilityTier.Primary, ["reconciliation", "breaks"], ["FundTrialBalance", "FundAuditTrail", "Diagnostics"]),
        Page("SecurityMaster", "Security Master", "Review reference data, listings, and security lifecycle posture.", "governance", "Assurance", "\uE72E", 40, ShellNavigationVisibilityTier.Primary, ["reference data", "security"], ["Symbols", "SymbolMapping", "DataQuality"]),
        Page("DataQuality", "Data Quality", "Track validation signals, integrity issues, and remediation state.", "governance", "Assurance", "\uE73E", 50, ShellNavigationVisibilityTier.Primary, ["quality", "validation"], ["ProviderHealth", "ArchiveHealth", "Diagnostics"]),
        Page("SystemHealth", "System Health", "Monitor host health, dependencies, and workstation readiness.", "governance", "Control Tower", "\uE9D9", 60, ShellNavigationVisibilityTier.Secondary, ["health", "system"], ["Diagnostics", "ServiceManager"]),
        Page("Diagnostics", "Diagnostics", "Run checks, inspect latency, and troubleshoot operator issues.", "governance", "Control Tower", "\uE90F", 70, ShellNavigationVisibilityTier.Secondary, ["diagnostics", "troubleshooting"], ["SystemHealth", "ServiceManager"]),
        Page("ProviderHealth", "Provider Health", "Inspect provider reachability, degraded states, and recovery guidance.", "governance", "Assurance", "\uEB51", 80, ShellNavigationVisibilityTier.Secondary, ["provider health", "reachability"], ["Provider", "DataQuality"]),
        Page("FundBanking", "Fund Banking", "Review banking posture and fund cash-account detail.", "governance", "Fund Ops", "\uE825", 90, ShellNavigationVisibilityTier.Secondary, ["banking", "cash"], ["FundAccounts", "FundCashFinancing"]),
        Page("FundPortfolio", "Fund Portfolio", "Inspect fund-scoped positions and exposure posture.", "governance", "Fund Ops", "\uE821", 100, ShellNavigationVisibilityTier.Secondary, ["fund portfolio", "exposure"], ["FundAccounts", "FundCashFinancing"]),
        Page("FundCashFinancing", "Fund Cash and Financing", "Review fund cash, financing, and liquidity metrics.", "governance", "Fund Ops", "\uEAFD", 110, ShellNavigationVisibilityTier.Secondary, ["cash financing", "liquidity"], ["FundBanking", "FundPortfolio"]),
        Page("FundTrialBalance", "Fund Trial Balance", "Review trial-balance detail for the active fund.", "governance", "Fund Ops", "\uE8D2", 120, ShellNavigationVisibilityTier.Secondary, ["trial balance", "accounting"], ["FundLedger", "FundReconciliation"]),
        Page("FundAuditTrail", "Fund Audit Trail", "Inspect approvals, audit references, and governance history.", "governance", "Fund Ops", "\uE8D7", 130, ShellNavigationVisibilityTier.Secondary, ["audit", "approvals"], ["FundLedger", "FundReconciliation"]),
        Page("ArchiveHealth", "Archive Health", "Review archive integrity, retention posture, and gap signals.", "governance", "Assurance", "\uE73E", 140, ShellNavigationVisibilityTier.Secondary, ["archive", "retention"], ["Storage", "RetentionAssurance"]),
        Page("StorageOptimization", "Storage Optimization", "Tune retention, footprint, and storage efficiency.", "governance", "Assurance", "\uE713", 150, ShellNavigationVisibilityTier.Overflow, ["optimization", "retention"], ["Storage", "ArchiveHealth"]),
        Page("RetentionAssurance", "Retention Assurance", "Validate retention-policy adherence and lifecycle posture.", "governance", "Assurance", "\uE8A5", 160, ShellNavigationVisibilityTier.Overflow, ["retention", "policy"], ["ArchiveHealth", "StorageOptimization"]),
        Page("ServiceManager", "Service Manager", "Inspect background services, logs, and operational controls.", "governance", "Control Tower", "\uECE7", 170, ShellNavigationVisibilityTier.Overflow, ["services", "logs"], ["SystemHealth", "Diagnostics"]),
        Page("AdminMaintenance", "Admin Maintenance", "Execute privileged maintenance tasks and governance operations.", "governance", "Control Tower", "\uE74D", 180, ShellNavigationVisibilityTier.Overflow, ["admin", "maintenance"], ["Diagnostics", "Settings"]),
        Page("MessagingHub", "Messaging Hub", "Inspect operator notifications and internal messaging pathways.", "governance", "Control Tower", "\uE715", 190, ShellNavigationVisibilityTier.Overflow, ["messaging", "notifications"], ["NotificationCenter", "ActivityLog"]),
        Page("NotificationCenter", "Notification Center", "Review active notifications, alerts, and workstation events.", "governance", "Control Tower", "\uE7F4", 200, ShellNavigationVisibilityTier.Overflow, ["notifications", "alerts"], ["MessagingHub", "ActivityLog"]),
        Page("ActivityLog", "Activity Log", "Review recent workstation actions and state changes.", "governance", "Control Tower", "\uE823", 210, ShellNavigationVisibilityTier.Overflow, ["activity", "history"], ["NotificationCenter", "FundAuditTrail"]),
        Page("Settings", "Settings", "Adjust workstation preferences, connections, and operator defaults.", "governance", "Support", "\uE713", 220, ShellNavigationVisibilityTier.Overflow, ["preferences", "settings"], ["CredentialManagement", "KeyboardShortcuts", "Help"]),
        Page("CredentialManagement", "Credential Management", "Manage provider credentials and validate secure access.", "governance", "Support", "\uE72E", 230, ShellNavigationVisibilityTier.Overflow, ["credentials", "security"], ["Settings", "AddProviderWizard"]),
        Page("KeyboardShortcuts", "Keyboard Shortcuts", "Review accelerator keys and workstation shortcuts.", "governance", "Support", "\uE765", 240, ShellNavigationVisibilityTier.Overflow, ["shortcuts", "keyboard"], ["Help", "Settings"]),
        Page("Help", "Help and Support", "Access support resources and workstation guidance.", "governance", "Support", "\uE897", 250, ShellNavigationVisibilityTier.Overflow, ["help", "support"], ["KeyboardShortcuts", "Settings"]),
        Page("SetupWizard", "Setup Wizard", "Complete initial workstation setup and guided configuration.", "governance", "Support", "\uE8B0", 260, ShellNavigationVisibilityTier.Overflow, ["setup", "wizard"], ["Settings", "CredentialManagement"], hideFromDefaultPalette: true),
        Page("Workspaces", "Workspaces", "Legacy workspace catalog and saved-layout surface.", "governance", "Legacy", "\uE8A9", 270, ShellNavigationVisibilityTier.Overflow, ["workspaces", "layouts"], ["GovernanceShell"], hideFromDefaultPalette: true),
        Page("Welcome", "Welcome", "Onboarding entry point and first-run guidance.", "governance", "Support", "\uE80F", 280, ShellNavigationVisibilityTier.Overflow, ["welcome", "onboarding"], ["SetupWizard", "Help"], hideFromDefaultPalette: true)
    ];

    private static readonly Lazy<IReadOnlyDictionary<string, WorkspaceShellDescriptor>> WorkspacesById =
        new(() => Workspaces.ToDictionary(static workspace => workspace.Id, StringComparer.OrdinalIgnoreCase));

    private static readonly Lazy<IReadOnlyDictionary<string, ShellPageDescriptor>> PagesByTag =
        new(() => Pages.ToDictionary(static page => page.PageTag, StringComparer.OrdinalIgnoreCase));

    public static WorkspaceShellDescriptor GetDefaultWorkspace() => Workspaces[0];

    public static WorkspaceShellDescriptor? GetWorkspace(string? workspaceId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            return null;
        }

        return WorkspacesById.Value.TryGetValue(workspaceId.Trim(), out var workspace)
            ? workspace
            : null;
    }

    public static ShellPageDescriptor? GetPage(string? pageTag)
    {
        if (string.IsNullOrWhiteSpace(pageTag))
        {
            return null;
        }

        return PagesByTag.Value.TryGetValue(pageTag.Trim(), out var descriptor)
            ? descriptor
            : null;
    }

    public static string? InferWorkspaceIdForPageTag(string? pageTag) => GetPage(pageTag)?.WorkspaceId;

    public static IReadOnlyList<ShellPageDescriptor> GetPagesForWorkspace(string workspaceId)
    {
        return Pages
            .Where(page => string.Equals(page.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(page => page.VisibilityTier)
            .ThenBy(page => page.Order)
            .ThenBy(page => page.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<ShellPageDescriptor> GetRelatedPages(string? pageTag)
    {
        var descriptor = GetPage(pageTag);
        if (descriptor is null || descriptor.RelatedPageTags.Count == 0)
        {
            return Array.Empty<ShellPageDescriptor>();
        }

        var related = new List<ShellPageDescriptor>();
        foreach (var relatedPageTag in descriptor.RelatedPageTags)
        {
            var relatedDescriptor = GetPage(relatedPageTag);
            if (relatedDescriptor is not null)
            {
                related.Add(relatedDescriptor);
            }
        }

        return related;
    }

    private static ShellPageDescriptor Page(
        string pageTag,
        string title,
        string subtitle,
        string workspaceId,
        string sectionLabel,
        string glyph,
        int order,
        ShellNavigationVisibilityTier visibilityTier,
        IReadOnlyList<string>? searchKeywords = null,
        IReadOnlyList<string>? relatedPageTags = null,
        bool hideFromDefaultPalette = false)
    {
        return new ShellPageDescriptor(
            PageTag: pageTag,
            Title: title,
            Subtitle: subtitle,
            WorkspaceId: workspaceId,
            SectionLabel: sectionLabel,
            Glyph: glyph,
            Order: order,
            VisibilityTier: visibilityTier,
            SearchKeywords: searchKeywords ?? Array.Empty<string>(),
            RelatedPageTags: relatedPageTags ?? Array.Empty<string>(),
            HideFromDefaultPalette: hideFromDefaultPalette);
    }
}

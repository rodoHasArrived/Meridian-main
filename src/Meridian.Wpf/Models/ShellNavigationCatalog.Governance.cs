using Meridian.Wpf.Views;

namespace Meridian.Wpf.Models;

public static partial class ShellNavigationCatalog
{
    private static readonly ShellPageDescriptor[] AccountingPages =
    [
        Page<WorkspaceCapabilityHomePage>("AccountingShell", "Accounting Workspace", "Review ledger, cash, reconciliation, trial balance, and audit workflows.", "accounting", "Launchpad", "\uE8D7", 0, ShellNavigationVisibilityTier.Primary, ["accounting", "ledger", "workspace", "control"], ["FundLedger", "FundReconciliation", "FundTrialBalance", "FundAuditTrail"], ["GovernanceShell", "GovernanceWorkspace"]),
        Page<FundLedgerPage>("FundLedger", "Fund operations", "Review journal, account, and accounting readiness in one view.", "accounting", "Fund Ops", "\uE82D", 10, ShellNavigationVisibilityTier.Primary, ["fund ledger", "journal", "operations"], ["FundAccounts", "FundTrialBalance", "FundAuditTrail"], ["FundOperations"]),
        Page<RunLedgerPage>("RunLedger", "Run ledger", "Review run postings and execution-linked cash movement.", "accounting", "Run Inspectors", "\uE82D", 20, ShellNavigationVisibilityTier.Primary, ["ledger", "postings", "review"], ["PositionBlotter", "RunRisk", "FundLedger"], ["LedgerInspector"]),
        Page<RunCashFlowPage>("RunCashFlow", "Run cash flow", "Review simulated cash, financing, and funding impacts.", "accounting", "Run Inspectors", "\uEAFD", 30, ShellNavigationVisibilityTier.Primary, ["cash flow", "cash", "financing"], ["RunPortfolio", "RunLedger"]),
        Page<FundLedgerPage>("FundBanking", "Fund banking", "Review banking readiness and fund cash-account detail.", "accounting", "Fund Ops", "\uE825", 40, ShellNavigationVisibilityTier.Secondary, ["banking", "cash"], ["FundAccounts", "FundCashFinancing"]),
        Page<FundLedgerPage>("FundCashFinancing", "Fund cash and financing", "Review fund cash, financing, and liquidity metrics.", "accounting", "Fund Ops", "\uEAFD", 50, ShellNavigationVisibilityTier.Secondary, ["cash financing", "liquidity"], ["FundBanking", "FundPortfolio"]),
        Page<FundLedgerPage>("FundTrialBalance", "Fund trial balance", "Review trial-balance detail for the active fund.", "accounting", "Fund Ops", "\uE8D2", 60, ShellNavigationVisibilityTier.Secondary, ["trial balance", "accounting"], ["FundLedger", "FundReconciliation"]),
        Page<FundLedgerPage>("FundReconciliation", "Fund reconciliation", "Inspect reconciliation runs, breaks, and exception detail.", "accounting", "Fund Ops", "\uE7BA", 70, ShellNavigationVisibilityTier.Primary, ["reconciliation", "breaks"], ["FundTrialBalance", "FundAuditTrail", "Diagnostics"]),
        Page<FundLedgerPage>("FundAuditTrail", "Fund audit trail", "Inspect approvals, audit references, and accounting history.", "accounting", "Fund Ops", "\uE8D7", 80, ShellNavigationVisibilityTier.Secondary, ["audit", "approvals"], ["FundLedger", "FundReconciliation"])
    ];

    private static readonly ShellPageDescriptor[] ReportingPages =
    [
        Page<WorkspaceCapabilityHomePage>("ReportingShell", "Reporting Workspace", "Prepare report packs, dashboards, analysis exports, and export presets.", "reporting", "Launchpad", "\uE8A5", 0, ShellNavigationVisibilityTier.Primary, ["reporting", "reports", "workspace", "export"], ["FundReportPack", "Dashboard", "AnalysisExport", "ExportPresets"]),
        Page<FundLedgerPage>("FundReportPack", "Fund report pack", "Preview report packs and downstream handoff readiness.", "reporting", "Report Packs", "\uE8A5", 10, ShellNavigationVisibilityTier.Primary, ["report pack", "reporting", "board pack"], ["FundCashFinancing", "FundAuditTrail", "FundTrialBalance"]),
        Page<DashboardPage>("Dashboard", "Reporting dashboard", "Monitor holdings, quality exceptions, maturities, and export readiness.", "reporting", "Dashboard", "\uE71D", 20, ShellNavigationVisibilityTier.Primary, ["overview", "portfolio", "holdings", "quality"], ["ReportingShell", "DataQuality", "SecurityMaster"], hideFromDefaultPalette: true),
        Page<AnalysisExportPage>("AnalysisExport", "Analysis export", "Export analysis packages for downstream review.", "reporting", "Exports", "\uEDE1", 30, ShellNavigationVisibilityTier.Primary, ["analysis", "export", "review"], ["AnalysisExportWizard", "DataExport"]),
        Page<AnalysisExportWizardPage>("AnalysisExportWizard", "Analysis export wizard", "Configure analysis export steps with guided prompts.", "reporting", "Exports", "\uE8B0", 40, ShellNavigationVisibilityTier.Secondary, ["wizard", "analysis", "configure"], ["AnalysisExport", "ExportPresets"]),
        Page<ExportPresetsPage>("ExportPresets", "Export presets", "Configure reusable export settings.", "reporting", "Exports", "\uE70B", 50, ShellNavigationVisibilityTier.Secondary, ["presets", "export", "configure"], ["DataExport", "AnalysisExport"])
    ];

    private static readonly ShellPageDescriptor[] SettingsPages =
    [
        Page<WorkspaceCapabilityHomePage>("SettingsShell", "Settings Workspace", "Manage preferences, credentials, diagnostics, services, alerts, and help.", "settings", "Launchpad", "\uE713", 0, ShellNavigationVisibilityTier.Primary, ["settings", "preferences", "workspace", "support"], ["Settings", "Diagnostics", "SystemHealth", "Help"]),
        Page<SettingsPage>("Settings", "Settings", "Adjust workstation preferences, connections, and operator defaults.", "settings", "Preferences", "\uE713", 10, ShellNavigationVisibilityTier.Primary, ["preferences", "settings"], ["CredentialManagement", "KeyboardShortcuts", "Help"], ["Preferences"]),
        Page<CredentialManagementPage>("CredentialManagement", "Credential management", "Manage provider credentials and validate secure access.", "settings", "Preferences", "\uE72E", 20, ShellNavigationVisibilityTier.Primary, ["credentials", "security"], ["Settings", "AddProviderWizard"]),
        Page<SystemHealthPage>("SystemHealth", "System health", "Monitor host health, dependencies, and workstation readiness.", "settings", "Operations", "\uE9D9", 30, ShellNavigationVisibilityTier.Primary, ["health", "system"], ["Diagnostics", "ServiceManager"]),
        Page<DiagnosticsPage>("Diagnostics", "Diagnostics", "Run checks, inspect latency, and troubleshoot operator issues.", "settings", "Operations", "\uE90F", 40, ShellNavigationVisibilityTier.Primary, ["diagnostics", "troubleshooting"], ["SystemHealth", "ServiceManager"]),
        Page<ServiceManagerPage>("ServiceManager", "Service manager", "Inspect background services, logs, and operational controls.", "settings", "Operations", "\uECE7", 50, ShellNavigationVisibilityTier.Secondary, ["services", "logs"], ["SystemHealth", "Diagnostics"]),
        Page<AdminMaintenancePage>("AdminMaintenance", "Admin maintenance", "Execute privileged maintenance tasks and operations.", "settings", "Operations", "\uE74D", 60, ShellNavigationVisibilityTier.Secondary, ["admin", "maintenance"], ["Diagnostics", "Settings"]),
        Page<EnvironmentDesignerPage>("EnvironmentDesigner", "Environment designer", "Draft, validate, publish, and roll back operating models.", "settings", "Operations", "\uE70F", 70, ShellNavigationVisibilityTier.Secondary, ["environment", "designer", "lanes"], ["AdminMaintenance", "SettingsShell"]),
        Page<MessagingHubPage>("MessagingHub", "Messaging hub", "Inspect operator notifications and internal messaging pathways.", "settings", "Notifications", "\uE715", 80, ShellNavigationVisibilityTier.Secondary, ["messaging", "notifications"], ["NotificationCenter", "ActivityLog"]),
        Page<NotificationCenterPage>("NotificationCenter", "Notification center", "Review active notifications, alerts, and workstation events.", "settings", "Notifications", "\uE7F4", 90, ShellNavigationVisibilityTier.Secondary, ["notifications", "alerts"], ["MessagingHub", "ActivityLog"], ["Alerts"]),
        Page<ActivityLogPage>("ActivityLog", "Activity log", "Review recent workstation actions and state changes.", "settings", "Notifications", "\uE823", 100, ShellNavigationVisibilityTier.Overflow, ["activity", "history"], ["NotificationCenter", "FundAuditTrail"]),
        Page<KeyboardShortcutsPage>("KeyboardShortcuts", "Keyboard shortcuts", "Review accelerator keys and workstation shortcuts.", "settings", "Support", "\uE765", 110, ShellNavigationVisibilityTier.Overflow, ["shortcuts", "keyboard"], ["Help", "Settings"]),
        Page<HelpPage>("Help", "Help and support", "Access support resources and workstation guidance.", "settings", "Support", "\uE897", 120, ShellNavigationVisibilityTier.Overflow, ["help", "support"], ["KeyboardShortcuts", "Settings"]),
        Page<SetupWizardPage>("SetupWizard", "Setup wizard", "Complete initial workstation setup and guided configuration.", "settings", "Support", "\uE8B0", 130, ShellNavigationVisibilityTier.Overflow, ["setup", "wizard"], ["Settings", "CredentialManagement"], hideFromDefaultPalette: true),
        Page<WorkspacePage>("Workspaces", "Workspace layouts", "Workspace layout catalog and saved-layout surface.", "settings", "Workspace layouts", "\uE8A9", 140, ShellNavigationVisibilityTier.Overflow, ["workspaces", "layouts"], ["SettingsShell"], hideFromDefaultPalette: true),
        Page<WorkflowLibraryPage>("WorkflowLibrary", "Workflow library", "Reusable workflow catalog and action launch surface.", "settings", "Workspace layouts", "\uE8FD", 145, ShellNavigationVisibilityTier.Secondary, ["workflow", "library", "actions", "templates"], ["Workspaces", "SettingsShell"]),
        Page<WelcomePage>("Welcome", "Welcome", "Onboarding entry point and first-run guidance.", "settings", "Support", "\uE80F", 150, ShellNavigationVisibilityTier.Overflow, ["welcome", "onboarding"], ["SetupWizard", "Help"], hideFromDefaultPalette: true)
    ];
}

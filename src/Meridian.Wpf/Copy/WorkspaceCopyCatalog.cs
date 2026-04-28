namespace Meridian.Wpf.Copy;

public sealed record WorkspaceDescriptorCopy(
    string Id,
    string Title,
    string Description,
    string Summary,
    string TileSummary,
    string ShellDisplayName);

public sealed record WorkspaceCopyEntry(string Key, string Text);

public static class WorkspaceCopyCatalog
{
    // Compatibility constants for existing XAML surfaces that still use legacy class names.
    public const string ResearchShellTitle = Strategy.ShellTitle;
    public const string DataOperationsShellTitle = Data.ShellTitle;

    public static class Trading
    {
        public const string WorkspaceId = "trading";
        public static readonly WorkspaceDescriptorCopy Descriptor = new(
            WorkspaceId,
            "Trading",
            "Live readiness, order flow, execution, and risk-aware monitoring.",
            "Operate trading workflows with paper/live separation and audit reachability.",
            "Live | Orders | Risk",
            "Trading Desk");

        public const string ShellTitle = "Trading Desk";
        public const string ShellSubtitle = "Risk-aware trading shell for live readiness, blotter review, safe staging, and docked execution detail.";
        public const string PrimaryScopeLabel = "Desk";
    }

    public static class Portfolio
    {
        public const string WorkspaceId = "portfolio";
        public static readonly WorkspaceDescriptorCopy Descriptor = new(
            WorkspaceId,
            "Portfolio",
            "Account, aggregate, fund, lending, and portfolio import review.",
            "Review account and fund exposure with import and lending workflows nearby.",
            "Accounts | Exposure | Import",
            "Portfolio Workspace");

        public const string ShellTitle = "Portfolio Workspace";
        public const string ShellSubtitle = "Account, aggregate, fund, lending, and import workflows for portfolio review.";
    }

    public static class Accounting
    {
        public const string WorkspaceId = "accounting";
        public static readonly WorkspaceDescriptorCopy Descriptor = new(
            WorkspaceId,
            "Accounting",
            "Ledger, cash, banking, reconciliation, trial balance, and audit review.",
            "Operate accounting, reconciliation, cash, banking, and audit evidence from one area.",
            "Ledger | Cash | Audit",
            "Accounting Workspace");

        public const string ShellTitle = "Accounting Workspace";
        public const string ShellSubtitleNoFund = "Fund-aware accounting shell for ledger, reconciliation, trial balance, and audit readiness.";
        public const string ShellSubtitleFund = "Review accounting, reconciliations, cash, financing, and approval gates without leaving the shell.";
    }

    public static class Reporting
    {
        public const string WorkspaceId = "reporting";
        public static readonly WorkspaceDescriptorCopy Descriptor = new(
            WorkspaceId,
            "Reporting",
            "Report packs, dashboards, analysis export, and reusable export presets.",
            "Prepare reporting outputs and analysis handoffs from a focused workspace.",
            "Packs | Dashboard | Export",
            "Reporting Workspace");

        public const string ShellTitle = "Reporting Workspace";
        public const string ShellSubtitle = "Report packs, dashboards, and analysis export workflows.";
    }

    public static class Strategy
    {
        public const string WorkspaceId = "strategy";
        public static readonly WorkspaceDescriptorCopy Descriptor = new(
            WorkspaceId,
            "Strategy",
            "Backtest studio, run comparison, charts, scripting, and replay flows.",
            "Operate model exploration with docked run, portfolio, and promotion context.",
            "Runs | Compare | Scripts",
            "Strategy Studio");

        public const string ShellTitle = "Strategy Workspace";
        public const string ShellSubtitle = "Market briefing, run studio, and promotion-aware strategy workflow.";
        public const string PrimaryScopeLabel = "Strategy";
    }

    public static class Data
    {
        public const string WorkspaceId = "data";
        public static readonly WorkspaceDescriptorCopy Descriptor = new(
            WorkspaceId,
            "Data",
            "Providers, ingestion, storage, schedules, quality, and data products.",
            "Operate collection queues, freshness checks, storage, and exports from one workspace.",
            "Providers | Storage | Quality",
            "Data Workspace");

        public const string ShellTitle = "Data Workspace";
        public const string ShellSubtitle = "Provider freshness, backfill pressure, storage health, and export job visibility in one operator shell.";
        public const string PrimaryScopeLabel = "Queue";
        public const string DefaultScopeLabel = "Provider and storage health";
        public const string DefaultScopeSummary = "Provider health, backfill priority, storage follow-up, and export delivery stay in one fixed shell.";
    }

    public static class Settings
    {
        public const string WorkspaceId = "settings";
        public static readonly WorkspaceDescriptorCopy Descriptor = new(
            WorkspaceId,
            "Settings",
            "Preferences, credentials, diagnostics, services, alerts, help, and setup.",
            "Manage workstation configuration, support, notifications, diagnostics, and setup.",
            "Prefs | Health | Help",
            "Settings Workspace");

        public const string ShellTitle = "Settings Workspace";
        public const string ShellSubtitle = "Workstation configuration, diagnostics, support, and operator setup.";
    }

    public static class Research
    {
        public const string WorkspaceId = Strategy.WorkspaceId;
        public static readonly WorkspaceDescriptorCopy Descriptor = Strategy.Descriptor;
        public const string ShellTitle = Strategy.ShellTitle;
        public const string ShellSubtitle = Strategy.ShellSubtitle;
        public const string PrimaryScopeLabel = Strategy.PrimaryScopeLabel;
    }

    public static class DataOperations
    {
        public const string WorkspaceId = Data.WorkspaceId;
        public static readonly WorkspaceDescriptorCopy Descriptor = Data.Descriptor;
        public const string ShellTitle = Data.ShellTitle;
        public const string ShellSubtitle = Data.ShellSubtitle;
        public const string PrimaryScopeLabel = Data.PrimaryScopeLabel;
        public const string DefaultScopeLabel = Data.DefaultScopeLabel;
        public const string DefaultScopeSummary = Data.DefaultScopeSummary;
    }

    public static class Governance
    {
        public const string WorkspaceId = Accounting.WorkspaceId;
        public static readonly WorkspaceDescriptorCopy Descriptor = Accounting.Descriptor;
        public const string ShellTitle = Accounting.ShellTitle;
        public const string ShellSubtitleNoFund = Accounting.ShellSubtitleNoFund;
        public const string ShellSubtitleFund = Accounting.ShellSubtitleFund;
    }

    public static IReadOnlyList<WorkspaceCopyEntry> Entries { get; } =
    [
        .. BuildEntries(Trading.WorkspaceId, Trading.Descriptor, Trading.ShellTitle, Trading.ShellSubtitle),
        .. BuildEntries(Portfolio.WorkspaceId, Portfolio.Descriptor, Portfolio.ShellTitle, Portfolio.ShellSubtitle),
        .. BuildEntries(Accounting.WorkspaceId, Accounting.Descriptor, Accounting.ShellTitle, Accounting.ShellSubtitleNoFund),
        .. BuildEntries(Reporting.WorkspaceId, Reporting.Descriptor, Reporting.ShellTitle, Reporting.ShellSubtitle),
        .. BuildEntries(Strategy.WorkspaceId, Strategy.Descriptor, Strategy.ShellTitle, Strategy.ShellSubtitle),
        .. BuildEntries(Data.WorkspaceId, Data.Descriptor, Data.ShellTitle, Data.ShellSubtitle),
        .. BuildEntries(Settings.WorkspaceId, Settings.Descriptor, Settings.ShellTitle, Settings.ShellSubtitle)
    ];

    private static IEnumerable<WorkspaceCopyEntry> BuildEntries(
        string keyPrefix,
        WorkspaceDescriptorCopy descriptor,
        string shellTitle,
        string shellSubtitle)
    {
        yield return new($"{keyPrefix}.workspace.title", descriptor.Title);
        yield return new($"{keyPrefix}.workspace.description", descriptor.Description);
        yield return new($"{keyPrefix}.workspace.summary", descriptor.Summary);
        yield return new($"{keyPrefix}.workspace.tile-summary", descriptor.TileSummary);
        yield return new($"{keyPrefix}.shell.title", shellTitle);
        yield return new($"{keyPrefix}.shell.subtitle", shellSubtitle);
    }
}

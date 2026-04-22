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
    // Copy key convention: workspace.section.intent
    public static class Research
    {
        public const string WorkspaceId = "research";
        public static readonly WorkspaceDescriptorCopy Descriptor = new(
            WorkspaceId,
            "Research",
            "Backtest studio, run comparison, charts, and investigation flows.",
            "Operate model exploration with docked run, portfolio, and promotion context.",
            "Runs · Compare · Promote",
            "Backtest Studio");

        public const string ShellTitle = "Research Workspace";
        public const string ShellSubtitle = "Market briefing, run studio, and promotion-aware research workflow.";
        public const string PrimaryScopeLabel = "Research";
    }

    public static class Trading
    {
        public const string WorkspaceId = "trading";
        public static readonly WorkspaceDescriptorCopy Descriptor = new(
            WorkspaceId,
            "Trading",
            "Live posture, order flow, execution, and risk-aware monitoring.",
            "Operate the trading cockpit with explicit paper/live separation and audit reachability.",
            "Live · Orders · Risk",
            "Trading Cockpit");

        public const string ShellTitle = "Trading Cockpit";
        public const string ShellSubtitle = "Risk-aware trading shell for live posture, blotter review, safe staging, and docked execution detail.";
        public const string PrimaryScopeLabel = "Desk";
    }

    public static class DataOperations
    {
        public const string WorkspaceId = "data-operations";
        public static readonly WorkspaceDescriptorCopy Descriptor = new(
            WorkspaceId,
            "Data Operations",
            "Providers, ingestion, storage, schedules, exports, and blocker visibility.",
            "Operate the collection queue, freshness posture, and export pipeline from one workstation.",
            "Providers · Storage · Jobs",
            "Data Operations Workspace");

        public const string ShellTitle = "Data Operations Workspace";
        public const string ShellSubtitle = "Provider freshness, backfill pressure, storage posture, and export job visibility in one fixed operator shell.";
        public const string PrimaryScopeLabel = "Queue";
        public const string DefaultScopeLabel = "Provider and storage posture";
        public const string DefaultScopeSummary = "Provider posture, backfill priority, storage follow-up, and export delivery stay in one fixed shell.";
    }

    public static class Governance
    {
        public const string WorkspaceId = "governance";
        public static readonly WorkspaceDescriptorCopy Descriptor = new(
            WorkspaceId,
            "Governance",
            "Controls, diagnostics, fund operations, reconciliation, and trust-critical review.",
            "Operate accounts, ledger, reconciliation, and audit work from a single control surface.",
            "Ledger · Audit · Controls",
            "Governance Workspace");

        public const string ShellTitle = "Governance Workspace";
        public const string ShellSubtitleNoFund = "Organization-aware review shell for operations, accounting, reconciliation, reporting, and audit posture.";
        public const string ShellSubtitleFund = "Review operations, accounting, reconciliations, reporting, and approval gates without leaving the workstation shell.";
    }

    public static IReadOnlyList<WorkspaceCopyEntry> Entries { get; } =
    [
        new("research.workspace.title", Research.Descriptor.Title),
        new("research.workspace.description", Research.Descriptor.Description),
        new("research.workspace.summary", Research.Descriptor.Summary),
        new("research.workspace.tile-summary", Research.Descriptor.TileSummary),
        new("research.shell.title", Research.ShellTitle),
        new("research.shell.subtitle", Research.ShellSubtitle),
        new("research.shell.primary-scope-label", Research.PrimaryScopeLabel),

        new("trading.workspace.title", Trading.Descriptor.Title),
        new("trading.workspace.description", Trading.Descriptor.Description),
        new("trading.workspace.summary", Trading.Descriptor.Summary),
        new("trading.workspace.tile-summary", Trading.Descriptor.TileSummary),
        new("trading.shell.title", Trading.ShellTitle),
        new("trading.shell.subtitle", Trading.ShellSubtitle),
        new("trading.shell.primary-scope-label", Trading.PrimaryScopeLabel),

        new("data-operations.workspace.title", DataOperations.Descriptor.Title),
        new("data-operations.workspace.description", DataOperations.Descriptor.Description),
        new("data-operations.workspace.summary", DataOperations.Descriptor.Summary),
        new("data-operations.workspace.tile-summary", DataOperations.Descriptor.TileSummary),
        new("data-operations.shell.title", DataOperations.ShellTitle),
        new("data-operations.shell.subtitle", DataOperations.ShellSubtitle),
        new("data-operations.shell.primary-scope-label", DataOperations.PrimaryScopeLabel),
        new("data-operations.shell.scope-default", DataOperations.DefaultScopeLabel),
        new("data-operations.shell.scope-summary-default", DataOperations.DefaultScopeSummary),

        new("governance.workspace.title", Governance.Descriptor.Title),
        new("governance.workspace.description", Governance.Descriptor.Description),
        new("governance.workspace.summary", Governance.Descriptor.Summary),
        new("governance.workspace.tile-summary", Governance.Descriptor.TileSummary),
        new("governance.shell.title", Governance.ShellTitle),
        new("governance.shell.subtitle.no-fund", Governance.ShellSubtitleNoFund),
        new("governance.shell.subtitle.fund", Governance.ShellSubtitleFund)
    ];
}

using System.ComponentModel;
using Meridian.Contracts.Workstation;

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
    public const string StrategyShellTitle = Strategy.ShellTitle;
    public const string DataShellTitle = Data.ShellTitle;

    public static class Trading
    {
        public const string WorkspaceId = WorkstationWorkspaceCatalog.TradingKey;
        public static readonly WorkspaceDescriptorCopy Descriptor = new(
            WorkspaceId,
            WorkstationWorkspaceCatalog.TradingLabel,
            WorkstationWorkspaceCatalog.TradingDesktopDescription,
            WorkstationWorkspaceCatalog.TradingDesktopSummary,
            WorkstationWorkspaceCatalog.TradingDesktopTileSummary,
            WorkstationWorkspaceCatalog.TradingDesktopShellDisplayName);

        public const string ShellTitle = "Trading Desk";
        public const string ShellSubtitle = "Risk-aware trading shell for live readiness, blotter review, safe staging, and docked execution detail.";
        public const string PrimaryScopeLabel = "Desk";
    }

    public static class Portfolio
    {
        public const string WorkspaceId = WorkstationWorkspaceCatalog.PortfolioKey;
        public static readonly WorkspaceDescriptorCopy Descriptor = new(
            WorkspaceId,
            WorkstationWorkspaceCatalog.PortfolioLabel,
            WorkstationWorkspaceCatalog.PortfolioDesktopDescription,
            WorkstationWorkspaceCatalog.PortfolioDesktopSummary,
            WorkstationWorkspaceCatalog.PortfolioDesktopTileSummary,
            WorkstationWorkspaceCatalog.PortfolioDesktopShellDisplayName);

        public const string ShellTitle = "Portfolio Workspace";
        public const string ShellSubtitle = "Account, aggregate, fund, lending, and import workflows for portfolio review.";
    }

    public static class Accounting
    {
        public const string WorkspaceId = WorkstationWorkspaceCatalog.AccountingKey;
        public static readonly WorkspaceDescriptorCopy Descriptor = new(
            WorkspaceId,
            WorkstationWorkspaceCatalog.AccountingLabel,
            WorkstationWorkspaceCatalog.AccountingDesktopDescription,
            WorkstationWorkspaceCatalog.AccountingDesktopSummary,
            WorkstationWorkspaceCatalog.AccountingDesktopTileSummary,
            WorkstationWorkspaceCatalog.AccountingDesktopShellDisplayName);

        public const string ShellTitle = "Accounting Workspace";
        public const string ShellSubtitleNoFund = "Fund-aware accounting shell for ledger, reconciliation, trial balance, and audit readiness.";
        public const string ShellSubtitleFund = "Review accounting, reconciliations, cash, financing, and approval gates without leaving the shell.";
    }

    public static class Reporting
    {
        public const string WorkspaceId = WorkstationWorkspaceCatalog.ReportingKey;
        public static readonly WorkspaceDescriptorCopy Descriptor = new(
            WorkspaceId,
            WorkstationWorkspaceCatalog.ReportingLabel,
            WorkstationWorkspaceCatalog.ReportingDesktopDescription,
            WorkstationWorkspaceCatalog.ReportingDesktopSummary,
            WorkstationWorkspaceCatalog.ReportingDesktopTileSummary,
            WorkstationWorkspaceCatalog.ReportingDesktopShellDisplayName);

        public const string ShellTitle = "Reporting Workspace";
        public const string ShellSubtitle = "Report packs, dashboards, and analysis export workflows.";
    }

    public static class Strategy
    {
        public const string WorkspaceId = WorkstationWorkspaceCatalog.StrategyKey;
        public static readonly WorkspaceDescriptorCopy Descriptor = new(
            WorkspaceId,
            WorkstationWorkspaceCatalog.StrategyLabel,
            WorkstationWorkspaceCatalog.StrategyDesktopDescription,
            WorkstationWorkspaceCatalog.StrategyDesktopSummary,
            WorkstationWorkspaceCatalog.StrategyDesktopTileSummary,
            WorkstationWorkspaceCatalog.StrategyDesktopShellDisplayName);

        public const string ShellTitle = "Strategy Workspace";
        public const string ShellSubtitle = "Market briefing, run studio, and promotion-aware strategy workflow.";
        public const string PrimaryScopeLabel = "Strategy";
    }

    public static class Data
    {
        public const string WorkspaceId = WorkstationWorkspaceCatalog.DataKey;
        public static readonly WorkspaceDescriptorCopy Descriptor = new(
            WorkspaceId,
            WorkstationWorkspaceCatalog.DataLabel,
            WorkstationWorkspaceCatalog.DataDesktopDescription,
            WorkstationWorkspaceCatalog.DataDesktopSummary,
            WorkstationWorkspaceCatalog.DataDesktopTileSummary,
            WorkstationWorkspaceCatalog.DataDesktopShellDisplayName);

        public const string ShellTitle = "Data Workspace";
        public const string ShellSubtitle = "Provider freshness, backfill pressure, storage health, and export job visibility in one operator shell.";
        public const string PrimaryScopeLabel = "Queue";
        public const string DefaultScopeLabel = "Provider and storage health";
        public const string DefaultScopeSummary = "Provider health, backfill priority, storage follow-up, and export delivery stay in one fixed shell.";
    }

    public static class Settings
    {
        public const string WorkspaceId = WorkstationWorkspaceCatalog.SettingsKey;
        public static readonly WorkspaceDescriptorCopy Descriptor = new(
            WorkspaceId,
            WorkstationWorkspaceCatalog.SettingsLabel,
            WorkstationWorkspaceCatalog.SettingsDesktopDescription,
            WorkstationWorkspaceCatalog.SettingsDesktopSummary,
            WorkstationWorkspaceCatalog.SettingsDesktopTileSummary,
            WorkstationWorkspaceCatalog.SettingsDesktopShellDisplayName);

        public const string ShellTitle = "Settings Workspace";
        public const string ShellSubtitle = "Workstation configuration, diagnostics, support, and operator setup.";
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class Research
    {
        public const string WorkspaceId = Strategy.WorkspaceId;
        public static readonly WorkspaceDescriptorCopy Descriptor = Strategy.Descriptor;
        public const string ShellTitle = Strategy.ShellTitle;
        public const string ShellSubtitle = Strategy.ShellSubtitle;
        public const string PrimaryScopeLabel = Strategy.PrimaryScopeLabel;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
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

    [EditorBrowsable(EditorBrowsableState.Never)]
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

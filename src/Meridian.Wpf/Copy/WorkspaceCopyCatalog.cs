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

/// <summary>
/// Centralizes desktop workspace titles, descriptions, scope labels, and compatibility aliases.
/// </summary>
public static class WorkspaceCopyCatalog
{
    public const string StrategyShellTitle = Strategy.ShellTitle;
    public const string DataShellTitle = Data.ShellTitle;

    /// <summary>
    /// Provides the two shared phrases the desktop shell uses when no scope is selected.
    /// </summary>
    public static class Scope
    {
        /// <summary>Shown where the page still lists everything the operator may see.</summary>
        public const string AllAuthorized = "All authorized scopes";

        /// <summary>Shown where the page stays empty or locked until a scope is chosen.</summary>
        public const string NoneSelected = "No scope selected";
    }

    /// <summary>
    /// Provides titles, descriptions, and scope copy for the trading workspace.
    /// </summary>
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

        public const string ShellTitle = "Trading Workspace";
        public const string ShellSubtitle = "See your positions and open orders, and check whether the account is ready to trade.";
        public const string PrimaryScopeLabel = "Desk";
    }

    /// <summary>
    /// Provides titles, descriptions, and scope copy for the portfolio workspace.
    /// </summary>
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
        public const string ShellSubtitle = "See what the portfolio holds, what it is worth, and how current the prices are.";
    }

    /// <summary>
    /// Provides titles, descriptions, and scope copy for the accounting workspace.
    /// </summary>
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
        public const string ShellSubtitleNoFund = "See what is blocking the close, who owns it, and the next action.";
        public const string ShellSubtitleFund = ShellSubtitleNoFund;
    }

    /// <summary>
    /// Provides titles, descriptions, and scope copy for the reporting workspace.
    /// </summary>
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
        public const string ShellSubtitle = "See which reports are blocked today, who owns each one, and the next action.";
    }

    /// <summary>
    /// Provides titles, descriptions, and scope copy for the strategy workspace.
    /// </summary>
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
        public const string ShellSubtitle = "Review recent strategy runs and open the one you want to work on.";
        public const string PrimaryScopeLabel = "Strategy";
    }

    /// <summary>
    /// Provides titles, descriptions, and scope copy for the data workspace.
    /// </summary>
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
        public const string ShellSubtitle = "Check that data providers are healthy and that data is arriving.";
        public const string PrimaryScopeLabel = "Queue";
        public const string DefaultScopeLabel = "Provider and storage health";
        public const string DefaultScopeSummary = "Provider health, backfill priority, storage follow-up, and export delivery stay in one fixed shell.";
    }

    /// <summary>
    /// Provides titles, descriptions, and scope copy for the settings workspace.
    /// </summary>
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
        public const string ShellSubtitle = "Pick the setup task you need.";
    }

    /// <summary>
    /// Preserves legacy research workspace copy by forwarding to the strategy workspace.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class Research
    {
        public const string WorkspaceId = Strategy.WorkspaceId;
        public static readonly WorkspaceDescriptorCopy Descriptor = Strategy.Descriptor;
        public const string ShellTitle = Strategy.ShellTitle;
        public const string ShellSubtitle = Strategy.ShellSubtitle;
        public const string PrimaryScopeLabel = Strategy.PrimaryScopeLabel;
    }

    /// <summary>
    /// Preserves legacy data-operations workspace copy by forwarding to the data workspace.
    /// </summary>
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

    /// <summary>
    /// Preserves legacy governance workspace copy by forwarding to the accounting workspace.
    /// </summary>
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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Meridian.Wpf.Models;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Shell.ViewModels;

public sealed class CommandPaletteViewModel : BindableBase
{
    private readonly ObservableCollection<ShellCommandPaletteEntry> _pages = [];
    private Visibility _visibility = Visibility.Collapsed;
    private string _query = string.Empty;
    private ShellCommandPaletteEntry? _selectedPage;
    private string _resultSummary = string.Empty;
    private Visibility _emptyVisibility = Visibility.Collapsed;

    public CommandPaletteViewModel()
    {
        Pages = new ReadOnlyObservableCollection<ShellCommandPaletteEntry>(_pages);
    }

    public ReadOnlyObservableCollection<ShellCommandPaletteEntry> Pages { get; }

    public Visibility Visibility
    {
        get => _visibility;
        private set => SetProperty(ref _visibility, value);
    }

    public string Query
    {
        get => _query;
        private set => SetProperty(ref _query, value);
    }

    public ShellCommandPaletteEntry? SelectedPage
    {
        get => _selectedPage;
        set => SetProperty(ref _selectedPage, value);
    }

    public string ResultSummary
    {
        get => _resultSummary;
        private set => SetProperty(ref _resultSummary, value);
    }

    public Visibility EmptyVisibility
    {
        get => _emptyVisibility;
        private set => SetProperty(ref _emptyVisibility, value);
    }

    public string EmptyTitle => string.IsNullOrWhiteSpace(Query)
        ? "No pages available"
        : $"No results for \"{Query.Trim()}\"";

    public string EmptyDescription => string.IsNullOrWhiteSpace(Query)
        ? "Try opening a workspace home or refresh the shell to register navigation targets."
        : "Search by page title, workspace, workflow, or page tag.";

    public bool CanOpenSelectedPage => SelectedPage is not null;

    public bool SetQuery(string value, string currentWorkspace, IEnumerable<string> registeredPageTags)
    {
        var normalized = value ?? string.Empty;
        if (!SetProperty(ref _query, normalized, nameof(Query)))
        {
            return false;
        }

        Refresh(currentWorkspace, registeredPageTags);
        RaisePropertyChanged(nameof(EmptyTitle));
        RaisePropertyChanged(nameof(EmptyDescription));
        return true;
    }

    public void Show(string currentWorkspace, IEnumerable<string> registeredPageTags)
    {
        Visibility = Visibility.Visible;
        Refresh(currentWorkspace, registeredPageTags);
    }

    public void Hide()
    {
        Visibility = Visibility.Collapsed;
    }

    public void Clear(string currentWorkspace, IEnumerable<string> registeredPageTags)
    {
        SetQuery(string.Empty, currentWorkspace, registeredPageTags);
    }

    public void Refresh(string currentWorkspace, IEnumerable<string> registeredPageTags)
    {
        var query = Query.Trim();
        var descriptors = registeredPageTags
            .Select(ShellNavigationCatalog.GetPage)
            .Where(static descriptor => descriptor is not null)
            .Select(static descriptor => descriptor!)
            .GroupBy(static descriptor => descriptor.PageTag, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .Where(page => string.IsNullOrWhiteSpace(query)
                ? !page.HideFromDefaultPalette
                : MatchesPaletteQuery(page, query))
            .OrderBy(page => GetPaletteRank(page, query, currentWorkspace))
            .ThenBy(page => page.Title, StringComparer.OrdinalIgnoreCase)
            .Select(page => ToCommandPaletteEntry(page, page.VisibilityTier != ShellNavigationVisibilityTier.Primary))
            .Concat(GetGlobalCommandEntries()
                .Where(command => string.IsNullOrWhiteSpace(query)
                    ? !command.RequiresConfirmation
                    : MatchesCommandQuery(command, query)))
            .GroupBy(command => command.PageTag, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.Last())
            .OrderBy(command => GetCommandRank(command, query, currentWorkspace))
            .ThenBy(command => command.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ReplaceCollection(_pages, descriptors);
        SelectedPage = _pages.FirstOrDefault();
        UpdatePresentation(query);
    }

    private void UpdatePresentation(string query)
    {
        var resultCount = _pages.Count;
        ResultSummary = string.IsNullOrWhiteSpace(query)
            ? resultCount == 0
                ? "No shell destinations are currently registered."
                : $"{resultCount} pages across all workspaces"
            : resultCount == 0
                ? $"No matches for \"{query}\""
                : $"{resultCount} result{(resultCount == 1 ? string.Empty : "s")} for \"{query}\"";

        EmptyVisibility = resultCount == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        RaisePropertyChanged(nameof(EmptyTitle));
        RaisePropertyChanged(nameof(EmptyDescription));
        RaisePropertyChanged(nameof(CanOpenSelectedPage));
    }

    private static bool MatchesPaletteQuery(ShellPageDescriptor descriptor, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var workspaceTitle = ShellNavigationCatalog.GetWorkspace(descriptor.WorkspaceId)?.Title ?? descriptor.WorkspaceId;
        return GetPaletteSearchFields(descriptor, workspaceTitle)
            .Any(field => field.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static int GetPaletteRank(ShellPageDescriptor descriptor, string query, string currentWorkspace)
    {
        var workspacePenalty = string.Equals(descriptor.WorkspaceId, currentWorkspace, StringComparison.OrdinalIgnoreCase) ? 0 : 100;
        if (string.IsNullOrWhiteSpace(query))
        {
            var homeBonus = descriptor.PageTag.EndsWith("Shell", StringComparison.OrdinalIgnoreCase) ? -20 : 0;
            return workspacePenalty
                   + homeBonus
                   + ((int)descriptor.VisibilityTier * 10)
                   + descriptor.Order;
        }

        var title = descriptor.Title;
        var pageTag = descriptor.PageTag;
        var workspaceTitle = ShellNavigationCatalog.GetWorkspace(descriptor.WorkspaceId)?.Title ?? descriptor.WorkspaceId;

        var matchRank = title.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0
            : pageTag.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1
            : title.Contains(query, StringComparison.OrdinalIgnoreCase) ? 2
            : descriptor.SearchKeywords.Any(keyword => keyword.StartsWith(query, StringComparison.OrdinalIgnoreCase)) ? 3
            : pageTag.Contains(query, StringComparison.OrdinalIgnoreCase) ? 4
            : workspaceTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ? 5
            : descriptor.SectionLabel.Contains(query, StringComparison.OrdinalIgnoreCase) ? 6
            : descriptor.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase) ? 7
            : 8;

        return (matchRank * 1000)
               + workspacePenalty
               + ((int)descriptor.VisibilityTier * 10)
               + descriptor.Order;
    }

    private static bool MatchesCommandQuery(ShellCommandPaletteEntry command, string query)
        => GetCommandSearchFields(command).Any(field => field.Contains(query, StringComparison.OrdinalIgnoreCase));

    private static int GetCommandRank(ShellCommandPaletteEntry command, string query, string currentWorkspace)
    {
        var workspacePenalty = string.Equals(command.Workspace, currentWorkspace, StringComparison.OrdinalIgnoreCase)
            || string.Equals(command.WorkspaceTitle, currentWorkspace, StringComparison.OrdinalIgnoreCase) ? 0 : 100;
        var riskPenalty = command.RequiresConfirmation ? 50 : 0;
        if (string.IsNullOrWhiteSpace(query))
        {
            return workspacePenalty + riskPenalty + (command.ExecutionKind == ShellCommandPaletteExecutionKind.SafeNavigation ? 0 : 40);
        }

        var matchRank = command.DisplayName.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0
            : command.PageTag.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1
            : command.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ? 2
            : command.Keywords.Any(keyword => keyword.StartsWith(query, StringComparison.OrdinalIgnoreCase)) ? 3
            : command.PageTag.Contains(query, StringComparison.OrdinalIgnoreCase) ? 4
            : command.WorkspaceTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ? 5
            : command.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ? 6
            : 8;

        return (matchRank * 1000) + workspacePenalty + riskPenalty;
    }

    private static IEnumerable<string> GetCommandSearchFields(ShellCommandPaletteEntry command)
    {
        yield return command.PageTag;
        yield return command.DisplayName;
        yield return command.Description;
        yield return command.Workspace;
        yield return command.WorkspaceTitle;
        yield return command.SectionLabel;
        yield return command.ExecutionLabel;
        foreach (var keyword in command.Keywords)
        {
            yield return keyword;
        }
    }

    private static IEnumerable<string> GetPaletteSearchFields(ShellPageDescriptor descriptor, string workspaceTitle)
    {
        yield return descriptor.PageTag;
        yield return descriptor.Title;
        yield return descriptor.Subtitle;
        yield return descriptor.SectionLabel;
        yield return workspaceTitle;
        yield return GetVisibilityLabel(descriptor.VisibilityTier);

        foreach (var keyword in descriptor.SearchKeywords)
        {
            yield return keyword;
        }
    }

    private static ShellCommandPaletteEntry ToCommandPaletteEntry(ShellPageDescriptor descriptor, bool includeVisibilityLabel)
    {
        var workspaceTitle = ShellNavigationCatalog.GetWorkspace(descriptor.WorkspaceId)?.Title ?? descriptor.WorkspaceId;
        return new ShellCommandPaletteEntry(
            PageTag: descriptor.PageTag,
            Title: descriptor.Title,
            Subtitle: descriptor.Subtitle,
            WorkspaceTitle: workspaceTitle,
            SectionLabel: descriptor.SectionLabel,
            Glyph: descriptor.Glyph,
            VisibilityLabel: includeVisibilityLabel ? GetVisibilityLabel(descriptor.VisibilityTier) : string.Empty,
            DisplayName: descriptor.Title,
            Keywords: descriptor.SearchKeywords,
            Workspace: descriptor.WorkspaceId,
            Description: descriptor.Subtitle,
            ExecutionKind: ShellCommandPaletteExecutionKind.SafeNavigation,
            RequiresConfirmation: false);
    }

    private static IReadOnlyList<ShellCommandPaletteEntry> GetGlobalCommandEntries() =>
    [
        Command("SetupWizard", "Open setup", "Complete guided workstation setup and configuration.", "settings", "Setup", "\uE8B0", ["setup", "wizard", "onboarding", "configuration"]),
        Command("ProviderHealth", "Open provider health", "Inspect provider reachability, degradation, and recovery guidance.", "data", "Operations Queue", "\uEB51", ["provider", "health", "degraded", "recovery"]),
        Command("FundReconciliation", "Open reconciliation queues", "Review breaks, match candidates, retained evidence, and close blockers.", "accounting", "Reconciliation", "\uE895", ["reconciliation", "breaks", "exceptions", "queues"]),
        Command("FundReportPack", "Open report drafts", "Review governed report-pack drafts, readiness, and retained evidence.", "reporting", "Drafts", "\uE8A5", ["report", "draft", "pack", "governed"]),
        Command("DataSampling", "Open sample-data flows", "Inspect bounded sample data quality before export or publication.", "data", "Sample Data", "\uF1AD", ["sample", "data", "quality", "preview"]),
        Command("FundStructureSetup", "Open fund setup", "Configure fund structure and account handoff readiness.", "accounting", "Setup", "\uE8B0", ["fund", "setup", "structure", "accounts"]),
        Command("ActivityLog", "Clear activity log", "Clear retained local workstation activity entries after operator confirmation.", "settings", "Notifications", "\uE74D", ["clear", "activity", "log", "destructive"], ShellCommandPaletteExecutionKind.DestructiveAction, true, "Clear visible workstation activity log entries? This cannot be undone."),
    ];

    private static ShellCommandPaletteEntry Command(
        string pageTag,
        string displayName,
        string description,
        string workspaceId,
        string sectionLabel,
        string glyph,
        IReadOnlyList<string> keywords,
        ShellCommandPaletteExecutionKind executionKind = ShellCommandPaletteExecutionKind.NonDestructiveAction,
        bool requiresConfirmation = false,
        string? confirmationPrompt = null)
    {
        var workspaceTitle = ShellNavigationCatalog.GetWorkspace(workspaceId)?.Title ?? workspaceId;
        return new ShellCommandPaletteEntry(pageTag, displayName, description, workspaceTitle, sectionLabel, glyph, string.Empty, displayName, keywords, workspaceId, description, executionKind, requiresConfirmation, confirmationPrompt);
    }

    private static string GetVisibilityLabel(ShellNavigationVisibilityTier visibilityTier)
        => visibilityTier switch
        {
            ShellNavigationVisibilityTier.Primary => string.Empty,
            ShellNavigationVisibilityTier.Secondary => "Specialized",
            ShellNavigationVisibilityTier.Overflow => "Support",
            _ => string.Empty
        };

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IReadOnlyCollection<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}

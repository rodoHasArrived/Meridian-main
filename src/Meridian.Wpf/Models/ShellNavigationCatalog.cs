using System.Windows.Controls;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Models;

public static partial class ShellNavigationCatalog
{
    private static readonly Lazy<IReadOnlyList<ShellPageDescriptor>> PagesValue =
        new(BuildPages);

    private static readonly Lazy<IReadOnlyList<WorkspaceShellDefinition>> WorkspaceShellsValue =
        new(BuildWorkspaceShells);

    private static readonly Lazy<IReadOnlyList<WorkspaceCapabilityDescriptor>> WorkspaceCapabilitiesValue =
        new(BuildWorkspaceCapabilities);

    public static IReadOnlyList<ShellPageDescriptor> Pages => PagesValue.Value;

    public static IReadOnlyList<WorkspaceShellDefinition> WorkspaceShells => WorkspaceShellsValue.Value;

    public static IReadOnlyList<WorkspaceCapabilityDescriptor> WorkspaceCapabilities => WorkspaceCapabilitiesValue.Value;

    private static readonly Lazy<IReadOnlyDictionary<string, WorkspaceShellDescriptor>> WorkspacesById =
        new(BuildWorkspaceLookup);

    private static readonly Lazy<IReadOnlyDictionary<string, WorkspaceShellDefinition>> WorkspaceShellsById =
        new(() => WorkspaceShells.ToDictionary(static shell => shell.WorkspaceId, StringComparer.OrdinalIgnoreCase));

    private static readonly Lazy<IReadOnlyDictionary<string, ShellPageDescriptor>> PagesByTag =
        new(BuildPageLookup);

    private static IReadOnlyList<ShellPageDescriptor> BuildPages()
        => WorkspaceCapabilities.SelectMany(static capability => capability.Pages).ToArray();

    private static IReadOnlyList<WorkspaceShellDefinition> BuildWorkspaceShells()
        => WorkspaceCapabilities.Select(static capability => capability.ShellDefinition).ToArray();

    private static IReadOnlyDictionary<string, WorkspaceShellDescriptor> BuildWorkspaceLookup()
    {
        ArgumentNullException.ThrowIfNull(Workspaces);
        return Workspaces.ToDictionary(static workspace => workspace.Id, StringComparer.OrdinalIgnoreCase);
    }

    public static WorkspaceShellDescriptor GetDefaultWorkspace()
        => GetWorkspace("strategy") ?? Workspaces[0];

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

    public static WorkspaceShellDefinition? GetWorkspaceShell(string? workspaceId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            return null;
        }

        return WorkspaceShellsById.Value.TryGetValue(workspaceId.Trim(), out var shell)
            ? shell
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

    public static string GetCanonicalPageTag(string? pageTag)
        => GetPage(pageTag)?.PageTag ?? pageTag?.Trim() ?? string.Empty;

    public static bool IsWorkspaceShellPageTag(string? pageTag)
    {
        var canonicalPageTag = GetCanonicalPageTag(pageTag);
        return !string.IsNullOrWhiteSpace(canonicalPageTag) &&
               Workspaces.Any(workspace => string.Equals(workspace.HomePageTag, canonicalPageTag, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyCollection<string> GetRegisteredPageTags() => PagesByTag.Value.Keys.ToArray();

    public static IReadOnlyCollection<Type> GetRegisteredPageTypes()
        => Pages
            .Select(static page => page.PageType)
            .Distinct()
            .ToArray();

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

        return descriptor.RelatedPageTags
            .Select(GetPage)
            .Where(static related => related is not null)
            .Select(static related => related!)
            .ToArray();
    }

    public static IReadOnlyList<WorkspacePaneDefinition> ResolveDefaultPanes(WorkspaceShellState state)
    {
        var definition = GetWorkspaceShell(state.WorkspaceId);
        if (definition is null)
        {
            return Array.Empty<WorkspacePaneDefinition>();
        }

        if (!state.HasPrimaryContext && definition.ContextlessPanes.Count > 0)
        {
            return definition.ContextlessPanes;
        }

        if (state.WindowMode == BoundedWindowMode.WorkbenchPreset &&
            !string.IsNullOrWhiteSpace(state.LayoutPresetId) &&
            definition.PresetPanes.TryGetValue(state.LayoutPresetId, out var presetPanes))
        {
            return presetPanes;
        }

        return definition.DefaultPanes;
    }

    public static string GetPageTitle(string pageTag)
        => GetPage(pageTag)?.Title ?? GetCanonicalPageTag(pageTag);

    private static IReadOnlyDictionary<string, ShellPageDescriptor> BuildPageLookup()
    {
        var lookup = new Dictionary<string, ShellPageDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in Pages)
        {
            RegisterTag(lookup, page.PageTag, page);

            foreach (var alias in page.Aliases)
            {
                RegisterTag(lookup, alias, page);
            }
        }

        return lookup;
    }

    private static void RegisterTag(IDictionary<string, ShellPageDescriptor> lookup, string tag, ShellPageDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        if (lookup.TryGetValue(tag, out var existing) &&
            !string.Equals(existing.PageTag, descriptor.PageTag, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Shell navigation tag '{tag}' is already registered for '{existing.PageTag}' and cannot also point to '{descriptor.PageTag}'.");
        }

        lookup[tag] = descriptor;
    }

    private static ShellPageDescriptor Page<TPage>(
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
        IReadOnlyList<string>? aliases = null,
        bool hideFromDefaultPalette = false)
        where TPage : class
    {
        return new ShellPageDescriptor(
            PageTag: pageTag,
            PageType: typeof(TPage),
            Title: title,
            Subtitle: subtitle,
            WorkspaceId: workspaceId,
            SectionLabel: sectionLabel,
            Glyph: glyph,
            Order: order,
            VisibilityTier: visibilityTier,
            SearchKeywords: searchKeywords ?? Array.Empty<string>(),
            RelatedPageTags: relatedPageTags ?? Array.Empty<string>(),
            Aliases: aliases ?? Array.Empty<string>(),
            HideFromDefaultPalette: hideFromDefaultPalette);
    }

    private static WorkspacePaneDefinition Pane(
        string pageTag,
        PaneDropAction action,
        WorkspacePaneParameterBinding parameterBinding = WorkspacePaneParameterBinding.None,
        bool openWithoutBoundParameter = false,
        string? fallbackPageTag = null,
        PaneDropAction? fallbackAction = null)
        => new(
            PageTag: pageTag,
            Action: action,
            ParameterBinding: parameterBinding,
            OpenWithoutBoundParameter: openWithoutBoundParameter,
            FallbackPageTag: fallbackPageTag,
            FallbackAction: fallbackAction);
}

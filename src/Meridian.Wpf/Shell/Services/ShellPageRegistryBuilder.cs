using Meridian.Ui.Services;
using Meridian.Wpf.Features;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Shell.Services;

public sealed class ShellPageRegistryBuilder
{
    private readonly List<ShellPageDescriptor> _pages = [];
    private readonly List<WorkspaceCapabilityDescriptor> _capabilities = [];

    public ShellPageRegistryBuilder Contribute(IEnumerable<ShellPageDescriptor> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        _pages.AddRange(pages);
        return this;
    }

    public ShellPageRegistryBuilder ContributeCapability(WorkspaceCapabilityDescriptor capability)
    {
        ArgumentNullException.ThrowIfNull(capability);

        _capabilities.Add(capability);
        return this;
    }

    public IShellPageRegistry Build()
    {
        var pagesByTag = new Dictionary<string, ShellPageDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in _capabilities.SelectMany(static capability => capability.Pages).Concat(_pages))
        {
            if (pagesByTag.TryGetValue(page.PageTag, out var existing) && !Equals(existing, page))
            {
                throw new InvalidOperationException(
                    $"Shell page '{page.PageTag}' is already registered by '{existing.PageType.Name}' and cannot also point to '{page.PageType.Name}'.");
            }

            pagesByTag[page.PageTag] = page;
        }

        var pages = pagesByTag.Values
            .OrderBy(static page => page.WorkspaceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static page => page.VisibilityTier)
            .ThenBy(static page => page.Order)
            .ThenBy(static page => page.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var workspaceOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["trading"] = 0,
            ["portfolio"] = 1,
            ["accounting"] = 2,
            ["reporting"] = 3,
            ["strategy"] = 4,
            ["data"] = 5,
            ["settings"] = 6
        };

        var capabilities = _capabilities
            .Select(capability =>
            {
                var workspacePages = capability.Pages
                    .Concat(pages.Where(page => string.Equals(page.WorkspaceId, capability.Workspace.Id, StringComparison.OrdinalIgnoreCase)))
                    .GroupBy(static page => page.PageTag, StringComparer.OrdinalIgnoreCase)
                    .Select(static group => group.First())
                    .OrderBy(static page => page.VisibilityTier)
                    .ThenBy(static page => page.Order)
                    .ThenBy(static page => page.Title, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return capability with { Pages = workspacePages };
            })
            .OrderBy(capability => workspaceOrder.GetValueOrDefault(capability.Workspace.Id, int.MaxValue))
            .ThenBy(static capability => capability.Workspace.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ShellPageRegistry(
            Pages: pages,
            WorkspaceCapabilities: capabilities,
            WorkspaceShells: capabilities.Select(static capability => capability.ShellDefinition).ToArray());
    }

    public static IShellPageRegistry BuildDefault()
    {
        var builder = new ShellPageRegistryBuilder();
        var featureOwnedWorkspaceIds = DesktopFeatureModuleRegistry.GetModules()
            .Select(static module => module.DescribeWorkspace()?.Workspace.Id)
            .Where(static workspaceId => !string.IsNullOrWhiteSpace(workspaceId))
            .Select(static workspaceId => workspaceId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var module in DesktopFeatureModuleRegistry.GetModules())
        {
            builder.Contribute(module.DescribePages());

            if (module.DescribeWorkspace() is { } capability)
            {
                builder.ContributeCapability(capability);
            }
        }

        foreach (var capability in ShellNavigationCatalog.BuildFallbackWorkspaceCapabilities(featureOwnedWorkspaceIds))
        {
            builder.ContributeCapability(capability);
        }

        return builder.Build();
    }

    public static ShellPageDescriptor Page<TPage>(
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

    public static WorkspacePaneDefinition Pane(
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

    private sealed record ShellPageRegistry(
        IReadOnlyList<ShellPageDescriptor> Pages,
        IReadOnlyList<WorkspaceCapabilityDescriptor> WorkspaceCapabilities,
        IReadOnlyList<WorkspaceShellDefinition> WorkspaceShells) : IShellPageRegistry;
}

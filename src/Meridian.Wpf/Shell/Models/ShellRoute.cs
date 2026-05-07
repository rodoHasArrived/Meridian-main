using Meridian.Wpf.Models;

namespace Meridian.Wpf.Shell.Models;

/// <summary>
/// Shell-owned route metadata projected from the legacy navigation catalog.
/// </summary>
public sealed record ShellRoute(
    string PageTag,
    Type PageType,
    string Title,
    string Subtitle,
    string WorkspaceId,
    string SectionLabel,
    string Glyph,
    int Order,
    ShellNavigationVisibilityTier VisibilityTier,
    IReadOnlyList<string> SearchKeywords,
    IReadOnlyList<string> RelatedPageTags,
    IReadOnlyList<string> Aliases,
    bool HideFromDefaultPalette = false)
{
    public static ShellRoute FromDescriptor(ShellPageDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return new ShellRoute(
            descriptor.PageTag,
            descriptor.PageType,
            descriptor.Title,
            descriptor.Subtitle,
            descriptor.WorkspaceId,
            descriptor.SectionLabel,
            descriptor.Glyph,
            descriptor.Order,
            descriptor.VisibilityTier,
            descriptor.SearchKeywords,
            descriptor.RelatedPageTags,
            descriptor.Aliases,
            descriptor.HideFromDefaultPalette);
    }
}

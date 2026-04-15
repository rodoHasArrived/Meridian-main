namespace Meridian.Wpf.Models;

public enum ShellNavigationVisibilityTier
{
    Primary,
    Secondary,
    Overflow
}

public enum WorkspaceChromePresentationMode
{
    Standalone,
    Docked
}

public sealed record WorkspaceShellDescriptor(
    string Id,
    string Title,
    string Description,
    string Summary,
    string HomePageTag,
    string TileSummary);

public sealed record ShellPageDescriptor(
    string PageTag,
    string Title,
    string Subtitle,
    string WorkspaceId,
    string SectionLabel,
    string Glyph,
    int Order,
    ShellNavigationVisibilityTier VisibilityTier,
    IReadOnlyList<string> SearchKeywords,
    IReadOnlyList<string> RelatedPageTags,
    bool HideFromDefaultPalette = false);

public sealed record ShellNavigationItem(
    string PageTag,
    string Title,
    string Subtitle,
    string WorkspaceTitle,
    string SectionLabel,
    string Glyph,
    string VisibilityLabel)
{
    public string MetaLine => string.IsNullOrWhiteSpace(VisibilityLabel)
        ? $"{WorkspaceTitle} · {SectionLabel}"
        : $"{WorkspaceTitle} · {SectionLabel} · {VisibilityLabel}";
}

public sealed record ShellCommandPaletteEntry(
    string PageTag,
    string Title,
    string Subtitle,
    string WorkspaceTitle,
    string SectionLabel,
    string Glyph,
    string VisibilityLabel)
{
    public string MetaLine => string.IsNullOrWhiteSpace(VisibilityLabel)
        ? $"{WorkspaceTitle} · {SectionLabel}"
        : $"{WorkspaceTitle} · {SectionLabel} · {VisibilityLabel}";
}

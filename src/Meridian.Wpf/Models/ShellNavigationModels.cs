using Meridian.Ui.Services;

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

public enum WorkspacePaneParameterBinding : byte
{
    None,
    ActiveRunId
}

public sealed record WorkspacePaneDefinition(
    string PageTag,
    PaneDropAction Action,
    WorkspacePaneParameterBinding ParameterBinding = WorkspacePaneParameterBinding.None,
    bool OpenWithoutBoundParameter = false,
    string? FallbackPageTag = null,
    PaneDropAction? FallbackAction = null);

public sealed record WorkspaceShellDefinition(
    string WorkspaceId,
    string LayoutId,
    string DisplayName,
    IReadOnlyList<WorkspacePaneDefinition> DefaultPanes,
    IReadOnlyDictionary<string, IReadOnlyList<WorkspacePaneDefinition>> PresetPanes,
    IReadOnlyList<WorkspacePaneDefinition> ContextlessPanes,
    Type? StateProviderType = null,
    Type? ViewModelType = null);

public sealed record ShellPageDescriptor(
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
    public string MetaLine => ShellNavigationMetaFormatter.FormatMetaLine(WorkspaceTitle, SectionLabel, VisibilityLabel);
}

public sealed record WorkspaceShellState(
    string WorkspaceId,
    string LayoutId,
    string DisplayName,
    string? LayoutScopeKey,
    BoundedWindowMode WindowMode,
    string? LayoutPresetId = null,
    bool HasPrimaryContext = true,
    string? ActiveRunId = null);

public sealed record ShellCommandPaletteEntry(
    string PageTag,
    string Title,
    string Subtitle,
    string WorkspaceTitle,
    string SectionLabel,
    string Glyph,
    string VisibilityLabel)
{
    public string MetaLine => ShellNavigationMetaFormatter.FormatMetaLine(WorkspaceTitle, SectionLabel, VisibilityLabel);
}

internal static class ShellNavigationMetaFormatter
{
    public static string FormatMetaLine(string workspaceTitle, string sectionLabel, string visibilityLabel)
    {
        var normalizedVisibility = NormalizeToken(visibilityLabel);
        var parts = new List<string>(3)
        {
            NormalizeToken(sectionLabel),
            NormalizeToken(workspaceTitle)
        };

        if (!string.IsNullOrWhiteSpace(normalizedVisibility) &&
            !Contains(parts, normalizedVisibility))
        {
            parts.Add(normalizedVisibility);
        }

        return string.Join(" · ", parts.Where(static part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string NormalizeToken(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static bool Contains(IEnumerable<string> values, string candidate)
        => values.Any(value => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));
}

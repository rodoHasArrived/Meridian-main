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
    public string MetaLine
    {
        get
        {
            var parts = new[] { SectionLabel, VisibilityLabel }
                .Where(static part => !string.IsNullOrWhiteSpace(part));

            return string.Join(" · ", parts);
        }
    }
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
    public string MetaLine
    {
        get
        {
            var parts = new[] { SectionLabel, VisibilityLabel }
                .Where(static part => !string.IsNullOrWhiteSpace(part));

            return string.Join(" · ", parts);
        }
    }
}

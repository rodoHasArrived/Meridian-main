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

public sealed record WorkspaceCapabilityDescriptor(
    WorkspaceShellDescriptor Workspace,
    WorkspaceShellDefinition ShellDefinition,
    IReadOnlyList<ShellPageDescriptor> Pages);

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

public enum ShellCommandPaletteExecutionKind
{
    SafeNavigation,
    NonDestructiveAction,
    StateChangingAction,
    DestructiveAction
}

public sealed record ShellCommandPaletteEntry(
    string PageTag,
    string Title,
    string Subtitle,
    string WorkspaceTitle,
    string SectionLabel,
    string Glyph,
    string VisibilityLabel,
    string DisplayName,
    IReadOnlyList<string> Keywords,
    string Workspace,
    string Description,
    ShellCommandPaletteExecutionKind ExecutionKind,
    bool RequiresConfirmation,
    string? ConfirmationPrompt = null)
{
    public ShellCommandPaletteEntry(
        string PageTag,
        string Title,
        string Subtitle,
        string WorkspaceTitle,
        string SectionLabel,
        string Glyph,
        string VisibilityLabel)
        : this(
            PageTag,
            Title,
            Subtitle,
            WorkspaceTitle,
            SectionLabel,
            Glyph,
            VisibilityLabel,
            DisplayName: Title,
            Keywords: Array.Empty<string>(),
            Workspace: WorkspaceTitle,
            Description: Subtitle,
            ExecutionKind: ShellCommandPaletteExecutionKind.SafeNavigation,
            RequiresConfirmation: false)
    {
    }

    public string MetaLine
    {
        get
        {
            var parts = new[] { SectionLabel, VisibilityLabel, ExecutionLabel }
                .Where(static part => !string.IsNullOrWhiteSpace(part));

            return string.Join(" · ", parts);
        }
    }

    public string ExecutionLabel => ExecutionKind switch
    {
        ShellCommandPaletteExecutionKind.NonDestructiveAction => "Safe action",
        ShellCommandPaletteExecutionKind.StateChangingAction => "Confirmation required",
        ShellCommandPaletteExecutionKind.DestructiveAction => "Destructive",
        _ => string.Empty
    };

    public bool IsRiskCommand => ExecutionKind is ShellCommandPaletteExecutionKind.StateChangingAction or ShellCommandPaletteExecutionKind.DestructiveAction;
}

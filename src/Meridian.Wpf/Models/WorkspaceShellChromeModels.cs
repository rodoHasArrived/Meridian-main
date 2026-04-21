namespace Meridian.Wpf.Models;

public static class WorkspaceTone
{
    public const string Neutral = "Neutral";
    public const string Info = "Info";
    public const string Success = "Success";
    public const string Warning = "Warning";
    public const string Danger = "Danger";
    public const string Primary = "Primary";
    public const string Secondary = "Secondary";
}

public sealed class WorkspaceShellContext
{
    public string WorkspaceTitle { get; init; } = string.Empty;

    public string WorkspaceSubtitle { get; init; } = string.Empty;

    public IReadOnlyList<WorkspaceShellBadge> Badges { get; init; } = Array.Empty<WorkspaceShellBadge>();
}

public sealed class WorkspaceShellBadge
{
    public string Label { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    public string Glyph { get; init; } = string.Empty;

    public string Tone { get; init; } = WorkspaceTone.Neutral;
}

public sealed class WorkspaceShellContextInput
{
    public string WorkspaceTitle { get; init; } = string.Empty;

    public string WorkspaceSubtitle { get; init; } = string.Empty;

    public string PrimaryScopeLabel { get; init; } = "Context";

    public string PrimaryScopeValue { get; init; } = string.Empty;

    public string AsOfValue { get; init; } = "—";

    public string FreshnessValue { get; init; } = string.Empty;

    public string ReviewStateLabel { get; init; } = "Review";

    public string ReviewStateValue { get; init; } = string.Empty;

    public string ReviewStateTone { get; init; } = WorkspaceTone.Neutral;

    public string CriticalLabel { get; init; } = "Attention";

    public string CriticalValue { get; init; } = string.Empty;

    public string CriticalTone { get; init; } = WorkspaceTone.Info;

    public IReadOnlyList<WorkspaceShellBadge> AdditionalBadges { get; init; } = Array.Empty<WorkspaceShellBadge>();
}

public sealed class WorkspaceCommandGroup
{
    public IReadOnlyList<WorkspaceCommandItem> PrimaryCommands { get; init; } = Array.Empty<WorkspaceCommandItem>();

    public IReadOnlyList<WorkspaceCommandItem> SecondaryCommands { get; init; } = Array.Empty<WorkspaceCommandItem>();
}

public sealed class WorkspaceCommandItem
{
    public string Id { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string ShortcutHint { get; init; } = string.Empty;

    public string Glyph { get; init; } = string.Empty;

    public string Tone { get; init; } = WorkspaceTone.Secondary;

    public bool IsEnabled { get; init; } = true;
}

public sealed class WorkspaceQueueItem
{
    public string Title { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public string StatusLabel { get; init; } = string.Empty;

    public string CountLabel { get; init; } = string.Empty;

    public string Tone { get; init; } = WorkspaceTone.Neutral;

    public bool IsBlocked { get; init; }

    public string PrimaryActionId { get; init; } = string.Empty;

    public string PrimaryActionLabel { get; init; } = string.Empty;

    public string SecondaryActionId { get; init; } = string.Empty;

    public string SecondaryActionLabel { get; init; } = string.Empty;

    public string AutomationName { get; init; } = string.Empty;
}

public sealed class WorkspaceRecentItem
{
    public string Title { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public string Meta { get; init; } = string.Empty;

    public string Tone { get; init; } = WorkspaceTone.Neutral;

    public string ActionId { get; init; } = string.Empty;

    public string ActionLabel { get; init; } = string.Empty;
}

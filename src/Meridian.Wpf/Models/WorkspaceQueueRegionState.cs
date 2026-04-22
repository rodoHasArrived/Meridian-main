namespace Meridian.Wpf.Models;

public sealed class WorkspaceQueueRegionState
{
    public bool IsLoading { get; init; }
    public bool IsEmpty { get; init; }
    public bool HasError { get; init; }
    public string IconGlyph { get; init; } = "\uE895";
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string PrimaryActionLabel { get; init; } = string.Empty;
    public string PrimaryActionId { get; init; } = string.Empty;
    public string SecondaryActionLabel { get; init; } = string.Empty;
    public string SecondaryActionId { get; init; } = string.Empty;

    public bool IsVisible => IsLoading || IsEmpty || HasError;

    public static WorkspaceQueueRegionState Loading(string title, string description) =>
        new()
        {
            IsLoading = true,
            IconGlyph = "\uE895",
            Title = title,
            Description = description
        };

    public static WorkspaceQueueRegionState Empty(string title, string description, string primaryActionLabel, string primaryActionId, string secondaryActionLabel = "", string secondaryActionId = "") =>
        new()
        {
            IsEmpty = true,
            IconGlyph = "\uE8B7",
            Title = title,
            Description = description,
            PrimaryActionLabel = primaryActionLabel,
            PrimaryActionId = primaryActionId,
            SecondaryActionLabel = secondaryActionLabel,
            SecondaryActionId = secondaryActionId
        };

    public static WorkspaceQueueRegionState Error(string title, string description, string primaryActionLabel, string primaryActionId, string secondaryActionLabel = "", string secondaryActionId = "") =>
        new()
        {
            HasError = true,
            IconGlyph = "\uEA39",
            Title = title,
            Description = description,
            PrimaryActionLabel = primaryActionLabel,
            PrimaryActionId = primaryActionId,
            SecondaryActionLabel = secondaryActionLabel,
            SecondaryActionId = secondaryActionId
        };

    public static WorkspaceQueueRegionState None => new();
}

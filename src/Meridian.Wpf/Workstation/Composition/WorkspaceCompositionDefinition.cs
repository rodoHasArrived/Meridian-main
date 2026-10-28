namespace Meridian.Wpf.Workstation.Composition;

/// <summary>
/// Associates a named workspace slot with its contributing module.
/// </summary>
public sealed class WorkspaceCompositionSlot
{
    public string SlotId { get; init; } = string.Empty;
    public string ModuleId { get; init; } = string.Empty;
}

/// <summary>
/// Maps a workspace identity to the module assigned to each composition slot.
/// </summary>
public sealed class WorkspaceCompositionDefinition
{
    public string WorkspaceId { get; init; } = string.Empty;
    public IReadOnlyList<WorkspaceCompositionSlot> Slots { get; init; } = Array.Empty<WorkspaceCompositionSlot>();
}

namespace Meridian.Wpf.Workstation.Composition;

public sealed class WorkspaceCompositionSlot
{
    public string SlotId { get; init; } = string.Empty;
    public string ModuleId { get; init; } = string.Empty;
}

public sealed class WorkspaceCompositionDefinition
{
    public string WorkspaceId { get; init; } = string.Empty;
    public IReadOnlyList<WorkspaceCompositionSlot> Slots { get; init; } = Array.Empty<WorkspaceCompositionSlot>();
}

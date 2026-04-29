namespace Meridian.Contracts.Workstation;

/// <summary>
/// Shared catalog projection for reusable workstation workflows.
/// </summary>
public sealed record WorkflowLibraryDto(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<WorkflowDefinitionDto> Workflows,
    IReadOnlyList<WorkflowActionDto> Actions);

/// <summary>
/// User-facing workflow definition that groups actions, evidence, and target workspace.
/// </summary>
public sealed record WorkflowDefinitionDto(
    string WorkflowId,
    string Title,
    string Summary,
    string WorkspaceId,
    string WorkspaceTitle,
    string EntryPageTag,
    string Tone,
    IReadOnlyList<WorkflowActionDto> Actions,
    IReadOnlyList<string> EvidenceTags,
    IReadOnlyList<string> MarketPatternTags);

/// <summary>
/// Reusable action target that can be used by summaries, inbox routing, and workflow UI.
/// </summary>
public sealed record WorkflowActionDto(
    string ActionId,
    string Label,
    string Detail,
    string TargetPageTag,
    string Tone,
    OperatorWorkItemKindDto? WorkItemKind,
    IReadOnlyList<string> RoutePrefixes,
    IReadOnlyList<string> RouteContains,
    IReadOnlyList<string> Aliases);

/// <summary>
/// Durable operator-saved workflow preset that can be launched from web or desktop shells.
/// </summary>
public sealed record WorkflowPresetDto(
    string PresetId,
    string Name,
    string? Description,
    string WorkflowId,
    string WorkflowTitle,
    string? ActionId,
    string ActionLabel,
    string WorkspaceId,
    string WorkspaceTitle,
    string TargetPageTag,
    IReadOnlyList<string> Tags,
    bool IsPinned,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastUsedAt);

/// <summary>
/// Shared preset catalog projection for the workstation workflow library.
/// </summary>
public sealed record WorkflowPresetLibraryDto(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<WorkflowPresetDto> Presets);

/// <summary>
/// Request to create or update an operator-saved workflow preset.
/// </summary>
public sealed record WorkflowPresetSaveRequest(
    string? PresetId,
    string Name,
    string? Description,
    string WorkflowId,
    string? ActionId,
    IReadOnlyList<string>? Tags,
    bool IsPinned);

/// <summary>
/// Request to update a workflow preset's pinned state.
/// </summary>
public sealed record WorkflowPresetPinRequest(bool IsPinned);

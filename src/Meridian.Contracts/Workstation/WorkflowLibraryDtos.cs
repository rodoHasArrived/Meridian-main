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

using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Workflows;

/// <summary>
/// Resolves workflow actions into shell page targets for summaries, queues, and UI.
/// </summary>
public interface IWorkflowActionCatalog
{
    IReadOnlyList<WorkflowDefinitionDto> GetWorkflowDefinitions();

    IReadOnlyList<WorkflowActionDto> GetActions();

    WorkflowActionDto? ResolveAction(string? actionId);

    WorkflowActionDto? ResolveOperatorWorkItem(OperatorWorkItemDto? workItem);

    WorkflowActionDto? ResolveRoute(string? targetRoute);

    string ResolveTargetPageTag(string? actionId, string fallbackPageTag);
}

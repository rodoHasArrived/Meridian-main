using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Workflows;

/// <summary>
/// Supplies reusable workstation workflow definitions.
/// </summary>
public interface IWorkflowDefinitionProvider
{
    IReadOnlyList<WorkflowDefinitionDto> GetWorkflowDefinitions();
}

using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.PrivateCapital;

public sealed partial class PrivateCapitalCloseCockpitService
{
    // First publication creates its package and lock receipt. Published records and active
    // reopen remediation remain evidence obligations; they cannot be exempted as future outputs.
    private static bool RequiresPublishedCloseEvidence(IReadOnlyList<OperationsContinuityWorkflowDto> workflows)
        => workflows.Any(static workflow => workflow.Status == OperationsWorkflowStatusDto.Closed ||
            (workflow.ClosePackage is not null && workflow.CloseReadiness?.IsReadyToClose != true));

    private static bool RequiresCloseControlLane(IReadOnlyList<OperationsContinuityWorkflowDto> workflows)
        => RequiresPublishedCloseEvidence(workflows) || workflows.Any(workflow => workflow.CloseChecklist.Any(task =>
            CloseControlRequirements.Any(requirement => MatchesCloseControlRequirement(task, requirement)) &&
            (IsCloseControlTaskBlocked(task) || !IsChecklistTaskComplete(task) || string.IsNullOrWhiteSpace(task.EvidencePointer))));
}

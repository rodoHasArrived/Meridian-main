using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.OperationsContinuity;

public sealed partial class OperationsContinuityWorkflowService
{
    private static OperationsWorkflowBlockerDto? ValidateRetainedChecklistControls(
        OperationsContinuityWorkflow workflow,
        IReadOnlyList<OperationsWorkflowAuditDto> auditTimeline,
        IReadOnlyList<OperationsChecklistControlApprovalDto>? requestedApprovals)
    {
        var checklist = BuildChecklist(workflow, auditTimeline.Select(ToTimelineEntry).ToArray());
        var retained = OperationsChecklistControlEvidence.Collect(checklist, workflow.Approvals, workflow.ApprovalState);

        foreach (var requested in requestedApprovals ?? [])
        {
            if (requested is null || !retained.Any(approval =>
                string.Equals(approval.TaskId, requested.TaskId?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(approval.ApprovedBy, requested.ApprovedBy?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                approval.ApprovedAtUtc == requested.ApprovedAtUtc))
            {
                return new OperationsWorkflowBlockerDto(
                    "CLOSE_CHECKLIST_CONTROL_APPROVAL_NOT_RETAINED",
                    $"Checklist control '{requested?.TaskId}' does not match a current retained acknowledgment or approval decision. Refresh the workflow and review its current evidence.",
                    OperationsGateKeyDto.Approval,
                    "Critical",
                    []);
            }
        }

        return null;
    }
}

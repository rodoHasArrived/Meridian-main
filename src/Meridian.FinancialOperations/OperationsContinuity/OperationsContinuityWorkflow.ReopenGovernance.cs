using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.OperationsContinuity;

public sealed partial class OperationsContinuityWorkflow
{
    public OperationsWorkflowBlockerDto? GetReopenTransitionBlocker(OperationsReopenWorkflowRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (PreparerAttestationApprovalBoundary.IsReservedReference(request.ApprovalReference))
        {
            return new OperationsWorkflowBlockerDto(
                "PREPARER_ATTESTATION_NOT_APPROVAL",
                "A preparer attestation cannot authorize reopening a controlled workflow.",
                OperationsGateKeyDto.Approval,
                "Critical",
                []);
        }

        if (!IsClosed)
        {
            return new OperationsWorkflowBlockerDto(
                "WORKFLOW_NOT_CLOSED",
                "Only a closed workflow can be reopened.",
                null,
                "Error",
                []);
        }

        if (!request.IsGovernedAdmin ||
            string.IsNullOrWhiteSpace(request.Rationale) ||
            string.IsNullOrWhiteSpace(request.IncidentId) ||
            string.IsNullOrWhiteSpace(request.Justification) ||
            string.IsNullOrWhiteSpace(request.ApprovalReference) ||
            string.IsNullOrWhiteSpace(request.ImpactSummary))
        {
            return new OperationsWorkflowBlockerDto(
                "REOPEN_GOVERNANCE_METADATA_REQUIRED",
                "Reopening requires governed administrator authority, rationale, incident metadata, justification, approval reference, and impact summary.",
                OperationsGateKeyDto.Approval,
                "Critical",
                []);
        }

        return null;
    }
}

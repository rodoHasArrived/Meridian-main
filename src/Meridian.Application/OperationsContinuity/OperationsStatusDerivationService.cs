using Meridian.Contracts.Workstation;

namespace Meridian.Application.OperationsContinuity;

public interface IOperationsStatusDerivationService
{
    OperationsWorkflowStatusDto Derive(OperationsContinuityWorkflow workflow);
}

public sealed class OperationsStatusDerivationService : IOperationsStatusDerivationService
{
    public OperationsWorkflowStatusDto Derive(OperationsContinuityWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        if (workflow.Gates.Any(static gate => gate.Status == OperationsGateStatusDto.Blocked))
        {
            return OperationsWorkflowStatusDto.Blocked;
        }

        if (workflow.IsClosed)
        {
            return OperationsWorkflowStatusDto.Closed;
        }

        if (workflow.Gates.All(static gate => gate.Status == OperationsGateStatusDto.Passed) &&
            workflow.ApprovalState == OperationsApprovalStateDto.Approved)
        {
            return OperationsWorkflowStatusDto.ReadyForClose;
        }

        if (workflow.Gates.Any(static gate => gate.Status == OperationsGateStatusDto.ReviewRequired))
        {
            return workflow.ReconciliationGate.Status == OperationsGateStatusDto.ReviewRequired
                ? OperationsWorkflowStatusDto.ReconciliationActive
                : OperationsWorkflowStatusDto.ApprovalPending;
        }

        if (workflow.BrokerIngestGate.Status == OperationsGateStatusDto.Passed &&
            workflow.SecurityMasterGate.Status == OperationsGateStatusDto.Passed &&
            workflow.LedgerPostingGate.Status == OperationsGateStatusDto.Passed &&
            workflow.ReconciliationGate.Status == OperationsGateStatusDto.Passed &&
            workflow.ApprovalGate.Status != OperationsGateStatusDto.Passed)
        {
            return OperationsWorkflowStatusDto.ApprovalPending;
        }

        if (workflow.ApprovalGate.Status == OperationsGateStatusDto.InProgress ||
            workflow.ApprovalState is OperationsApprovalStateDto.Submitted or OperationsApprovalStateDto.ReviewerAssigned)
        {
            return OperationsWorkflowStatusDto.ApprovalPending;
        }

        if (workflow.ReconciliationGate.Status == OperationsGateStatusDto.InProgress ||
            workflow.ReconciliationState is OperationsReconciliationStateDto.AutoMatched or
                OperationsReconciliationStateDto.ExceptionsOpen or
                OperationsReconciliationStateDto.InReview)
        {
            return OperationsWorkflowStatusDto.ReconciliationActive;
        }

        if (workflow.LedgerPostingGate.Status == OperationsGateStatusDto.InProgress ||
            workflow.LedgerPostingState is OperationsLedgerPostingStateDto.Drafted or OperationsLedgerPostingStateDto.Validated)
        {
            return OperationsWorkflowStatusDto.LedgerPostingDraft;
        }

        if (workflow.SecurityMasterGate.Status == OperationsGateStatusDto.InProgress ||
            workflow.SecurityMasterState is OperationsSecurityMasterStateDto.ResolvedAllInstruments or
                OperationsSecurityMasterStateDto.OverridesRequested or
                OperationsSecurityMasterStateDto.OverridesApproved)
        {
            return OperationsWorkflowStatusDto.SecurityMasterValidation;
        }

        if (workflow.BrokerIngestGate.Status == OperationsGateStatusDto.InProgress ||
            workflow.BrokerIntakeState is OperationsBrokerIntakeStateDto.Imported or
                OperationsBrokerIntakeStateDto.Normalized or
                OperationsBrokerIntakeStateDto.MatchedToInternalRun)
        {
            return OperationsWorkflowStatusDto.CollectingBrokerData;
        }

        return OperationsWorkflowStatusDto.NotStarted;
    }
}

using Meridian.Contracts.Workstation;
using Meridian.FSharp.Operations;

namespace Meridian.FinancialOperations.OperationsContinuity;

public interface IOperationsStatusDerivationService
{
    OperationsWorkflowStatusDto Derive(OperationsContinuityWorkflow workflow);
}

public sealed class OperationsStatusDerivationService : IOperationsStatusDerivationService
{
    public OperationsWorkflowStatusDto Derive(OperationsContinuityWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return OperationsContinuityInterop.DeriveStatus(
            workflow.IsClosed,
            workflow.BrokerIntakeState,
            workflow.SecurityMasterState,
            workflow.LedgerPostingState,
            workflow.ReconciliationState,
            workflow.ApprovalState,
            workflow.BrokerIngestGate.Status,
            workflow.SecurityMasterGate.Status,
            workflow.LedgerPostingGate.Status,
            workflow.ReconciliationGate.Status,
            workflow.ApprovalGate.Status);
    }
}

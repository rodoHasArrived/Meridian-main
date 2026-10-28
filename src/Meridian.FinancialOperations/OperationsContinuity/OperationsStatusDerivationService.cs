using Meridian.Contracts.Workstation;
using Meridian.FSharp.Operations;

namespace Meridian.FinancialOperations.OperationsContinuity;

/// <summary>
/// Derives workflow status from operations continuity state and control evidence.
/// </summary>
public interface IOperationsStatusDerivationService
{
    OperationsWorkflowStatusDto Derive(OperationsContinuityWorkflow workflow);
}

/// <summary>
/// Calculates workflow status from retained operations state and control evidence.
/// </summary>
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

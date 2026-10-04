using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.OperationsContinuity;

public sealed partial class FinancialOperationsCommandCenterReadService
{
    // Retain the diagnostic count for outputs produced by first publication without turning
    // those outputs into prerequisites. Previously published evidence remains required.
    private static int PendingCloseOutputCount(OperationsContinuityWorkflowDto? workflow)
        => workflow is null || workflow.ClosePackage is not null || workflow.Status == OperationsWorkflowStatusDto.Closed
            ? 0
            : workflow.AccountingRecordSummary?.EvidenceCategories.Count(static category =>
                !category.IsComplete && category.Key is "exports" or "restatement-lineage") ?? 0;
}

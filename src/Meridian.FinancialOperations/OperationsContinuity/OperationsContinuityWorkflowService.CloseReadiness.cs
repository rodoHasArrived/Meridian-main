using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.OperationsContinuity;

public sealed partial class OperationsContinuityWorkflowService
{
    private Task<IReadOnlyList<OperationsWorkflowBlockerDto>> ValidatePublicationReadinessAsync(
        Guid workflowId, OperationsCloseWorkflowRequestDto request, CancellationToken ct)
    {
        var scope = request.CloseScope;
        if (scope is null || string.IsNullOrWhiteSpace(scope.FundProfileId) ||
            scope.LedgerBookId is null || scope.LedgerBookId == Guid.Empty ||
            scope.FundAccountId is null || scope.FundAccountId == Guid.Empty ||
            string.IsNullOrWhiteSpace(scope.EntityId) || string.IsNullOrWhiteSpace(scope.PeriodId))
            return Task.FromResult<IReadOnlyList<OperationsWorkflowBlockerDto>>([
                new("CLOSE_SCOPE_REQUIRED", "The complete selected close subject is required before publication.",
                    OperationsGateKeyDto.Approval, "Critical", [])]);
        return _closeReadinessGuard is null
            ? Task.FromResult<IReadOnlyList<OperationsWorkflowBlockerDto>>([
                new("CLOSE_READINESS_UNAVAILABLE", "Shared close evidence authority is required before publication.",
                    OperationsGateKeyDto.Approval, "Critical", [])])
            : _closeReadinessGuard.ValidateAsync(workflowId, request.ExpectedVersion, scope, ct: ct);
    }
}

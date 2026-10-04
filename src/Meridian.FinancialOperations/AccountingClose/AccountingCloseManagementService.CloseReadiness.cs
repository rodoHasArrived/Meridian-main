using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.AccountingClose;

public sealed partial class AccountingCloseManagementService
{
    private async Task<IReadOnlyList<AccountingConfigurationValidationIssueDto>> ValidateSharedCloseReadinessAsync(
        LockClosePeriodRequestDto request,
        ClosePeriodPlanDto plan,
        string? tenantId,
        string? companyId,
        CancellationToken ct)
    {
        var blockers = _closeReadinessGuard is null
            ? new OperationsWorkflowBlockerDto[]
            {
                new("CLOSE_READINESS_REQUIRED", "Shared close-readiness authority is unavailable.", null, "Critical", [])
            }
            : await _closeReadinessGuard.ValidateAsync(request.WorkflowId, request.ExpectedWorkflowVersion,
                request.CloseScope, tenantId, companyId, ct).ConfigureAwait(false);
        return blockers.Select(blocker => new AccountingConfigurationValidationIssueDto(
            blocker.Code,
            AccountingConfigurationValidationSeverityDto.Critical,
            blocker.Message,
            plan.ClosePlanId,
            "Resolve shared close evidence and refresh the selected scope before period lock.")).ToArray();
    }
}

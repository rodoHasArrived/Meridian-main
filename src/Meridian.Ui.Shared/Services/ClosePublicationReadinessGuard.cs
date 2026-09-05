using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Endpoints;

namespace Meridian.Ui.Shared.Services;

/// <summary>Resolves the shared reader lazily because that reader also consumes workflow state.</summary>
public sealed class ClosePublicationReadinessGuard(
    Func<IFinancialOperationsCommandCenterReadService?> authorityFactory,
    IWorkstationTenantContextAccessor? tenantAccessor = null) : IClosePublicationReadinessGuard
{
    public async Task<IReadOnlyList<OperationsWorkflowBlockerDto>> ValidateAsync(
        Guid workflowId, long expectedVersion, CloseReadinessScopeDto? scope,
        string? tenantId = null, string? companyId = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (scope is null || string.IsNullOrWhiteSpace(scope.FundProfileId) ||
            scope.LedgerBookId is null || scope.LedgerBookId == Guid.Empty)
            return Block("CLOSE_SCOPE_REQUIRED", "Select the complete fund, book, account, entity, and period before closing.");
        if (scope.FundAccountId is null || scope.FundAccountId == Guid.Empty ||
            string.IsNullOrWhiteSpace(scope.EntityId) || string.IsNullOrWhiteSpace(scope.PeriodId))
            return Block("CLOSE_SCOPE_REQUIRED", "Select the complete fund, book, account, entity, and period before closing.");
        if (tenantAccessor is not null && tenantAccessor.TryGetCurrent(out var current))
        {
            if ((!string.IsNullOrWhiteSpace(tenantId) && tenantId != current.TenantId) ||
                (!string.IsNullOrWhiteSpace(companyId) && companyId != current.CompanyId))
                return Block("CLOSE_TENANT_SCOPE_MISMATCH", "Close authority does not match the current authenticated tenant and company.");
            tenantId = current.TenantId;
            companyId = current.CompanyId;
        }
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(companyId))
            return Block("CLOSE_TENANT_SCOPE_REQUIRED", "An authenticated tenant and company scope is required to evaluate close evidence.");

        try
        {
            var authority = authorityFactory();
            if (authority is null)
                return Block("CLOSE_READINESS_UNAVAILABLE", "The shared close evidence authority is unavailable.");
            var decision = await authority.GetCommandCenterAsync(scope.FundProfileId, scope.LedgerBookId,
                scope.FundAccountId, scope.PeriodId, scope.EntityId, ct, tenantId, companyId).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var readiness = decision.CloseReadiness;
            if (readiness is null || readiness.Scope != scope ||
                readiness.EvaluatedAtUtc < now.AddMinutes(-5) || readiness.EvaluatedAtUtc > now.AddMinutes(1) ||
                decision.ActiveWorkflow is not { } workflow || workflow.WorkflowId != workflowId ||
                workflow.Version != expectedVersion || workflow.LedgerBookId != scope.LedgerBookId ||
                workflow.FundAccountId != scope.FundAccountId || workflow.PeriodId != scope.PeriodId)
                return Block("CLOSE_READINESS_STALE_OR_MISMATCHED", "Refresh close evidence for the exact workflow version and selected subject.");
            if (readiness is { IsComplete: true, IsReadyToClose: true } && readiness.Blockers.Count == 0)
                return [];
            var blockers = readiness.Blockers.Select(static blocker => new OperationsWorkflowBlockerDto(
                blocker.Code, blocker.Message, null, blocker.Severity, [])).ToArray();
            return blockers.Length > 0 ? blockers : Block("CLOSE_READINESS_REQUIRED", "Resolve missing or incomplete shared close evidence before closing.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return Block("CLOSE_READINESS_UNAVAILABLE", "The shared close evidence authority could not be evaluated. Refresh and retry after resolving its availability.");
        }
    }

    private static IReadOnlyList<OperationsWorkflowBlockerDto> Block(string code, string message)
        => [new(code, message, OperationsGateKeyDto.Approval, "Critical", [])];
}

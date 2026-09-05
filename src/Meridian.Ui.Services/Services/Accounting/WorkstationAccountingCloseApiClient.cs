using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.AccountingClose;

namespace Meridian.Ui.Services.Services.Accounting;

/// <summary>Governed server close management for operator workstations.</summary>
public interface IWorkstationAccountingCloseApiClient : IAccountingCloseManagementService;

/// <summary>
/// Transports close commands and retained plans through the server's accounting authority.
/// Tenant, company, actor and controller authority are resolved by authenticated server middleware.
/// </summary>
public sealed class WorkstationAccountingCloseApiClient(ApiClientService apiClient) : IWorkstationAccountingCloseApiClient
{
    public async Task<ClosePeriodPlanDto?> GetPeriodPlanAsync(Guid workflowId, CancellationToken ct = default)
    {
        var response = await apiClient.GetWithResponseAsync<ClosePeriodPlanDto>(
            UiApiRoutes.LedgerCloseManagementPeriodPlan.Replace("{workflowId:guid}", workflowId.ToString("D")), ct)
            .ConfigureAwait(false);
        var plan = Unwrap(response, "load the close plan");
        if (plan is not null && plan.WorkflowId != workflowId)
            throw new InvalidOperationException("The server returned close evidence for a different workflow. Reload the selected workflow.");
        return plan;
    }

    public Task<ClosePeriodPlanDto?> GetPeriodPlanScopedAsync(Guid workflowId, string? tenantId,
        string? companyId, CancellationToken ct = default)
        => GetPeriodPlanAsync(workflowId, ct);

    public Task<ClosePeriodPlanDto?> ConfigurePeriodPlanAsync(UpsertClosePeriodPlanConfigurationRequestDto request,
        string actor, CancellationToken ct = default)
        => PostAsync<ClosePeriodPlanDto>(UiApiRoutes.LedgerCloseManagementPeriodPlanConfiguration, request, ct);

    public Task<ClosePeriodPlanDto?> RequestLateAdjustmentAsync(CreateLateAdjustmentRequestDto request,
        string actor, CancellationToken ct = default)
        => PostAsync<ClosePeriodPlanDto>(UiApiRoutes.LedgerCloseManagementLateAdjustments, request, ct);

    public Task<ClosePeriodPlanDto?> ReviewLateAdjustmentAsync(ReviewLateAdjustmentRequestDto request,
        string actor, CancellationToken ct = default)
        => PostAsync<ClosePeriodPlanDto>(UiApiRoutes.LedgerCloseManagementLateAdjustmentReview, request, ct);

    public Task<ClosePeriodPlanDto?> SignOffCloseTaskAsync(SignOffCloseTaskRequestDto request,
        string actor, CancellationToken ct = default)
        => PostAsync<ClosePeriodPlanDto>(UiApiRoutes.LedgerCloseManagementTaskSignOffs, request, ct);

    public Task<ClosePeriodPlanDto?> ReviewCloseEvidenceAsync(ReviewCloseEvidenceRequestDto request,
        string actor, CancellationToken ct = default)
        => PostAsync<ClosePeriodPlanDto>(UiApiRoutes.LedgerCloseManagementEvidenceReview, request, ct);

    public Task<ClosePeriodLockResultDto?> LockClosePeriodAsync(LockClosePeriodRequestDto request,
        string actor, CancellationToken ct = default)
        => PostAsync<ClosePeriodLockResultDto>(UiApiRoutes.LedgerCloseManagementPeriodLock, request, ct);

    public Task<ClosePeriodLockResultDto?> LockClosePeriodScopedAsync(LockClosePeriodRequestDto request,
        string actor, string? tenantId, string? companyId, CancellationToken ct = default)
        => LockClosePeriodAsync(request, actor, ct);

    public Task<ClosePeriodReopenResultDto?> ReopenClosePeriodAsync(ReopenClosePeriodRequestDto request,
        string actor, CancellationToken ct = default)
        => PostAsync<ClosePeriodReopenResultDto>(UiApiRoutes.LedgerCloseManagementPeriodReopen, request, ct);

    public Task<ClosePeriodReopenResultDto?> ReopenClosePeriodScopedAsync(ReopenClosePeriodRequestDto request,
        string actor, string? tenantId, string? companyId, CancellationToken ct = default)
        => ReopenClosePeriodAsync(request, actor, ct);

    private async Task<T?> PostAsync<T>(string route, object request, CancellationToken ct) where T : class
        => Unwrap(await apiClient.PostWithResponseAsync<T>(route, request, ct).ConfigureAwait(false), "retain the close command");

    private static T? Unwrap<T>(ApiResponse<T> response, string operation) where T : class
    {
        if (response.Success && response.Data is not null)
            return response.Data;
        if (response.StatusCode == 404)
            return null;
        var reason = !string.IsNullOrWhiteSpace(response.ErrorMessage) ? response.ErrorMessage
            : response.IsConnectionError ? "the workstation service is unreachable" : $"HTTP {response.StatusCode}";
        throw new InvalidOperationException($"Could not {operation}: {reason}");
    }
}

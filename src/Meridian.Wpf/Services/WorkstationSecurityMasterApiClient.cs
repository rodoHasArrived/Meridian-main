using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Services;

public interface IWorkstationSecurityMasterApiClient
{
    // ── Operator overrides (shared-contract parity with the browser workstation) ──────────────
    Task<OperatorOverridesDto?> GetOperatorOverridesAsync(
        Guid securityId, CancellationToken ct = default);

    Task<ApiResponse<OperatorOverridesDto>> RecordOperatorOverrideDecisionAsync(
        Guid securityId, OperatorOverrideDecisionRequest request, CancellationToken ct = default);

    Task<SecurityMasterTrustSnapshotDto?> GetTrustSnapshotAsync(
        Guid securityId,
        string? fundProfileId,
        CancellationToken ct = default);

    Task<InstrumentPassportDto?> GetInstrumentPassportAsync(
        Guid securityId,
        string? fundProfileId,
        CancellationToken ct = default);

    Task<ApiResponse<BulkResolveSecurityMasterConflictsResult>> BulkResolveConflictsAsync(
        BulkResolveSecurityMasterConflictsRequest request,
        CancellationToken ct = default);

    // ── Passport Workbench governed writes (Phase 4) ─────────────────────────
    // The path securityId is authoritative and the acting principal is server-derived; the desktop
    // posts the business fields and the server overwrites identity/scope.

    Task<ApiResponse<SecurityMasterEditResultDto>> UpdateFieldAsync(
        Guid securityId, UpdateSecurityFieldRequest request, CancellationToken ct = default);

    Task<ApiResponse<SecurityMasterConflictResolutionDto>> ResolveConflictAsync(
        Guid securityId, ResolveSourceConflictRequest request, CancellationToken ct = default);

    Task<ApiResponse<SecurityMasterEditResultDto>> SubmitRevisionAsync(
        Guid securityId, SubmitSecurityMasterRevisionRequest request, CancellationToken ct = default);

    Task<ApiResponse<SecurityMasterEditResultDto>> ApproveRevisionAsync(
        Guid securityId, ApproveSecurityMasterRevisionRequest request, CancellationToken ct = default);

    Task<ApiResponse<SecurityMasterPublishResultDto>> PublishRevisionAsync(
        Guid securityId, PublishSecurityMasterRevisionRequest request, CancellationToken ct = default);
}

public sealed class WorkstationSecurityMasterApiClient : IWorkstationSecurityMasterApiClient
{
    private readonly ApiClientService _apiClient;

    public WorkstationSecurityMasterApiClient(ApiClientService apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public async Task<OperatorOverridesDto?> GetOperatorOverridesAsync(
        Guid securityId, CancellationToken ct = default)
        => (await _apiClient.GetWithResponseAsync<OperatorOverridesDto>(
            SecurityIdRoute(UiApiRoutes.SecurityMasterOperatorOverrides, securityId), ct).ConfigureAwait(false)).DataOrLoggedNull("Get operator overrides");

    public Task<ApiResponse<OperatorOverridesDto>> RecordOperatorOverrideDecisionAsync(
        Guid securityId, OperatorOverrideDecisionRequest request, CancellationToken ct = default)
        => _apiClient.PostWithResponseAsync<OperatorOverridesDto>(
            SecurityIdRoute(UiApiRoutes.SecurityMasterOperatorOverrideDecision, securityId), request, ct);

    public async Task<SecurityMasterTrustSnapshotDto?> GetTrustSnapshotAsync(
        Guid securityId,
        string? fundProfileId,
        CancellationToken ct = default)
    {
        var endpoint = $"/api/workstation/security-master/securities/{securityId}/trust-snapshot";
        if (!string.IsNullOrWhiteSpace(fundProfileId))
        {
            endpoint += $"?fundProfileId={Uri.EscapeDataString(fundProfileId.Trim())}";
        }

        return (await _apiClient.GetWithResponseAsync<SecurityMasterTrustSnapshotDto>(endpoint, ct).ConfigureAwait(false)).DataOrLoggedNull("Get security trust snapshot");
    }

    public async Task<InstrumentPassportDto?> GetInstrumentPassportAsync(
        Guid securityId,
        string? fundProfileId,
        CancellationToken ct = default)
    {
        var endpoint = $"/api/workstation/security-master/securities/{securityId}/passport";
        if (!string.IsNullOrWhiteSpace(fundProfileId))
        {
            endpoint += $"?fundProfileId={Uri.EscapeDataString(fundProfileId.Trim())}";
        }

        return (await _apiClient.GetWithResponseAsync<InstrumentPassportDto>(endpoint, ct).ConfigureAwait(false)).DataOrLoggedNull("Get instrument passport");
    }

    public Task<ApiResponse<BulkResolveSecurityMasterConflictsResult>> BulkResolveConflictsAsync(
        BulkResolveSecurityMasterConflictsRequest request,
        CancellationToken ct = default)
        => _apiClient.PostWithResponseAsync<BulkResolveSecurityMasterConflictsResult>(
            "/api/workstation/security-master/conflicts/bulk-resolve",
            request,
            ct);

    public Task<ApiResponse<SecurityMasterEditResultDto>> UpdateFieldAsync(
        Guid securityId, UpdateSecurityFieldRequest request, CancellationToken ct = default)
        => _apiClient.PostWithResponseAsync<SecurityMasterEditResultDto>(
            SecurityIdRoute(UiApiRoutes.SecurityMasterWorkbenchField, securityId), request, ct);

    public Task<ApiResponse<SecurityMasterConflictResolutionDto>> ResolveConflictAsync(
        Guid securityId, ResolveSourceConflictRequest request, CancellationToken ct = default)
        => _apiClient.PostWithResponseAsync<SecurityMasterConflictResolutionDto>(
            SecurityIdRoute(UiApiRoutes.SecurityMasterWorkbenchResolveConflict, securityId), request, ct);

    public Task<ApiResponse<SecurityMasterEditResultDto>> SubmitRevisionAsync(
        Guid securityId, SubmitSecurityMasterRevisionRequest request, CancellationToken ct = default)
        => _apiClient.PostWithResponseAsync<SecurityMasterEditResultDto>(
            SecurityIdRoute(UiApiRoutes.SecurityMasterWorkbenchSubmit, securityId), request, ct);

    public Task<ApiResponse<SecurityMasterEditResultDto>> ApproveRevisionAsync(
        Guid securityId, ApproveSecurityMasterRevisionRequest request, CancellationToken ct = default)
        => _apiClient.PostWithResponseAsync<SecurityMasterEditResultDto>(
            SecurityIdRoute(UiApiRoutes.SecurityMasterWorkbenchApprove, securityId), request, ct);

    public Task<ApiResponse<SecurityMasterPublishResultDto>> PublishRevisionAsync(
        Guid securityId, PublishSecurityMasterRevisionRequest request, CancellationToken ct = default)
        => _apiClient.PostWithResponseAsync<SecurityMasterPublishResultDto>(
            SecurityIdRoute(UiApiRoutes.SecurityMasterWorkbenchPublish, securityId), request, ct);

    private static string SecurityIdRoute(string routeTemplate, Guid securityId)
        => routeTemplate.Replace("{securityId:guid}", securityId.ToString("D"), StringComparison.Ordinal);
}

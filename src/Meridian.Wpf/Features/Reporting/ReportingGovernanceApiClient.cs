using Meridian.Contracts.Api;
using Meridian.Contracts.Reporting;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;

namespace Meridian.Wpf.Features.Reporting;

/// <summary>
/// Thin desktop client for the canonical reporting API. Every transition request intentionally
/// contains only user-entered business data and an optimistic concurrency token; the server owns
/// actor, tenant, company, permissions, readiness, snapshot, artifact, and evidence resolution.
/// </summary>
public interface IReportingGovernanceApiClient
{
    Task<ApiResponse<ReportingRunReadinessDto>> AssessReadinessAsync(
        ReportingRunRequestDto request,
        CancellationToken ct = default);

    Task<ApiResponse<ReportingRunResultDto>> RunAsync(
        ReportingRunRequestDto request,
        CancellationToken ct = default);

    Task<ApiResponse<GovernedReportingRunDto>> GetGovernedRunAsync(
        string runId,
        CancellationToken ct = default);

    Task<ApiResponse<GovernedReportingRunDto>> GovernCompletedRunAsync(
        string runId,
        CancellationToken ct = default);

    Task<ApiResponse<GovernedReportingRunDto>> ValidateAsync(
        string runId,
        long expectedVersion,
        CancellationToken ct = default);

    Task<ApiResponse<GovernedReportingRunDto>> SubmitAsync(
        string runId,
        long expectedVersion,
        CancellationToken ct = default);

    Task<ApiResponse<GovernedReportingRunDto>> ApproveAsync(
        string runId,
        long expectedVersion,
        string decisionNote,
        CancellationToken ct = default);

    Task<ApiResponse<GovernedReportingRunDto>> ReleaseAsync(
        string runId,
        long expectedVersion,
        CancellationToken ct = default);

    Task<ApiResponse<ReportingGovernanceRestatementDto>> RequestRestatementAsync(
        string runId,
        long expectedVersion,
        string reason,
        CancellationToken ct = default);

    Task<ApiResponse<ReportingGovernanceRestatementApprovalDto>> ApproveRestatementAsync(
        string requestId,
        long expectedVersion,
        CancellationToken ct = default);

    Task<ApiResponse<SecureReportingDistributionCapabilityCatalog>> GetDistributionCapabilitiesAsync(
        CancellationToken ct = default);

    Task<ApiResponse<SecureReportingDeliveryResponse[]>> ListDeliveriesAsync(
        string runId,
        CancellationToken ct = default);

    Task<ApiResponse<SecureReportingDeliveryResponse>> QueueDeliveryAsync(
        SecureReportingDeliveryQueueCommand request,
        CancellationToken ct = default);

    Task<ApiResponse<SecureReportingGrantResponse>> IssueAccessGrantAsync(
        SecureReportingGrantIssueCommand request,
        CancellationToken ct = default);

    Task<ApiResponse<SecureReportingAccessGrantSummaryResponse[]>> ListAccessGrantsAsync(
        string runId,
        CancellationToken ct = default);

    Task<ApiResponse<SecureReportingGrantRevocationResponse>> RevokeAccessGrantAsync(
        string grantId,
        string reason,
        CancellationToken ct = default);
}

public sealed class ReportingGovernanceApiClient : IReportingGovernanceApiClient
{
    private readonly ApiClientService _apiClient;

    public ReportingGovernanceApiClient(ApiClientService apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public Task<ApiResponse<ReportingRunReadinessDto>> AssessReadinessAsync(
        ReportingRunRequestDto request,
        CancellationToken ct = default) =>
        _apiClient.PostWithResponseAsync<ReportingRunReadinessDto>(
            UiApiRoutes.ReportingRunReadiness,
            request,
            ct);

    public Task<ApiResponse<ReportingRunResultDto>> RunAsync(
        ReportingRunRequestDto request,
        CancellationToken ct = default) =>
        _apiClient.PostWithResponseAsync<ReportingRunResultDto>(
            UiApiRoutes.ReportingRuns,
            request,
            ct);

    public Task<ApiResponse<GovernedReportingRunDto>> GetGovernedRunAsync(
        string runId,
        CancellationToken ct = default) =>
        _apiClient.GetWithResponseAsync<GovernedReportingRunDto>(RunRoute(UiApiRoutes.ReportingGovernedRun, runId), ct);

    public Task<ApiResponse<GovernedReportingRunDto>> GovernCompletedRunAsync(
        string runId,
        CancellationToken ct = default) =>
        _apiClient.PostWithResponseAsync<GovernedReportingRunDto>(
            RunRoute(UiApiRoutes.ReportingGovernedRunCreate, runId),
            body: null,
            ct: ct);

    public Task<ApiResponse<GovernedReportingRunDto>> ValidateAsync(
        string runId,
        long expectedVersion,
        CancellationToken ct = default) =>
        PostVersionedAsync(UiApiRoutes.ReportingGovernedRunValidate, runId, expectedVersion, ct);

    public Task<ApiResponse<GovernedReportingRunDto>> SubmitAsync(
        string runId,
        long expectedVersion,
        CancellationToken ct = default) =>
        PostVersionedAsync(UiApiRoutes.ReportingGovernedRunSubmit, runId, expectedVersion, ct);

    public Task<ApiResponse<GovernedReportingRunDto>> ApproveAsync(
        string runId,
        long expectedVersion,
        string decisionNote,
        CancellationToken ct = default) =>
        _apiClient.PostWithResponseAsync<GovernedReportingRunDto>(
            RunRoute(UiApiRoutes.ReportingGovernedRunApprove, runId),
            new ReportingGovernanceApprovalRequestDto(expectedVersion, decisionNote),
            ct);

    public Task<ApiResponse<GovernedReportingRunDto>> ReleaseAsync(
        string runId,
        long expectedVersion,
        CancellationToken ct = default) =>
        PostVersionedAsync(UiApiRoutes.ReportingGovernedRunRelease, runId, expectedVersion, ct);

    public Task<ApiResponse<ReportingGovernanceRestatementDto>> RequestRestatementAsync(
        string runId,
        long expectedVersion,
        string reason,
        CancellationToken ct = default) =>
        _apiClient.PostWithResponseAsync<ReportingGovernanceRestatementDto>(
            RunRoute(UiApiRoutes.ReportingGovernedRunRestatementRequests, runId),
            new ReportingGovernanceRestatementRequestDto(expectedVersion, reason),
            ct);

    public Task<ApiResponse<ReportingGovernanceRestatementApprovalDto>> ApproveRestatementAsync(
        string requestId,
        long expectedVersion,
        CancellationToken ct = default) =>
        _apiClient.PostWithResponseAsync<ReportingGovernanceRestatementApprovalDto>(
            UiApiRoutes.WithParam(UiApiRoutes.ReportingGovernedRestatementApprove, "requestId", requestId),
            new ReportingGovernanceRestatementApprovalRequestDto(expectedVersion),
            ct);

    public Task<ApiResponse<SecureReportingDistributionCapabilityCatalog>> GetDistributionCapabilitiesAsync(
        CancellationToken ct = default) =>
        _apiClient.GetWithResponseAsync<SecureReportingDistributionCapabilityCatalog>(
            UiApiRoutes.ReportingDistributionTransports,
            ct);

    public Task<ApiResponse<SecureReportingDeliveryResponse[]>> ListDeliveriesAsync(
        string runId,
        CancellationToken ct = default) =>
        _apiClient.GetWithResponseAsync<SecureReportingDeliveryResponse[]>(
            RunRoute(UiApiRoutes.ReportingDistributionPackageDeliveries, runId),
            ct);

    public Task<ApiResponse<SecureReportingDeliveryResponse>> QueueDeliveryAsync(
        SecureReportingDeliveryQueueCommand request,
        CancellationToken ct = default) =>
        _apiClient.PostWithResponseAsync<SecureReportingDeliveryResponse>(
            UiApiRoutes.ReportingDistributionQueueDelivery,
            request,
            ct);

    public Task<ApiResponse<SecureReportingGrantResponse>> IssueAccessGrantAsync(
        SecureReportingGrantIssueCommand request,
        CancellationToken ct = default) =>
        _apiClient.PostWithResponseAsync<SecureReportingGrantResponse>(
            UiApiRoutes.ReportingDistributionIssueAccessGrant,
            request,
            ct);

    public Task<ApiResponse<SecureReportingAccessGrantSummaryResponse[]>> ListAccessGrantsAsync(
        string runId,
        CancellationToken ct = default) =>
        _apiClient.GetWithResponseAsync<SecureReportingAccessGrantSummaryResponse[]>(
            RunRoute(UiApiRoutes.ReportingDistributionPackageAccessGrants, runId),
            ct);

    public Task<ApiResponse<SecureReportingGrantRevocationResponse>> RevokeAccessGrantAsync(
        string grantId,
        string reason,
        CancellationToken ct = default) =>
        _apiClient.PostWithResponseAsync<SecureReportingGrantRevocationResponse>(
            UiApiRoutes.WithParam(UiApiRoutes.ReportingDistributionRevokeAccessGrant, "grantId", grantId),
            new SecureReportingGrantRevocationRequest(reason),
            ct);

    private Task<ApiResponse<GovernedReportingRunDto>> PostVersionedAsync(
        string route,
        string runId,
        long expectedVersion,
        CancellationToken ct) =>
        _apiClient.PostWithResponseAsync<GovernedReportingRunDto>(
            RunRoute(route, runId),
            new ReportingGovernanceVersionRequestDto(expectedVersion),
            ct);

    private static string RunRoute(string route, string runId) =>
        UiApiRoutes.WithParam(route, "runId", runId);
}

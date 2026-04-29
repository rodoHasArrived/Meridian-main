using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Services;

public interface IWorkstationSecurityMasterApiClient
{
    Task<SecurityMasterTrustSnapshotDto?> GetTrustSnapshotAsync(
        Guid securityId,
        string? fundProfileId,
        CancellationToken ct = default);

    Task<ApiResponse<BulkResolveSecurityMasterConflictsResult>> BulkResolveConflictsAsync(
        BulkResolveSecurityMasterConflictsRequest request,
        CancellationToken ct = default);
}

public sealed class WorkstationSecurityMasterApiClient : IWorkstationSecurityMasterApiClient
{
    private readonly ApiClientService _apiClient;

    public WorkstationSecurityMasterApiClient(ApiClientService apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public Task<SecurityMasterTrustSnapshotDto?> GetTrustSnapshotAsync(
        Guid securityId,
        string? fundProfileId,
        CancellationToken ct = default)
    {
        var endpoint = $"/api/workstation/security-master/securities/{securityId}/trust-snapshot";
        if (!string.IsNullOrWhiteSpace(fundProfileId))
        {
            endpoint += $"?fundProfileId={Uri.EscapeDataString(fundProfileId.Trim())}";
        }

        return _apiClient.GetAsync<SecurityMasterTrustSnapshotDto>(endpoint, ct);
    }

    public Task<ApiResponse<BulkResolveSecurityMasterConflictsResult>> BulkResolveConflictsAsync(
        BulkResolveSecurityMasterConflictsRequest request,
        CancellationToken ct = default)
        => _apiClient.PostWithResponseAsync<BulkResolveSecurityMasterConflictsResult>(
            "/api/workstation/security-master/conflicts/bulk-resolve",
            request,
            ct);
}

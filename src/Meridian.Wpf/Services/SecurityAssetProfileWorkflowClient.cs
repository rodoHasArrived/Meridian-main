using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Services;

public interface ISecurityAssetProfileWorkflowClient
{
    Task<IReadOnlyList<SecurityAssetProfileDefinitionDto>> GetProfilesAsync(CancellationToken ct = default);

    Task<SecurityAssetProfileLineageDto?> GetLineageAsync(string profileId, CancellationToken ct = default);

    Task<ApiResponse<SecurityAssetProfileGovernanceResultDto>> DraftProfileAsync(
        SecurityAssetProfileDraftRequestDto request,
        CancellationToken ct = default);

    Task<ApiResponse<SecurityAssetProfileGovernanceResultDto>> ApproveProfileAsync(
        SecurityAssetProfileApprovalRequestDto request,
        CancellationToken ct = default);

    Task<ApiResponse<SecurityAssetProfileGovernanceResultDto>> RollbackProfileAsync(
        SecurityAssetProfileRollbackRequestDto request,
        CancellationToken ct = default);

    Task<ApiResponse<SecurityDetailDto>> CreateSecurityAsync(CreateSecurityRequest request, CancellationToken ct = default);
}

public sealed class SecurityAssetProfileWorkflowClient : ISecurityAssetProfileWorkflowClient
{
    private readonly ApiClientService _apiClient;

    public SecurityAssetProfileWorkflowClient(ApiClientService apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public async Task<IReadOnlyList<SecurityAssetProfileDefinitionDto>> GetProfilesAsync(CancellationToken ct = default)
    {
        var profiles = await _apiClient.GetAsync<SecurityAssetProfileDefinitionDto[]>(
            UiApiRoutes.SecurityMasterAssetProfiles,
            ct).ConfigureAwait(false);

        return profiles ?? [];
    }

    public Task<SecurityAssetProfileLineageDto?> GetLineageAsync(string profileId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return Task.FromResult<SecurityAssetProfileLineageDto?>(null);
        }

        var endpoint = UiApiRoutes.SecurityMasterAssetProfileLineage.Replace(
            "{profileId}",
            Uri.EscapeDataString(profileId.Trim()),
            StringComparison.Ordinal);

        return _apiClient.GetAsync<SecurityAssetProfileLineageDto>(endpoint, ct);
    }

    public Task<ApiResponse<SecurityAssetProfileGovernanceResultDto>> DraftProfileAsync(
        SecurityAssetProfileDraftRequestDto request,
        CancellationToken ct = default)
        => _apiClient.PostWithResponseAsync<SecurityAssetProfileGovernanceResultDto>(
            UiApiRoutes.SecurityMasterAssetProfileDrafts,
            request,
            ct);

    public Task<ApiResponse<SecurityAssetProfileGovernanceResultDto>> ApproveProfileAsync(
        SecurityAssetProfileApprovalRequestDto request,
        CancellationToken ct = default)
        => _apiClient.PostWithResponseAsync<SecurityAssetProfileGovernanceResultDto>(
            UiApiRoutes.SecurityMasterAssetProfileApprove,
            request,
            ct);

    public Task<ApiResponse<SecurityAssetProfileGovernanceResultDto>> RollbackProfileAsync(
        SecurityAssetProfileRollbackRequestDto request,
        CancellationToken ct = default)
        => _apiClient.PostWithResponseAsync<SecurityAssetProfileGovernanceResultDto>(
            UiApiRoutes.SecurityMasterAssetProfileRollback,
            request,
            ct);

    public Task<ApiResponse<SecurityDetailDto>> CreateSecurityAsync(CreateSecurityRequest request, CancellationToken ct = default)
        => _apiClient.PostWithResponseAsync<SecurityDetailDto>(
            UiApiRoutes.SecurityMasterCreate,
            request,
            ct);
}

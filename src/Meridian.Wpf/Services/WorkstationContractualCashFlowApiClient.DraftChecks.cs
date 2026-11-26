using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.Services;

public interface IPreparerDraftCheckApiClient
{
    Task<ApiResponse<SinglePrincipalAttestationWorkspaceDto>> GetDraftCheckWorkspaceAsync(CancellationToken ct = default)
        => Task.FromResult(ApiResponse<SinglePrincipalAttestationWorkspaceDto>.Fail("Draft-check authority is unavailable.", 501));
    Task<ApiResponse<SinglePrincipalAttestationSubjectSnapshotDto>> InspectDraftCheckAsync(Guid securityId, Guid runId, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<SinglePrincipalAttestationSubjectSnapshotDto>.Fail("Exact retained draft inspection is unavailable.", 501));
    Task<ApiResponse<PreparerAttestationReceiptDto>> RecordDraftCheckAsync(RecordPreparerAttestationRequestDto request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<PreparerAttestationReceiptDto>.Fail("Draft preparer attestation is unavailable.", 501));
    Task<ApiResponse<PreparerAttestationRevocationDto>> RevokeDraftCheckAsync(RevokePreparerAttestationRequestDto request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<PreparerAttestationRevocationDto>.Fail("Draft-check correction is unavailable.", 501));
}

public sealed partial class WorkstationContractualCashFlowApiClient
{
    public Task<ApiResponse<SinglePrincipalAttestationWorkspaceDto>> GetDraftCheckWorkspaceAsync(CancellationToken ct = default)
        => _apiClient.GetWithResponseAsync<SinglePrincipalAttestationWorkspaceDto>("/api/workstation/evidence/attestations", ct);
    public Task<ApiResponse<SinglePrincipalAttestationSubjectSnapshotDto>> InspectDraftCheckAsync(Guid securityId, Guid runId, CancellationToken ct = default)
        => _apiClient.GetWithResponseAsync<SinglePrincipalAttestationSubjectSnapshotDto>(
            $"/api/workstation/evidence/attestations/drafts/{securityId:D}/{runId:D}", ct);
    public Task<ApiResponse<PreparerAttestationReceiptDto>> RecordDraftCheckAsync(RecordPreparerAttestationRequestDto request, CancellationToken ct = default)
        => _apiClient.PostWithResponseAsync<PreparerAttestationReceiptDto>("/api/workstation/evidence/attestations/receipts", request, ct: ct);
    public Task<ApiResponse<PreparerAttestationRevocationDto>> RevokeDraftCheckAsync(RevokePreparerAttestationRequestDto request, CancellationToken ct = default)
        => _apiClient.PostWithResponseAsync<PreparerAttestationRevocationDto>("/api/workstation/evidence/attestations/revocations", request, ct: ct);
}

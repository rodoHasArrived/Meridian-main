using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Wpf.Services;

/// <summary>Thin native access to the server-owned, bounded W14 cash-dividend treatment.</summary>
public interface ICashDividendAttestationApiClient
{
    Task<ApiResponse<CorporateActionProcessingCaseDto[]>> ListCashDividendCasesAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<CorporateActionProcessingCaseDto[]>.Fail("Corporate-action case access is unavailable.", 501));
    Task<ApiResponse<CorporateActionCashDividendWorkspaceDto>> GetCashDividendWorkspaceAsync(Guid caseId, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<CorporateActionCashDividendWorkspaceDto>.Fail("Cash-dividend attestation is unavailable.", 501));
    Task<ApiResponse<CashDividendAttestationDeclarationResultDto>> ChangeCashDividendDeclarationAsync(Guid caseId, ChangeCashDividendAttestationDeclarationRequestDto request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<CashDividendAttestationDeclarationResultDto>.Fail("Cash-dividend declaration administration is unavailable.", 501));
    Task<ApiResponse<CorporateActionCashDividendAttestationResultDto>> AttestCashDividendAsync(AttestCorporateActionCashDividendRequestDto request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<CorporateActionCashDividendAttestationResultDto>.Fail("Cash-dividend preparer attestation is unavailable.", 501));
    Task<ApiResponse<CorporateActionCashDividendPostingResultDto>> PostAttestedCashDividendAsync(PostAttestedCorporateActionCashDividendRequestDto request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<CorporateActionCashDividendPostingResultDto>.Fail("Attested cash-dividend posting is unavailable.", 501));
}

public sealed partial class WorkstationSecurityMasterApiClient
{
    private static string CashDividendRoute(Guid caseId) => $"/api/security-master/corporate-actions/cases/{caseId:D}/cash-dividend-attestation";
    public Task<ApiResponse<CorporateActionProcessingCaseDto[]>> ListCashDividendCasesAsync(Guid securityId, CancellationToken ct = default)
        => _apiClient.GetWithResponseAsync<CorporateActionProcessingCaseDto[]>(
            $"/api/security-master/corporate-actions/cases?securityId={securityId:D}&take=100", ct);
    public Task<ApiResponse<CorporateActionCashDividendWorkspaceDto>> GetCashDividendWorkspaceAsync(Guid caseId, CancellationToken ct = default)
        => _apiClient.GetWithResponseAsync<CorporateActionCashDividendWorkspaceDto>(CashDividendRoute(caseId), ct);
    public Task<ApiResponse<CashDividendAttestationDeclarationResultDto>> ChangeCashDividendDeclarationAsync(Guid caseId, ChangeCashDividendAttestationDeclarationRequestDto request, CancellationToken ct = default)
        => _apiClient.PostWithResponseAsync<CashDividendAttestationDeclarationResultDto>(CashDividendRoute(caseId) + "/declaration", request, ct: ct);
    public Task<ApiResponse<CorporateActionCashDividendAttestationResultDto>> AttestCashDividendAsync(AttestCorporateActionCashDividendRequestDto request, CancellationToken ct = default)
        => _apiClient.PostWithResponseAsync<CorporateActionCashDividendAttestationResultDto>(CashDividendRoute(request.CaseId) + "/attest", request, ct: ct);
    public Task<ApiResponse<CorporateActionCashDividendPostingResultDto>> PostAttestedCashDividendAsync(PostAttestedCorporateActionCashDividendRequestDto request, CancellationToken ct = default)
        => _apiClient.PostWithResponseAsync<CorporateActionCashDividendPostingResultDto>(CashDividendRoute(request.CaseId) + "/post", request, ct: ct);
}

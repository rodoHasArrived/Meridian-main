using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Native economic attestation authority. Reservations commit before journal effects and never
/// disappear on an uncertain or failed posting; a current lease serializes posting with withdrawal.
/// </summary>
public interface ICorporateActionCashDividendAccountingStore
{
    Task<CashDividendAttestationDeclarationDto?> GetCashDividendDeclarationAsync(
        string tenantId, string companyId, CancellationToken ct = default);

    Task<CashDividendAttestationDeclarationStateDto> GetCashDividendDeclarationStateAsync(
        string tenantId, string companyId, CancellationToken ct = default);

    Task<CashDividendAttestationDeclarationResultDto> ChangeCashDividendDeclarationAsync(
        ChangeCashDividendAttestationDeclarationRequestDto request, CancellationToken ct = default);

    Task<CashDividendAttestationDeclarationResultDto?> GetCashDividendDeclarationReceiptAsync(
        ChangeCashDividendAttestationDeclarationRequestDto request, CancellationToken ct = default);

    Task<CorporateActionCashDividendAttestationDto?> GetCashDividendAttestationAsync(
        Guid caseId, string tenantId, string companyId, CancellationToken ct = default);

    Task<CorporateActionCashDividendPostingDto?> GetCashDividendPostingAsync(
        Guid caseId, string tenantId, string companyId, CancellationToken ct = default);

    Task<CorporateActionCashDividendPostedProofDto?> GetCashDividendPostedProofAsync(
        Guid journalEntryId, string tenantId, string companyId, CancellationToken ct = default);

    Task<CorporateActionCashDividendAttestationResultDto?> GetCashDividendAttestationReceiptAsync(
        Guid caseId, string idempotencyKey, string requestFingerprint, CancellationToken ct = default);

    Task<CorporateActionCashDividendAttestationResultDto> AttestCashDividendAsync(
        AttestCorporateActionCashDividendRequestDto request,
        CorporateActionCashDividendAttestationDto attestation,
        Guid transitionId,
        string requestFingerprint,
        CancellationToken ct = default);

    Task<CorporateActionCashDividendPostingResultDto> ExecuteAttestedCashDividendPostingAsync(
        PostAttestedCorporateActionCashDividendRequestDto request,
        Func<CorporateActionCashDividendPostingLease, CancellationToken, Task<CorporateActionCashDividendPostingDto>> post,
        string requestFingerprint,
        CancellationToken ct = default);

    Task<CorporateActionCashDividendCaseFactsDto> GetCashDividendCaseFactsAsync(
        Guid caseId, string tenantId, string companyId, long expectedCaseVersion, CancellationToken ct = default)
        => Task.FromException<CorporateActionCashDividendCaseFactsDto>(
            new NotSupportedException("Complete scoped cash-dividend processing facts are unavailable."));
}

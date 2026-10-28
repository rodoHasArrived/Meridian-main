using Meridian.Contracts.CertificatesOfDeposit;

namespace Meridian.Instruments.CertificatesOfDeposit;

/// <summary>
/// Queries certificate-of-deposit reference data by security identifier and supported asset-
/// specific filters.
/// </summary>
public interface ICertificateOfDepositReferenceService
{
    Task<CertificateOfDepositReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default);
    Task<IReadOnlyList<CertificateOfDepositReferenceDto>> GetByIssuerAsync(string issuerName, CancellationToken ct = default);
    Task<IReadOnlyList<CertificateOfDepositReferenceDto>> GetMaturingBeforeAsync(DateOnly beforeDate, CancellationToken ct = default);
}

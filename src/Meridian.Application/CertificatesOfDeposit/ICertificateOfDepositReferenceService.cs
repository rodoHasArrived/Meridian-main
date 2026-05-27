using Meridian.Contracts.CertificatesOfDeposit;

namespace Meridian.Application.CertificatesOfDeposit;

public interface ICertificateOfDepositReferenceService
{
    Task<CertificateOfDepositReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default);
    Task<IReadOnlyList<CertificateOfDepositReferenceDto>> GetByIssuerAsync(string issuerName, CancellationToken ct = default);
    Task<IReadOnlyList<CertificateOfDepositReferenceDto>> GetMaturingBeforeAsync(DateOnly beforeDate, CancellationToken ct = default);
}

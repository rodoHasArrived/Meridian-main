namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Queries certificate of deposit reference projections by security, issuer, and maturity.
/// </summary>
public interface ICertificateOfDepositReferenceProjectionStore
{
    Task<CertificateOfDepositProjectionRow?> GetCertificateOfDepositAsync(Guid securityId, CancellationToken ct = default);
    Task<IReadOnlyList<CertificateOfDepositProjectionRow>> GetByIssuerAsync(string issuerName, CancellationToken ct = default);
    Task<IReadOnlyList<CertificateOfDepositProjectionRow>> GetMaturingBeforeAsync(DateOnly beforeDate, CancellationToken ct = default);
}

public sealed record CertificateOfDepositProjectionRow(
    Guid SecurityId,
    string DisplayName,
    string Currency,
    string IssuerName,
    DateOnly Maturity,
    decimal? CouponRate,
    DateOnly? CallableDate,
    string? DayCount,
    string PrimaryIdentifierValue,
    long Version);

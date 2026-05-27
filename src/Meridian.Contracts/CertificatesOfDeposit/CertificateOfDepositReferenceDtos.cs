namespace Meridian.Contracts.CertificatesOfDeposit;

public sealed record CertificateOfDepositReferenceDto(
    Guid SecurityId,
    string DisplayName,
    string Currency,
    string IssuerName,
    DateOnly Maturity,
    decimal? CouponRate,
    DateOnly? CallableDate,
    string? DayCount,
    string PrimaryIdentifier,
    long Version);

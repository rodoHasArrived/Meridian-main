using Meridian.Contracts.SecurityMaster;

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
    long Version)
{
    /// <summary>
    /// <see cref="DayCount"/> as a typed convention, or null when the stored text is absent or names
    /// no recognized basis. The raw text stays on <see cref="DayCount"/>.
    /// </summary>
    public Meridian.Contracts.SecurityMaster.DayCountConvention? DayCountConvention
        => DayCountConventions.TryParse(DayCount, out var convention) ? convention : null;
}

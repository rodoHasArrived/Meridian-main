using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.Deposits;

public sealed record DepositReferenceDto(
    Guid SecurityId,
    string DisplayName,
    string Currency,
    string DepositType,
    string InstitutionName,
    DateOnly? Maturity,
    decimal? InterestRate,
    string? DayCount,
    bool IsCallable,
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

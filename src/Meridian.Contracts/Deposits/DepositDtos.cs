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
    long Version);

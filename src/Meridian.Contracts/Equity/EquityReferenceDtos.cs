namespace Meridian.Contracts.Equity;

public sealed record EquityReferenceDto(
    Guid SecurityId,
    string DisplayName,
    string Currency,
    string? ShareClass,
    string? VotingRightsCat,
    string? Classification,
    string? ExchangeCode,
    string? CountryOfRisk,
    string? IssuerName,
    string PrimaryIdentifier,
    long Version);

namespace Meridian.Contracts.Commodities;

public sealed record CommodityReferenceDto(
    Guid SecurityId,
    string DisplayName,
    string Currency,
    string CommodityType,
    string? Denomination,
    decimal? ContractSize,
    string? ExchangeCode,
    string? DeliveryCountry,
    string PrimaryIdentifier,
    long Version);

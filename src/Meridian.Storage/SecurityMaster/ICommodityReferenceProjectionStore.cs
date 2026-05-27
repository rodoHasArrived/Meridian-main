namespace Meridian.Storage.SecurityMaster;

public interface ICommodityReferenceProjectionStore
{
    Task<CommodityProjectionRow?> GetCommodityAsync(Guid securityId, CancellationToken ct = default);
    Task<IReadOnlyList<CommodityProjectionRow>> GetByCommodityTypeAsync(string commodityType, CancellationToken ct = default);
    Task<IReadOnlyList<CommodityProjectionRow>> GetByExchangeAsync(string exchangeCode, CancellationToken ct = default);
}

public sealed record CommodityProjectionRow(
    Guid SecurityId,
    string DisplayName,
    string Currency,
    string CommodityType,
    string? Denomination,
    decimal? ContractSize,
    string? ExchangeCode,
    string? DeliveryCountry,
    string PrimaryIdentifierValue,
    long Version);

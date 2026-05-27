using Meridian.Contracts.Commodities;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.Commodities;

public sealed class CommodityProjectionService : ICommodityReferenceService
{
    private readonly ISecurityMasterStore _securityMasterStore;
    private readonly ICommodityReferenceProjectionStore _projectionStore;

    public CommodityProjectionService(
        ISecurityMasterStore securityMasterStore,
        ICommodityReferenceProjectionStore projectionStore)
    {
        _securityMasterStore = securityMasterStore;
        _projectionStore = projectionStore;
    }

    public async Task<CommodityReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default)
    {
        var security = await _securityMasterStore.GetProjectionAsync(securityId, ct).ConfigureAwait(false);
        if (security is null || !string.Equals(security.AssetClass, "Commodity", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var row = await _projectionStore.GetCommodityAsync(securityId, ct).ConfigureAwait(false);
        return row is null ? null : MapRow(row);
    }

    public async Task<IReadOnlyList<CommodityReferenceDto>> GetByCommodityTypeAsync(string commodityType, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(commodityType))
        {
            return Array.Empty<CommodityReferenceDto>();
        }

        var rows = await _projectionStore.GetByCommodityTypeAsync(commodityType.Trim(), ct).ConfigureAwait(false);
        return rows.Select(MapRow).ToArray();
    }

    public async Task<IReadOnlyList<CommodityReferenceDto>> GetByExchangeAsync(string exchangeCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(exchangeCode))
        {
            return Array.Empty<CommodityReferenceDto>();
        }

        var rows = await _projectionStore.GetByExchangeAsync(exchangeCode.Trim(), ct).ConfigureAwait(false);
        return rows.Select(MapRow).ToArray();
    }

    private static CommodityReferenceDto MapRow(CommodityProjectionRow row)
        => new(
            row.SecurityId,
            row.DisplayName,
            row.Currency,
            row.CommodityType,
            row.Denomination,
            row.ContractSize,
            row.ExchangeCode,
            row.DeliveryCountry,
            row.PrimaryIdentifierValue,
            row.Version);
}

public sealed class NullCommodityReferenceService : ICommodityReferenceService
{
    public Task<CommodityReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult<CommodityReferenceDto?>(null);

    public Task<IReadOnlyList<CommodityReferenceDto>> GetByCommodityTypeAsync(string commodityType, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CommodityReferenceDto>>(Array.Empty<CommodityReferenceDto>());

    public Task<IReadOnlyList<CommodityReferenceDto>> GetByExchangeAsync(string exchangeCode, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CommodityReferenceDto>>(Array.Empty<CommodityReferenceDto>());
}

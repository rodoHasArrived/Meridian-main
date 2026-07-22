using Meridian.Contracts.Commodities;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Instruments.Commodities;

public sealed class CommodityProjectionService
    : InstrumentProjectionServiceBase<CommodityProjectionRow, CommodityReferenceDto>, ICommodityReferenceService
{
    private readonly ICommodityReferenceProjectionStore _projectionStore;

    public CommodityProjectionService(
        ISecurityMasterStore securityMasterStore,
        ICommodityReferenceProjectionStore projectionStore)
        : base(securityMasterStore)
    {
        _projectionStore = projectionStore;
    }

    protected override string AssetClass => "Commodity";

    protected override Task<CommodityProjectionRow?> FetchRowAsync(Guid securityId, CancellationToken ct)
        => _projectionStore.GetCommodityAsync(securityId, ct);

    public Task<IReadOnlyList<CommodityReferenceDto>> GetByCommodityTypeAsync(string commodityType, CancellationToken ct = default)
        => QueryByTermAsync(commodityType, _projectionStore.GetByCommodityTypeAsync, ct);

    public Task<IReadOnlyList<CommodityReferenceDto>> GetByExchangeAsync(string exchangeCode, CancellationToken ct = default)
        => QueryByTermAsync(exchangeCode, _projectionStore.GetByExchangeAsync, ct);

    protected override CommodityReferenceDto MapRow(CommodityProjectionRow row)
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

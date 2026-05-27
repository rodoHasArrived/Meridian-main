using Meridian.Contracts.Commodities;

namespace Meridian.Application.Commodities;

public interface ICommodityReferenceService
{
    Task<CommodityReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default);
    Task<IReadOnlyList<CommodityReferenceDto>> GetByCommodityTypeAsync(string commodityType, CancellationToken ct = default);
    Task<IReadOnlyList<CommodityReferenceDto>> GetByExchangeAsync(string exchangeCode, CancellationToken ct = default);
}

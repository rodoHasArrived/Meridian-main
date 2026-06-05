using Meridian.Contracts.MoneyMarketFunds;

namespace Meridian.Instruments.MoneyMarketFunds;

public interface IMoneyMarketFundReferenceService
{
    Task<MoneyMarketFundReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default);
    Task<IReadOnlyList<MoneyMarketFundReferenceDto>> GetByFundFamilyAsync(string fundFamily, CancellationToken ct = default);
    Task<IReadOnlyList<MoneyMarketFundReferenceDto>> GetBySweepEligibilityAsync(bool sweepEligible, CancellationToken ct = default);
}

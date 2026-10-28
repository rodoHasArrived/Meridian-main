using Meridian.Contracts.MoneyMarketFunds;

namespace Meridian.Instruments.MoneyMarketFunds;

/// <summary>
/// Queries money-market fund reference data by security identifier and supported asset-specific
/// filters.
/// </summary>
public interface IMoneyMarketFundReferenceService
{
    Task<MoneyMarketFundReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default);
    Task<IReadOnlyList<MoneyMarketFundReferenceDto>> GetByFundFamilyAsync(string fundFamily, CancellationToken ct = default);
    Task<IReadOnlyList<MoneyMarketFundReferenceDto>> GetBySweepEligibilityAsync(bool sweepEligible, CancellationToken ct = default);
}

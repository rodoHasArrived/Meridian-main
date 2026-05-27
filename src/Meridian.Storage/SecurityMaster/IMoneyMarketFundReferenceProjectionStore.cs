namespace Meridian.Storage.SecurityMaster;

public interface IMoneyMarketFundReferenceProjectionStore
{
    Task<MoneyMarketFundProjectionRow?> GetMoneyMarketFundAsync(Guid securityId, CancellationToken ct = default);
    Task<IReadOnlyList<MoneyMarketFundProjectionRow>> GetByFundFamilyAsync(string fundFamily, CancellationToken ct = default);
    Task<IReadOnlyList<MoneyMarketFundProjectionRow>> GetBySweepEligibilityAsync(bool sweepEligible, CancellationToken ct = default);
}

public sealed record MoneyMarketFundProjectionRow(
    Guid SecurityId,
    string DisplayName,
    string Currency,
    string? FundFamily,
    bool SweepEligible,
    int? WeightedAverageMaturityDays,
    bool LiquidityFeeEligible,
    string PrimaryIdentifierValue,
    long Version);

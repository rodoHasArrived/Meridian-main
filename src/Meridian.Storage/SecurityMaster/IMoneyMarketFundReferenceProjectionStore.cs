namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Queries money market fund reference projections by security, fund family, and sweep eligibility.
/// </summary>
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

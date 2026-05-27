namespace Meridian.Contracts.MoneyMarketFunds;

public sealed record MoneyMarketFundReferenceDto(
    Guid SecurityId,
    string DisplayName,
    string Currency,
    string? FundFamily,
    bool SweepEligible,
    int? WeightedAverageMaturityDays,
    bool LiquidityFeeEligible,
    string PrimaryIdentifier,
    long Version);

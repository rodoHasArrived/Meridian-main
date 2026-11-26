using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class MoneyMarketFundStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "MoneyMarketFund",
            TableName: "money_market_fund_projection",
            Columns:
            [
                Optional("fund_family", "fundFamily", SecurityAssetTermFieldType.String, SecurityTermsProjectionTextNormalization.Trim),
                Flag("sweep_eligible", "sweepEligible", defaultWhenAbsent: false),
                Optional("weighted_average_maturity_days", "weightedAverageMaturityDays", SecurityAssetTermFieldType.Integer),
                Flag("liquidity_fee_eligible", "liquidityFeeEligible", defaultWhenAbsent: false)
            ],
            ChildTables: []));
}

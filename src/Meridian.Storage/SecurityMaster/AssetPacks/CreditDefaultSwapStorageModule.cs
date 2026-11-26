using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class CreditDefaultSwapStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "CreditDefaultSwap",
            TableName: "credit_default_swap_projection",
            Columns:
            [
                Gate("reference_entity", "referenceEntity", SecurityAssetTermFieldType.String),
                Optional("reference_obligation", "referenceObligation", SecurityAssetTermFieldType.String),
                Gate("protection_side", "protectionSide", SecurityAssetTermFieldType.String),
                Gate("notional", "notional", SecurityAssetTermFieldType.Decimal),
                Gate("fixed_coupon_bps", "fixedCouponBps", SecurityAssetTermFieldType.Decimal),
                Gate("effective_date", "effectiveDate", SecurityAssetTermFieldType.Date),
                Gate("maturity_date", "maturityDate", SecurityAssetTermFieldType.Date),
                Optional("payment_frequency", "paymentFrequency", SecurityAssetTermFieldType.String),
                Optional("day_count", "dayCount", SecurityAssetTermFieldType.String),
                Optional("seniority", "seniority", SecurityAssetTermFieldType.String),
                Optional("restructuring_clause", "restructuringClause", SecurityAssetTermFieldType.String),
                Optional("upfront_points", "upfrontPoints", SecurityAssetTermFieldType.Decimal),
                Optional("recovery_rate_assumption", "recoveryRateAssumption", SecurityAssetTermFieldType.Decimal),
                Optional("index_name", "indexName", SecurityAssetTermFieldType.String)
            ],
            ChildTables: []));
}

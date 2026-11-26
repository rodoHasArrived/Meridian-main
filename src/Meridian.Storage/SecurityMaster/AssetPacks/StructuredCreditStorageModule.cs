using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class StructuredCreditStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "StructuredCredit",
            TableName: "structured_credit_projection",
            Columns:
            [
                Gate("tranche", "tranche", SecurityAssetTermFieldType.String),
                Optional("pool_id", "poolId", SecurityAssetTermFieldType.String),
                Gate("collateral_type", "collateralType", SecurityAssetTermFieldType.String),
                Gate("original_face", "originalFace", SecurityAssetTermFieldType.Decimal),
                Optional("current_factor", "currentFactor", SecurityAssetTermFieldType.Decimal),
                Gate("coupon_or_index", "couponOrIndex", SecurityAssetTermFieldType.String),
                // The free-text trustee-report pointer, kept distinct from the typed dated schedule
                // in the child table so a reader cannot mistake prose for factor data.
                Optional("factor_schedule_reference", "factorSchedule", SecurityAssetTermFieldType.String),
                Optional("maturity_date", "maturity", SecurityAssetTermFieldType.Date),
                // Typed tranche terms (asset-class terms version 2, migration 045).
                Optional("structured_credit_kind", "structuredCreditKind", SecurityAssetTermFieldType.String),
                Optional("seniority", "seniority", SecurityAssetTermFieldType.String),
                Optional("attachment_point", "attachmentPoint", SecurityAssetTermFieldType.Decimal),
                Optional("detachment_point", "detachmentPoint", SecurityAssetTermFieldType.Decimal),
                Optional("weighted_average_coupon", "weightedAverageCoupon", SecurityAssetTermFieldType.Decimal),
                Optional("weighted_average_maturity_months", "weightedAverageMaturityMonths", SecurityAssetTermFieldType.Integer),
                Optional("weighted_average_loan_age_months", "weightedAverageLoanAgeMonths", SecurityAssetTermFieldType.Integer),
                Optional("reinvestment_period_end", "reinvestmentPeriodEnd", SecurityAssetTermFieldType.Date),
                Optional("non_call_period_end", "nonCallPeriodEnd", SecurityAssetTermFieldType.Date)
            ],
            ChildTables:
            [
                new(
                    TableName: "structured_credit_factor_schedule_projection",
                    TermKey: "factorScheduleEntries",
                    Columns:
                    [
                        new("as_of_date", "asOfDate", SecurityAssetTermFieldType.Date, Required: true),
                        new("factor", "factor", SecurityAssetTermFieldType.Decimal, Required: true)
                    ])
            ]));
}

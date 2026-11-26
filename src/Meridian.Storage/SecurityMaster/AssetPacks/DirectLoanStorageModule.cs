using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class DirectLoanStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "DirectLoan",
            TableName: "direct_loan_projection",
            Columns:
            [
                Gate("borrower", "borrower", SecurityAssetTermFieldType.String),
                Optional("maturity_date", "maturity", SecurityAssetTermFieldType.Date),
                Optional("reference_index", "referenceIndex", SecurityAssetTermFieldType.String),
                Optional("spread_bps", "spreadBps", SecurityAssetTermFieldType.Decimal),
                Optional("current_coupon_rate", "currentCouponRate", SecurityAssetTermFieldType.Decimal),
                Optional("reset_frequency", "resetFrequency", SecurityAssetTermFieldType.String),
                Optional("pricing_source", "pricingSource", SecurityAssetTermFieldType.String)
            ],
            ChildTables:
            [
                new(
                    TableName: "direct_loan_covenant_projection",
                    TermKey: "covenants",
                    Columns:
                    [
                        new("covenant_type", "covenantType", SecurityAssetTermFieldType.String, Required: true),
                        // The canonical covenant threshold is a STRING ("4.5x", "2.00x fixed charge"),
                        // not a number — projecting it as numeric would lose every ratio covenant.
                        new("threshold", "threshold", SecurityAssetTermFieldType.String, Required: true),
                        new("notes", "notes", SecurityAssetTermFieldType.String)
                    ]),
                new(
                    TableName: "direct_loan_principal_schedule_projection",
                    TermKey: "principalSchedule",
                    Columns:
                    [
                        new("payment_date", "paymentDate", SecurityAssetTermFieldType.Date, Required: true),
                        new("amount", "amount", SecurityAssetTermFieldType.Decimal, Required: true)
                    ])
            ]));
}

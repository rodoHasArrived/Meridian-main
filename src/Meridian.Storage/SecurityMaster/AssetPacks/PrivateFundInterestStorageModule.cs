using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class PrivateFundInterestStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "PrivateFundInterest",
            TableName: "private_fund_interest_projection",
            Columns:
            [
                Gate("gp_sponsor", "gpSponsor", SecurityAssetTermFieldType.String),
                Gate("strategy", "strategy", SecurityAssetTermFieldType.String),
                Gate("vintage", "vintage", SecurityAssetTermFieldType.Integer),
                Gate("commitment", "commitment", SecurityAssetTermFieldType.Decimal),
                Optional("funded_amount", "fundedAmount", SecurityAssetTermFieldType.Decimal),
                Optional("unfunded_amount", "unfundedAmount", SecurityAssetTermFieldType.Decimal),
                Gate("nav_date", "navDate", SecurityAssetTermFieldType.Date),
                Optional("lockup", "lockup", SecurityAssetTermFieldType.String)
            ],
            ChildTables: []));
}

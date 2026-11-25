using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class PrivateCompanyEquityStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "PrivateCompanyEquity",
            TableName: "private_company_equity_projection",
            Columns:
            [
                Gate("issuer", "issuer", SecurityAssetTermFieldType.String),
                Gate("share_class", "shareClass", SecurityAssetTermFieldType.String),
                Gate("round", "round", SecurityAssetTermFieldType.String),
                Optional("ownership_percent", "ownershipPercent", SecurityAssetTermFieldType.Decimal),
                Gate("cost_basis", "costBasis", SecurityAssetTermFieldType.Decimal),
                Optional("latest_valuation", "latestValuation", SecurityAssetTermFieldType.Decimal),
                Optional("transfer_restrictions", "transferRestrictions", SecurityAssetTermFieldType.String)
            ],
            ChildTables: []));
}

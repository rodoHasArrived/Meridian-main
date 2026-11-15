using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class OtherSecurityStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "OtherSecurity",
            TableName: "other_security_projection",
            Columns:
            [
                Gate("category", "category", SecurityAssetTermFieldType.String),
                Optional("sub_type", "subType", SecurityAssetTermFieldType.String),
                Optional("maturity_date", "maturity", SecurityAssetTermFieldType.Date),
                Optional("issuer_name", "issuerName", SecurityAssetTermFieldType.String),
                Optional("settlement_type", "settlementType", SecurityAssetTermFieldType.String)
            ],
            ChildTables: []));
}

using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class CfdStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "Cfd",
            TableName: "cfd_projection",
            Columns:
            [
                Gate("underlying_asset_class", "underlyingAssetClass", SecurityAssetTermFieldType.String),
                Optional("underlying_description", "underlyingDescription", SecurityAssetTermFieldType.String),
                Optional("leverage", "leverage", SecurityAssetTermFieldType.Decimal)
            ],
            ChildTables: []));
}

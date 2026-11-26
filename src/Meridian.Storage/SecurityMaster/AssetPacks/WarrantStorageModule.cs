using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class WarrantStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "Warrant",
            TableName: "warrant_projection",
            Columns:
            [
                // The first Guid-typed projected term: an underlying Security Master identity, so it
                // is a uuid column that joins straight back to securities.security_id.
                Gate("underlying_id", "underlyingId", SecurityAssetTermFieldType.Guid),
                Gate("warrant_type", "warrantType", SecurityAssetTermFieldType.String),
                Optional("strike", "strike", SecurityAssetTermFieldType.Decimal),
                Optional("expiry_date", "expiry", SecurityAssetTermFieldType.Date),
                Optional("multiplier", "multiplier", SecurityAssetTermFieldType.Decimal)
            ],
            ChildTables: []));
}

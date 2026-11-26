using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class CommercialPaperStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "CommercialPaper",
            TableName: "commercial_paper_projection",
            Columns:
            [
                Gate("issuer_name", "issuerName", SecurityAssetTermFieldType.String),
                Gate("maturity_date", "maturity", SecurityAssetTermFieldType.Date),
                Optional("discount_rate", "discountRate", SecurityAssetTermFieldType.Decimal),
                Optional("day_count", "dayCount", SecurityAssetTermFieldType.String, Verbatim),
                Flag("is_asset_backed", "isAssetBacked", defaultWhenAbsent: false)
            ],
            ChildTables: []));
}

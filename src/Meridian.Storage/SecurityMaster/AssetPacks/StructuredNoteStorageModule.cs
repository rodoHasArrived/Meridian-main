using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class StructuredNoteStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "StructuredNote",
            TableName: "structured_note_projection",
            Columns:
            [
                Gate("issuer_name", "issuerName", SecurityAssetTermFieldType.String),
                Gate("underlying_description", "underlyingDescription", SecurityAssetTermFieldType.String),
                Gate("maturity_date", "maturity", SecurityAssetTermFieldType.Date),
                Optional("issue_date", "issueDate", SecurityAssetTermFieldType.Date),
                Optional("denomination", "denomination", SecurityAssetTermFieldType.Decimal),
                Optional("principal_protection_pct", "principalProtectionPct", SecurityAssetTermFieldType.Decimal),
                Optional("participation_rate", "participationRate", SecurityAssetTermFieldType.Decimal),
                Optional("return_cap", "returnCap", SecurityAssetTermFieldType.Decimal),
                Optional("coupon_rate", "couponRate", SecurityAssetTermFieldType.Decimal),
                Optional("payment_frequency", "paymentFrequency", SecurityAssetTermFieldType.String),
                Optional("barrier_level", "barrierLevel", SecurityAssetTermFieldType.Decimal)
            ],
            ChildTables: []));
}

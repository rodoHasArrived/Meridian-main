using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class CertificateOfDepositStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "CertificateOfDeposit",
            TableName: "certificate_of_deposit_projection",
            Columns:
            [
                Gate("issuer_name", "issuerName", SecurityAssetTermFieldType.String),
                Gate("maturity_date", "maturity", SecurityAssetTermFieldType.Date),
                Optional("coupon_rate", "couponRate", SecurityAssetTermFieldType.Decimal),
                Optional("callable_date", "callableDate", SecurityAssetTermFieldType.Date),
                Optional("day_count", "dayCount", SecurityAssetTermFieldType.String, Verbatim)
            ],
            ChildTables: []));
}

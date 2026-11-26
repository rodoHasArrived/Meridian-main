using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class TreasuryBillStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "TreasuryBill",
            TableName: "treasury_bill_projection",
            Columns:
            [
                Gate("maturity_date", "maturity", SecurityAssetTermFieldType.Date),
                Optional("auction_date", "auctionDate", SecurityAssetTermFieldType.Date),
                Optional("cusip", "cusip", SecurityAssetTermFieldType.String, SecurityTermsProjectionTextNormalization.TrimUpperInvariant),
                Optional("discount_rate", "discountRate", SecurityAssetTermFieldType.Decimal)
            ],
            ChildTables: []));
}

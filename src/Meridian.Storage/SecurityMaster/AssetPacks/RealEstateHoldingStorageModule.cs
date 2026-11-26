using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class RealEstateHoldingStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "RealEstateHolding",
            TableName: "real_estate_holding_projection",
            Columns:
            [
                Gate("property_type", "propertyType", SecurityAssetTermFieldType.String),
                Gate("address_or_market", "addressOrMarket", SecurityAssetTermFieldType.String),
                Gate("ownership_percent", "ownershipPercent", SecurityAssetTermFieldType.Decimal),
                Gate("appraisal_value", "appraisalValue", SecurityAssetTermFieldType.Decimal),
                Gate("valuation_date", "valuationDate", SecurityAssetTermFieldType.Date),
                Optional("debt_stack", "debtStack", SecurityAssetTermFieldType.String),
                Optional("sponsor", "sponsor", SecurityAssetTermFieldType.String)
            ],
            ChildTables: []));
}

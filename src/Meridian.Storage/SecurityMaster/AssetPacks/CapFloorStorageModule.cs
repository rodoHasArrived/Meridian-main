using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class CapFloorStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "CapFloor",
            TableName: "cap_floor_projection",
            Columns:
            [
                Gate("cap_floor_type", "capFloorType", SecurityAssetTermFieldType.String),
                Gate("reference_index", "referenceIndex", SecurityAssetTermFieldType.String),
                Optional("cap_strike", "capStrike", SecurityAssetTermFieldType.Decimal),
                Optional("floor_strike", "floorStrike", SecurityAssetTermFieldType.Decimal),
                Gate("notional", "notional", SecurityAssetTermFieldType.Decimal),
                Gate("effective_date", "effectiveDate", SecurityAssetTermFieldType.Date),
                Gate("maturity_date", "maturityDate", SecurityAssetTermFieldType.Date),
                Optional("reset_frequency", "resetFrequency", SecurityAssetTermFieldType.String),
                Optional("day_count", "dayCount", SecurityAssetTermFieldType.String),
                Optional("premium_amount", "premiumAmount", SecurityAssetTermFieldType.Decimal)
            ],
            ChildTables: []));
}

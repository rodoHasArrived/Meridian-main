using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class SwaptionStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "Swaption",
            TableName: "swaption_projection",
            Columns:
            [
                Gate("payer_receiver", "payerReceiver", SecurityAssetTermFieldType.String),
                Optional("exercise_style", "exerciseStyle", SecurityAssetTermFieldType.String),
                Gate("strike_rate", "strikeRate", SecurityAssetTermFieldType.Decimal),
                Gate("expiry_date", "expiry", SecurityAssetTermFieldType.Date),
                Optional("underlying_effective_date", "underlyingEffectiveDate", SecurityAssetTermFieldType.Date),
                Gate("underlying_maturity_date", "underlyingMaturityDate", SecurityAssetTermFieldType.Date),
                Gate("notional", "notional", SecurityAssetTermFieldType.Decimal),
                Optional("floating_index", "floatingIndex", SecurityAssetTermFieldType.String),
                Optional("fixed_leg_payment_frequency", "fixedLegPaymentFrequency", SecurityAssetTermFieldType.String),
                Optional("fixed_leg_day_count", "fixedLegDayCount", SecurityAssetTermFieldType.String),
                Optional("settlement_type", "settlementType", SecurityAssetTermFieldType.String)
            ],
            ChildTables: []));
}

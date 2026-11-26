using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class FxForwardStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "FxForward",
            TableName: "fx_forward_projection",
            Columns:
            [
                Gate("base_currency", "baseCurrency", SecurityAssetTermFieldType.String, SecurityTermsProjectionTextNormalization.TrimUpperInvariant),
                Gate("quote_currency", "quoteCurrency", SecurityAssetTermFieldType.String, SecurityTermsProjectionTextNormalization.TrimUpperInvariant),
                Optional("notional", "notional", SecurityAssetTermFieldType.Decimal),
                Optional("forward_rate", "forwardRate", SecurityAssetTermFieldType.Decimal),
                Optional("trade_date", "tradeDate", SecurityAssetTermFieldType.Date),
                Gate("value_date", "valueDate", SecurityAssetTermFieldType.Date),
                Optional("settlement_type", "settlementType", SecurityAssetTermFieldType.String)
            ],
            ChildTables: []));
}

using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class NonDeliverableForwardStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "NonDeliverableForward",
            TableName: "ndf_projection",
            Columns:
            [
                Gate("base_currency", "baseCurrency", SecurityAssetTermFieldType.String, SecurityTermsProjectionTextNormalization.TrimUpperInvariant),
                Gate("quote_currency", "quoteCurrency", SecurityAssetTermFieldType.String, SecurityTermsProjectionTextNormalization.TrimUpperInvariant),
                Gate("settlement_currency", "settlementCurrency", SecurityAssetTermFieldType.String, SecurityTermsProjectionTextNormalization.TrimUpperInvariant),
                Optional("notional", "notional", SecurityAssetTermFieldType.Decimal),
                Optional("forward_rate", "forwardRate", SecurityAssetTermFieldType.Decimal),
                Gate("fixing_date", "fixingDate", SecurityAssetTermFieldType.Date),
                Gate("value_date", "valueDate", SecurityAssetTermFieldType.Date),
                Optional("fixing_source", "fixingSource", SecurityAssetTermFieldType.String)
            ],
            ChildTables: []));
}

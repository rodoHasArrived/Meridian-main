using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class CommodityStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "Commodity",
            TableName: "commodity_projection",
            Columns:
            [
                Gate("commodity_type", "commodityType", SecurityAssetTermFieldType.String, SecurityTermsProjectionTextNormalization.Trim),
                Optional("denomination", "denomination", SecurityAssetTermFieldType.String, Verbatim),
                Optional("contract_size", "contractSize", SecurityAssetTermFieldType.Decimal),
                // exchangeCode and deliveryCountry are not declared terms — the canonical serializer
                // never writes them — so they are read verbatim for rows written by older importers.
                SecurityTermsProjectionColumn.Derived("exchange_code", static record =>
                    PostgresSecurityMasterStore.GetOptionalString(record.AssetSpecificTerms, "exchangeCode")),
                SecurityTermsProjectionColumn.Derived("delivery_country", static record =>
                    PostgresSecurityMasterStore.GetOptionalString(record.AssetSpecificTerms, "deliveryCountry"))
            ],
            ChildTables: []));
}

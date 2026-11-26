using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class FxSpotStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "FxSpot",
            TableName: "fxspot_projection",
            Columns:
            [
                Gate("base_currency", "baseCurrency", SecurityAssetTermFieldType.String, SecurityTermsProjectionTextNormalization.TrimUpperInvariant),
                Gate("quote_currency", "quoteCurrency", SecurityAssetTermFieldType.String, SecurityTermsProjectionTextNormalization.TrimUpperInvariant),
                SecurityTermsProjectionColumn.Derived("pair_code", static record =>
                    $"{PostgresSecurityMasterStore.GetOptionalString(record.AssetSpecificTerms, "baseCurrency")?.Trim().ToUpperInvariant()}" +
                    $"{PostgresSecurityMasterStore.GetOptionalString(record.AssetSpecificTerms, "quoteCurrency")?.Trim().ToUpperInvariant()}")
            ],
            ChildTables: [],
            ProjectsCurrency: false));
}

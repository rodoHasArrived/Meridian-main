using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class CryptoCurrencyStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "CryptoCurrency",
            TableName: "crypto_projection",
            Columns:
            [
                Gate("base_currency", "baseCurrency", SecurityAssetTermFieldType.String, SecurityTermsProjectionTextNormalization.TrimUpperInvariant),
                Gate("quote_currency", "quoteCurrency", SecurityAssetTermFieldType.String, SecurityTermsProjectionTextNormalization.TrimUpperInvariant),
                Optional("network", "network", SecurityAssetTermFieldType.String, Verbatim)
            ],
            ChildTables: [],
            // A crypto pair states both currencies as terms; migration 012 gave it no currency column.
            ProjectsCurrency: false));
}

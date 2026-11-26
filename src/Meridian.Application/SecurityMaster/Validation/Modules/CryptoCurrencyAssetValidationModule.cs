using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the CryptoCurrency asset module.</summary>
internal static class CryptoCurrencyAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("CryptoCurrency", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "CryptoCurrency",
            [
                DistinctStringRule.Required("baseCurrency", "quoteCurrency", "SM_CRYPTO_PAIR_INVALID", "Crypto pair is invalid", "Crypto base and quote currencies must differ.")
            ]);
}

using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the FxSpot asset module.</summary>
internal static class FxSpotAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("FxSpot", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "FxSpot",
            [
                DistinctStringRule.Required("baseCurrency", "quoteCurrency", "SM_FX_PAIR_INVALID", "FX currency pair is invalid", "FX base and quote currencies must differ.")
            ]);
}

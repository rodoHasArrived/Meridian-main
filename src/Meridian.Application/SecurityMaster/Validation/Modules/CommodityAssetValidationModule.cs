using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the Commodity asset module.</summary>
internal static class CommodityAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("Commodity", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "Commodity",
            [
                FieldRule.OptionalPositive("contractSize", "SM_COMMODITY_CONTRACT_SIZE_INVALID", "Commodity contract size is invalid", "Commodity contract size must be greater than zero when supplied.")
            ]);
}

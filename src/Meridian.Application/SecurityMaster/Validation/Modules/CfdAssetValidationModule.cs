using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the Cfd asset module.</summary>
internal static class CfdAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("Cfd", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "Cfd",
            [
                FieldRule.OptionalPositive("leverage", "SM_CFD_LEVERAGE_INVALID", "CFD leverage is invalid", "CFD leverage must be greater than zero when supplied.")
            ]);
}

using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the RealEstateHolding asset module.</summary>
internal static class RealEstateHoldingAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("RealEstateHolding", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "RealEstateHolding",
            [
                FieldRule.OptionalPositive("ownershipPercent", "SM_REAL_ESTATE_OWNERSHIP_INVALID", "Real estate ownership percent is invalid", "Real estate ownership percentages must be greater than zero."),
                FieldRule.OptionalPositive("appraisalValue", "SM_REAL_ESTATE_APPRAISAL_INVALID", "Real estate appraisal value is invalid", "Real estate holdings must include a positive appraisal value."),
            ]);
}

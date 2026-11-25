using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the CommercialPaper asset module.</summary>
internal static class CommercialPaperAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("CommercialPaper", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "CommercialPaper",
            [
                FieldRule.OptionalNonNegative("discountRate", "SM_CP_DISCOUNT_RATE_INVALID", "Commercial paper discount rate is invalid", "Commercial paper discount rates must be zero or greater.")
            ]);
}

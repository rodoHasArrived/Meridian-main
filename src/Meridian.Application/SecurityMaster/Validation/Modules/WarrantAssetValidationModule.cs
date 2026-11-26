using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the Warrant asset module.</summary>
internal static class WarrantAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("Warrant", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "Warrant",
            [
                FieldRule.OptionalAllowedString("warrantType", SecurityReferenceTaxonomyCatalog.Default.GetValues(SecurityReferenceTaxonomyKeys.WarrantType), "SM_WARRANT_TYPE_INVALID", "Warrant type is invalid", "Warrant type must be Put or Call."),
                FieldRule.OptionalPositive("strike", "SM_WARRANT_STRIKE_INVALID", "Warrant strike is invalid", "Warrant strike must be greater than zero when supplied."),
                FieldRule.OptionalPositive("multiplier", "SM_WARRANT_MULTIPLIER_INVALID", "Warrant multiplier is invalid", "Warrant multiplier must be greater than zero when supplied.")
            ]);
}

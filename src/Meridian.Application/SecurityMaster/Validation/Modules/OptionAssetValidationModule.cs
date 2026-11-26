using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the Option asset module.</summary>
internal static class OptionAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("Option", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "Option",
            [
                FieldRule.OptionalAllowedString("putCall", SecurityReferenceTaxonomyCatalog.Default.GetValues(SecurityReferenceTaxonomyKeys.OptionPutCall), "SM_OPTION_PUT_CALL_INVALID", "Option put/call flag is invalid", "Option putCall must be Put or Call."),
                FieldRule.OptionalPositive("strike", "SM_OPTION_STRIKE_INVALID", "Option strike is invalid", "Option strike must be greater than zero."),
                FieldRule.OptionalPositive("multiplier", "SM_OPTION_MULTIPLIER_INVALID", "Option multiplier is invalid", "Option multiplier must be greater than zero.")
            ]);
}

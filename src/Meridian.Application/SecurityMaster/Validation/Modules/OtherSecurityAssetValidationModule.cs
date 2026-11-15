using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the OtherSecurity asset module.</summary>
internal static class OtherSecurityAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("OtherSecurity", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new CompositeAssetClassValidator(
            "OtherSecurity",
            [
                new JsonAssetClassValidator(
                    "OtherSecurity",
                    [
                        DisallowedStringValuesRule.Create(
                            "category",
                            SecurityCanonicalAssetHomes.SecuritizedLabels,
                            SecurityValidationSeverityDto.Error,
                            "SM_OTHER_SECURITY_SECURITIZED_NONCANONICAL",
                            "Securitized products must be modeled as StructuredCredit",
                            "OtherSecurity category '{0}' names a securitized product whose canonical home is StructuredCredit.",
                            "Re-model the instrument as StructuredCredit through a governed amendment.")
                    ]),
                new SecurityAssetProfileAssetClassValidator("OtherSecurity", assetProfileCatalog, requireProfileReference: false)
            ]);
}

using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the CustomAsset asset module.</summary>
internal static class CustomAssetAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("CustomAsset", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new CompositeAssetClassValidator(
            "CustomAsset",
            [
                new SecurityAssetProfileAssetClassValidator("CustomAsset", assetProfileCatalog, requireProfileReference: true),
                // ADR-022: a CustomAsset envelope whose classification metadata names a
                // securitized product should resolve to StructuredCredit via a registered
                // reclassifying profile (e.g. structured-credit-io-po), not stay a generic
                // custom asset — CustomAsset remains the home for private/other assets with no
                // first-class kind. Errors block the alternate home; the profile map decides identity.
                new JsonAssetClassValidator(
                    "CustomAsset",
                    [
                        DisallowedStringValuesRule.Create(
                            "category",
                            SecurityCanonicalAssetHomes.SecuritizedLabels,
                            SecurityValidationSeverityDto.Error,
                            "SM_CUSTOM_ASSET_SECURITIZED_NONCANONICAL",
                            "Securitized products belong in StructuredCredit",
                            "CustomAsset category '{0}' names a securitized product; StructuredCredit is the canonical home for MBS/ABS/CLO/CMBS tranches (ADR-022).",
                            "Pin the record to a profile that resolves to StructuredCredit (e.g. structured-credit-io-po) or re-model it as a first-class StructuredCredit record."),
                        DisallowedStringValuesRule.Create(
                            "accountingClassification",
                            SecurityCanonicalAssetHomes.SecuritizedLabels,
                            SecurityValidationSeverityDto.Error,
                            "SM_CUSTOM_ASSET_SECURITIZED_NONCANONICAL",
                            "Securitized products belong in StructuredCredit",
                            "CustomAsset accounting classification '{0}' names a securitized product whose canonical home is StructuredCredit.",
                            "Select a profile that resolves to StructuredCredit or a first-class StructuredCredit definition.")
                    ])
            ]);
}

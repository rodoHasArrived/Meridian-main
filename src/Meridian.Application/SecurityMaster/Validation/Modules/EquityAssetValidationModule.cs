using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the Equity asset module.</summary>
internal static class EquityAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("Equity", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "Equity",
            [
                FieldRule.OptionalNonNegative("preferredTerms.dividendRate", "SM_EQUITY_DIVIDEND_RATE_INVALID", "Preferred dividend rate is invalid", "Preferred equity dividend rates must be zero or greater."),
                FieldRule.OptionalPositive("convertibleTerms.conversionRatio", "SM_EQUITY_CONVERSION_RATIO_INVALID", "Conversion ratio is invalid", "Convertible equity conversion ratios must be greater than zero."),
                DateOrderRule.Optional("convertibleTerms.conversionStartDate", "convertibleTerms.conversionEndDate", "SM_EQUITY_CONVERSION_WINDOW_INVALID", "Conversion window is invalid", "Convertible equity conversion start date must be on or before conversion end date.")
            ]);
}

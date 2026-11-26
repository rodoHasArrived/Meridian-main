using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the PrivateCompanyEquity asset module.</summary>
internal static class PrivateCompanyEquityAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("PrivateCompanyEquity", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "PrivateCompanyEquity",
            [
                FieldRule.OptionalNonNegative("ownershipPercent", "SM_PRIVATE_COMPANY_OWNERSHIP_INVALID", "Private company ownership percent is invalid", "Private company ownership percentages must be zero or greater when supplied."),
                FieldRule.OptionalPositive("costBasis", "SM_PRIVATE_COMPANY_COST_BASIS_INVALID", "Private company cost basis is invalid", "Private company equity records must include a positive cost basis."),
                FieldRule.OptionalNonNegative("latestValuation", "SM_PRIVATE_COMPANY_VALUATION_INVALID", "Private company valuation is invalid", "Private company latest valuation must be zero or greater when supplied.")
            ]);
}

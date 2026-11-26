using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the MoneyMarketFund asset module.</summary>
internal static class MoneyMarketFundAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("MoneyMarketFund", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "MoneyMarketFund",
            [
                FieldRule.OptionalNonNegative("weightedAverageMaturityDays", "SM_MMF_WAM_INVALID", "Money market WAM is invalid", "Weighted average maturity must be zero or greater when supplied.")
            ]);
}

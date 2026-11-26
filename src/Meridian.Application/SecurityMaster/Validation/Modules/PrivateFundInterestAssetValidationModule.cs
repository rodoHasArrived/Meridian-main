using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the PrivateFundInterest asset module.</summary>
internal static class PrivateFundInterestAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("PrivateFundInterest", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "PrivateFundInterest",
            [
                FieldRule.OptionalPositive("vintage", "SM_PRIVATE_FUND_VINTAGE_INVALID", "Private fund vintage is invalid", "Private fund interests must include a positive vintage year."),
                FieldRule.OptionalPositive("commitment", "SM_PRIVATE_FUND_COMMITMENT_INVALID", "Private fund commitment is invalid", "Private fund commitments must be greater than zero."),
                FieldRule.OptionalNonNegative("fundedAmount", "SM_PRIVATE_FUND_FUNDED_INVALID", "Private fund funded amount is invalid", "Private fund funded amounts must be zero or greater when supplied."),
                FieldRule.OptionalNonNegative("unfundedAmount", "SM_PRIVATE_FUND_UNFUNDED_INVALID", "Private fund unfunded amount is invalid", "Private fund unfunded amounts must be zero or greater when supplied."),
            ]);
}

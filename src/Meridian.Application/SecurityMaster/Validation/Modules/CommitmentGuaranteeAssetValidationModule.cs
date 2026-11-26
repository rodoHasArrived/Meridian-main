using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the CommitmentGuarantee asset module.</summary>
internal static class CommitmentGuaranteeAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("CommitmentGuarantee", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "CommitmentGuarantee",
            [
                FieldRule.OptionalPositive("committedAmount", "SM_COMMITMENT_GUARANTEE_AMOUNT_INVALID", "Committed or guaranteed amount is invalid", "Commitment and guarantee amounts must be greater than zero."),
                FieldRule.OptionalNonNegative("unfundedAmount", "SM_COMMITMENT_GUARANTEE_EXPOSURE_INVALID", "Unfunded/exposure amount is invalid", "Unfunded or exposure amounts must be zero or greater when supplied."),
                DateOrderRule.Optional("effectiveDate", "expiryDate", "SM_COMMITMENT_GUARANTEE_DATE_RANGE_INVALID", "Commitment/guarantee date range is invalid", "Commitment or guarantee effective date must be on or before expiry date."),
                FieldRule.OptionalNonNegative("feeRate", "SM_COMMITMENT_GUARANTEE_FEE_INVALID", "Commitment/guarantee fee rate is invalid", "Commitment or guarantee fee rates must be zero or greater when supplied.")
            ]);
}

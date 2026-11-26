using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the StructuredNote asset module.</summary>
internal static class StructuredNoteAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("StructuredNote", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "StructuredNote",
            [
                DateOrderRule.Optional("issueDate", "maturity", "SM_STRUCTURED_NOTE_ISSUE_AFTER_MATURITY", "Structured note issue date is after maturity", "Structured note issue date must be on or before maturity."),
                FieldRule.OptionalPositive("denomination", "SM_STRUCTURED_NOTE_DENOMINATION_INVALID", "Structured note denomination is invalid", "Structured note denomination must be greater than zero when present."),
                FieldRule.OptionalFraction("principalProtectionPct", "SM_STRUCTURED_NOTE_PROTECTION_INVALID", "Structured note principal protection is invalid", "Principal protection must be a fraction between 0 and 1 (1 = fully protected)."),
                FieldRule.OptionalNonNegative("participationRate", "SM_STRUCTURED_NOTE_PARTICIPATION_INVALID", "Structured note participation is invalid", "Participation rate must be zero or greater when present."),
                FieldRule.OptionalNonNegative("returnCap", "SM_STRUCTURED_NOTE_RETURN_CAP_INVALID", "Structured note return cap is invalid", "Return cap must be zero or greater when present."),
                FieldRule.OptionalNonNegative("couponRate", "SM_STRUCTURED_NOTE_COUPON_INVALID", "Structured note coupon is invalid", "Structured note coupon must be zero or greater when present."),
                FieldRule.OptionalPositive("barrierLevel", "SM_STRUCTURED_NOTE_BARRIER_INVALID", "Structured note barrier is invalid", "Structured note barrier must be greater than zero when present.")
            ]);
}

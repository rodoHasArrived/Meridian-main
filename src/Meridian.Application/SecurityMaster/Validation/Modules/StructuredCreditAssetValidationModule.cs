using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the StructuredCredit asset module.</summary>
internal static class StructuredCreditAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("StructuredCredit", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "StructuredCredit",
            [
                FieldRule.OptionalPositive("originalFace", "SM_STRUCTURED_CREDIT_ORIGINAL_FACE_INVALID", "Structured credit original face is invalid", "Structured credit original face must be greater than zero."),
                FieldRule.OptionalNonNegative("currentFactor", "SM_STRUCTURED_CREDIT_CURRENT_FACTOR_INVALID", "Structured credit current factor is invalid", "Structured credit current factor must be zero or greater when supplied."),
                // The free-text factorSchedule is a trustee-report pointer, not a schedule: pool
                // amortization and cash-flow projection read the dated factorScheduleEntries.
                TypedScheduleBackingRule.Create(
                    "factorSchedule",
                    "factorScheduleEntries",
                    SecurityValidationSeverityDto.Warning,
                    "SM_STRUCTURED_CREDIT_FACTOR_ENTRIES_MISSING",
                    "Structured credit factor schedule has no dated entries",
                    "factorSchedule names a trustee or servicer report but the record carries no dated factorScheduleEntries; calculated paydown and cash-flow projection cannot use a report reference.",
                    "Add dated factorScheduleEntries ({asOfDate, factor}) taken from the referenced report."),
                FieldRule.OptionalAllowedString("structuredCreditKind", SecurityReferenceTaxonomyCatalog.Default.GetValues(SecurityReferenceTaxonomyKeys.StructuredCreditKind), "SM_STRUCTURED_CREDIT_KIND_INVALID", "Structured credit kind is not a governed value", "Structured credit kind must be one of the structured-credit-kind taxonomy values when supplied."),
                FieldRule.OptionalString("seniority", "SM_STRUCTURED_CREDIT_SENIORITY_INVALID", "Structured credit seniority is not a string", "Structured credit seniority must be a non-blank string when supplied."),
                FieldRule.OptionalFraction("attachmentPoint", "SM_STRUCTURED_CREDIT_ATTACHMENT_INVALID", "Structured credit attachment point is invalid", "Structured credit attachment point must be a fraction between zero and one when supplied."),
                FieldRule.OptionalFraction("detachmentPoint", "SM_STRUCTURED_CREDIT_DETACHMENT_INVALID", "Structured credit detachment point is invalid", "Structured credit detachment point must be a fraction between zero and one when supplied."),
                NumericOrderRule.Optional("attachmentPoint", "detachmentPoint", "SM_STRUCTURED_CREDIT_ATTACHMENT_ABOVE_DETACHMENT", "Structured credit attachment point exceeds detachment point", "Structured credit attachment point must be at or below the detachment point when both are supplied."),
                FieldRule.OptionalNonNegative("weightedAverageCoupon", "SM_STRUCTURED_CREDIT_WAC_INVALID", "Structured credit weighted-average coupon is invalid", "Structured credit weighted-average coupon must be zero or greater when supplied."),
                FieldRule.OptionalNonNegative("weightedAverageMaturityMonths", "SM_STRUCTURED_CREDIT_WAM_INVALID", "Structured credit weighted-average maturity is invalid", "Structured credit weighted-average maturity months must be zero or greater when supplied."),
                FieldRule.OptionalNonNegative("weightedAverageLoanAgeMonths", "SM_STRUCTURED_CREDIT_WALA_INVALID", "Structured credit weighted-average loan age is invalid", "Structured credit weighted-average loan age months must be zero or greater when supplied."),
                // The ordering rules below read an unparseable date as "not supplied"; these two
                // rules are what report a supplied period end that is not a date.
                FieldRule.OptionalDate("reinvestmentPeriodEnd", "SM_STRUCTURED_CREDIT_REINVESTMENT_END_NOT_A_DATE", "Structured credit reinvestment period end is not a date", "Structured credit reinvestment period end must be an ISO-8601 date when supplied."),
                FieldRule.OptionalDate("nonCallPeriodEnd", "SM_STRUCTURED_CREDIT_NON_CALL_END_NOT_A_DATE", "Structured credit non-call period end is not a date", "Structured credit non-call period end must be an ISO-8601 date when supplied."),
                DateOrderRule.Optional("reinvestmentPeriodEnd", "maturity", "SM_STRUCTURED_CREDIT_REINVESTMENT_END_INVALID", "Structured credit reinvestment period end is invalid", "Structured credit reinvestment period end must be on or before maturity."),
                DateOrderRule.Optional("nonCallPeriodEnd", "maturity", "SM_STRUCTURED_CREDIT_NON_CALL_END_INVALID", "Structured credit non-call period end is invalid", "Structured credit non-call period end must be on or before maturity.")
            ]);
}

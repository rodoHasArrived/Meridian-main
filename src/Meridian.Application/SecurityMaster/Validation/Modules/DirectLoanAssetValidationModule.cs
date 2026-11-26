using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>
/// Semantic validation contributed by the DirectLoan asset module. The economic-consistency rules are
/// Warnings while existing records are measured; the schema carries no origination, commitment,
/// funded, floor or cap terms, so those relations are not checkable here.
/// </summary>
internal static class DirectLoanAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("DirectLoan", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "DirectLoan",
            [
                DependentTermRule.Create(
                    "spreadBps",
                    "referenceIndex",
                    SecurityValidationSeverityDto.Warning,
                    "SM_DIRECTLOAN_SPREAD_WITHOUT_INDEX",
                    "Loan spread has no reference index",
                    "spreadBps is set but referenceIndex is not; a spread is quoted over an index, so the floating coupon cannot be resolved.",
                    "Add the loan's reference index (for example SOFR) or remove the spread."),
                TermScheduleDateOrderRule.Create(
                    "principalSchedule",
                    "paymentDate",
                    SecurityValidationSeverityDto.Warning,
                    "SM_DIRECTLOAN_PRINCIPAL_SCHEDULE_ORDER",
                    "Loan principal schedule is out of order",
                    "principalSchedule payment dates must be strictly increasing; a repeated or earlier date makes the repayment profile ambiguous.",
                    "Sort the principal schedule by payment date and combine payments that share a date."),
                TermArrayElementRule.Create(
                    "principalSchedule",
                    SecurityValidationSeverityDto.Warning,
                    "SM_DIRECTLOAN_PRINCIPAL_AMOUNT_INVALID",
                    "Loan principal payment is not positive",
                    "Every principalSchedule amount must be greater than zero.",
                    "Correct or remove the zero or negative principal payment.",
                    static payment => payment.TryGetDecimal("amount", out var amount) && amount <= 0m ? "amount" : null),
                TermArrayElementRule.Create(
                    "principalSchedule",
                    SecurityValidationSeverityDto.Warning,
                    "SM_DIRECTLOAN_PRINCIPAL_AFTER_MATURITY",
                    "Loan principal payment falls after maturity",
                    "A principalSchedule payment date is after the loan's maturity.",
                    "Correct the payment date or the loan maturity.",
                    static payment => payment.TryGetDate("paymentDate", out var paid)
                                      && payment.TryGetRecordDate("maturity", out var maturity)
                                      && paid > maturity
                        ? "paymentDate"
                        : null)
            ]);
}

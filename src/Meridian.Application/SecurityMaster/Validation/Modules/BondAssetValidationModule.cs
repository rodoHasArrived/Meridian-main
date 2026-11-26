using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the Bond asset module.</summary>
internal static class BondAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("Bond", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "Bond",
            [
                FieldRule.OptionalNonNegative("couponRate", "SM_BOND_COUPON_RATE_INVALID", "Bond coupon rate is invalid", "Fixed coupon rates must be zero or greater."),
                FieldRule.OptionalNonNegative("spreadBps", "SM_BOND_FLOATING_SPREAD_INVALID", "Bond floating spread is invalid", "Floating-rate spread must be zero or greater when supplied."),
                DateOrderRule.Optional("issueDate", "maturity", "SM_BOND_ISSUE_DATE_INVALID", "Bond issue date is invalid", "Bond issue date must be on or before maturity."),
                DateOrderRule.Optional("callDate", "maturity", "SM_BOND_CALL_DATE_INVALID", "Bond call date is invalid", "Bond call date must be on or before maturity."),
                // ADR-022 canonical-home enforcement: securitized products (MBS/ABS/CLO/CMBS/CDO
                // tranches and IO/PO strips) have ONE canonical home — StructuredCredit, which
                // carries the tranche, pool, factor-schedule, and maturity economics that pool
                // amortization and cash-flow math require and BondTerms lacks. A Bond classified
                // into a securitized subclass is a label those pipelines cannot act on, and a
                // second modeling route reporting/risk/reconciliation cannot partition against.
                DisallowedStringValuesRule.Create(
                    "subclass",
                    SecurityCanonicalAssetHomes.SecuritizedLabels,
                    SecurityValidationSeverityDto.Error,
                    "SM_BOND_SECURITIZED_SUBCLASS_NONCANONICAL",
                    "Securitized products must be modeled as StructuredCredit",
                    "Bond subclass '{0}' models a securitized product; StructuredCredit is the canonical home for MBS/ABS/CLO/CMBS/CDO tranches and IO/PO strips (ADR-022).",
                    "Re-model the instrument as StructuredCredit (tranche, pool, factor schedule) through a governed amendment so downstream cash-flow, amortization, and reconciliation pipelines can act on its economics."),
                DisallowedStringValuesRule.Create(
                    "subclass",
                    ["BankLoan"],
                    SecurityValidationSeverityDto.Error,
                    "SM_BOND_BANK_LOAN_SUBCLASS_NONCANONICAL",
                    "Bank loans must be modeled as DirectLoan",
                    "Bond subclass '{0}' models a bank loan; DirectLoan is its canonical home.",
                    "Re-model the loan as DirectLoan with its borrower, commitment, repayment and covenant terms through a governed amendment.")
            ]);
}

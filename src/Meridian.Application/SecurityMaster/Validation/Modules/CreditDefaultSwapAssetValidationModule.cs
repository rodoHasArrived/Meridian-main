using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the CreditDefaultSwap asset module.</summary>
internal static class CreditDefaultSwapAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("CreditDefaultSwap", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "CreditDefaultSwap",
            [
                FieldRule.OptionalAllowedString("protectionSide", ["Buy", "Sell"], "SM_CDS_PROTECTION_SIDE_INVALID", "CDS protection side is invalid", "CDS protection side must be 'Buy' or 'Sell'."),
                FieldRule.OptionalPositive("notional", "SM_CDS_NOTIONAL_REQUIRED", "CDS notional is missing or invalid", "Credit default swaps must include a notional greater than zero."),
                DateOrderRule.Optional("effectiveDate", "maturityDate", "SM_CDS_DATE_RANGE_INVALID", "CDS date range is invalid", "CDS effective date must be on or before the maturity date."),
                FieldRule.OptionalNonNegative("fixedCouponBps", "SM_CDS_FIXED_COUPON_INVALID", "CDS fixed coupon is missing or invalid", "Credit default swaps must include a running fixed coupon of zero or more basis points."),
                FieldRule.OptionalFraction("recoveryRateAssumption", "SM_CDS_RECOVERY_RATE_INVALID", "CDS recovery rate is invalid", "CDS recovery rate assumption must be a fraction between 0 and 1.")
            ]);
}

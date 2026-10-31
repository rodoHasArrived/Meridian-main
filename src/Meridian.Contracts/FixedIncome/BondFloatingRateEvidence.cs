using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.FixedIncome;

/// <summary>Frozen inputs for one declared coupon. Rates are annual fractions; cash is unrounded.</summary>
public sealed record BondFloatingRateEvidence(
    DateOnly ResetDate,
    bool IsObserved,
    string IndexName,
    decimal IndexRate,
    decimal Spread,
    decimal? Cap,
    decimal? Floor,
    decimal Principal,
    decimal AccrualFraction,
    SecurityCouponRateUnit OriginalRateUnit,
    string Source)
{
    public const string ModelVersion = "bond-floating-coupon-v1";

    public bool TryCoupon(decimal forecastIndexShift, out decimal cash, out string? refusal)
    {
        cash = 0m;
        refusal = null;
        if (ResetDate == default || string.IsNullOrWhiteSpace(IndexName) || string.IsNullOrWhiteSpace(Source)
            || OriginalRateUnit is not (SecurityCouponRateUnit.Percent or SecurityCouponRateUnit.Fraction)
            || Principal <= 0m || AccrualFraction <= 0m || Cap is { } cap && Floor is { } floor && floor > cap)
        {
            refusal = "Floating coupon lacks complete supported reset evidence.";
            return false;
        }
        var rate = IndexRate + (IsObserved ? 0m : forecastIndexShift) + Spread;
        if (Cap is { } upper)
            rate = Math.Min(rate, upper);
        if (Floor is { } lower)
            rate = Math.Max(rate, lower);
        if (rate < 0m)
        {
            refusal = "Negative floating coupon cash requires a supported signed-interest model.";
            return false;
        }
        cash = checked(Principal * rate * AccrualFraction);
        return true;
    }
}

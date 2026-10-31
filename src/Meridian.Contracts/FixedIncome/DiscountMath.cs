namespace Meridian.Contracts.FixedIncome;

/// <summary>
/// The single home for discount-factor arithmetic. Two entry points exist on purpose and must not
/// be merged: <see cref="TryYieldFactor"/> is the double-first formula behind
/// <c>bond-pricing-math-v1</c> (prices, yields and risk sensitivities), while
/// <see cref="TryContractualFactor"/> is the decimal-first formula whose exact operation order is
/// part of every retained <c>contractual-cashflow-v1</c> packet hash. Changing either arithmetic
/// requires a new model or engine version; the golden fixtures pin both.
/// </summary>
public static class DiscountMath
{
    /// <summary>
    /// Discount factor and its yield sensitivities for one cash flow under a bond yield convention.
    /// <paramref name="frequency"/> is compounding periods per year for <see cref="BondYieldConvention.NominalPeriodic"/>
    /// and ignored otherwise. Returns false when the discount base is not positive.
    /// </summary>
    public static bool TryYieldFactor(double yield, double time, BondYieldConvention convention, double frequency,
        out double discount, out double modified, out double convexity)
    {
        var divisor = convention == BondYieldConvention.NominalPeriodic ? frequency : 1d;
        var basis = convention == BondYieldConvention.Simple ? 1d + yield * time : 1d + yield / divisor;
        discount = modified = convexity = 0d;
        if (basis <= 0d)
            return false;
        if (convention == BondYieldConvention.Simple)
        {
            discount = 1d / basis;
            modified = time / basis;
            convexity = 2d * modified * modified;
        }
        else
        {
            discount = Math.Pow(basis, -divisor * time);
            modified = time / basis;
            convexity = time * (time + 1d / divisor) / (basis * basis);
        }
        return !double.IsNaN(discount);
    }

    /// <summary>
    /// Contractual cash-flow engine discount factor: <paramref name="compounds"/> 0 is continuous
    /// compounding, a positive value is periods per year. The decimal base and exponent are formed
    /// before the double power, exactly as retained packets were hashed. Returns false with a
    /// refusal code when the periodic base is not positive or the factor leaves the finite range.
    /// </summary>
    public static bool TryContractualFactor(decimal rate, decimal years, int compounds, out decimal factor, out string? refusalCode, out string? refusalMessage)
    {
        factor = 0m;
        refusalCode = null;
        refusalMessage = null;
        if (compounds > 0 && 1m + rate / compounds <= 0)
        {
            refusalCode = "DiscountRateInvalid";
            refusalMessage = "Periodic discount base must be positive.";
            return false;
        }
        var value = compounds == 0 ? Math.Exp(-(double)(rate * years)) : Math.Pow((double)(1m + rate / compounds), -(double)(compounds * years));
        if (!double.IsFinite(value) || value <= 0)
        {
            refusalCode = "DiscountRateInvalid";
            refusalMessage = "Discount factor is outside the supported range.";
            return false;
        }
        factor = (decimal)value;
        return true;
    }
}

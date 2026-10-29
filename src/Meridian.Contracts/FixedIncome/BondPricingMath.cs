namespace Meridian.Contracts.FixedIncome;

/// <summary>One nonnegative cash flow at a positive year fraction from settlement.</summary>
public sealed record BondPricingCashFlow(decimal Amount, decimal TimeInYears);

public sealed record BondPriceMeasures(
    decimal DirtyPrice,
    decimal MacaulayDuration,
    decimal ModifiedDuration,
    decimal Convexity,
    decimal Dv01);

/// <summary>
/// Pure schedule-based discounting and bracketed yield solving. Extends the monotone bisection
/// approach of FaceValueLot to nonintegral periods without changing historical lot arithmetic.
/// Fractional powers use double precision; results are rounded to twelve decimal places and
/// every solve is checked against its target price before it is reported as converged.
/// </summary>
public static class BondPricingMath
{
    public const string ModelVersion = "bond-pricing-math-v1";
    private const int MaxFlows = 1200;

    public static bool TryPrice(
        IReadOnlyList<BondPricingCashFlow> flows,
        decimal annualYield,
        BondYieldConvention convention,
        int paymentsPerYear,
        out BondPriceMeasures? measures,
        out string? refusal)
    {
        measures = null;
        if (!Validate(flows, convention, paymentsPerYear, out refusal)) return false;
        var y = (double)annualYield;
        var m = (double)paymentsPerYear;
        var pv = 0d;
        var weightedTime = 0d;
        var sensitivity = 0d;
        var secondDerivative = 0d;
        foreach (var flow in flows)
        {
            var time = (double)flow.TimeInYears;
            if (!Discount(y, time, convention, m, out var discount, out var modified, out var convexity))
            {
                refusal = "Yield produces a nonpositive discount base or exceeds supported calculation bounds.";
                return false;
            }
            var value = (double)flow.Amount * discount;
            pv += value;
            weightedTime += value * time;
            sensitivity += value * modified;
            secondDerivative += value * convexity;
        }
        if (!double.IsFinite(pv) || pv <= 0 || pv > (double)decimal.MaxValue
            || !double.IsFinite(weightedTime) || !double.IsFinite(sensitivity) || !double.IsFinite(secondDerivative))
        {
            refusal = "Present value or risk sensitivity exceeds supported calculation bounds.";
            return false;
        }
        try
        {
            measures = new(Round(pv), Round(weightedTime / pv), Round(sensitivity / pv),
                Round(secondDerivative / pv), Round(sensitivity * 0.0001d));
            if (measures.DirtyPrice <= 0m)
            {
                measures = null;
                refusal = "Present value is below supported twelve-decimal price precision.";
                return false;
            }
            refusal = null;
            return true;
        }
        catch (OverflowException)
        {
            refusal = "Risk sensitivity exceeds supported decimal bounds.";
            return false;
        }
    }

    public static bool TrySolveYield(
        IReadOnlyList<BondPricingCashFlow> flows,
        decimal dirtyPrice,
        BondYieldConvention convention,
        int paymentsPerYear,
        out decimal annualYield,
        out string? refusal)
    {
        annualYield = 0m;
        if (!Validate(flows, convention, paymentsPerYear, out refusal)) return false;
        if (dirtyPrice <= 0m)
        {
            refusal = "Dirty price must be positive.";
            return false;
        }
        var lowerBound = convention switch
        {
            BondYieldConvention.NominalPeriodic => -(double)paymentsPerYear,
            BondYieldConvention.AnnualEffective => -1d,
            _ => -1d / (double)flows.Max(static flow => flow.TimeInYears)
        };
        var low = lowerBound + Math.Max(Math.Abs(lowerBound) * 1e-12d, 1e-14d);
        var high = 1d;
        var target = (double)dirtyPrice;
        var lowPrice = Price(flows, low, convention, paymentsPerYear);
        var highPrice = Price(flows, high, convention, paymentsPerYear);
        while (highPrice > target && high < 1_000_000d)
        {
            high *= 2d;
            highPrice = Price(flows, high, convention, paymentsPerYear);
        }
        if (lowPrice < target || highPrice > target || double.IsNaN(lowPrice) || double.IsNaN(highPrice))
        {
            refusal = "No yield can be bracketed within supported discounting bounds.";
            return false;
        }
        var mid = 0d;
        for (var i = 0; i < 200; i++)
        {
            mid = (low + high) / 2d;
            var price = Price(flows, mid, convention, paymentsPerYear);
            if (Math.Abs(price - target) <= Math.Max(1e-10d, target * 1e-12d)) break;
            if (price > target) low = mid; else high = mid;
        }
        annualYield = Round(mid);
        if (!TryPrice(flows, annualYield, convention, paymentsPerYear, out var solved, out refusal)
            || Math.Abs(solved!.DirtyPrice - dirtyPrice) > Math.Max(0.00000001m, dirtyPrice * 0.000000001m))
        {
            refusal = "Yield solver did not converge to the requested price tolerance.";
            return false;
        }
        return true;
    }

    private static bool Validate(IReadOnlyList<BondPricingCashFlow> flows, BondYieldConvention convention,
        int frequency, out string? refusal)
    {
        refusal = null;
        if (flows is null || flows.Count == 0 || flows.Count > MaxFlows
            || flows.Any(static flow => flow is null || flow.Amount < 0m || flow.TimeInYears <= 0m)
            || !flows.Any(static flow => flow.Amount > 0m))
            refusal = "Pricing requires one to 1200 nonnegative future cash flows and positive aggregate cash.";
        else if (!Enum.IsDefined(convention) || frequency is < 1 or > 12)
            refusal = "Yield convention or compounding frequency is unsupported.";
        return refusal is null;
    }

    private static double Price(IReadOnlyList<BondPricingCashFlow> flows, double yield,
        BondYieldConvention convention, int frequency)
    {
        var sum = 0d;
        foreach (var flow in flows)
        {
            if (!Discount(yield, (double)flow.TimeInYears, convention, frequency,
                    out var discount, out _, out _)) return double.PositiveInfinity;
            sum += (double)flow.Amount * discount;
        }
        return sum;
    }

    private static bool Discount(double y, double time, BondYieldConvention convention, double frequency,
        out double discount, out double modified, out double convexity)
    {
        var divisor = convention == BondYieldConvention.NominalPeriodic ? frequency : 1d;
        var basis = convention == BondYieldConvention.Simple ? 1d + y * time : 1d + y / divisor;
        discount = modified = convexity = 0d;
        if (basis <= 0d) return false;
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

    private static decimal Round(double value) => decimal.Round((decimal)value, 12, MidpointRounding.AwayFromZero);
}

using Meridian.Contracts.FixedIncome;

namespace Meridian.Contracts.Accounting.Lots;

/// <summary>Annual effective ACT/365 yield for the adopted zero-coupon income model.</summary>
internal static class BondIncomeZeroCouponMath
{
    public static decimal Solve(IReadOnlyList<BondWorkflowCashFlow> flows, DateOnly from, decimal clean, decimal accrued)
    {
        Require(flows, clean, accrued);
        var future = flows.Where(f => f.PaymentDate > from && f.Principal > 0m).ToArray();
        if (future.Length == 0)
            throw new ArgumentException("Zero-coupon yield requires remaining contractual principal.");
        if (future.Length == 1)
        {
            var accumulation = Math.Pow((double)(future[0].Principal / clean), 365d / (future[0].PaymentDate.DayNumber - from.DayNumber));
            if (!double.IsFinite(accumulation) || accumulation <= 0d || accumulation > (double)decimal.MaxValue)
                throw new ArgumentException("Zero-coupon yield is outside the supported numerical domain.");
            return (decimal)accumulation - 1m;
        }
        double Evaluate(double y) => future.Sum(f => (double)f.Principal /
            Math.Pow(1d + y, (f.PaymentDate.DayNumber - from.DayNumber) / 365d)) - (double)clean;
        var low = -0.999999999d;
        var high = 1d;
        for (var i = 0; Evaluate(high) > 0d && i < 40; i++)
            high *= 2d;
        if (Evaluate(low) <= 0d || Evaluate(high) >= 0d)
            throw new ArgumentException("Zero-coupon accounting yield is unavailable: YieldNotBracketed.");
        for (var i = 0; i < 160; i++)
        {
            var mid = (low + high) / 2d;
            if (Evaluate(mid) > 0d)
                low = mid;
            else
                high = mid;
        }
        return (decimal)((low + high) / 2d);
    }

    public static decimal Roll(IReadOnlyList<BondWorkflowCashFlow> flows, DateOnly from, DateOnly through,
        decimal clean, decimal yield, decimal accrued)
    {
        Require(flows, clean, accrued);
        if (yield <= -1m || through < from)
            throw new ArgumentException("Zero-coupon yield is outside its positive accumulation domain.");
        var startBasis = clean;
        var prior = from;
        foreach (var payment in flows.Where(f => f.Principal > 0m && f.PaymentDate > from && f.PaymentDate <= through)
            .GroupBy(f => f.PaymentDate).OrderBy(g => g.Key))
        {
            clean = Grow(clean, prior, payment.Key, yield) - payment.Sum(f => f.Principal);
            prior = payment.Key;
        }
        clean = Grow(clean, prior, through, yield);
        // The effective-yield solver uses binary floating point only for powers. A fully paid
        // schedule is exactly extinguished; its bounded numerical residue is never posted.
        if (flows.All(f => f.PaymentDate <= through) && Math.Abs(clean) <= Math.Max(1m, startBasis) * 0.0000000001m)
            return 0m;
        return clean;
    }

    private static decimal Grow(decimal basis, DateOnly from, DateOnly to, decimal yield) =>
        basis * (decimal)Math.Pow((double)(1m + yield), (to.DayNumber - from.DayNumber) / 365d);

    private static void Require(IReadOnlyList<BondWorkflowCashFlow> flows, decimal clean, decimal accrued)
    {
        if (clean <= 0m || accrued != 0m || flows.Any(f => f.Interest != 0m || f.Principal < 0m))
            throw new ArgumentException("Zero-coupon income requires positive clean basis, no purchased coupon accrual and nonnegative contractual principal.");
    }
}

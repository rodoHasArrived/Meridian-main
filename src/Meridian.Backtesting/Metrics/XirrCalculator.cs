namespace Meridian.Backtesting.Metrics;

/// <summary>
/// Computes the extended internal rate of return (XIRR) for an irregular cash-flow series
/// using Newton-Raphson iteration with bisection fallback.
/// </summary>
internal static class XirrCalculator
{
    private const double Tolerance = 1e-7;
    private const int MaxIterations = 200;

    /// <summary>
    /// Solves for <c>r</c> such that:
    ///   ∑ CF_i / (1 + r)^(t_i / 365) = 0
    /// where <c>t_i</c> is days from the reference date.
    /// </summary>
    /// <param name="cashFlows">Sequence of (date, amount) pairs. Must contain at least one positive and one negative value.</param>
    /// <param name="guess">Initial rate guess (default 10%).</param>
    /// <returns>XIRR as a decimal fraction (e.g. 0.12 for 12%), or <c>double.NaN</c> if no solution found.</returns>
    public static double Calculate(
        IReadOnlyList<(DateTimeOffset date, decimal amount)> cashFlows,
        double guess = 0.10)
    {
        if (cashFlows.Count < 2)
            return double.NaN;

        var reference = cashFlows[0].date;
        var days = cashFlows.Select(cf => (cf.date - reference).TotalDays).ToArray();
        var amounts = cashFlows.Select(cf => (double)cf.amount).ToArray();

        double Npv(double r)
        {
            var sum = 0.0;
            for (var i = 0; i < amounts.Length; i++)
                sum += amounts[i] / Math.Pow(1.0 + r, days[i] / 365.0);
            return sum;
        }

        double NpvDerivative(double r)
        {
            var sum = 0.0;
            for (var i = 0; i < amounts.Length; i++)
            {
                var t = days[i] / 365.0;
                sum -= amounts[i] * t / Math.Pow(1.0 + r, t + 1.0);
            }
            return sum;
        }

        // Newton-Raphson
        var rate = guess;
        for (var i = 0; i < MaxIterations; i++)
        {
            var npv = Npv(rate);
            if (Math.Abs(npv) < Tolerance)
                return rate;
            var deriv = NpvDerivative(rate);
            if (Math.Abs(deriv) < 1e-10)
                break;
            var next = rate - npv / deriv;
            if (next <= -1.0)
                break;
            if (Math.Abs(next - rate) < Tolerance)
                return next;
            rate = next;
        }

        // Bisection fallback in [-0.999, 10.0]
        return Bisect(Npv, -0.999, 10.0);
    }

    private static double Bisect(Func<double, double> f, double lo, double hi)
    {
        var fLo = f(lo);
        var fHi = f(hi);
        if (fLo * fHi > 0)
            return double.NaN;

        for (var i = 0; i < MaxIterations; i++)
        {
            var mid = (lo + hi) / 2.0;
            var fMid = f(mid);
            if (Math.Abs(fMid) < Tolerance || (hi - lo) / 2.0 < Tolerance)
                return mid;
            if (fLo * fMid < 0)
                hi = mid;
            else
            { lo = mid; fLo = fMid; }
        }
        return (lo + hi) / 2.0;
    }
}

using Meridian.QuantScript.Plotting;

namespace Meridian.QuantScript.Api;

/// <summary>
/// Result of a portfolio construction operation.
/// Holds constituent weights and exposes return/risk analytics.
/// </summary>
public sealed class PortfolioResult
{
    private readonly IReadOnlyList<PriceSeries> _series;

    public IReadOnlyDictionary<string, double> Weights { get; }
    public IReadOnlyList<string> Symbols { get; }

    internal PortfolioResult(
        IReadOnlyDictionary<string, double> weights,
        IReadOnlyList<PriceSeries> series)
    {
        Weights = weights;
        Symbols = [.. weights.Keys];
        _series = series;
    }

    /// <summary>Weighted portfolio daily return series.</summary>
    public ReturnSeries Returns()
    {
        var dailyBySymbol = _series.ToDictionary(
            s => s.Symbol,
            s => s.DailyReturns().Points.ToDictionary(p => p.Date, p => p.Value));

        var allDates = dailyBySymbol.Values
            .SelectMany(d => d.Keys)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        var pts = allDates.Select(date =>
        {
            double r = 0;
            foreach (var (sym, returns) in dailyBySymbol)
            {
                if (returns.TryGetValue(date, out var ret) && Weights.TryGetValue(sym, out var w))
                    r += w * ret;
            }
            return new ReturnPoint(date, r);
        }).ToList();

        return new ReturnSeries("Portfolio", ReturnKind.Arithmetic, pts);
    }

    public double[,] CorrelationMatrix()
    {
        var streams = _series.Select(s => (IReadOnlyList<double>)s.DailyReturns().Points.Select(p => p.Value).ToList()).ToList();
        return StatisticsEngine.CorrelationMatrix(streams);
    }

    public double[,] CovarianceMatrix()
    {
        var streams = _series.Select(s => (IReadOnlyList<double>)s.DailyReturns().Points.Select(p => p.Value).ToList()).ToList();
        return StatisticsEngine.CovarianceMatrix(streams);
    }

    public double SharpeRatio(double riskFreeRate = 0.04) =>
        Returns().SharpeRatio(riskFreeRate);

    public IReadOnlyList<ReturnPoint> Drawdowns() =>
        Returns().DrawdownSeries();

    /// <summary>Enqueues a correlation heatmap chart.</summary>
    public void PlotHeatmap(string? title = null)
    {
        var matrix = CorrelationMatrix();
        var n = Symbols.Count;
        var data = new double[n][];
        for (var i = 0; i < n; i++)
        {
            data[i] = new double[n];
            for (var j = 0; j < n; j++)
                data[i][j] = matrix[i, j];
        }
        ScriptContext.PlotQueue?.Enqueue(new PlotRequest(
            title ?? "Correlation Heatmap",
            PlotType.Heatmap,
            HeatmapData: data,
            HeatmapLabels: [.. Symbols]));
    }

    /// <summary>Enqueues a cumulative return overlay for all constituent series.</summary>
    public void PlotCumulative(string? title = null)
    {
        var multiSeries = _series
            .Select(s =>
            {
                var cum = s.CumulativeReturns();
                return (s.Symbol, (IReadOnlyList<(DateOnly, double)>)[.. cum.Points.Select(p => (p.Date, p.Value))]);
            })
            .ToList();

        ScriptContext.PlotQueue?.Enqueue(new PlotRequest(
            title ?? "Portfolio Cumulative Returns",
            PlotType.MultiLine,
            MultiSeries: multiSeries));
    }
}

/// <summary>
/// Factory for constructing portfolio weighting schemes.
/// </summary>
public static class PortfolioBuilder
{
    /// <summary>Equal-weight portfolio across all provided series.</summary>
    public static PortfolioResult EqualWeight(params PriceSeries[] series)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (series.Length == 0)
            throw new ArgumentException("At least one series is required", nameof(series));
        var w = 1.0 / series.Length;
        var weights = series.ToDictionary(s => s.Symbol, _ => w);
        return new PortfolioResult(weights, series);
    }

    /// <summary>Custom weight portfolio. Weights must sum to ~1.0; keys are symbols.</summary>
    public static PortfolioResult CustomWeight(
        IReadOnlyDictionary<string, double> weights, params PriceSeries[] series)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(series);
        return new PortfolioResult(weights, series);
    }

    /// <summary>
    /// Minimum-variance portfolio on the efficient frontier meeting the specified return target,
    /// subject to per-asset weight bounds.
    /// <para>
    /// Uses projected gradient descent with an augmented Lagrangian penalty for the return
    /// constraint. All arithmetic is pure .NET — no external optimisation library is required.
    /// Falls back to equal-weight when the input has fewer than two assets or degenerate
    /// (zero-variance) return streams.
    /// </para>
    /// </summary>
    public static PortfolioResult EfficientFrontier(
        EfficientFrontierConstraints constraints, params PriceSeries[] series)
    {
        ArgumentNullException.ThrowIfNull(constraints);
        ArgumentNullException.ThrowIfNull(series);
        if (series.Length == 0)
            throw new ArgumentException("At least one series is required", nameof(series));

        if (series.Length == 1)
            return new PortfolioResult(new Dictionary<string, double> { [series[0].Symbol] = 1.0 }, series);

        // Collect aligned daily return streams.
        var returnStreams = series
            .Select(static s => (IReadOnlyList<double>)s.DailyReturns().Points.Select(static p => p.Value).ToList())
            .ToList();

        var mu = returnStreams.Select(static r => r.Count > 0 ? r.Average() : 0.0).ToArray();
        var sigma = StatisticsEngine.CovarianceMatrix(returnStreams);

        // Degenerate covariance — fall back to equal weight.
        var sigmaTrace = 0.0;
        for (var i = 0; i < series.Length; i++)
            sigmaTrace += sigma[i, i];
        if (sigmaTrace < 1e-14)
            return EqualWeight(series);

        var minW = constraints.MinWeight ?? 0.0;
        var maxW = constraints.MaxWeight ?? 1.0;

        var rawWeights = MvOptimizer.Optimize(mu, sigma, constraints.TargetReturn, minW, maxW);

        var weights = series
            .Zip(rawWeights, static (s, w) => (s.Symbol, w))
            .ToDictionary(static t => t.Symbol, static t => t.w);

        return new PortfolioResult(weights, series);
    }
}

/// <summary>
/// Internal mean-variance portfolio optimiser.
/// Minimises portfolio variance subject to:
///   · sum(w) = 1
///   · minWeight ≤ w_i ≤ maxWeight
///   · μ'w ≥ targetReturn  (enforced via augmented Lagrangian)
/// Uses projected gradient descent; all operations are O(n²) per iteration.
/// </summary>
internal static class MvOptimizer
{
    private const int MaxIterations = 1000;
    private const double Tolerance = 1e-9;

    internal static double[] Optimize(
        double[] mu,
        double[,] sigma,
        double targetReturn,
        double minWeight,
        double maxWeight)
    {
        var n = mu.Length;
        if (n == 0) return [];
        if (n == 1) return [1.0];

        // Step size: proportional to the inverse of the operator norm (estimated by trace / n).
        var sigmaTrace = 0.0;
        for (var i = 0; i < n; i++) sigmaTrace += sigma[i, i];
        var step = sigmaTrace > 0 ? 1.0 / (2.0 * sigmaTrace) : 0.01;

        var w = InitializeFeasible(n, minWeight, maxWeight);

        // Dual variable for the soft return constraint:  λ ≥ 0, active when μ'w < targetReturn.
        var lambda = 0.0;
        const double rho = 50.0;

        for (var iter = 0; iter < MaxIterations; iter++)
        {
            // Gradient of 0.5·w'Σw = Σw.
            var grad = MatVec(sigma, w);

            // Augmented Lagrangian gradient contribution for the return constraint.
            var portReturn = Dot(mu, w);
            var violation = targetReturn - portReturn;
            var penaltyMult = lambda + rho * Math.Max(0.0, violation);
            if (penaltyMult > 0.0)
                for (var i = 0; i < n; i++)
                    grad[i] -= penaltyMult * mu[i];

            // Projected gradient step onto the budget + box feasible set.
            var wNew = ProjectBudgetBox(Axpy(-step, grad, w), minWeight, maxWeight);

            // Dual variable update.
            lambda = Math.Max(0.0, lambda + rho * violation);

            if (Dist(wNew, w) < Tolerance)
            {
                w = wNew;
                break;
            }

            w = wNew;
        }

        return w;
    }

    /// <summary>
    /// Projects <paramref name="v"/> onto {w : sum(w)=1, lb ≤ w_i ≤ ub} using binary search on
    /// the Lagrange multiplier θ such that Σ clip(v_i − θ, lb, ub) = 1.
    /// </summary>
    private static double[] ProjectBudgetBox(double[] v, double lb, double ub)
    {
        var n = v.Length;
        if (n == 0) return v;

        var totalMin = lb * n;
        var totalMax = ub * n;

        // Constraints are infeasible — return equal weight as a safe fallback.
        if (totalMin > 1.0 + 1e-10 || totalMax < 1.0 - 1e-10)
            return Enumerable.Repeat(1.0 / n, n).ToArray();

        // Binary search: find θ so that sum(clip(v_i − θ, lb, ub)) = 1.
        var lo = double.MaxValue;
        var hi = double.MinValue;
        for (var i = 0; i < n; i++)
        {
            if (v[i] - ub < lo) lo = v[i] - ub;
            if (v[i] - lb > hi) hi = v[i] - lb;
        }

        for (var k = 0; k < 200; k++)
        {
            var mid = (lo + hi) * 0.5;
            var s = 0.0;
            for (var i = 0; i < n; i++)
                s += Math.Clamp(v[i] - mid, lb, ub);
            if (s > 1.0) lo = mid;
            else hi = mid;
        }

        var theta = (lo + hi) * 0.5;
        var result = new double[n];
        for (var i = 0; i < n; i++)
            result[i] = Math.Clamp(v[i] - theta, lb, ub);
        return result;
    }

    private static double[] InitializeFeasible(int n, double lb, double ub)
    {
        // Equal weight, projected onto the feasible box+budget set.
        var w = new double[n];
        var eq = 1.0 / n;
        for (var i = 0; i < n; i++) w[i] = Math.Clamp(eq, lb, ub);
        return ProjectBudgetBox(w, lb, ub);
    }

    private static double[] MatVec(double[,] a, double[] x)
    {
        var n = x.Length;
        var result = new double[n];
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
                result[i] += a[i, j] * x[j];
        return result;
    }

    /// <summary>Returns alpha*x + y (BLAS Axpy).</summary>
    private static double[] Axpy(double alpha, double[] x, double[] y)
    {
        var n = x.Length;
        var result = new double[n];
        for (var i = 0; i < n; i++)
            result[i] = alpha * x[i] + y[i];
        return result;
    }

    private static double Dot(double[] a, double[] b)
    {
        var s = 0.0;
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
            s += a[i] * b[i];
        return s;
    }

    private static double Dist(double[] a, double[] b)
    {
        var s = 0.0;
        for (var i = 0; i < a.Length; i++)
        {
            var d = a[i] - b[i];
            s += d * d;
        }
        return Math.Sqrt(s);
    }
}

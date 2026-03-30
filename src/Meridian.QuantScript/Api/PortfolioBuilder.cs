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
    /// Efficient frontier portfolio stub — returns equal-weight in v1.
    /// Full quadratic optimisation is deferred to v2.
    /// </summary>
    // TODO: Implement full MV quadratic optimisation (e.g. via MathNet.Numerics or custom QP solver)
    public static PortfolioResult EfficientFrontier(
        EfficientFrontierConstraints constraints, params PriceSeries[] series)
        => EqualWeight(series);
}

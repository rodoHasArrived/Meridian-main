namespace Meridian.QuantScript.Api;

public sealed record ReturnPoint(DateOnly Date, double Value);

public enum ReturnKind { Arithmetic, Log, Cumulative, Rolling, DrawdownSeries, RollingStat }

/// <summary>
/// An ordered series of per-period returns (arithmetic or log).
/// Produced by PriceSeries extension methods; all statistical methods are instance methods here.
/// </summary>
public sealed class ReturnSeries
{
    public string Symbol { get; }
    public ReturnKind Kind { get; }
    public IReadOnlyList<ReturnPoint> Points { get; }
    public int Count => Points.Count;

    public ReturnSeries(string symbol, ReturnKind kind, IReadOnlyList<ReturnPoint> points)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(points);
        Symbol = symbol;
        Kind = kind;
        Points = points;
    }

    private IReadOnlyList<double> Values => [.. Points.Select(p => p.Value)];

    /// <summary>Annualised Sharpe ratio (252 trading days). Risk-free rate in decimal (e.g. 0.04).</summary>
    public double SharpeRatio(double riskFreeRate = 0.04) =>
        StatisticsEngine.Sharpe(Values, riskFreeRate);

    /// <summary>Annualised Sortino ratio using downside deviation.</summary>
    public double SortinoRatio(double riskFreeRate = 0.04) =>
        StatisticsEngine.Sortino(Values, riskFreeRate);

    /// <summary>Annualised volatility (std dev of daily returns × √252).</summary>
    public double AnnualizedVolatility() =>
        StatisticsEngine.AnnualizedVolatility(Values);

    /// <summary>Maximum peak-to-trough drawdown as a fraction (e.g. 0.25 = 25%).</summary>
    public double MaxDrawdown() =>
        StatisticsEngine.MaxDrawdown(Values);

    /// <summary>Full drawdown series for every period.</summary>
    public IReadOnlyList<ReturnPoint> DrawdownSeries()
    {
        var dd = StatisticsEngine.DrawdownSeries(Values);
        return Points.Select((p, i) => new ReturnPoint(p.Date, dd[i])).ToList();
    }

    /// <summary>Beta relative to a benchmark return series.</summary>
    public double Beta(ReturnSeries benchmark)
    {
        var (a, b) = StatisticsEngine.AlignByDate(this, benchmark);
        return StatisticsEngine.Beta(a, b);
    }

    /// <summary>Jensen's Alpha relative to a benchmark return series (annualised).</summary>
    public double Alpha(ReturnSeries benchmark, double riskFreeRate = 0.04)
    {
        var (a, b) = StatisticsEngine.AlignByDate(this, benchmark);
        return StatisticsEngine.Alpha(a, b, riskFreeRate);
    }

    /// <summary>Pearson correlation with another return series (aligned by date).</summary>
    public double Correlation(ReturnSeries other)
    {
        var (a, b) = StatisticsEngine.AlignByDate(this, other);
        return StatisticsEngine.Correlation(a, b);
    }

    /// <summary>Sample skewness.</summary>
    public double Skewness() => StatisticsEngine.Skewness(Values);

    /// <summary>Excess kurtosis (normal = 0).</summary>
    public double Kurtosis() => StatisticsEngine.Kurtosis(Values);

    /// <summary>Rolling arithmetic mean over <paramref name="window"/> periods.</summary>
    public ReturnSeries RollingMean(int window)
    {
        var result = StatisticsEngine.RollingMean(Values, window);
        return new ReturnSeries(Symbol, ReturnKind.RollingStat, Points.Select((p, i) => new ReturnPoint(p.Date, result[i])).ToList());
    }

    /// <summary>Rolling sample standard deviation over <paramref name="window"/> periods.</summary>
    public ReturnSeries RollingSd(int window)
    {
        var result = StatisticsEngine.RollingSd(Values, window);
        return new ReturnSeries(Symbol, ReturnKind.RollingStat, Points.Select((p, i) => new ReturnPoint(p.Date, result[i])).ToList());
    }

    /// <summary>Cumulative return series (compounded).</summary>
    public ReturnSeries Cumulative()
    {
        double running = 1.0;
        var pts = Points.Select(p => { running *= (1.0 + p.Value); return new ReturnPoint(p.Date, running - 1.0); }).ToList();
        return new ReturnSeries(Symbol, ReturnKind.Cumulative, pts);
    }

    // ── Plot terminals ──────────────────────────────────────────────────────

    /// <summary>Enqueues a line chart of this return series to the results panel.</summary>
    public void Plot(string? title = null) =>
        EnqueuePlot(new Plotting.PlotRequest(
            title ?? $"{Symbol} Returns",
            Plotting.PlotType.Line,
            Series: [.. Points.Select(p => (p.Date, p.Value))]));

    /// <summary>Enqueues a cumulative return chart.</summary>
    public void PlotCumulative(string? title = null)
    {
        var cum = Cumulative();
        EnqueuePlot(new Plotting.PlotRequest(
            title ?? $"{Symbol} Cumulative Return",
            Plotting.PlotType.CumulativeReturn,
            Series: [.. cum.Points.Select(p => (p.Date, p.Value))]));
    }

    /// <summary>Enqueues an underwater (drawdown) chart.</summary>
    public void PlotDrawdown(string? title = null)
    {
        var dd = DrawdownSeries();
        EnqueuePlot(new Plotting.PlotRequest(
            title ?? $"{Symbol} Drawdown",
            Plotting.PlotType.Drawdown,
            Series: [.. dd.Select(p => (p.Date, p.Value))]));
    }

    private void EnqueuePlot(Plotting.PlotRequest request) =>
        ScriptContext.PlotQueue?.Enqueue(request);
}

using Meridian.QuantScript.API;
using Meridian.QuantScript.Plotting;

namespace Meridian.QuantScript.Compilation;

/// <summary>
/// The globals class injected into every Roslyn script.
/// All public members appear as top-level identifiers in .csx scripts.
/// </summary>
public sealed class QuantScriptGlobals
{
    private readonly List<KeyValuePair<string, string>> _metrics = new();
    private readonly Dictionary<string, ParameterDescriptor> _registeredParams = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyDictionary<string, object?> _paramOverrides;

    internal QuantScriptGlobals(
        IQuantDataContext dataContext,
        BacktestProxy backtestProxy,
        PlotQueue plotQueue,
        IReadOnlyDictionary<string, object?> paramOverrides,
        CancellationToken cancellationToken)
    {
        Data = new DataProxy(dataContext);
        Backtest = backtestProxy;
        Plots = plotQueue;
        CancellationToken = cancellationToken;
        _paramOverrides = paramOverrides;

        // Make the plot queue reachable from static extension methods
        PlotQueue.Current = plotQueue;
    }

    // ── Primary APIs ─────────────────────────────────────────────────────────

    /// <summary>Synchronous data access facade. See <see cref="DataProxy"/> for restrictions.</summary>
    public DataProxy Data { get; }

    /// <summary>Fluent backtest builder.</summary>
    public BacktestProxy Backtest { get; }

    /// <summary>Plot queue — receives charts produced by the script.</summary>
    public PlotQueue Plots { get; }

    /// <summary>Cancellation token propagated from the UI Stop button.</summary>
    public CancellationToken CancellationToken { get; }

    // ── Output helpers ────────────────────────────────────────────────────────

    /// <summary>Writes a line to the console output panel.</summary>
    public void Print(object? value)
        => ConsoleLines.Add(value?.ToString() ?? string.Empty);

    /// <summary>Writes a formatted table to the console output panel.</summary>
    public void PrintTable<T>(IEnumerable<T> rows)
    {
        foreach (var row in rows)
            Print(row?.ToString());
    }

    /// <summary>Records a named metric visible in the Metrics tab.</summary>
    public void PrintMetric(string label, object? value, string? category = null)
    {
        _metrics.Add(new KeyValuePair<string, string>(
            label,
            value?.ToString() ?? string.Empty));
    }

    // ── Statistics helpers ────────────────────────────────────────────────────

    /// <summary>Annualised Sharpe ratio (daily returns, 252 trading days).</summary>
    public double SharpeRatio(ReturnSeries returns, double annualRiskFreeRate = 0.04)
        => StatisticsEngine.SharpeRatio(returns.ToList().Select(r => r.Value).ToArray(), annualRiskFreeRate);

    /// <summary>Annualised Sortino ratio.</summary>
    public double SortinoRatio(ReturnSeries returns, double annualRiskFreeRate = 0.04)
        => StatisticsEngine.SortinoRatio(returns.ToList().Select(r => r.Value).ToArray(), annualRiskFreeRate);

    /// <summary>Maximum drawdown as a positive fraction.</summary>
    public double MaxDrawdown(ReturnSeries returns)
        => StatisticsEngine.MaxDrawdown(returns.ToList().Select(r => r.Value).ToArray());

    /// <summary>Annualised volatility (standard deviation of daily returns).</summary>
    public double AnnualizedVolatility(ReturnSeries returns)
        => StatisticsEngine.StdDev(returns.ToList().Select(r => r.Value).ToArray()) * Math.Sqrt(252.0);

    /// <summary>Beta relative to a benchmark return series.</summary>
    public double Beta(ReturnSeries returns, ReturnSeries benchmark)
        => StatisticsEngine.Beta(
            returns.ToList().Select(r => r.Value).ToArray(),
            benchmark.ToList().Select(r => r.Value).ToArray());

    /// <summary>Jensen's alpha (annualised) relative to a benchmark.</summary>
    public double Alpha(ReturnSeries returns, ReturnSeries benchmark, double annualRfr = 0.04)
        => StatisticsEngine.Alpha(
            returns.ToList().Select(r => r.Value).ToArray(),
            benchmark.ToList().Select(r => r.Value).ToArray(),
            annualRfr);

    /// <summary>Pearson correlation between two return series.</summary>
    public double Correlation(ReturnSeries a, ReturnSeries b)
        => StatisticsEngine.Correlation(
            a.ToList().Select(r => r.Value).ToArray(),
            b.ToList().Select(r => r.Value).ToArray());

    // ── Portfolio helpers ─────────────────────────────────────────────────────

    /// <summary>Builds an equal-weight portfolio from the supplied price series.</summary>
    public PortfolioResult EqualWeight(params PriceSeries[] series)
    {
        var builder = new PortfolioBuilder();
        foreach (var ps in series)
            builder.AddAsset(ps.Symbol, ps);
        return builder.Build();
    }

    // ── Parameter registration ────────────────────────────────────────────────

    /// <summary>
    /// Registers a script parameter at runtime and returns its current value.
    /// If the user has overridden the value in the sidebar, that value is returned.
    /// Otherwise the default is returned.
    /// </summary>
    public T Param<T>(
        string name,
        T defaultValue,
        double min = double.MinValue,
        double max = double.MaxValue,
        string? description = null)
    {
        var descriptor = new ParameterDescriptor(
            Name: name,
            TypeName: typeof(T).Name,
            Label: name,
            DefaultValue: defaultValue,
            Min: min,
            Max: max,
            Description: description);

        _registeredParams[name] = descriptor;

        if (_paramOverrides.TryGetValue(name, out var overrideValue) && overrideValue is T typed)
            return typed;

        return defaultValue;
    }

    // ── Internal state accessors ──────────────────────────────────────────────

    internal List<string> ConsoleLines { get; } = new();

    internal IReadOnlyList<KeyValuePair<string, string>> GetMetrics() => _metrics;

    internal IReadOnlyDictionary<string, ParameterDescriptor> GetRegisteredParams() => _registeredParams;
}

using Meridian.QuantScript.Api;

namespace Meridian.QuantScript.Compilation;

/// <summary>
/// A single line of console output produced by <see cref="QuantScriptGlobals.Print"/> or
/// <see cref="QuantScriptGlobals.PrintMetric"/>.
/// </summary>
public sealed record ConsoleOutputEntry(
    string Text,
    bool IsMetric = false,
    string? MetricLabel = null,
    string? Category = null);

/// <summary>
/// Injected as the Roslyn script globals object. All public members are visible as top-level
/// identifiers inside .csx scripts.
/// </summary>
public sealed class QuantScriptGlobals
{
    private readonly List<ConsoleOutputEntry> _output = [];
    private readonly object _outputLock = new();
    private readonly IReadOnlyDictionary<string, object?> _parameters;

    internal QuantScriptGlobals(
        DataProxy data,
        BacktestProxy backtest,
        CancellationToken ct,
        IReadOnlyDictionary<string, object?>? parameters = null)
    {
        Data = data;
        Backtest = backtest;
        CancellationToken = ct;
        _parameters = parameters ?? new Dictionary<string, object?>();
    }

    // ── Primary APIs ─────────────────────────────────────────────────────────
    public DataProxy Data { get; }
    public BacktestProxy Backtest { get; }

    // ── Portfolio factory ────────────────────────────────────────────────────
    public PortfolioResult EqualWeight(params PriceSeries[] series) =>
        PortfolioBuilder.EqualWeight(series);

    public PortfolioResult CustomWeight(
        IReadOnlyDictionary<string, double> weights, params PriceSeries[] series) =>
        PortfolioBuilder.CustomWeight(weights, series);

    // ── Standalone statistical helpers ───────────────────────────────────────
    public double SharpeRatio(ReturnSeries r, double riskFreeRate = 0.04) => r.SharpeRatio(riskFreeRate);
    public double SortinoRatio(ReturnSeries r, double riskFreeRate = 0.04) => r.SortinoRatio(riskFreeRate);
    public double AnnualizedVolatility(ReturnSeries r) => r.AnnualizedVolatility();
    public double MaxDrawdown(ReturnSeries r) => r.MaxDrawdown();
    public double Beta(ReturnSeries r, ReturnSeries benchmark) => r.Beta(benchmark);
    public double Alpha(ReturnSeries r, ReturnSeries benchmark, double rfr = 0.04) => r.Alpha(benchmark, rfr);
    public double Correlation(ReturnSeries a, ReturnSeries b) => a.Correlation(b);

    // ── Output ───────────────────────────────────────────────────────────────

    /// <summary>Writes a line to the console output panel.</summary>
    public void Print(object? value)
    {
        lock (_outputLock)
            _output.Add(new ConsoleOutputEntry(value?.ToString() ?? ""));
    }

    /// <summary>Prints multiple rows to the console output panel.</summary>
    public void PrintTable<T>(IEnumerable<T> rows)
    {
        foreach (var row in rows)
            lock (_outputLock)
                _output.Add(new ConsoleOutputEntry(row?.ToString() ?? ""));
    }

    /// <summary>Records a named scalar metric for display in the Metrics tab.</summary>
    /// <param name="label">Metric name.</param>
    /// <param name="value">Metric value.</param>
    /// <param name="category">Optional grouping category shown as a prefix (e.g. "Risk-Adjusted").</param>
    public void PrintMetric(string label, object value, string? category = null)
    {
        var key = category is not null ? $"{category}: {label}" : label;
        lock (_outputLock)
            _output.Add(new ConsoleOutputEntry(value?.ToString() ?? "", IsMetric: true, MetricLabel: key, Category: category));
    }

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the value of a named parameter supplied by the sidebar, or
    /// <paramref name="defaultValue"/> if none was provided.
    /// </summary>
    public T Param<T>(string name, T defaultValue = default!, double min = double.MinValue,
        double max = double.MaxValue, string? description = null)
    {
        if (!_parameters.TryGetValue(name, out var val)) return defaultValue;
        if (val is T typed) return typed;
        if (val is not null)
        {
            try { return (T)Convert.ChangeType(val, typeof(T)); }
            catch { /* fall through to default */ }
        }
        return defaultValue;
    }

    // ── Cancellation ─────────────────────────────────────────────────────────
    public CancellationToken CancellationToken { get; }

    // ── Internal result access ────────────────────────────────────────────────

    /// <summary>Returns all non-metric console lines as a single joined string.</summary>
    internal string GetConsoleOutput()
    {
        lock (_outputLock)
            return string.Join(Environment.NewLine, _output.Where(e => !e.IsMetric).Select(e => e.Text));
    }

    /// <summary>Returns all metrics recorded via <see cref="PrintMetric"/>.</summary>
    internal IReadOnlyList<KeyValuePair<string, string>> GetMetrics()
    {
        lock (_outputLock)
            return _output
                .Where(e => e.IsMetric)
                .Select(e => new KeyValuePair<string, string>(e.MetricLabel ?? "", e.Text))
                .ToList();
    }
}

using System.Threading.Channels;
using Meridian.Backtesting.Engine;
using Meridian.QuantScript.Api;
using Meridian.QuantScript.Plotting;

namespace Meridian.QuantScript.Compilation;

/// <summary>
/// Output entry from a script's Print() / PrintMetric() calls.
/// </summary>
public sealed record ConsoleOutputEntry(string Text, bool IsMetric = false, string? MetricLabel = null);

/// <summary>
/// Injected as the Roslyn script globals object. All members are visible as top-level
/// identifiers inside .csx scripts.
/// </summary>
public sealed class QuantScriptGlobals
{
    private readonly Channel<ConsoleOutputEntry> _consoleChannel =
        Channel.CreateUnbounded<ConsoleOutputEntry>();

    internal QuantScriptGlobals(
        DataProxy data,
        BacktestProxy backtest,
        CancellationToken ct)
    {
        Data = data;
        Backtest = backtest;
        CancellationToken = ct;
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
    public void Print(object? value) =>
        _consoleChannel.Writer.TryWrite(new ConsoleOutputEntry(value?.ToString() ?? ""));

    public void PrintTable<T>(IEnumerable<T> rows)
    {
        foreach (var row in rows)
            _consoleChannel.Writer.TryWrite(new ConsoleOutputEntry(row?.ToString() ?? ""));
    }

    public void PrintMetric(string label, object value) =>
        _consoleChannel.Writer.TryWrite(new ConsoleOutputEntry(
            value?.ToString() ?? "", IsMetric: true, MetricLabel: label));

    // ── Cancellation ─────────────────────────────────────────────────────────
    public CancellationToken CancellationToken { get; }

    internal IAsyncEnumerable<ConsoleOutputEntry> ReadConsoleAsync(CancellationToken ct = default) =>
        _consoleChannel.Reader.ReadAllAsync(ct);

    internal void CompleteConsole() => _consoleChannel.Writer.TryComplete();
}

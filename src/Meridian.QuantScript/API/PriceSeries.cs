using Meridian.QuantScript.Plotting;

namespace Meridian.QuantScript.API;

/// <summary>
/// A time-indexed OHLCV price series with statistical and charting helpers.
/// </summary>
public sealed class PriceSeries
{
    private readonly IReadOnlyList<PriceBar> _bars;

    internal PriceSeries(string symbol, string interval, IReadOnlyList<PriceBar> bars)
    {
        Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        Interval = interval ?? throw new ArgumentNullException(nameof(interval));
        _bars = bars ?? throw new ArgumentNullException(nameof(bars));
    }

    /// <summary>The ticker symbol.</summary>
    public string Symbol { get; }

    /// <summary>The bar interval, e.g. "1d", "1h".</summary>
    public string Interval { get; }

    /// <summary>Number of bars in the series.</summary>
    public int Count => _bars.Count;

    /// <summary>Returns the underlying list of bars.</summary>
    public IReadOnlyList<PriceBar> ToList() => _bars;

    /// <summary>Computes simple (arithmetic) daily returns.</summary>
    public ReturnSeries DailyReturns() => ComputeReturns(ReturnKind.Simple);

    /// <summary>Computes logarithmic daily returns.</summary>
    public ReturnSeries LogReturns() => ComputeReturns(ReturnKind.Log);

    private ReturnSeries ComputeReturns(ReturnKind kind)
    {
        if (_bars.Count < 2)
            return new ReturnSeries(Array.Empty<ReturnPoint>());

        var result = new List<ReturnPoint>(_bars.Count - 1);
        for (var i = 1; i < _bars.Count; i++)
        {
            var prev = _bars[i - 1].Close;
            var curr = _bars[i].Close;
            if (prev == 0) continue;
            var ret = kind == ReturnKind.Log
                ? Math.Log(curr / prev)
                : (curr - prev) / prev;
            result.Add(new ReturnPoint(_bars[i].Timestamp, ret));
        }
        return new ReturnSeries(result);
    }

    /// <summary>
    /// Enqueues a closing-price line chart into the active <see cref="PlotQueue"/>.
    /// </summary>
    public PriceSeries Plot(string? title = null)
    {
        var points = _bars.Select(b => (b.Timestamp, b.Close)).ToList();
        PlotQueue.Current?.Enqueue(new PlotRequest(title ?? $"{Symbol} Price", PlotType.Line, points));
        return this;
    }

    /// <summary>
    /// Enqueues a cumulative return chart into the active <see cref="PlotQueue"/>.
    /// </summary>
    public PriceSeries PlotCumulative(string? title = null)
    {
        DailyReturns().PlotCumulative(title ?? $"{Symbol} Cumulative Returns");
        return this;
    }
}

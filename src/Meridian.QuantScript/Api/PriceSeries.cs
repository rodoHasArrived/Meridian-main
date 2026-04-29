namespace Meridian.QuantScript.Api;

/// <summary>
/// An ordered, immutable OHLCV price series for a single symbol.
/// Produced by <c>DataProxy.Prices</c> and consumed by returns/indicator extensions.
/// </summary>
public sealed class PriceSeries
{
    public string Symbol { get; }
    public IReadOnlyList<PriceBar> Bars { get; }
    public int Count => Bars.Count;
    public DateOnly From => Bars.Count > 0 ? Bars[0].Date : default;
    public DateOnly To => Bars.Count > 0 ? Bars[^1].Date : default;

    public PriceSeries(string symbol, IReadOnlyList<PriceBar> bars)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(bars);
        Symbol = symbol;
        Bars = bars;
    }
}

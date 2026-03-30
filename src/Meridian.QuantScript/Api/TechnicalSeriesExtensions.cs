using Skender.Stock.Indicators;

namespace Meridian.QuantScript.Api;

/// <summary>
/// Technical indicator extension methods on <see cref="PriceSeries"/>.
/// Delegates to Skender.Stock.Indicators where available; pure math otherwise.
/// </summary>
public static class TechnicalSeriesExtensions
{
    public static IReadOnlyList<(DateOnly Date, double? Sma)> Sma(this PriceSeries series, int period)
    {
        ArgumentNullException.ThrowIfNull(series);
        var quotes = ToQuotes(series);
        return [.. quotes.GetSma(period)
            .Select((r, i) => (series.Bars[i].Date, r.Sma))];
    }

    public static IReadOnlyList<(DateOnly Date, double? Ema)> Ema(this PriceSeries series, int period)
    {
        ArgumentNullException.ThrowIfNull(series);
        var quotes = ToQuotes(series);
        return [.. quotes.GetEma(period)
            .Select((r, i) => (series.Bars[i].Date, r.Ema))];
    }

    public static IReadOnlyList<(DateOnly Date, double? Rsi)> Rsi(this PriceSeries series, int period = 14)
    {
        ArgumentNullException.ThrowIfNull(series);
        var quotes = ToQuotes(series);
        return [.. quotes.GetRsi(period)
            .Select((r, i) => (series.Bars[i].Date, r.Rsi))];
    }

    public static IReadOnlyList<(DateOnly Date, double? Macd, double? Signal, double? Histogram)>
        Macd(this PriceSeries series, int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9)
    {
        ArgumentNullException.ThrowIfNull(series);
        var quotes = ToQuotes(series);
        return [.. quotes.GetMacd(fastPeriod, slowPeriod, signalPeriod)
            .Select((r, i) => (series.Bars[i].Date, r.Macd, r.Signal, r.Histogram))];
    }

    public static IReadOnlyList<(DateOnly Date, double? Upper, double? Mid, double? Lower)>
        BollingerBands(this PriceSeries series, int period = 20, double stdDevMultiplier = 2.0)
    {
        ArgumentNullException.ThrowIfNull(series);
        var quotes = ToQuotes(series);
        return [.. quotes.GetBollingerBands(period, stdDevMultiplier)
            .Select((r, i) => (series.Bars[i].Date, r.UpperBand, r.Sma, r.LowerBand))];
    }

    private static IEnumerable<Quote> ToQuotes(PriceSeries series) =>
        series.Bars.Select(b => new Quote
        {
            Date = b.Date.ToDateTime(TimeOnly.MinValue),
            Open = b.Open,
            High = b.High,
            Low = b.Low,
            Close = b.Close,
            Volume = b.Volume
        });
}

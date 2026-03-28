using Skender.Stock.Indicators;

namespace Meridian.QuantScript.API;

/// <summary>
/// Extension methods that add technical indicator calculations to <see cref="PriceSeries"/>.
/// Delegates to Skender.Stock.Indicators under the hood.
/// </summary>
public static class TechnicalSeriesExtensions
{
    private static IEnumerable<Quote> ToQuotes(this PriceSeries series) =>
        series.ToList().Select(b => new Quote
        {
            Date = b.Timestamp,
            Open = (decimal)b.Open,
            High = (decimal)b.High,
            Low = (decimal)b.Low,
            Close = (decimal)b.Close,
            Volume = b.Volume
        });

    /// <summary>Simple Moving Average.</summary>
    public static IReadOnlyList<double?> Sma(this PriceSeries series, int period)
    {
        return series.ToQuotes()
            .GetSma(period)
            .Select(r => r.Sma.HasValue ? (double?)r.Sma.Value : null)
            .ToList();
    }

    /// <summary>Exponential Moving Average.</summary>
    public static IReadOnlyList<double?> Ema(this PriceSeries series, int period)
    {
        return series.ToQuotes()
            .GetEma(period)
            .Select(r => r.Ema.HasValue ? (double?)r.Ema.Value : null)
            .ToList();
    }

    /// <summary>Relative Strength Index.</summary>
    public static IReadOnlyList<double?> Rsi(this PriceSeries series, int period = 14)
    {
        return series.ToQuotes()
            .GetRsi(period)
            .Select(r => r.Rsi.HasValue ? (double?)r.Rsi.Value : null)
            .ToList();
    }

    /// <summary>MACD result including MACD line, signal line, and histogram.</summary>
    public sealed record MacdResult(
        DateTime Date,
        double? Macd,
        double? Signal,
        double? Histogram);

    /// <summary>Moving Average Convergence Divergence.</summary>
    public static IReadOnlyList<MacdResult> Macd(
        this PriceSeries series,
        int fastPeriod = 12,
        int slowPeriod = 26,
        int signalPeriod = 9)
    {
        return series.ToQuotes()
            .GetMacd(fastPeriod, slowPeriod, signalPeriod)
            .Select(r => new MacdResult(
                r.Date,
                r.Macd.HasValue ? (double?)r.Macd.Value : null,
                r.Signal.HasValue ? (double?)r.Signal.Value : null,
                r.Histogram.HasValue ? (double?)r.Histogram.Value : null))
            .ToList();
    }

    /// <summary>Bollinger Bands result.</summary>
    public sealed record BollingerResult(
        DateTime Date,
        double? Upper,
        double? Middle,
        double? Lower);

    /// <summary>Bollinger Bands.</summary>
    public static IReadOnlyList<BollingerResult> BollingerBands(
        this PriceSeries series,
        int period = 20,
        double stdDevs = 2.0)
    {
        return series.ToQuotes()
            .GetBollingerBands(period, stdDevs)
            .Select(r => new BollingerResult(
                r.Date,
                r.UpperBand.HasValue ? (double?)r.UpperBand.Value : null,
                r.Sma.HasValue ? (double?)r.Sma.Value : null,
                r.LowerBand.HasValue ? (double?)r.LowerBand.Value : null))
            .ToList();
    }
}

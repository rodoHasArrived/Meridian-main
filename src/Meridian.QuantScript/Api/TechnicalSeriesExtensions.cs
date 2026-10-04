using Skender.Stock.Indicators;

namespace Meridian.QuantScript.Api;

/// <summary>
/// Technical indicator extension methods on <see cref="PriceSeries"/>.
/// Delegates to Skender.Stock.Indicators where available; pure math otherwise.
/// </summary>
/// <remarks>
/// Every result is dated from the indicator result's own <c>Date</c> rather than by zipping the
/// result sequence against <see cref="PriceSeries.Bars"/> positionally. Indicators differ in how
/// they treat warm-up periods, so a positional zip is only correct while every indicator happens to
/// emit one result per input bar — a property that is not guaranteed and would misdate an entire
/// series silently if it ever failed.
/// </remarks>
public static class TechnicalSeriesExtensions
{
    // ── Trend / moving averages ──────────────────────────────────────────────

    public static IReadOnlyList<(DateOnly Date, double? Sma)> Sma(this PriceSeries series, int period) =>
        [.. Quotes(series).GetSma(period).Select(static r => (Day(r.Date), r.Sma))];

    public static IReadOnlyList<(DateOnly Date, double? Ema)> Ema(this PriceSeries series, int period) =>
        [.. Quotes(series).GetEma(period).Select(static r => (Day(r.Date), r.Ema))];

    /// <summary>Average Directional Index with its +DI / -DI directional components.</summary>
    public static IReadOnlyList<(DateOnly Date, double? Adx, double? PlusDi, double? MinusDi)> Adx(
        this PriceSeries series,
        int period = 14) =>
        [.. Quotes(series).GetAdx(period).Select(static r => (Day(r.Date), r.Adx, r.Pdi, r.Mdi))];

    /// <summary>SuperTrend bands and the prevailing trend direction.</summary>
    public static IReadOnlyList<(DateOnly Date, double? SuperTrend, double? UpperBand, double? LowerBand)> SuperTrend(
        this PriceSeries series,
        int lookbackPeriods = 10,
        double multiplier = 3.0) =>
        [.. Quotes(series).GetSuperTrend(lookbackPeriods, multiplier)
            .Select(static r => (Day(r.Date), (double?)r.SuperTrend, (double?)r.UpperBand, (double?)r.LowerBand))];

    // ── Momentum / oscillators ───────────────────────────────────────────────

    public static IReadOnlyList<(DateOnly Date, double? Rsi)> Rsi(this PriceSeries series, int period = 14) =>
        [.. Quotes(series).GetRsi(period).Select(static r => (Day(r.Date), r.Rsi))];

    public static IReadOnlyList<(DateOnly Date, double? Macd, double? Signal, double? Histogram)> Macd(
        this PriceSeries series,
        int fastPeriod = 12,
        int slowPeriod = 26,
        int signalPeriod = 9) =>
        [.. Quotes(series).GetMacd(fastPeriod, slowPeriod, signalPeriod)
            .Select(static r => (Day(r.Date), r.Macd, r.Signal, r.Histogram))];

    /// <summary>Stochastic oscillator (%K) with its signal line (%D).</summary>
    public static IReadOnlyList<(DateOnly Date, double? Oscillator, double? Signal)> Stochastic(
        this PriceSeries series,
        int lookbackPeriods = 14,
        int signalPeriods = 3,
        int smoothPeriods = 3) =>
        [.. Quotes(series).GetStoch(lookbackPeriods, signalPeriods, smoothPeriods)
            .Select(static r => (Day(r.Date), r.Oscillator, r.Signal))];

    /// <summary>Commodity Channel Index.</summary>
    public static IReadOnlyList<(DateOnly Date, double? Cci)> Cci(this PriceSeries series, int period = 20) =>
        [.. Quotes(series).GetCci(period).Select(static r => (Day(r.Date), r.Cci))];

    /// <summary>Williams %R.</summary>
    public static IReadOnlyList<(DateOnly Date, double? WilliamsR)> WilliamsR(
        this PriceSeries series,
        int period = 14) =>
        [.. Quotes(series).GetWilliamsR(period).Select(static r => (Day(r.Date), r.WilliamsR))];

    /// <summary>Rate of change, expressed in percent.</summary>
    public static IReadOnlyList<(DateOnly Date, double? Roc)> Roc(this PriceSeries series, int period = 20) =>
        [.. Quotes(series).GetRoc(period).Select(static r => (Day(r.Date), r.Roc))];

    // ── Volatility / channels ────────────────────────────────────────────────

    public static IReadOnlyList<(DateOnly Date, double? Upper, double? Mid, double? Lower)> BollingerBands(
        this PriceSeries series,
        int period = 20,
        double stdDevMultiplier = 2.0) =>
        [.. Quotes(series).GetBollingerBands(period, stdDevMultiplier)
            .Select(static r => (Day(r.Date), r.UpperBand, r.Sma, r.LowerBand))];

    /// <summary>Average True Range, with the percent-of-price form alongside it.</summary>
    public static IReadOnlyList<(DateOnly Date, double? Atr, double? AtrPercent)> Atr(
        this PriceSeries series,
        int period = 14) =>
        [.. Quotes(series).GetAtr(period).Select(static r => (Day(r.Date), r.Atr, r.Atrp))];

    /// <summary>Donchian channel — the classic breakout envelope.</summary>
    public static IReadOnlyList<(DateOnly Date, double? Upper, double? Centerline, double? Lower)> Donchian(
        this PriceSeries series,
        int period = 20) =>
        [.. Quotes(series).GetDonchian(period)
            .Select(static r => (Day(r.Date), (double?)r.UpperBand, (double?)r.Centerline, (double?)r.LowerBand))];

    /// <summary>Keltner channel — an ATR-width envelope around an EMA.</summary>
    public static IReadOnlyList<(DateOnly Date, double? Upper, double? Centerline, double? Lower)> Keltner(
        this PriceSeries series,
        int emaPeriods = 20,
        double multiplier = 2.0,
        int atrPeriods = 10) =>
        [.. Quotes(series).GetKeltner(emaPeriods, multiplier, atrPeriods)
            .Select(static r => (Day(r.Date), r.UpperBand, r.Centerline, r.LowerBand))];

    // ── Volume ───────────────────────────────────────────────────────────────

    /// <summary>On-balance volume.</summary>
    public static IReadOnlyList<(DateOnly Date, double Obv)> Obv(this PriceSeries series) =>
        [.. Quotes(series).GetObv().Select(static r => (Day(r.Date), r.Obv))];

    /// <summary>
    /// Volume-weighted average price. VWAP is conventionally an intraday measure anchored to the
    /// session open; on a daily series it accumulates from the first bar of the series instead.
    /// </summary>
    public static IReadOnlyList<(DateOnly Date, double? Vwap)> Vwap(this PriceSeries series) =>
        [.. Quotes(series).GetVwap().Select(static r => (Day(r.Date), r.Vwap))];

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static DateOnly Day(DateTime date) => DateOnly.FromDateTime(date);

    private static IEnumerable<Quote> Quotes(PriceSeries series)
    {
        ArgumentNullException.ThrowIfNull(series);
        return series.Bars.Select(static b => new Quote
        {
            Date = b.Date.ToDateTime(TimeOnly.MinValue),
            Open = b.Open,
            High = b.High,
            Low = b.Low,
            Close = b.Close,
            Volume = b.Volume
        });
    }
}

using Meridian.QuantScript.Tests.Helpers;

namespace Meridian.QuantScript.Tests;

/// <summary>
/// Covers the technical-indicator surface exposed to scripts.
/// </summary>
/// <remarks>
/// The alignment assertions matter more than the value assertions. Indicators differ in how they
/// treat warm-up periods, so results are dated from each indicator result rather than zipped
/// positionally against the input bars. A regression there would not throw — it would silently
/// shift an entire series by one or more days, which is exactly the kind of error a backtest
/// turns into a plausible-looking edge.
/// </remarks>
public sealed class TechnicalSeriesExtensionsTests
{
    private static PriceSeries Series(int bars = 120) => TestPriceSeriesBuilder.Build("SPY", bars);

    // ── Date alignment ───────────────────────────────────────────────────────

    [Fact]
    public void EveryIndicator_DatesMatchTheInputBarsInOrder()
    {
        var series = Series();
        var expected = series.Bars.Select(b => b.Date).ToList();

        var actual = new Dictionary<string, List<DateOnly>>
        {
            ["Sma"] = [.. series.Sma(10).Select(r => r.Date)],
            ["Ema"] = [.. series.Ema(10).Select(r => r.Date)],
            ["Rsi"] = [.. series.Rsi().Select(r => r.Date)],
            ["Macd"] = [.. series.Macd().Select(r => r.Date)],
            ["BollingerBands"] = [.. series.BollingerBands().Select(r => r.Date)],
            ["Adx"] = [.. series.Adx().Select(r => r.Date)],
            ["SuperTrend"] = [.. series.SuperTrend().Select(r => r.Date)],
            ["Stochastic"] = [.. series.Stochastic().Select(r => r.Date)],
            ["Cci"] = [.. series.Cci().Select(r => r.Date)],
            ["WilliamsR"] = [.. series.WilliamsR().Select(r => r.Date)],
            ["Roc"] = [.. series.Roc().Select(r => r.Date)],
            ["Atr"] = [.. series.Atr().Select(r => r.Date)],
            ["Donchian"] = [.. series.Donchian().Select(r => r.Date)],
            ["Keltner"] = [.. series.Keltner().Select(r => r.Date)],
            ["Obv"] = [.. series.Obv().Select(r => r.Date)],
            ["Vwap"] = [.. series.Vwap().Select(r => r.Date)]
        };

        foreach (var (indicator, dates) in actual)
        {
            dates.Should().Equal(expected, $"{indicator} must report each value against its own bar date");
        }
    }

    // ── Warm-up behaviour ────────────────────────────────────────────────────

    [Fact]
    public void Sma_IsNullThroughWarmupThenPopulated()
    {
        var result = Series().Sma(10);

        result.Take(9).Should().OnlyContain(r => r.Sma == null);
        result.Skip(9).Should().OnlyContain(r => r.Sma != null);
    }

    [Fact]
    public void Atr_IsPopulatedAfterWarmup()
    {
        Series().Atr(14).Skip(20).Should().OnlyContain(r => r.Atr != null);
    }

    [Fact]
    public void Adx_IsPopulatedAfterWarmup()
    {
        // ADX needs roughly twice its period before it produces a value.
        Series().Adx(14).Skip(40).Should().OnlyContain(r => r.Adx != null);
    }

    // ── Value domains ────────────────────────────────────────────────────────

    [Fact]
    public void Rsi_StaysWithinItsBoundedRange()
    {
        Series().Rsi().Where(r => r.Rsi.HasValue)
            .Should().OnlyContain(r => r.Rsi >= 0 && r.Rsi <= 100);
    }

    [Fact]
    public void Stochastic_StaysWithinItsBoundedRange()
    {
        Series().Stochastic().Where(r => r.Oscillator.HasValue)
            .Should().OnlyContain(r => r.Oscillator >= 0 && r.Oscillator <= 100);
    }

    [Fact]
    public void WilliamsR_StaysWithinItsBoundedRange()
    {
        Series().WilliamsR().Where(r => r.WilliamsR.HasValue)
            .Should().OnlyContain(r => r.WilliamsR >= -100 && r.WilliamsR <= 0);
    }

    [Fact]
    public void Adx_StaysWithinItsBoundedRange()
    {
        Series().Adx().Where(r => r.Adx.HasValue)
            .Should().OnlyContain(r => r.Adx >= 0 && r.Adx <= 100);
    }

    // ── Channel ordering ─────────────────────────────────────────────────────

    [Fact]
    public void Donchian_UpperIsNeverBelowLower()
    {
        Series().Donchian().Where(r => r.Upper.HasValue && r.Lower.HasValue)
            .Should().OnlyContain(r => r.Upper >= r.Lower);
    }

    [Fact]
    public void Keltner_UpperIsNeverBelowLower()
    {
        Series().Keltner().Where(r => r.Upper.HasValue && r.Lower.HasValue)
            .Should().OnlyContain(r => r.Upper >= r.Lower);
    }

    [Fact]
    public void BollingerBands_UpperIsNeverBelowLower()
    {
        Series().BollingerBands().Where(r => r.Upper.HasValue && r.Lower.HasValue)
            .Should().OnlyContain(r => r.Upper >= r.Lower);
    }

    [Fact]
    public void Donchian_BandsSummariseThePriorWindowExcludingTheCurrentBar()
    {
        // The channel is built from the N bars *before* each row, not including it. That exclusion
        // is the whole point of the indicator: a bar trading outside the band is the breakout
        // signal, so an implementation that folded the current bar in could never report one.
        const int Period = 20;
        var series = Series();
        var bars = series.Bars;
        var indexByDate = bars.Select((b, i) => (b.Date, i)).ToDictionary(x => x.Date, x => x.i);

        var checkedRows = 0;
        foreach (var row in series.Donchian(Period).Where(r => r.Upper.HasValue && r.Lower.HasValue))
        {
            var index = indexByDate[row.Date];
            if (index < Period)
            {
                continue;
            }

            var window = bars.Skip(index - Period).Take(Period).ToList();
            row.Upper!.Value.Should().BeApproximately((double)window.Max(b => b.High), 1e-6);
            row.Lower!.Value.Should().BeApproximately((double)window.Min(b => b.Low), 1e-6);
            checkedRows++;
        }

        checkedRows.Should().BeGreaterThan(0, "the fixture must be long enough to exercise the channel");
    }

    // ── Guards ───────────────────────────────────────────────────────────────

    [Fact]
    public void Indicators_RejectNullSeries()
    {
        var act = () => ((PriceSeries)null!).Rsi();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Obv_ReturnsOneValuePerBar()
    {
        var series = Series();

        series.Obv().Should().HaveCount(series.Bars.Count);
    }
}

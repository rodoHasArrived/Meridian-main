using Meridian.QuantScript.Tests.Helpers;

namespace Meridian.QuantScript.Tests;

public sealed class PriceSeriesTests
{
    private static PriceSeries BuildSeries(int count, decimal startPrice = 100m)
    {
        var bars = Enumerable.Range(0, count)
            .Select(i => new PriceBar(
                DateOnly.FromDateTime(DateTime.Today.AddDays(i)),
                startPrice + i,
                startPrice + i + 2,
                startPrice + i - 1,
                startPrice + i + 1,
                1_000_000L))
            .ToList();
        return new PriceSeries("TEST", bars);
    }

    [Fact]
    public void Constructor_SetsProperties()
    {
        var series = BuildSeries(5);
        series.Symbol.Should().Be("TEST");
        series.Count.Should().Be(5);
        series.From.Should().Be(series.Bars[0].Date);
        series.To.Should().Be(series.Bars[4].Date);
    }

    [Fact]
    public void Constructor_ThrowsOnEmptySymbol()
    {
        var act = () => new PriceSeries("", []);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DailyReturns_Returns_N_Minus_One_Points()
    {
        var series = TestPriceSeriesBuilder.Build(barCount: 10);
        var returns = series.DailyReturns();
        returns.Count.Should().Be(9);
    }

    [Fact]
    public void DailyReturns_ReturnsOneLessThanBars()
    {
        var series = BuildSeries(10);
        var returns = series.DailyReturns();
        returns.Count.Should().Be(9);
        returns.Kind.Should().Be(ReturnKind.Arithmetic);
    }

    [Fact]
    public void DailyReturns_Values_Are_Positive_For_Ascending_Prices()
    {
        var series = TestPriceSeriesBuilder.Build(barCount: 10);
        var returns = series.DailyReturns().Points.ToList();
        returns.Should().OnlyContain(r => r.Value > 0);
    }

    [Fact]
    public void DailyReturns_EmptySeriesReturnsEmpty()
    {
        var series = BuildSeries(1);
        series.DailyReturns().Count.Should().Be(0);
    }

    [Fact]
    public void DailyReturns_Empty_For_Single_Bar_Series()
    {
        var series = TestPriceSeriesBuilder.Build(barCount: 1);
        series.DailyReturns().Count.Should().Be(0);
    }

    [Fact]
    public void LogReturns_Returns_N_Minus_One_Points()
    {
        var series = TestPriceSeriesBuilder.Build(barCount: 5);
        var returns = series.LogReturns();
        returns.Count.Should().Be(4);
    }

    [Fact]
    public void LogReturns_ProducesNegativeForDecline()
    {
        var bars = new[]
        {
            new PriceBar(new DateOnly(2024,1,1), 100, 102, 99, 100, 1000),
            new PriceBar(new DateOnly(2024,1,2), 100,  98, 95,  95, 1000),
        };
        var series = new PriceSeries("X", bars);
        var returns = series.LogReturns();
        returns.Points[0].Value.Should().BeLessThan(0);
    }

    [Fact]
    public void CumulativeReturns_IsMonotonicallyIncreasing_WhenAllPositive()
    {
        var bars = Enumerable.Range(0, 20)
            .Select(i => new PriceBar(
                DateOnly.FromDateTime(DateTime.Today.AddDays(i)),
                100m + i, 100m + i + 2, 100m + i - 1, 100m + i + 1, 1_000_000L))
            .ToList();
        var series = new PriceSeries("UP", bars);
        var cum = series.CumulativeReturns();
        for (var i = 1; i < cum.Points.Count; i++)
            cum.Points[i].Value.Should().BeGreaterThan(cum.Points[i - 1].Value);
    }

    [Fact]
    public void PlotCumulative_EnqueuesIntoPlotQueue()
    {
        var series = TestPriceSeriesBuilder.Build(barCount: 5);
        var queue = new PlotQueue();
        PlotQueue.Current = queue;
        try
        {
            series.CumulativeReturns().PlotCumulative("Test Cumulative");
            var plots = queue.DrainRemaining();
            plots.Should().NotBeEmpty();
            plots[0].Title.Should().Be("Test Cumulative");
        }
        finally
        {
            PlotQueue.Current = null;
        }
    }

    [Fact]
    public void RollingReturns_WindowOne_EqualsDailyReturns()
    {
        var series = BuildSeries(10);
        var rolling = series.RollingReturns(1);
        var daily = series.DailyReturns();
        rolling.Count.Should().Be(daily.Count);
    }

    [Fact]
    public void RollingReturns_ThrowsOnZeroWindow()
    {
        var series = BuildSeries(5);
        var act = () => series.RollingReturns(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void LogReturns_And_SimpleReturns_Differ_For_Same_Series()
    {
        var series = TestPriceSeriesBuilder.Build(barCount: 10);
        var simple = series.DailyReturns().Points.ToList();
        var log = series.LogReturns().Points.ToList();

        for (var i = 0; i < simple.Count; i++)
            simple[i].Value.Should().NotBeApproximately(log[i].Value, 1e-12);
    }
}

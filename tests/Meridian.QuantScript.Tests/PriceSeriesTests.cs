namespace Meridian.QuantScript.Tests;

public sealed class PriceSeriesTests
{
    [Fact]
    public void DailyReturns_Returns_N_Minus_One_Points()
    {
        var series = TestPriceSeriesBuilder.Build(barCount: 10);
        var returns = series.DailyReturns();
        returns.Count.Should().Be(9);
    }

    [Fact]
    public void DailyReturns_Values_Are_Positive_For_Ascending_Prices()
    {
        var series = TestPriceSeriesBuilder.Build(barCount: 10);
        var returns = series.DailyReturns().ToList();
        returns.Should().OnlyContain(r => r.Value > 0);
    }

    [Fact]
    public void LogReturns_Returns_N_Minus_One_Points()
    {
        var series = TestPriceSeriesBuilder.Build(barCount: 5);
        var returns = series.LogReturns();
        returns.Count.Should().Be(4);
    }

    [Fact]
    public void LogReturns_And_SimpleReturns_Differ_For_Same_Series()
    {
        var series = TestPriceSeriesBuilder.Build(barCount: 10);
        var simple = series.DailyReturns().ToList();
        var log = series.LogReturns().ToList();

        for (var i = 0; i < simple.Count; i++)
            simple[i].Value.Should().NotBeApproximately(log[i].Value, 1e-12);
    }

    [Fact]
    public void DailyReturns_Empty_For_Single_Bar_Series()
    {
        var series = TestPriceSeriesBuilder.Build(barCount: 1);
        series.DailyReturns().Count.Should().Be(0);
    }

    [Fact]
    public void PlotCumulative_EnqueuesIntoPlotQueue()
    {
        var series = TestPriceSeriesBuilder.Build(barCount: 5);
        var queue = new PlotQueue();
        PlotQueue.Current = queue;
        try
        {
            series.PlotCumulative("Test Cumulative");
            queue.TryRead(out var req).Should().BeTrue();
            req!.Title.Should().Be("Test Cumulative");
        }
        finally
        {
            PlotQueue.Current = null;
        }
    }
}

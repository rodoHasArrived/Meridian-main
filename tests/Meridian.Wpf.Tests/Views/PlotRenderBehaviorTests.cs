using System.IO;
using ScottPlot.WPF;
using Meridian.QuantScript.Api;
using Meridian.QuantScript.Plotting;
using Meridian.Wpf.Behaviors;
using Meridian.Wpf.Tests.Support;

namespace Meridian.Wpf.Tests.Views;

public sealed class PlotRenderBehaviorTests
{
    [Fact]
    public void CandlestickRequest_RendersCandlestickPlottable_AndDateAxisTicks()
    {
        WpfTestThread.Run(() =>
        {
            var request = new PlotRequest(
                Title: "Candles",
                Type: PlotType.Candlestick,
                Candlestick:
                [
                    new PriceBar(new DateOnly(2025, 1, 2), 100m, 105m, 99m, 104m, 1000),
                    new PriceBar(new DateOnly(2025, 1, 3), 104m, 106m, 102m, 103m, 1100)
                ]);

            var plot = new WpfPlot();
            PlotRenderBehavior.SetRequest(plot, request);

            plot.Plot.GetPlottables().Select(p => p.GetType().Name)
                .Should().Contain(typeName => typeName.Contains("Candlestick", StringComparison.Ordinal));

            plot.Plot.Axes.Bottom.TickGenerator.GetType().Name
                .Should().Contain("DateTime", StringComparison.Ordinal);
        });
    }

    [Fact]
    public void HeatmapRequest_WithLabels_UsesManualAxisTicks()
    {
        WpfTestThread.Run(() =>
        {
            var request = new PlotRequest(
                Title: "Correlation",
                Type: PlotType.Heatmap,
                HeatmapData:
                [
                    [1.0, 0.4],
                    [0.4, 1.0]
                ],
                HeatmapLabels: ["AAPL", "MSFT"]);

            var plot = new WpfPlot();
            PlotRenderBehavior.SetRequest(plot, request);

            plot.Plot.Axes.Bottom.TickGenerator.GetType().Name
                .Should().Contain("NumericManual", StringComparison.Ordinal);

            plot.Plot.Axes.Left.TickGenerator.GetType().Name
                .Should().Contain("NumericManual", StringComparison.Ordinal);
        });
    }

    [Fact]
    public void HeatmapRequest_WithIrregularMatrix_WritesDiagnostic_AndSkipsHeatmap()
    {
        WpfTestThread.Run(() =>
        {
            var request = new PlotRequest(
                Title: "BadMatrix",
                Type: PlotType.Heatmap,
                HeatmapData:
                [
                    [1.0, 0.5],
                    [0.8]
                ]);

            var plot = new WpfPlot();
            var originalOut = Console.Out;
            using var writer = new StringWriter();

            try
            {
                Console.SetOut(writer);
                PlotRenderBehavior.SetRequest(plot, request);
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            writer.ToString().Should().Contain("Invalid plot request", StringComparison.Ordinal);
            plot.Plot.GetPlottables().Should().BeEmpty();
        });
    }
}

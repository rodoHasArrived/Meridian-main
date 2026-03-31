using System.Windows;
using Meridian.QuantScript.Plotting;
using Meridian.Ui.Services.Services;
using ScottPlot;
using ScottPlot.WPF;
using Palette = Meridian.Ui.Services.Services.ColorPalette;

namespace Meridian.Wpf.Behaviors;

/// <summary>
/// Attached property that renders a <see cref="PlotRequest"/> into a <see cref="WpfPlot"/> control.
/// Sourced colors from <see cref="ColorPalette"/> so charts stay in sync with the application theme.
/// </summary>
public static class PlotRenderBehavior
{
    private static readonly ColorPalette.ArgbColor[] SeriesPalette =
    [
        Palette.ChartPrimary,
        Palette.ChartSecondary,
        Palette.ChartTertiary,
        Palette.ChartPositive,
        Palette.ChartNegative
    ];

    public static readonly DependencyProperty RequestProperty =
        DependencyProperty.RegisterAttached(
            "Request",
            typeof(PlotRequest),
            typeof(PlotRenderBehavior),
            new PropertyMetadata(null, OnRequestChanged));

    public static PlotRequest? GetRequest(DependencyObject obj)
        => (PlotRequest?)obj.GetValue(RequestProperty);

    public static void SetRequest(DependencyObject obj, PlotRequest? value)
        => obj.SetValue(RequestProperty, value);

    private static void OnRequestChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not WpfPlot wpfPlot || e.NewValue is not PlotRequest request)
            return;

        RenderPlot(wpfPlot, request);
    }

    private static void RenderPlot(WpfPlot wpfPlot, PlotRequest request)
    {
        var plot = wpfPlot.Plot;
        plot.Clear();

        // Apply theme colours
        var bg = Palette.CardBackground;
        var dataBg = Palette.SubtleBackground;
        plot.FigureBackground.Color = ToScottPlot(bg);
        plot.DataBackground.Color = ToScottPlot(dataBg);
        plot.Axes.Color(ToScottPlot(Palette.MutedText));

        if (request.Series is not { Count: > 0 } && request.MultiSeries is not { Count: > 0 } && request.HeatmapData is null)
        {
            wpfPlot.Refresh();
            return;
        }
        var seriesColor = ToScottPlot(SeriesPalette[0]);

        switch (request.Type)
        {
            case PlotType.Line:
            case PlotType.CumulativeReturn:
            case PlotType.Drawdown:
            case PlotType.Scatter:
            case PlotType.Bar:
            case PlotType.Histogram:
                if (request.Series is { Count: > 0 } series)
                {
                    var xs = series.Select(p => p.Date.ToDateTime(TimeOnly.MinValue).ToOADate()).ToArray();
                    var ys = series.Select(p => p.Value).ToArray();
                    var scatter = plot.Add.Scatter(xs, ys);
                    scatter.Color = seriesColor;
                    scatter.LineWidth = 1.5f;
                    scatter.MarkerSize = request.Type == PlotType.Scatter ? 5 : 0;
                }
                break;

            case PlotType.MultiLine:
                if (request.MultiSeries is { Count: > 0 } multiSeries)
                {
                    var paletteIndex = 0;
                    foreach (var (label, values) in multiSeries)
                    {
                        var xs = values.Select(p => p.Date.ToDateTime(TimeOnly.MinValue).ToOADate()).ToArray();
                        var ys = values.Select(p => p.Value).ToArray();
                        var scatter = plot.Add.Scatter(xs, ys);
                        scatter.Color = ToScottPlot(SeriesPalette[paletteIndex % SeriesPalette.Length]);
                        scatter.LegendText = label;
                        scatter.MarkerSize = 0;
                        paletteIndex++;
                    }

                    plot.ShowLegend();
                }
                break;

            case PlotType.Heatmap:
                if (request.HeatmapData is { Length: > 0 } heatmapData)
                {
                    var rowCount = heatmapData.Length;
                    var columnCount = heatmapData[0].Length;
                    var positions = Enumerable.Range(0, Math.Min(rowCount, columnCount)).Select(static i => (double)i).ToArray();
                    var values = positions.Select(i => heatmapData[(int)i][(int)i]).ToArray();
                    plot.Add.Bars(positions, values);
                }
                break;
        }

        plot.Axes.DateTimeTicksBottom();

        wpfPlot.Refresh();
    }

    private static ScottPlot.Color ToScottPlot(ColorPalette.ArgbColor c)
        => new(c.R, c.G, c.B, c.A);
}

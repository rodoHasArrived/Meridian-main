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

        if (request.Points.Count == 0)
        {
            wpfPlot.Refresh();
            return;
        }

        var xs = request.Points.Select(p => p.X.ToOADate()).ToArray();
        var ys = request.Points.Select(p => p.Y).ToArray();
        var seriesColor = ToScottPlot(SeriesPalette[0]);

        switch (request.Type)
        {
            case PlotType.Scatter:
                var scatter = plot.Add.Scatter(xs, ys);
                scatter.Color = seriesColor;
                break;

            case PlotType.Bar:
                var bars = new List<Bar>();
                for (var i = 0; i < xs.Length; i++)
                    bars.Add(new Bar { Position = xs[i], Value = ys[i], FillColor = seriesColor });
                plot.Add.Bars(bars);
                break;

            case PlotType.Histogram:
                var hist = plot.Add.Bars(
                    xs.Zip(ys, (x, y) => new Bar { Position = x, Value = y, FillColor = seriesColor }).ToArray());
                break;

            default: // Line
                var line = plot.Add.Scatter(xs, ys);
                line.Color = seriesColor;
                line.LineWidth = 1.5f;
                line.MarkerSize = 0;
                break;
        }

        plot.Axes.DateTimeTicksBottom();

        wpfPlot.Refresh();
    }

    private static ScottPlot.Color ToScottPlot(ColorPalette.ArgbColor c)
        => ScottPlot.Color.FromARGB(c.A, c.R, c.G, c.B);
}

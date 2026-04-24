using System.Windows;
using Meridian.QuantScript.Plotting;
using Meridian.Ui.Services.Services;
using ScottPlot;
using ScottPlot.WPF;
using TickGenerators = ScottPlot.TickGenerators;
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

        // Match the Meridian chart previews from the extracted design-system bundle.
        var bg = Palette.ChartBackground;
        var dataBg = Palette.ChartDataBackground;
        plot.FigureBackground.Color = ToScottPlot(bg);
        plot.DataBackground.Color = ToScottPlot(dataBg);
        plot.Axes.Color(ToScottPlot(Palette.ChartAxis));
        plot.Grid.MajorLineColor = ToScottPlot(Palette.ChartGrid);

        var usesDateAxis =
            request.Type is PlotType.Line
            or PlotType.CumulativeReturn
            or PlotType.Drawdown
            or PlotType.Scatter
            or PlotType.Bar
            or PlotType.Histogram
            or PlotType.MultiLine
            or PlotType.Candlestick;

        if (request.Series is not { Count: > 0 } &&
            request.MultiSeries is not { Count: > 0 } &&
            request.HeatmapData is null)
        {
            wpfPlot.Refresh();
            return;
        }

        var seriesColor = ResolveSeriesColor(request.Type);

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
                    scatter.LineWidth = 2f;
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
                        scatter.LineWidth = 1.5f;
                        scatter.MarkerSize = 0;
                        paletteIndex++;
                    }

                    plot.ShowLegend();
                }
                break;

            case PlotType.Heatmap:
                if (request.HeatmapData is { Length: > 0 } heatmapData)
                {
                    if (!TryConvertHeatmapMatrix(heatmapData, out var matrix, out var heatmapDiagnostic))
                    {
                        WriteInvalidStructureDiagnostic(request, heatmapDiagnostic);
                        break;
                    }

                    plot.Add.Heatmap(matrix);
                    ApplyHeatmapLabels(plot, matrix.GetLength(0), matrix.GetLength(1), request.HeatmapLabels, request);
                }
                break;

            case PlotType.Candlestick:
                if (request.Candlestick is { Count: > 0 } candlestick)
                {
                    var ohlcs = candlestick
                        .Select(bar => new OHLC(
                            (double)bar.Open,
                            (double)bar.High,
                            (double)bar.Low,
                            (double)bar.Close,
                            bar.Date.ToDateTime(TimeOnly.MinValue),
                            TimeSpan.FromDays(1)))
                        .ToArray();

                    plot.Add.Candlestick(ohlcs);
                }
                break;
        }

        if (usesDateAxis)
            plot.Axes.DateTimeTicksBottom();

        wpfPlot.Refresh();
    }

    private static ScottPlot.Color ToScottPlot(ColorPalette.ArgbColor c)
        => new(c.R, c.G, c.B, c.A);

    private static ScottPlot.Color ResolveSeriesColor(PlotType type)
        => type switch
        {
            PlotType.CumulativeReturn => ToScottPlot(Palette.ChartPositive),
            PlotType.Drawdown => ToScottPlot(Palette.ChartNegative),
            _ => ToScottPlot(SeriesPalette[0])
        };

    private static bool TryConvertHeatmapMatrix(
        double[][] heatmapData,
        out double[,] matrix,
        out string diagnostic)
    {
        matrix = new double[0, 0];
        diagnostic = string.Empty;

        if (heatmapData.Length == 0)
        {
            diagnostic = "HeatmapData must contain at least one row.";
            return false;
        }

        if (heatmapData[0] is not { Length: > 0 })
        {
            diagnostic = "HeatmapData first row must contain at least one value.";
            return false;
        }

        var rowCount = heatmapData.Length;
        var columnCount = heatmapData[0].Length;

        for (var row = 0; row < rowCount; row++)
        {
            if (heatmapData[row] is not { Length: > 0 })
            {
                diagnostic = $"HeatmapData row {row} is null or empty.";
                return false;
            }

            if (heatmapData[row].Length != columnCount)
            {
                diagnostic =
                    $"HeatmapData row {row} has {heatmapData[row].Length} columns; expected {columnCount}.";
                return false;
            }
        }

        matrix = new double[rowCount, columnCount];
        for (var row = 0; row < rowCount; row++)
        {
            for (var column = 0; column < columnCount; column++)
                matrix[row, column] = heatmapData[row][column];
        }

        return true;
    }

    private static void ApplyHeatmapLabels(
        Plot plot,
        int rowCount,
        int columnCount,
        string[]? heatmapLabels,
        PlotRequest request)
    {
        if (heatmapLabels is not { Length: > 0 })
            return;

        var xCount = Math.Min(columnCount, heatmapLabels.Length);
        var yCount = Math.Min(rowCount, heatmapLabels.Length);

        if (xCount != columnCount || yCount != rowCount)
        {
            WriteInvalidStructureDiagnostic(
                request,
                $"HeatmapLabels count ({heatmapLabels.Length}) does not match matrix dimensions ({rowCount}x{columnCount}). Labels were truncated.");
        }

        var xPositions = Enumerable.Range(0, xCount).Select(static i => (double)i).ToArray();
        var yPositions = Enumerable.Range(0, yCount).Select(static i => (double)i).ToArray();
        var xLabels = heatmapLabels.Take(xCount).ToArray();
        var yLabels = heatmapLabels.Take(yCount).ToArray();

        plot.Axes.Bottom.TickGenerator = new TickGenerators.NumericManual(xPositions, xLabels);
        plot.Axes.Left.TickGenerator = new TickGenerators.NumericManual(yPositions, yLabels);
    }

    private static void WriteInvalidStructureDiagnostic(PlotRequest request, string reason)
    {
        Console.WriteLine($"[PlotRenderBehavior] Invalid plot request for '{request.Title}' ({request.Type}): {reason}");
    }
}

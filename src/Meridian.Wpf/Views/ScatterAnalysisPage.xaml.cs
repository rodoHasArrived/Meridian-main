using System;
using System.Windows;
using ScottPlot;
using Meridian.Wpf.ViewModels;
using Palette = Meridian.Ui.Services.Services.ColorPalette;

namespace Meridian.Wpf.Views;

/// <summary>
/// Code-behind for the Scatter Analysis page.
/// Keeps business logic in <see cref="ScatterAnalysisViewModel"/>; this file handles:
///   • ScottPlot chart rendering (UI-specific, not MVVM-bindable)
///   • Tab panel visibility switching
///   • Time-range RadioButton selection → ViewModel propagation
/// </summary>
public partial class ScatterAnalysisPage
{
    // ScottPlot theme colors (reused across renders).
    private static readonly ScottPlot.Color BgColour = ToScottPlot(Palette.ChartBackground);
    private static readonly ScottPlot.Color DataBgColour = ToScottPlot(Palette.ChartDataBackground);
    private static readonly ScottPlot.Color AxisColour = ToScottPlot(Palette.ChartAxis);
    private static readonly ScottPlot.Color GridColour = ToScottPlot(Palette.ChartGrid);
    private static readonly ScottPlot.Color HistoryColour = ToScottPlot(Palette.ChartSecondary, 180);
    private static readonly ScottPlot.Color CurrentColour = ToScottPlot(Palette.ChartNegative);
    private static readonly ScottPlot.Color RegrColour = ToScottPlot(Palette.ChartPrimary, 220);

    private readonly ScatterAnalysisViewModel _vm;

    private static ScottPlot.Color ToScottPlot(Palette.ArgbColor color, byte? alpha = null)
        => new(color.R, color.G, color.B, alpha ?? color.A);

    public ScatterAnalysisPage(ScatterAnalysisViewModel viewModel)
    {
        _vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _vm;

        Loaded   += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        // Re-subscribe safely: detach first to prevent duplicates if the page is
        // navigated away from and back without a full reconstruction.
        _vm.ChartDataReady -= OnChartDataReady;
        _vm.ChartDataReady += OnChartDataReady;

        ApplyPlotTheme();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _vm.ChartDataReady -= OnChartDataReady;
    }

    // ── Chart rendering ───────────────────────────────────────────────────────

    private void ApplyPlotTheme()
    {
        var plot = ScatterPlot.Plot;
        plot.FigureBackground.Color = BgColour;
        plot.DataBackground.Color   = DataBgColour;
        plot.Axes.Color(AxisColour);
        plot.Grid.MajorLineColor    = GridColour;
        ScatterPlot.Refresh();
    }

    private void OnChartDataReady(object? sender, EventArgs e)
    {
        // Marshal to the UI thread without blocking the caller.
        if (Dispatcher.CheckAccess())
            RenderChart();
        else
            Dispatcher.BeginInvoke(RenderChart);
    }

    private void RenderChart()
    {
        var plot = ScatterPlot.Plot;
        plot.Clear();

        // Restore theme (Clear() resets styling)
        plot.FigureBackground.Color = BgColour;
        plot.DataBackground.Color   = DataBgColour;
        plot.Axes.Color(AxisColour);
        plot.Grid.MajorLineColor    = GridColour;

        // ── History scatter ───────────────────────────────────────────────────
        var hist = _vm.HistoryPoints;
        if (hist.Count > 0)
        {
            var hxs = new double[hist.Count];
            var hys = new double[hist.Count];
            for (var i = 0; i < hist.Count; i++) { hxs[i] = hist[i].X; hys[i] = hist[i].Y; }

            var historySeries       = plot.Add.Scatter(hxs, hys);
            historySeries.Color      = HistoryColour;
            historySeries.MarkerSize = 4;
            historySeries.LineWidth  = 0;
            historySeries.LegendText = "History";
        }

        // ── Current point ─────────────────────────────────────────────────────
        var cur = _vm.CurrentPoint;
        if (cur != null)
        {
            var curSeries       = plot.Add.Scatter(new[] { cur.X }, new[] { cur.Y });
            curSeries.Color      = CurrentColour;
            curSeries.MarkerSize = 9;
            curSeries.LineWidth  = 0;
            curSeries.LegendText = "Current";
        }

        // ── Regression line ───────────────────────────────────────────────────
        var reg = _vm.RegressionLine;
        if (reg.HasValue)
        {
            var line         = plot.Add.Line(reg.Value.X1, reg.Value.Y1, reg.Value.X2, reg.Value.Y2);
            line.Color       = RegrColour;
            line.LineWidth   = 1.5f;
            line.LinePattern = LinePattern.DenselyDashed;
        }

        // ── Axis labels ───────────────────────────────────────────────────────
        plot.XLabel(_vm.XSymbol);
        plot.YLabel(_vm.YSymbol);

        if (hist.Count > 0 && cur != null)
            plot.ShowLegend();

        ScatterPlot.Refresh();
    }

    // ── Tab switching ─────────────────────────────────────────────────────────

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Primitives.ToggleButton button) return;

        var tag = button.Tag?.ToString();
        PanelExpressionEditor.Visibility = tag == "0" ? Visibility.Visible : Visibility.Collapsed;
        PanelDataSheet.Visibility        = tag == "1" ? Visibility.Visible : Visibility.Collapsed;
        PanelStatistics.Visibility       = tag == "2" ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Time-range RadioButton ─────────────────────────────────────────────────

    private void TimeRange_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.RadioButton rb && rb.Tag is string tag)
            _vm.SelectedTimeRange = tag;
    }
}


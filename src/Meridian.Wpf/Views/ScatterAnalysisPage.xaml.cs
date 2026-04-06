using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using ScottPlot;
using ScottPlot.WPF;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Code-behind for the Scatter Analysis page.
/// Keeps business logic in <see cref="ScatterAnalysisViewModel"/>; this file handles:
///   • DI wiring of the ViewModel
///   • ScottPlot chart rendering (UI-specific, not MVVM-bindable)
///   • Tab panel visibility switching via RadioButton.Checked
///   • Time-range toggle-button mutual exclusion
/// </summary>
public partial class ScatterAnalysisPage
{
    // ── ScottPlot theme colours (reused across renders) ───────────────────────
    private static readonly ScottPlot.Color BgColour     = new(16,  26,  40,  255);
    private static readonly ScottPlot.Color DataBgColour = new(11,  20,  34,  255);
    private static readonly ScottPlot.Color AxisColour   = new(130, 145, 165, 255);
    private static readonly ScottPlot.Color GridColour   = new(32,  48,  68,  255);
    private static readonly ScottPlot.Color HistoryColour= new(76, 141, 255, 180);   // semi-transparent blue
    private static readonly ScottPlot.Color CurrentColour= new(245,  80,  80, 255);  // red-orange highlight
    private static readonly ScottPlot.Color RegrColour   = new(120, 190, 255, 220);  // light blue line

    private ScatterAnalysisViewModel? _vm;

    public ScatterAnalysisPage()
    {
        InitializeComponent();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        // Resolve ViewModel from DI or create directly
        _vm = DataContext as ScatterAnalysisViewModel
              ?? new ScatterAnalysisViewModel(Meridian.Ui.Services.BackfillService.Instance);

        DataContext = _vm;
        _vm.ChartDataReady += OnChartDataReady;

        ApplyPlotTheme();
    }

    // ── Chart rendering ───────────────────────────────────────────────────────

    private void ApplyPlotTheme()
    {
        var plot = ScatterPlot.Plot;
        plot.FigureBackground.Color = BgColour;
        plot.DataBackground.Color   = DataBgColour;
        plot.Axes.Color(AxisColour);
        plot.Grid.MajorLineColor = GridColour;
        ScatterPlot.Refresh();
    }

    private void OnChartDataReady(object? sender, EventArgs e)
    {
        // ScottPlot must be accessed on the UI thread.
        Dispatcher.Invoke(RenderChart);
    }

    private void RenderChart()
    {
        if (_vm == null) return;

        var plot = ScatterPlot.Plot;
        plot.Clear();

        // Restore theme (Clear() resets styling)
        plot.FigureBackground.Color = BgColour;
        plot.DataBackground.Color   = DataBgColour;
        plot.Axes.Color(AxisColour);
        plot.Grid.MajorLineColor = GridColour;

        // ── History scatter ───────────────────────────────────────────────────
        var hist = _vm.HistoryPoints;
        if (hist.Count > 0)
        {
            var hxs = new double[hist.Count];
            var hys = new double[hist.Count];
            for (var i = 0; i < hist.Count; i++) { hxs[i] = hist[i].X; hys[i] = hist[i].Y; }

            var historySeries = plot.Add.Scatter(hxs, hys);
            historySeries.Color      = HistoryColour;
            historySeries.MarkerSize = 4;
            historySeries.LineWidth  = 0;
            historySeries.LegendText = "History";
        }

        // ── Current point ─────────────────────────────────────────────────────
        var cur = _vm.CurrentPoint;
        if (cur != null)
        {
            var curSeries = plot.Add.Scatter(new[] { cur.X }, new[] { cur.Y });
            curSeries.Color      = CurrentColour;
            curSeries.MarkerSize = 9;
            curSeries.LineWidth  = 0;
            curSeries.LegendText = "Current";
        }

        // ── Regression line ───────────────────────────────────────────────────
        var reg = _vm.RegressionLine;
        if (reg.HasValue)
        {
            var line = plot.Add.Line(reg.Value.X1, reg.Value.Y1, reg.Value.X2, reg.Value.Y2);
            line.Color     = RegrColour;
            line.LineWidth = 1.5f;
            line.LinePattern = LinePattern.DenselyDashed;
        }

        // ── Axis labels ───────────────────────────────────────────────────────
        plot.XLabel(_vm.XSymbol);
        plot.YLabel(_vm.YSymbol);

        // Legend only when both series are present
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

    // ── Time-range mutual exclusion ───────────────────────────────────────────

    private void TimeRange_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clicked) return;

        // Uncheck all other toggle buttons in the time-range strip
        foreach (var child in ((System.Windows.Controls.StackPanel)clicked.Parent).Children)
        {
            if (child is ToggleButton tb && tb != clicked)
                tb.IsChecked = false;
        }

        clicked.IsChecked = true;

        if (_vm != null && clicked.Tag is string tag)
            _vm.SelectedTimeRange = tag;
    }
}

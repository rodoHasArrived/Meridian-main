using System.Windows;
using System.Windows.Controls;
using Meridian.QuantScript.Plotting;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Code-behind for QuantScriptPage. Intentionally thin: DI wiring, AvalonEdit synchronisation,
/// and ScottPlot chart rendering (ScottPlot's imperative API cannot be data-bound in XAML).
/// </summary>
public partial class QuantScriptPage : Page
{
    private QuantScriptViewModel? _vm;
    private bool _suppressSync;

    public QuantScriptPage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _vm = DataContext as QuantScriptViewModel;
        if (_vm is null) return;

        // Restore persisted column widths
        var (leftWidth, rightWidth) = _vm.OnActivated();
        if (leftWidth > 0)  LeftColumn.Width  = new GridLength(leftWidth,  GridUnitType.Star);
        if (rightWidth > 0) RightColumn.Width = new GridLength(rightWidth, GridUnitType.Star);

        ScriptEditor.TextChanged += OnScriptEditorTextChanged;
        _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        ScriptEditor.TextChanged -= OnScriptEditorTextChanged;
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm.SaveLayout(LeftColumn.Width.Value, RightColumn.Width.Value);
        _vm.Dispose();
    }

    private void OnScriptEditorTextChanged(object? sender, EventArgs e)
    {
        if (_suppressSync || _vm is null) return;
        _suppressSync = true;
        _vm.ScriptSource = ScriptEditor.Text;
        _suppressSync = false;
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(QuantScriptViewModel.ScriptSource) || _vm is null) return;
        if (_suppressSync || ScriptEditor.Text == _vm.ScriptSource) return;

        _suppressSync = true;
        ScriptEditor.Text = _vm.ScriptSource;
        _suppressSync = false;
    }

    /// <summary>
    /// Renders a ScottPlot chart imperatively into the Border declared for each chart entry.
    /// ScottPlot's WpfPlot has no data-binding API.
    /// </summary>
    private void OnPlotBorderLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not PlotRequest request) return;

        var plot = new ScottPlot.WPF.WpfPlot();
        RenderPlotRequest(plot, request);
        border.Child = plot;
    }

    private static void RenderPlotRequest(ScottPlot.WPF.WpfPlot wpfPlot, PlotRequest request)
    {
        wpfPlot.Plot.Clear();

        switch (request.Type)
        {
            case PlotType.Line:
            case PlotType.CumulativeReturn:
            case PlotType.Drawdown:
                if (request.Series is { Count: > 0 } series)
                {
                    var dates  = series.Select(p => p.Date.ToDateTime(TimeOnly.MinValue).ToOADate()).ToArray();
                    var values = series.Select(p => p.Value).ToArray();
                    var scatter = wpfPlot.Plot.Add.Scatter(dates, values);
                    scatter.LegendText = request.Title;
                }
                break;

            case PlotType.MultiLine:
                if (request.MultiSeries is { } multi)
                {
                    foreach (var (label, pts) in multi)
                    {
                        var dates  = pts.Select(p => p.Item1.ToDateTime(TimeOnly.MinValue).ToOADate()).ToArray();
                        var values = pts.Select(p => p.Item2).ToArray();
                        var scatter = wpfPlot.Plot.Add.Scatter(dates, values);
                        scatter.LegendText = label;
                    }
                    wpfPlot.Plot.ShowLegend();
                }
                break;

            case PlotType.Heatmap:
                if (request.HeatmapData is { } hm && request.HeatmapLabels is { } labels)
                {
                    var positions = Enumerable.Range(0, labels.Length).Select(i => (double)i).ToArray();
                    var values    = Enumerable.Range(0, labels.Length).Select(i => hm[i][i]).ToArray();
                    wpfPlot.Plot.Add.Bars(positions, values);
                }
                break;
        }

        wpfPlot.Plot.Title(request.Title);
        wpfPlot.Refresh();
    }
}

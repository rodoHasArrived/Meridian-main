using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using Meridian.QuantScript.Plotting;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Converts <see cref="ConsoleEntryKind"/> to a WPF <see cref="Brush"/> for console text colouring.
/// </summary>
[System.Windows.Data.ValueConversion(typeof(ConsoleEntryKind), typeof(Brush))]
public sealed class ConsoleKindToBrushConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        value is ConsoleEntryKind kind ? kind switch
        {
            ConsoleEntryKind.Warning => Brushes.Orange,
            ConsoleEntryKind.Error   => Brushes.Red,
            _                        => SystemColors.ControlTextBrush
        } : DependencyProperty.UnsetValue;

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Code-behind for QuantScriptPage.
/// Wires AvalonEdit TextChanged → ViewModel.ScriptSource and renders ScottPlot charts
/// imperatively in <see cref="OnPlotBorderLoaded"/> (ScottPlot WpfPlot has no binding API).
/// </summary>
public partial class QuantScriptPage : Page
{
    private QuantScriptViewModel? _vm;

    public QuantScriptPage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _vm = DataContext as QuantScriptViewModel;
        // AvalonEdit does not support standard Binding on its Document property.
        // Wire TextChanged to propagate edits to the ViewModel.
        ScriptEditor.TextChanged += OnScriptEditorTextChanged;
        ScriptEditor.Text = _vm?.ScriptSource ?? string.Empty;
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        ScriptEditor.TextChanged -= OnScriptEditorTextChanged;
        _vm?.Dispose();
    }

    private void OnScriptEditorTextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null && ScriptEditor.Text != _vm.ScriptSource)
            _vm.ScriptSource = ScriptEditor.Text;
    }

    /// <summary>
    /// Renders a ScottPlot <see cref="ScottPlot.WPF.WpfPlot"/> into the Border declared
    /// for each chart entry. ScottPlot's imperative API requires this code-behind approach.
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
                    var dates = series.Select(p => p.Date.ToDateTime(TimeOnly.MinValue).ToOADate()).ToArray();
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
                        var dates = pts.Select(p => p.Item1.ToDateTime(TimeOnly.MinValue).ToOADate()).ToArray();
                        var values = pts.Select(p => p.Item2).ToArray();
                        var scatter = wpfPlot.Plot.Add.Scatter(dates, values);
                        scatter.LegendText = label;
                    }
                    wpfPlot.Plot.ShowLegend();
                }
                break;

            case PlotType.Heatmap:
                // Minimal heatmap: render first row as bar chart of diagonal (correlation with self)
                if (request.HeatmapData is { } hm && request.HeatmapLabels is { } labels)
                {
                    var positions = Enumerable.Range(0, labels.Length).Select(i => (double)i).ToArray();
                    var values = Enumerable.Range(0, labels.Length).Select(i => hm[i][i]).ToArray();
                    wpfPlot.Plot.Add.Bars(positions, values);
                }
                break;

            default:
                break;
        }

        wpfPlot.Plot.Title(request.Title);
        wpfPlot.Refresh();
    }
}

using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Code-behind for QuantScriptPage. Intentionally thin: DI wiring and layout persistence.
/// ScottPlot rendering is handled by <see cref="Behaviors.PlotRenderBehavior"/> via the attached property.
/// </summary>
public partial class QuantScriptPage : Page
{
    private QuantScriptViewModel? _vm;

    public QuantScriptPage(QuantScriptViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = _vm;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _vm = DataContext as QuantScriptViewModel;
        if (_vm is null) return;

        // Restore persisted row heights
        var (chartHeight, editorHeight) = _vm.OnActivated();
        if (chartHeight > 0)  ChartRow.Height  = new GridLength(chartHeight,  GridUnitType.Star);
        if (editorHeight > 0) EditorRow.Height = new GridLength(editorHeight, GridUnitType.Star);
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.SaveLayout(ChartRow.Height.Value, EditorRow.Height.Value);
        _vm.Dispose();
    }
}

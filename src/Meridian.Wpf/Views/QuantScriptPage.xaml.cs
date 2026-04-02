using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Code-behind for QuantScriptPage. Intentionally thin: DI wiring and AvalonEdit synchronisation.
/// ScottPlot rendering is handled by <see cref="Behaviors.PlotRenderBehavior"/> via the attached property.
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

        // Restore persisted row heights
        var (chartHeight, editorHeight) = _vm.OnActivated();
        if (chartHeight > 0)  ChartRow.Height  = new GridLength(chartHeight,  GridUnitType.Star);
        if (editorHeight > 0) EditorRow.Height = new GridLength(editorHeight, GridUnitType.Star);

        ScriptEditor.TextChanged += OnScriptEditorTextChanged;
        _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        ScriptEditor.TextChanged -= OnScriptEditorTextChanged;
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm.SaveLayout(ChartRow.Height.Value, EditorRow.Height.Value);
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
}

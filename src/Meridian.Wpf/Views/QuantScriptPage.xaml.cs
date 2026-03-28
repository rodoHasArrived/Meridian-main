using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit.Document;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// QuantScript interactive scripting page.
/// Code-behind is intentionally thin: DI wiring + AvalonEdit synchronisation only.
/// </summary>
public partial class QuantScriptPage : Page
{
    private readonly QuantScriptViewModel _vm;
    private bool _suppressSync;

    public QuantScriptPage(QuantScriptViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        InitializeComponent();
        DataContext = _vm;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _vm.OnActivated();

        // Wire AvalonEdit → ViewModel
        ScriptEditor.TextChanged += OnEditorTextChanged;

        // Wire ViewModel → AvalonEdit (e.g. when loading a script file)
        _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_suppressSync) return;
        _suppressSync = true;
        _vm.ScriptSource = ScriptEditor.Text;
        _suppressSync = false;
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(QuantScriptViewModel.ScriptSource)) return;
        if (_suppressSync) return;
        if (ScriptEditor.Text == _vm.ScriptSource) return;

        _suppressSync = true;
        ScriptEditor.Text = _vm.ScriptSource;
        _suppressSync = false;
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        ScriptEditor.TextChanged -= OnEditorTextChanged;
        _vm.PropertyChanged -= OnVmPropertyChanged;

        // Persist column widths
        _vm.SaveLayout(LeftColumn.Width.Value, RightColumn.Width.Value);
        _vm.Dispose();
    }
}

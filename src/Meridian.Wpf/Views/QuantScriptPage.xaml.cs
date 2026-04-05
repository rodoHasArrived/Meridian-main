using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using Meridian.Wpf.Models;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Code-behind for the notebook-style QuantScript page. Keeps layout persistence and editor focus
/// handling out of the view model while leaving execution orchestration in the VM.
/// </summary>
public partial class QuantScriptPage : Page
{
    private readonly QuantScriptViewModel _vm;
    private readonly Dictionary<string, TextEditor> _editors = new(StringComparer.Ordinal);

    public QuantScriptPage(QuantScriptViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        InitializeComponent();
        DataContext = _vm;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        var (chartHeight, editorHeight) = _vm.OnActivated();
        if (chartHeight > 0)
            ChartRow.Height = new GridLength(chartHeight, GridUnitType.Star);
        if (editorHeight > 0)
            EditorRow.Height = new GridLength(editorHeight, GridUnitType.Star);

        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm.PropertyChanged += OnVmPropertyChanged;

        FocusSelectedEditor();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm.SaveLayout(ChartRow.Height.Value, EditorRow.Height.Value);
        _vm.Dispose();
        _editors.Clear();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuantScriptViewModel.SelectedCellId))
            FocusSelectedEditor();
    }

    private void OnCellEditorLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextEditor editor && editor.DataContext is QuantScriptCellViewModel cell)
        {
            _editors[cell.CellId] = editor;
            if (_vm.SelectedCellId == cell.CellId)
                FocusSelectedEditor();
        }
    }

    private void OnCellEditorUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextEditor editor && editor.DataContext is QuantScriptCellViewModel cell)
            _editors.Remove(cell.CellId);
    }

    private void OnCellEditorGotKeyboardFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextEditor editor && editor.DataContext is QuantScriptCellViewModel cell)
            _vm.SelectCellCommand.Execute(cell);
    }

    private async void OnPagePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (_vm.SaveNotebookCommand.CanExecute(null))
            {
                e.Handled = true;
                await _vm.SaveNotebookCommand.ExecuteAsync(null);
            }

            return;
        }

        if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None && _vm.RunAllCommand.CanExecute(null))
        {
            e.Handled = true;
            await _vm.RunAllCommand.ExecuteAsync(null);
        }
    }

    private void FocusSelectedEditor()
    {
        if (string.IsNullOrWhiteSpace(_vm.SelectedCellId) ||
            !_editors.TryGetValue(_vm.SelectedCellId, out var editor))
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            editor.Focus();
            editor.TextArea.Caret.BringCaretToView();
        }, DispatcherPriority.Background);
    }
}

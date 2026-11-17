using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Keyboard shortcuts page: lists every registered shortcut from
/// <see cref="KeyboardShortcutService"/>, grouped by category, and lets the operator rebind,
/// enable or disable, and reset each one. State and validation live in
/// <see cref="KeyboardShortcutsViewModel"/>; this code-behind only lays out the category cards
/// and forwards key presses from the focused capture surface.
/// </summary>
public partial class KeyboardShortcutsPage : Page
{
    private readonly KeyboardShortcutsViewModel _viewModel;

    public KeyboardShortcutsPage()
    {
        InitializeComponent();
        _viewModel = new KeyboardShortcutsViewModel(KeyboardShortcutService.Instance, LoggingService.Instance);
        DataContext = _viewModel;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Refresh();
        PopulateDynamicShortcuts();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        // Never leave global shortcut handling suspended when the operator navigates away
        // mid-capture.
        _viewModel.CancelCapture();
    }

    /// <summary>
    /// Lays out one card per shortcut category in two columns. Each card is rendered from
    /// <c>ShortcutCategoryCardTemplate</c> bound to its <see cref="ShortcutCategoryGroupViewModel"/>.
    /// </summary>
    private void PopulateDynamicShortcuts()
    {
        DynamicShortcutsPanel.Children.Clear();

        var outerGrid = new Grid();
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var cardTemplate = (DataTemplate)FindResource("ShortcutCategoryCardTemplate");
        var index = 0;
        foreach (var group in _viewModel.Categories)
        {
            var card = new ContentControl
            {
                Content = group,
                ContentTemplate = cardTemplate,
                Focusable = false,
                VerticalAlignment = VerticalAlignment.Top
            };

            var gridColumn = index % 2 == 0 ? 0 : 2;
            var gridRow = index / 2;
            while (outerGrid.RowDefinitions.Count <= gridRow * 2)
            {
                outerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            }

            Grid.SetColumn(card, gridColumn);
            Grid.SetRow(card, gridRow * 2);
            outerGrid.Children.Add(card);
            index++;
        }

        DynamicShortcutsPanel.Children.Add(outerGrid);
    }

    private void OnCaptureSurfacePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_viewModel.IsCapturing)
        {
            return;
        }

        var key = KeyboardShortcutsViewModel.ResolveCapturedKey(e.Key, e.SystemKey, e.ImeProcessedKey);
        if (!_viewModel.HandleCapturedKey(key, Keyboard.Modifiers))
        {
            return;
        }

        // Swallow the press so it neither reaches page controls nor runs as a command.
        e.Handled = true;
        if (!_viewModel.IsCapturing && sender is FrameworkElement { Tag: UIElement rebindButton })
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => rebindButton.Focus()));
        }
    }

    private void OnCaptureSurfaceLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ShortcutBindingRowViewModel { IsCapturing: true } })
        {
            _viewModel.CancelCapture();
        }
    }

    private void OnCaptureSurfaceIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is UIElement surface)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => Keyboard.Focus(surface)));
        }
    }
}

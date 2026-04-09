using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using WpfNavigationEventArgs = System.Windows.Navigation.NavigationEventArgs;
using WpfNavigationService = Meridian.Wpf.Services.NavigationService;

namespace Meridian.Wpf.Views;

public partial class MainPage : Page
{
    private readonly WpfNavigationService _navigationService;
    private readonly MainPageViewModel _viewModel;
    private bool _splitPaneHostReady;
    private Point? _navigationDragStartPoint;
    private Point? _commandPaletteDragStartPoint;
    private Point? _recentPageDragStartPoint;

    public MainPage(MainPageViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _navigationService = (WpfNavigationService)_viewModel.NavigationService;
        _navigationService.Navigated += OnNavigationServiceNavigated;

        DataContext = _viewModel;
        InitializeComponent();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        InitializeNavigationTarget();

        _viewModel.ActivateShell();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _navigationService.Navigated -= OnNavigationServiceNavigated;
        _viewModel.Dispose();
    }

    private void CommandPaletteOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _viewModel.HideCommandPaletteCommand.Execute(null);
    }

    private void CommandPaletteBorder_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void CommandPaletteResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Mouse.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        _viewModel.OpenSelectedCommandPalettePageCommand.Execute(null);
    }

    private void OnOpenCommandPaletteClick(object sender, RoutedEventArgs e)
    {
        ShowCommandPaletteOverlay();
    }

    /// <summary>
    /// Shows the inline command palette overlay and focuses the search input.
    /// Called from the main window when the global Ctrl+K shortcut fires so that
    /// the <c>CommandPaletteInput</c> element remains a descendant of the main window
    /// and is therefore discoverable by UI-Automation tooling (e.g. screenshot scripts).
    /// </summary>
    public void ShowCommandPaletteOverlay()
    {
        _viewModel.ShowCommandPaletteCommand.Execute(null);
        CommandPaletteTextBox.Focus();
        CommandPaletteTextBox.SelectAll();
    }

    /// <summary>
    /// Activates the inline command-palette overlay and focuses the search box.
    /// Called by <see cref="MainWindow"/> when the user presses Ctrl+K.
    /// </summary>
    public void OpenCommandPalette()
    {
        _viewModel.ShowCommandPaletteCommand.Execute(null);
        CommandPaletteTextBox.Focus();
        CommandPaletteTextBox.SelectAll();
    }

    private void OnContentFrameNavigated(object sender, WpfNavigationEventArgs e)
    {
        _viewModel.SyncNavigationState();
    }

    private void InitializeNavigationTarget()
    {
        if (_splitPaneHostReady)
        {
            var activePane = SplitPaneHost.GetPaneFrame(_viewModel.SplitPane.ActivePaneIndex)
                ?? SplitPaneHost.GetPaneFrame(0);

            if (activePane is not null)
            {
                _navigationService.Initialize(activePane);
                return;
            }
        }

        _navigationService.Initialize(ContentFrame);
    }

    private void OnNavigationServiceNavigated(object? sender, Meridian.Ui.Services.Contracts.NavigationEventArgs e)
    {
        if (!_splitPaneHostReady)
        {
            return;
        }

        _viewModel.SplitPane.AssignPageToPane(e.PageTag);
        RefreshSplitPaneContent();
    }

    private void OnNavigationListPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            _navigationDragStartPoint = e.GetPosition(listBox);
        }
    }

    private void OnNavigationListMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _navigationDragStartPoint = null;
            return;
        }

        if (!HasExceededDragThreshold(_navigationDragStartPoint, e.GetPosition(listBox)) ||
            listBox.SelectedValue is not string pageTag ||
            string.IsNullOrWhiteSpace(pageTag))
        {
            return;
        }

        _navigationDragStartPoint = null;
        var data = new DataObject();
        data.SetData(SplitPaneHostControl.PageTagFormat, pageTag);
        data.SetData(DataFormats.StringFormat, pageTag);
        DragDrop.DoDragDrop(listBox, data, DragDropEffects.Move);
    }

    private void OnCommandPaletteResultsPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            _commandPaletteDragStartPoint = e.GetPosition(listBox);
        }
    }

    private void OnCommandPaletteResultsMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _commandPaletteDragStartPoint = null;
            return;
        }

        if (!HasExceededDragThreshold(_commandPaletteDragStartPoint, e.GetPosition(CommandPaletteResults)) ||
            CommandPaletteResults.SelectedItem is not string pageTag ||
            string.IsNullOrWhiteSpace(pageTag))
        {
            return;
        }

        _commandPaletteDragStartPoint = null;
        var data = new DataObject();
        data.SetData(SplitPaneHostControl.PageTagFormat, pageTag);
        data.SetData(DataFormats.StringFormat, pageTag);
        DragDrop.DoDragDrop(CommandPaletteResults, data, DragDropEffects.Move);
    }

    private void OnRecentPageButtonPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button button)
        {
            _recentPageDragStartPoint = e.GetPosition(button);
        }
    }

    private void OnRecentPageButtonMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _recentPageDragStartPoint = null;
            return;
        }

        if (!HasExceededDragThreshold(_recentPageDragStartPoint, e.GetPosition(button)) ||
            button.Tag is not string pageTag ||
            string.IsNullOrWhiteSpace(pageTag))
        {
            return;
        }

        _recentPageDragStartPoint = null;
        var data = new DataObject();
        data.SetData(SplitPaneHostControl.PageTagFormat, pageTag);
        data.SetData(DataFormats.StringFormat, pageTag);
        DragDrop.DoDragDrop(button, data, DragDropEffects.Move);
    }

    private static bool HasExceededDragThreshold(Point? dragStart, Point currentPosition)
    {
        if (dragStart is null)
        {
            return false;
        }

        return Math.Abs(currentPosition.X - dragStart.Value.X) >= SystemParameters.MinimumHorizontalDragDistance ||
               Math.Abs(currentPosition.Y - dragStart.Value.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }
}

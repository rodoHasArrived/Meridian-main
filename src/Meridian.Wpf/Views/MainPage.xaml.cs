using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using WpfNavigationEventArgs = System.Windows.Navigation.NavigationEventArgs;
using WpfNavigationService = Meridian.Wpf.Services.NavigationService;

namespace Meridian.Wpf.Views;

public partial class MainPage : Page
{
    private readonly WpfNavigationService _navigationService;
    private readonly MainPageViewModel _viewModel;
    private readonly TaskCompletionSource _shellReadyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public MainPage(MainPageViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _navigationService = (WpfNavigationService)_viewModel.NavigationService;

        InitializeComponent();
        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _navigationService.Initialize(ContentFrame);

        _viewModel.ActivateShell();
        _shellReadyTcs.TrySetResult();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
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

    private void CommandPaletteTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (TryHandleCommandPaletteDirectionalKey(e.Key))
        {
            e.Handled = true;
        }
    }

    private void WorkspaceNavigationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: ShellNavigationItem item })
        {
            return;
        }

        if (string.Equals(_viewModel.CurrentPageTag, item.PageTag, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _viewModel.NavigateToPageCommand.Execute(item.PageTag);
    }

    private void OnOpenCommandPaletteClick(object sender, RoutedEventArgs e)
    {
        ShowCommandPaletteOverlay();
    }

    private void OnContentFrameNavigated(object sender, WpfNavigationEventArgs e)
    {
        _viewModel.SyncNavigationState();
    }

    private bool TryHandleCommandPaletteDirectionalKey(Key key)
    {
        var offset = key switch
        {
            Key.Down => 1,
            Key.Up => -1,
            _ => 0
        };

        if (offset == 0 || _viewModel.CommandPalettePages.Count == 0)
        {
            return false;
        }

        var selectedIndex = _viewModel.SelectedCommandPalettePage is null
            ? -1
            : _viewModel.CommandPalettePages.IndexOf(_viewModel.SelectedCommandPalettePage);

        var nextIndex = selectedIndex < 0
            ? offset > 0 ? 0 : _viewModel.CommandPalettePages.Count - 1
            : Math.Clamp(selectedIndex + offset, 0, _viewModel.CommandPalettePages.Count - 1);

        var selectedPage = _viewModel.CommandPalettePages[nextIndex];
        _viewModel.SelectedCommandPalettePage = selectedPage;
        CommandPaletteResults.ScrollIntoView(selectedPage);
        return true;
    }

    public void ShowCommandPaletteOverlay()
    {
        _viewModel.ShowCommandPaletteCommand.Execute(null);
        CommandPaletteTextBox.Focus();
        CommandPaletteTextBox.SelectAll();
    }

    public Task WaitForShellReadyAsync()
    {
        if (IsLoaded)
        {
            _shellReadyTcs.TrySetResult();
        }

        return _shellReadyTcs.Task;
    }
}

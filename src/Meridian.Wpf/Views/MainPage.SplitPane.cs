using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Views;

// Partial extension of MainPage wiring the split-pane host to NavigationService.
// Fields _navigationService and _viewModel are declared in MainPage.xaml.cs.
public partial class MainPage
{
    private SplitPaneHostControl? _splitPaneHost;
    private bool _splitPaneEventsHooked;

    private void OnSplitPaneHostLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not SplitPaneHostControl host)
            return;

        _splitPaneHost = host;
        host.PaneDropRequested -= OnSplitPanePaneDropRequested;
        host.PaneDropRequested += OnSplitPanePaneDropRequested;

        if (string.IsNullOrWhiteSpace(_viewModel.PaneHost.GetAssignedPageTag(0)))
        {
            _viewModel.PaneHost.AssignPageToPane(_viewModel.CurrentPageTag, 0);
        }

        if (!_splitPaneEventsHooked)
        {
            _viewModel.PaneHost.LayoutChanged += OnSplitPaneLayoutChanged;
            _viewModel.PaneHost.ActivePaneChanged += OnSplitPaneActivePaneChanged;
            _splitPaneEventsHooked = true;
        }

        SyncSplitPaneContent();
    }

    private void OnSplitPanePaneDropRequested(object? sender, PaneDropEventArgs e)
    {
        var result = _shellNavigationCoordinator.ApplyPaneDrop(
            _viewModel.PaneHost,
            e.PageTag,
            e.TargetPaneIndex,
            e.Action);
        SyncSplitPaneContent();

        var activePane = _splitPaneHost?.GetPaneFrame(result.ActivePaneIndex);
        if (activePane is not null)
        {
            _shellNavigationCoordinator.InitializeNavigationFrame(activePane);
        }

        _viewModel.NavigateToPageCommand.Execute(result.PageTag);
    }

    private void OnSplitPaneLayoutChanged(object? sender, PaneLayout layout)
    {
        SyncSplitPaneContent();
    }

    private void OnSplitPaneActivePaneChanged(object? sender, int paneIndex)
    {
        SyncSplitPaneContent();
    }

    private void SyncSplitPaneContent()
    {
        if (_splitPaneHost is null)
        {
            return;
        }

        foreach (var state in _shellNavigationCoordinator.GetVisiblePaneContentStates(_viewModel.PaneHost))
        {
            var frame = _splitPaneHost.GetPaneFrame(state.PaneIndex);
            if (frame is null)
            {
                continue;
            }

            frame.Content = _shellNavigationCoordinator.CreatePaneContent(state);
        }

        var activePane = _splitPaneHost.GetPaneFrame(_viewModel.PaneHost.ActivePaneIndex);
        if (activePane is not null)
        {
            _shellNavigationCoordinator.InitializeNavigationFrame(activePane);
        }
    }
}

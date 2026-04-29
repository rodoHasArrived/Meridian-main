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

        if (string.IsNullOrWhiteSpace(_viewModel.SplitPane.GetAssignedPageTag(0)))
        {
            _viewModel.SplitPane.AssignPageToPane(_viewModel.CurrentPageTag, 0);
        }

        if (!_splitPaneEventsHooked)
        {
            _viewModel.SplitPane.LayoutChanged += OnSplitPaneLayoutChanged;
            _viewModel.SplitPane.ActivePaneChanged += OnSplitPaneActivePaneChanged;
            _splitPaneEventsHooked = true;
        }

        SyncSplitPaneContent();
    }

    private void OnSplitPanePaneDropRequested(object? sender, PaneDropEventArgs e)
    {
        var activePaneIndex = _viewModel.SplitPane.ApplyPaneDrop(e.PageTag, e.TargetPaneIndex, e.Action);
        SyncSplitPaneContent();

        var activePane = _splitPaneHost?.GetPaneFrame(activePaneIndex);
        if (activePane is not null)
        {
            _navigationService.Initialize(activePane);
        }

        _viewModel.NavigateToPageCommand.Execute(e.PageTag);
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

        for (var paneIndex = 0; paneIndex < _viewModel.SplitPane.SelectedLayout.PaneCount; paneIndex++)
        {
            var frame = _splitPaneHost.GetPaneFrame(paneIndex);
            if (frame is null)
            {
                continue;
            }

            var pageTag = _viewModel.SplitPane.GetAssignedPageTag(paneIndex);
            if (string.IsNullOrWhiteSpace(pageTag))
            {
                continue;
            }

            frame.Content = _navigationService.CreatePageContent(pageTag);
        }

        var activePane = _splitPaneHost.GetPaneFrame(_viewModel.SplitPane.ActivePaneIndex);
        if (activePane is not null)
        {
            _navigationService.Initialize(activePane);
        }
    }
}

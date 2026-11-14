using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Meridian.Wpf.Models;
using WpfNavigatingCancelEventArgs = System.Windows.Navigation.NavigatingCancelEventArgs;
using WpfNavigationEventArgs = System.Windows.Navigation.NavigationEventArgs;
using WpfNavigationMode = System.Windows.Navigation.NavigationMode;
using WpfNavigationService = Meridian.Wpf.Services.NavigationService;
using ShellNavigationEventArgs = Meridian.Ui.Services.Contracts.NavigationEventArgs;

namespace Meridian.Wpf.Views;

// Partial extension of MainPage wiring the split-pane host to NavigationService.
// Fields _navigationService and _viewModel are declared in MainPage.xaml.cs.
public partial class MainPage
{
    private SplitPaneHostControl? _splitPaneHost;
    private bool _splitPaneEventsHooked;
    private int? _pendingNavigationPaneIndex;
    private bool _restoringFrameHistory;

    private void DetachFrameNavigationHandlers()
    {
        if (_lastShellNavigationFrame is { } frame)
        {
            frame.Navigating -= OnShellFrameNavigating;
            frame.Navigated -= OnShellFrameNavigated;
        }

        _lastShellNavigationFrame = null;
        _restoringFrameHistory = false;
    }

    private bool IsCurrentShellNavigationFrame(object navigator)
        => navigator is Frame frame &&
           ReferenceEquals(frame, _lastShellNavigationFrame) &&
           _navigationService.IsNavigationFrame(frame, WorkspaceChromePresentationMode.Docked) &&
           (ReferenceEquals(frame, ContentFrame) ||
            ReferenceEquals(frame, _splitPaneHost?.GetPaneFrame(_viewModel.PaneHost.ActivePaneIndex)));

    private void OnShellFrameNavigating(object sender, WpfNavigatingCancelEventArgs e)
    {
        if (IsCurrentShellNavigationFrame(e.Navigator))
        {
            _restoringFrameHistory = e.NavigationMode is WpfNavigationMode.Back or WpfNavigationMode.Forward;
        }
    }

    private void OnShellFrameNavigated(object sender, WpfNavigationEventArgs e)
    {
        if (!IsCurrentShellNavigationFrame(e.Navigator))
        {
            return;
        }

        var restoringHistory = _restoringFrameHistory;
        _restoringFrameHistory = false;
        if (!restoringHistory ||
            WpfNavigationService.GetContentPageTag(e.Content) is not { } pageTag)
        {
            return;
        }

        // Retain the landed instance and its parameters; synchronizing shell state must not navigate.
        var activePaneIndex = _viewModel.PaneHost.ActivePaneIndex;
        var previousPendingPaneIndex = _pendingNavigationPaneIndex;
        _pendingNavigationPaneIndex = activePaneIndex;
        try
        {
            _viewModel.OnNavigated(this, new ShellNavigationEventArgs { PageTag = pageTag, Parameter = e.ExtraData });
            if (IsWorkspaceHomePage(pageTag) && !ReferenceEquals(e.Navigator, ContentFrame))
            {
                _viewModel.PaneHost.AssignPageToPane(pageTag, activePaneIndex);
            }
        }
        finally
        {
            _pendingNavigationPaneIndex = previousPendingPaneIndex;
        }
    }

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

            AutomationProperties.SetItemType(frame,
                state.PaneIndex == _viewModel.PaneHost.ActivePaneIndex ? "ActiveWorkspacePane" : "WorkspacePane");
            if (state.PaneIndex != _pendingNavigationPaneIndex && !string.Equals(
                    WpfNavigationService.GetContentPageTag(frame.Content),
                    state.CanonicalPageTag,
                    StringComparison.OrdinalIgnoreCase))
            {
                frame.Content = _shellNavigationCoordinator.CreatePaneContent(state);
            }
        }

        var canonicalPageTag = ShellNavigationCatalog.GetCanonicalPageTag(_viewModel.CurrentPageTag);
        var targetFrame = IsWorkspaceHomePage(canonicalPageTag)
            ? _splitPaneHost.GetPaneFrame(_viewModel.PaneHost.ActivePaneIndex)
            : ContentFrame;
        if (targetFrame is not null && OwnsNavigationFrame())
        {
            BindNavigationFrame(targetFrame);
        }
    }
}

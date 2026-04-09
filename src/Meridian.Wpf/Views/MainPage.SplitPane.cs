using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Views;

public partial class MainPage
{
    private void OnSplitPaneHostLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not SplitPaneHostControl host)
        {
            return;
        }

        _splitPaneHostReady = true;

        var firstFrame = host.GetPaneFrame(0);
        if (firstFrame is null)
        {
            return;
        }

        _navigationService.Initialize(firstFrame);
        _viewModel.SplitPane.AssignPageToPane(_viewModel.CurrentPageTag, 0);
        RefreshSplitPaneContent();

        if (_viewModel is null)
        {
            return;
        }

        _viewModel.SplitPane.LayoutChanged += (_, _) =>
        {
            RefreshSplitPaneContent();
            var pane = host.GetPaneFrame(_viewModel.SplitPane.ActivePaneIndex);
            if (pane is not null)
            {
                _navigationService.Initialize(pane);
            }
        };

        _viewModel.SplitPane.ActivePaneChanged += (_, idx) =>
        {
            host.ActivePaneIndex = idx;
            var pane = host.GetPaneFrame(idx);
            if (pane is not null)
            {
                _navigationService.Initialize(pane);
            }
        };

        _viewModel.SplitPane.PaneAssignmentsChanged += (_, _) => RefreshSplitPaneContent();
        host.PaneActivated += (_, idx) => _viewModel.SplitPane.FocusPaneCommand.Execute(idx);
        host.PaneCloseRequested += (_, paneId) => _viewModel.SplitPane.ClosePaneCommand.Execute(paneId);
    }

    private void OnSplitPanePaneDropRequested(object sender, PaneDropEventArgs e)
    {
        var targetPaneIndex = Math.Clamp(
            e.TargetPaneIndex,
            0,
            Math.Max(0, _viewModel.SplitPane.SelectedLayout.PaneCount - 1));

        if (e.Action == PaneDropAction.FloatWindow)
        {
            // Pop the dragged page out into a standalone floating window.
            Services.FloatingPageService.Instance.OpenPage(
                e.PageTag,
                tag => _navigationService.CreatePageContent(tag));
            return;
        }

        if (e.Action == PaneDropAction.SplitLeft)
        {
            _viewModel.SplitPane.FocusPaneCommand.Execute(targetPaneIndex);
            _viewModel.SplitPane.SplitPaneCommand.Execute("Left");
            // SplitLeft inserts the new pane at the same index; targetPaneIndex stays unchanged.
        }
        else if (e.Action == PaneDropAction.SplitRight)
        {
            _viewModel.SplitPane.FocusPaneCommand.Execute(targetPaneIndex);
            _viewModel.SplitPane.SplitPaneCommand.Execute("Right");
            targetPaneIndex = Math.Min(_viewModel.SplitPane.SelectedLayout.PaneCount - 1, targetPaneIndex + 1);
        }
        else if (e.Action == PaneDropAction.SplitBelow)
        {
            _viewModel.SplitPane.FocusPaneCommand.Execute(targetPaneIndex);
            _viewModel.SplitPane.SplitPaneCommand.Execute("Below");
            targetPaneIndex = Math.Min(_viewModel.SplitPane.SelectedLayout.PaneCount - 1, targetPaneIndex + 1);
        }

        _viewModel.SplitPane.AssignPageToPane(e.PageTag, targetPaneIndex);
        RefreshSplitPaneContent();
    }

    private void RefreshSplitPaneContent()
    {
        if (!_splitPaneHostReady)
        {
            return;
        }

        for (var i = 0; i < _viewModel.SplitPane.SelectedLayout.PaneCount; i++)
        {
            var frame = SplitPaneHost.GetPaneFrame(i);
            var pageTag = _viewModel.SplitPane.GetAssignedPageTag(i);
            if (frame is null || string.IsNullOrWhiteSpace(pageTag))
            {
                continue;
            }

            var content = _navigationService.CreatePageContent(pageTag);
            if (frame.Content is Page page &&
                page.GetType() == content.GetType())
            {
                continue;
            }

            frame.Navigate(content);
        }
    }
}

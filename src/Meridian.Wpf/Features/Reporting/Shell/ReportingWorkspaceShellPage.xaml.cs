using System.Windows;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Features.Reporting.Shell;

public partial class ReportingWorkspaceShellPage : ReportingWorkspaceShellPageBase
{
    public ReportingWorkspaceShellPage(
        NavigationService navigationService,
        ReportingWorkspaceShellStateProvider stateProvider,
        ReportingWorkspaceShellViewModel viewModel)
        : base(navigationService, stateProvider, viewModel)
    {
        InitializeComponent();
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await RestoreDockLayoutAsync(ReportingDockManager).ConfigureAwait(true);
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _ = SaveDockLayoutAsync(ReportingDockManager);
    }

    private void OnPaneDropRequested(object? sender, PaneDropEventArgs e)
        => OpenWorkspacePage(ReportingDockManager, e.PageTag, e.Action);

    private void OnCommandBarCommandInvoked(object sender, WorkspaceCommandInvokedEventArgs e)
        => NavigateToRegisteredPage(e.Command.Id);

    private void OnCockpitDecisionInvoked(object sender, WorkspaceDecisionInvokedEventArgs e)
        => NavigateToRegisteredPage(e.ActionId);

    private void OnNavigateButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string pageTag })
        {
            NavigateToRegisteredPage(pageTag);
        }
    }

    private void NavigateToRegisteredPage(string pageTag)
    {
        if (!string.IsNullOrWhiteSpace(pageTag))
        {
            NavigationService.NavigateTo(pageTag);
        }
    }
}

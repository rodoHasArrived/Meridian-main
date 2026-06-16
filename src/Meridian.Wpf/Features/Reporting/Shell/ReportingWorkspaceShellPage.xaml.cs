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
        ViewModel.RefreshRequested += OnRefreshRequested;
        ViewModel.Start();
        await ViewModel.RefreshAsync().ConfigureAwait(true);
        await RestoreShellDockLayoutAsync(ReportingDockManager).ConfigureAwait(true);
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.RefreshRequested -= OnRefreshRequested;
        ViewModel.Stop();
        SaveShellDockLayout(ReportingDockManager);
    }

    private async void OnRefreshRequested(object? sender, EventArgs e)
        => await ViewModel.RefreshAsync().ConfigureAwait(true);

    private void OnPaneDropRequested(object? sender, PaneDropEventArgs e)
        => OpenDroppedPane(ReportingDockManager, e);

    private void OnCommandBarCommandInvoked(object sender, WorkspaceCommandInvokedEventArgs e)
        => NavigateToRegisteredPage(e.Command.Id);

    private void OnCockpitDecisionInvoked(object sender, WorkspaceDecisionInvokedEventArgs e)
        => NavigateToRegisteredPage(e.ActionId);

    private void OnNavigateButtonClick(object sender, RoutedEventArgs e)
        => NavigateToTaggedPage(sender);
}

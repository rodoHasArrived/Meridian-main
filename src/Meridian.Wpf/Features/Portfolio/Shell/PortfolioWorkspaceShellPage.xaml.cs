using System.Windows;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Features.Portfolio.Shell;

public partial class PortfolioWorkspaceShellPage : PortfolioWorkspaceShellPageBase
{
    public PortfolioWorkspaceShellPage(
        NavigationService navigationService,
        PortfolioWorkspaceShellStateProvider stateProvider,
        PortfolioWorkspaceShellViewModel viewModel)
        : base(navigationService, stateProvider, viewModel)
    {
        InitializeComponent();
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.RefreshRequested += OnRefreshRequested;
        ViewModel.Start();
        await ViewModel.RefreshShellContextAsync().ConfigureAwait(true);
        await RestoreShellDockLayoutAsync(PortfolioDockManager).ConfigureAwait(true);
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.RefreshRequested -= OnRefreshRequested;
        ViewModel.Stop();
        SaveShellDockLayout(PortfolioDockManager);
    }

    private async void OnRefreshRequested(object? sender, EventArgs e)
        => await ViewModel.RefreshShellContextAsync().ConfigureAwait(true);

    private void OnPaneDropRequested(object? sender, PaneDropEventArgs e)
        => OpenDroppedPane(PortfolioDockManager, e);

    private void OnCommandBarCommandInvoked(object sender, WorkspaceCommandInvokedEventArgs e)
        => NavigateToRegisteredPage(e.Command.Id);

    private void OnCockpitDecisionInvoked(object sender, WorkspaceDecisionInvokedEventArgs e)
        => NavigateToRegisteredPage(e.ActionId);

    private void OnNavigateButtonClick(object sender, RoutedEventArgs e)
        => NavigateToTaggedPage(sender);
}

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
        await RestoreDockLayoutAsync(PortfolioDockManager).ConfigureAwait(true);
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _ = SaveDockLayoutAsync(PortfolioDockManager);
    }

    private void OnPaneDropRequested(object? sender, PaneDropEventArgs e)
        => OpenWorkspacePage(PortfolioDockManager, e.PageTag, e.Action);
}

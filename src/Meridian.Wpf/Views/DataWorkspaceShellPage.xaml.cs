using System.Windows;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public sealed class DataWorkspaceShellPage : DataOperationsWorkspaceShellPageBase
{
    private readonly MeridianDockingManager _dockManager = new();

    public DataWorkspaceShellPage(
        NavigationService navigationService,
        DataOperationsWorkspaceShellStateProvider stateProvider,
        DataWorkspaceShellViewModel viewModel)
        : base(navigationService, stateProvider, viewModel)
    {
        Content = _dockManager;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
        => await RestoreDockLayoutAsync(_dockManager).ConfigureAwait(true);

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnPageLoaded;
        Unloaded -= OnPageUnloaded;
        _ = SaveDockLayoutAsync(_dockManager);
    }
}

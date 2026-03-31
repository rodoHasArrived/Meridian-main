using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

public partial class StrategyRunsPage : Page
{
    private readonly StrategyRunBrowserViewModel _viewModel;

    public StrategyRunsPage()
    {
        InitializeComponent();
        _viewModel = new StrategyRunBrowserViewModel(
            WpfServices.StrategyRunWorkspaceService.Instance,
            WpfServices.NavigationService.Instance,
            WpfServices.WorkspaceService.Instance);
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnPageLoaded;
        await _viewModel.InitializeAsync();
    }
}

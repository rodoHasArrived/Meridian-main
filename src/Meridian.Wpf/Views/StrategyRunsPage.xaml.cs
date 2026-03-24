using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class StrategyRunsPage : Page
{
    private readonly StrategyRunBrowserViewModel _viewModel;

    public StrategyRunsPage()
    {
        InitializeComponent();
        _viewModel = new StrategyRunBrowserViewModel();
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnPageLoaded;
        await _viewModel.InitializeAsync();
    }
}

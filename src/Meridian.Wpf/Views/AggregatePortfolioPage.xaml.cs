using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Code-behind for <see cref="AggregatePortfolioPage"/>.
/// Thin code-behind — all state lives in <see cref="AggregatePortfolioViewModel"/>.
/// </summary>
public partial class AggregatePortfolioPage : Page
{
    private readonly AggregatePortfolioViewModel _viewModel;

    public AggregatePortfolioPage()
    {
        InitializeComponent();
        _viewModel = new AggregatePortfolioViewModel(ApiClientService.Instance);
        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _ = _viewModel.InitializeAsync();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Dispose();
    }
}

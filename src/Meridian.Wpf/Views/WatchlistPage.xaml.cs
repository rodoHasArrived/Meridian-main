using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Watchlist management page — thin code-behind.
/// All state, business logic, and commands live in <see cref="WatchlistViewModel"/>.
/// </summary>
public partial class WatchlistPage : Page
{
    private readonly WatchlistViewModel _viewModel;

    public WatchlistPage(WatchlistViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e) =>
        await _viewModel.StartAsync();

    private void OnPageUnloaded(object sender, RoutedEventArgs e) =>
        _viewModel.Stop();
}

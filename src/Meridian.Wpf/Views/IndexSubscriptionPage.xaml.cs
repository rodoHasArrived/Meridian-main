using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Index subscription page for subscribing to index constituents
/// (S&amp;P 500, Nasdaq 100, Dow 30, sector ETFs) for bulk symbol management.
/// </summary>
public partial class IndexSubscriptionPage : Page
{
    private readonly IndexSubscriptionViewModel _viewModel = new();

    public IndexSubscriptionPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.LoadSectorETFs();
    }

    private async void SubscribeSP500_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.SubscribeIndexAsync("SPY", "S&P 500");
    }

    private async void SubscribeNasdaq100_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.SubscribeIndexAsync("QQQ", "Nasdaq 100");
    }

    private async void SubscribeDow30_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.SubscribeIndexAsync("DIA", "Dow Jones 30");
    }

    private async void SubscribeSectorETF_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string symbol)
        {
            var etf = _viewModel.SectorETFs.FirstOrDefault(s => s.Symbol == symbol);
            var name = etf?.Name ?? symbol;
            await _viewModel.SubscribeIndexAsync(symbol, name);
        }
    }

    private async void LoadCustomIndex_Click(object sender, RoutedEventArgs e)
    {
        var symbol = _viewModel.CustomSymbol.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            MessageBox.Show("Please enter a symbol.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await _viewModel.SubscribeIndexAsync(symbol, symbol);
    }
}

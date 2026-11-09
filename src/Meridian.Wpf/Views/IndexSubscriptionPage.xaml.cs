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
        => await global::Meridian.Wpf.Services.AsyncHandlerGuard.RunAsync(() => SubscribeSP500_ClickCoreAsync(sender, e), nameof(SubscribeSP500_Click)).ConfigureAwait(true);

    private async System.Threading.Tasks.Task SubscribeSP500_ClickCoreAsync(object sender, RoutedEventArgs e)
    {
        await _viewModel.SubscribeIndexAsync("SPY", "S&P 500");
    }

    private async void SubscribeNasdaq100_Click(object sender, RoutedEventArgs e)
        => await global::Meridian.Wpf.Services.AsyncHandlerGuard.RunAsync(() => SubscribeNasdaq100_ClickCoreAsync(sender, e), nameof(SubscribeNasdaq100_Click)).ConfigureAwait(true);

    private async System.Threading.Tasks.Task SubscribeNasdaq100_ClickCoreAsync(object sender, RoutedEventArgs e)
    {
        await _viewModel.SubscribeIndexAsync("QQQ", "Nasdaq 100");
    }

    private async void SubscribeDow30_Click(object sender, RoutedEventArgs e)
        => await global::Meridian.Wpf.Services.AsyncHandlerGuard.RunAsync(() => SubscribeDow30_ClickCoreAsync(sender, e), nameof(SubscribeDow30_Click)).ConfigureAwait(true);

    private async System.Threading.Tasks.Task SubscribeDow30_ClickCoreAsync(object sender, RoutedEventArgs e)
    {
        await _viewModel.SubscribeIndexAsync("DIA", "Dow Jones 30");
    }

    private async void SubscribeSectorETF_Click(object sender, RoutedEventArgs e)
        => await global::Meridian.Wpf.Services.AsyncHandlerGuard.RunAsync(() => SubscribeSectorETF_ClickCoreAsync(sender, e), nameof(SubscribeSectorETF_Click)).ConfigureAwait(true);

    private async System.Threading.Tasks.Task SubscribeSectorETF_ClickCoreAsync(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string symbol)
        {
            var etf = _viewModel.SectorETFs.FirstOrDefault(s => s.Symbol == symbol);
            var name = etf?.Name ?? symbol;
            await _viewModel.SubscribeIndexAsync(symbol, name);
        }
    }

    private async void LoadCustomIndex_Click(object sender, RoutedEventArgs e)
        => await global::Meridian.Wpf.Services.AsyncHandlerGuard.RunAsync(() => LoadCustomIndex_ClickCoreAsync(sender, e), nameof(LoadCustomIndex_Click)).ConfigureAwait(true);

    private async System.Threading.Tasks.Task LoadCustomIndex_ClickCoreAsync(object sender, RoutedEventArgs e)
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

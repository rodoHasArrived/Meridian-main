using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Index subscription page for subscribing to index constituents
/// (S&amp;P 500, Nasdaq 100, Dow 30, sector ETFs) for bulk symbol management.
/// </summary>
public partial class IndexSubscriptionPage : Page
{
    private readonly PortfolioImportService _portfolioService;
    private readonly WpfServices.LoggingService _loggingService;
    private readonly ObservableCollection<SectorETFItem> _sectorETFs = new();

    public IndexSubscriptionPage()
    {
        InitializeComponent();

        _portfolioService = PortfolioImportService.Instance;
        _loggingService = WpfServices.LoggingService.Instance;

        SectorETFsList.ItemsSource = _sectorETFs;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        LoadSectorETFs();
    }

    private void LoadSectorETFs()
    {
        _sectorETFs.Clear();

        var etfs = new[]
        {
            ("XLK", "Technology Select Sector", "~70"),
            ("XLF", "Financial Select Sector", "~70"),
            ("XLV", "Health Care Select Sector", "~65"),
            ("XLY", "Consumer Discretionary", "~55"),
            ("XLP", "Consumer Staples", "~35"),
            ("XLE", "Energy Select Sector", "~25"),
            ("XLI", "Industrial Select Sector", "~75"),
            ("XLB", "Materials Select Sector", "~30"),
            ("XLU", "Utilities Select Sector", "~30"),
            ("XLRE", "Real Estate Select Sector", "~30")
        };

        foreach (var (symbol, name, holdings) in etfs)
        {
            _sectorETFs.Add(new SectorETFItem
            {
                Symbol = symbol,
                Name = name,
                HoldingsText = $"{holdings} holdings"
            });
        }
    }

    private async void SubscribeSP500_Click(object sender, RoutedEventArgs e)
    {
        await SubscribeIndexAsync("SPY", "S&P 500");
    }

    private async void SubscribeNasdaq100_Click(object sender, RoutedEventArgs e)
    {
        await SubscribeIndexAsync("QQQ", "Nasdaq 100");
    }

    private async void SubscribeDow30_Click(object sender, RoutedEventArgs e)
    {
        await SubscribeIndexAsync("DIA", "Dow Jones 30");
    }

    private async void SubscribeSectorETF_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string symbol)
        {
            var etf = _sectorETFs.FirstOrDefault(s => s.Symbol == symbol);
            var name = etf?.Name ?? symbol;
            await SubscribeIndexAsync(symbol, name);
        }
    }

    private async void LoadCustomIndex_Click(object sender, RoutedEventArgs e)
    {
        var symbol = CustomSymbolBox.Text?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            MessageBox.Show("Please enter a symbol.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await SubscribeIndexAsync(symbol, symbol);
    }

    private async System.Threading.Tasks.Task SubscribeIndexAsync(string symbol, string displayName)
    {
        ShowStatus($"Loading constituents for {displayName}...", true);

        try
        {
            var constituents = await _portfolioService.GetIndexConstituentsAsync(symbol);
            if (constituents != null && constituents.Symbols.Count > 0)
            {
                var result = MessageBox.Show(
                    $"Found {constituents.Symbols.Count} constituents for {displayName}.\n\n" +
                    $"Subscribe to {(SubscribeTradesCheck.IsChecked == true ? "trades" : "")}" +
                    $"{(SubscribeDepthCheck.IsChecked == true ? " + depth" : "")} data?\n\n" +
                    $"Symbols: {string.Join(", ", constituents.Symbols.Take(10))}" +
                    (constituents.Symbols.Count > 10 ? $"... and {constituents.Symbols.Count - 10} more" : ""),
                    "Confirm Subscription",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    ShowStatus($"Subscribing to {constituents.Symbols.Count} symbols...", true);
                    await _portfolioService.ImportSymbolsAsync(constituents.Symbols);
                    ShowStatus($"Successfully subscribed to {constituents.Symbols.Count} symbols from {displayName}.", false);

                    WpfServices.MessagingService.Instance.SendNamed(
                        WpfServices.MessageTypes.SymbolsUpdated,
                        new { Source = displayName, Count = constituents.Symbols.Count });
                }
                else
                {
                    HideStatus();
                }
            }
            else
            {
                ShowStatus($"No constituents found for {displayName}. The symbol may not be a supported index or ETF.", false);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to subscribe to " + displayName, ex);
            ShowStatus($"Failed to load constituents: {ex.Message}", false);
        }
    }

    private void ShowStatus(string message, bool showProgress)
    {
        StatusPanel.Visibility = Visibility.Visible;
        StatusText.Text = message;
        SubscriptionProgress.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HideStatus()
    {
        StatusPanel.Visibility = Visibility.Collapsed;
    }

    public sealed class SectorETFItem
    {
        public string Symbol { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string HoldingsText { get; set; } = string.Empty;
    }
}

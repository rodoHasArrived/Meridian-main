using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Meridian.Ui.Services;
using LoggingService = Meridian.Wpf.Services.LoggingService;
using MessagingService = Meridian.Wpf.Services.MessagingService;
using MessageTypes = Meridian.Wpf.Services.MessageTypes;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Index Subscription page. Manages sector ETF data and subscription
/// operations for bulk symbol management.
/// </summary>
public sealed class IndexSubscriptionViewModel : BindableBase
{
    private readonly PortfolioImportService _portfolioService;
    private readonly LoggingService _loggingService;

    private bool _isStatusVisible;
    private string _statusMessage = string.Empty;
    private bool _isSubscriptionInProgress;
    private bool _subscribeTrades = true;
    private bool _subscribeDepth;
    private string _customSymbol = string.Empty;

    public IndexSubscriptionViewModel()
    {
        _portfolioService = PortfolioImportService.Instance;
        _loggingService = LoggingService.Instance;
        SectorETFs = new ObservableCollection<SectorETFItem>();
    }

    public ObservableCollection<SectorETFItem> SectorETFs { get; }

    public bool IsStatusVisible
    {
        get => _isStatusVisible;
        private set => SetProperty(ref _isStatusVisible, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsSubscriptionInProgress
    {
        get => _isSubscriptionInProgress;
        private set => SetProperty(ref _isSubscriptionInProgress, value);
    }

    public bool SubscribeTrades
    {
        get => _subscribeTrades;
        set => SetProperty(ref _subscribeTrades, value);
    }

    public bool SubscribeDepth
    {
        get => _subscribeDepth;
        set => SetProperty(ref _subscribeDepth, value);
    }

    public string CustomSymbol
    {
        get => _customSymbol;
        set => SetProperty(ref _customSymbol, value);
    }

    public void LoadSectorETFs()
    {
        SectorETFs.Clear();

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
            SectorETFs.Add(new SectorETFItem
            {
                Symbol = symbol,
                Name = name,
                HoldingsText = $"{holdings} holdings"
            });
        }
    }

    public async Task SubscribeIndexAsync(string symbol, string displayName)
    {
        ShowStatus($"Loading constituents for {displayName}...", true);

        try
        {
            var constituents = await _portfolioService.GetIndexConstituentsAsync(symbol);
            if (constituents != null && constituents.Symbols.Count > 0)
            {
                var tradesText = SubscribeTrades ? "trades" : string.Empty;
                var depthText = SubscribeDepth ? " + depth" : string.Empty;

                var result = MessageBox.Show(
                    $"Found {constituents.Symbols.Count} constituents for {displayName}.\n\n" +
                    $"Subscribe to {tradesText}{depthText} data?\n\n" +
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

                    MessagingService.Instance.SendNamed(
                        MessageTypes.SymbolsUpdated,
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
        StatusMessage = message;
        IsStatusVisible = true;
        IsSubscriptionInProgress = showProgress;
    }

    private void HideStatus()
    {
        IsStatusVisible = false;
        IsSubscriptionInProgress = false;
        StatusMessage = string.Empty;
    }
}

public sealed class SectorETFItem
{
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string HoldingsText { get; set; } = string.Empty;
}

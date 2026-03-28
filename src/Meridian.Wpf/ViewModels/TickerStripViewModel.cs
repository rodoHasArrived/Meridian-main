using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Meridian.Ui.Services;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Represents a single ticker item in the always-on-top ticker strip.
/// Tracks bid/ask/last price display and a brief flash on new trades.
/// </summary>
public sealed class TickerItemViewModel : BindableBase
{
    private static readonly SolidColorBrush UptickBrush;
    private static readonly SolidColorBrush DowntickBrush;
    private static readonly SolidColorBrush NeutralBrush;

    static TickerItemViewModel()
    {
        UptickBrush = new SolidColorBrush(Color.FromRgb(63, 185, 80));
        DowntickBrush = new SolidColorBrush(Color.FromRgb(244, 67, 54));
        NeutralBrush = new SolidColorBrush(Colors.White);
        UptickBrush.Freeze();
        DowntickBrush.Freeze();
        NeutralBrush.Freeze();
    }

    private readonly DispatcherTimer _flashTimer;

    private string _symbol = string.Empty;
    private string _bidText = "--";
    private string _askText = "--";
    private string _lastText = "--";
    private SolidColorBrush _priceColor = NeutralBrush;
    private bool _isFlashing;

    public TickerItemViewModel()
    {
        _flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _flashTimer.Tick += (_, _) =>
        {
            IsFlashing = false;
            _flashTimer.Stop();
        };
    }

    public string Symbol { get => _symbol; set => SetProperty(ref _symbol, value); }
    public string BidText { get => _bidText; set => SetProperty(ref _bidText, value); }
    public string AskText { get => _askText; set => SetProperty(ref _askText, value); }
    public string LastText { get => _lastText; set => SetProperty(ref _lastText, value); }
    public SolidColorBrush PriceColor { get => _priceColor; set => SetProperty(ref _priceColor, value); }

    /// <summary>Briefly true on each new trade tick; auto-resets after 500 ms.</summary>
    public bool IsFlashing { get => _isFlashing; set => SetProperty(ref _isFlashing, value); }

    /// <summary>Updates displayed prices and triggers a 500 ms flash.</summary>
    public void Update(decimal bid, decimal ask, decimal last, bool uptick)
    {
        BidText = bid > 0 ? bid.ToString("F2") : "--";
        AskText = ask > 0 ? ask.ToString("F2") : "--";
        LastText = last > 0 ? last.ToString("F2") : "--";
        PriceColor = last > 0 ? (uptick ? UptickBrush : DowntickBrush) : NeutralBrush;

        if (last > 0)
        {
            IsFlashing = true;
            _flashTimer.Stop();
            _flashTimer.Start();
        }
    }
}

/// <summary>
/// ViewModel for the always-on-top ticker strip window.
/// Polls the backend HTTP API for live bid/ask/last data for each watchlist symbol.
/// </summary>
public sealed class TickerStripViewModel : BindableBase, IDisposable
{
    private static readonly HttpClient _httpClient = new();

    private readonly DispatcherTimer _pollTimer;
    private readonly System.Collections.Generic.Dictionary<string, decimal> _lastPrices = new();
    private CancellationTokenSource? _cts;
    private IDisposable? _watchlistSubscription;
    private readonly WpfServices.MessagingService _messagingService;
    private readonly SymbolManagementService _symbolManagementService;
    private readonly WpfServices.ConfigService _configService;
    private readonly WpfServices.StatusService _statusService;

    private bool _isVisible;

    public ObservableCollection<TickerItemViewModel> Items { get; } = new();

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public TickerStripViewModel(
        WpfServices.MessagingService messagingService,
        SymbolManagementService symbolManagementService,
        WpfServices.ConfigService configService,
        WpfServices.StatusService statusService)
    {
        _messagingService = messagingService;
        _symbolManagementService = symbolManagementService;
        _configService = configService;
        _statusService = statusService;

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _pollTimer.Tick += async (_, _) => await PollAllSymbolsAsync();

        // Reload symbol list whenever the watchlist changes
        _watchlistSubscription = _messagingService.Subscribe(
            WpfServices.MessageTypes.WatchlistUpdated,
            _ => System.Windows.Application.Current?.Dispatcher.InvokeAsync(async () => await LoadSymbolsAsync()));
    }

    /// <summary>Starts polling. Call once after the strip window is shown.</summary>
    public void Start()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = LoadSymbolsAsync();
        _pollTimer.Start();
    }

    /// <summary>Stops polling. Call when the strip window is hidden or closed.</summary>
    public void Stop()
    {
        _pollTimer.Stop();
        _cts?.Cancel();
    }

    /// <summary>
    /// Applies a price update to the matching ticker item (or creates one if new).
    /// Must be called on the UI thread.
    /// </summary>
    public void UpdateSymbol(string symbol, decimal bid, decimal ask, decimal last, bool uptick)
    {
        var item = Items.FirstOrDefault(i => i.Symbol == symbol);
        if (item is null)
        {
            item = new TickerItemViewModel { Symbol = symbol };
            Items.Add(item);
        }

        item.Update(bid, ask, last, uptick);
        _lastPrices[symbol] = last;
    }

    private async Task LoadSymbolsAsync()
    {
        try
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            // Use the same symbol-loading strategy as LiveDataViewerViewModel
            var symbolService = _symbolManagementService;
            var result = await symbolService.GetAllSymbolsAsync(_cts.Token);

            System.Collections.Generic.IEnumerable<string> symbols;
            if (result.Success && result.Symbols.Count > 0)
            {
                symbols = result.Symbols.Select(s => s.Symbol);
            }
            else
            {
                var configSymbols = await _configService.GetConfiguredSymbolsAsync(_cts.Token);
                symbols = configSymbols.Select(s => s.Symbol);
            }

            var symbolList = symbols.Take(30).ToList(); // cap at 30 symbols for strip width

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                // Add new symbols, preserve existing items so data is not lost
                foreach (var sym in symbolList.Where(s => Items.All(i => i.Symbol != s)))
                {
                    Items.Add(new TickerItemViewModel { Symbol = sym });
                }

                // Remove symbols that are no longer in the list
                for (var i = Items.Count - 1; i >= 0; i--)
                {
                    if (!symbolList.Contains(Items[i].Symbol))
                        Items.RemoveAt(i);
                }
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TickerStripViewModel] LoadSymbols error: {ex.Message}");
        }
    }

    private async Task PollAllSymbolsAsync()
    {
        var baseUrl = _statusService.BaseUrl;
        var symbols = System.Windows.Application.Current?.Dispatcher.Invoke(() => Items.Select(i => i.Symbol).ToList())
                      ?? new System.Collections.Generic.List<string>();

        foreach (var symbol in symbols)
        {
            await PollSymbolAsync(baseUrl, symbol);
        }
    }

    private async Task PollSymbolAsync(string baseUrl, string symbol)
    {
        try
        {
            var url = $"{baseUrl}/api/live/{Uri.EscapeDataString(symbol)}/quote";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return;

            var json = await response.Content.ReadAsStringAsync();
            var quote = JsonSerializer.Deserialize<JsonElement>(json);

            var bid = quote.TryGetProperty("bid", out var b) ? b.GetDecimal() : 0m;
            var ask = quote.TryGetProperty("ask", out var a) ? a.GetDecimal() : 0m;
            var last = quote.TryGetProperty("last", out var l) ? l.GetDecimal() : 0m;

            // Fall back to "price" key if "last" is absent (some endpoints use this)
            if (last == 0 && quote.TryGetProperty("price", out var p)) last = p.GetDecimal();

            var prevLast = _lastPrices.TryGetValue(symbol, out var prev) ? prev : 0m;
            var uptick = last >= prevLast;

            System.Windows.Application.Current?.Dispatcher.Invoke(() => UpdateSymbol(symbol, bid, ask, last, uptick));
        }
        catch (HttpRequestException) { }
        catch (JsonException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TickerStripViewModel] Poll error for {symbol}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Stop();
        _watchlistSubscription?.Dispose();
        _cts?.Dispose();
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Models;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Order Book page.
/// Holds all state, HTTP polling, symbol loading, and connection-status tracking so that
/// the code-behind is thinned to lifecycle wiring only.
/// </summary>
public sealed class OrderBookViewModel : BindableBase, IDisposable
{
    private readonly HttpClient _httpClient = new();
    private readonly WpfServices.StatusService _statusService;
    private readonly WpfServices.ConnectionService _connectionService;
    private readonly WpfServices.LoggingService _loggingService;

    private readonly DispatcherTimer _refreshTimer;
    private CancellationTokenSource? _cts;
    private readonly string _baseUrl;

    // ── Collections ───────────────────────────────────────────────────────────────

    public ObservableCollection<OrderBookDisplayLevel> Bids { get; } = new();
    public ObservableCollection<OrderBookDisplayLevel> Asks { get; } = new();
    public ObservableCollection<RecentTradeModel> RecentTrades { get; } = new();
    public ObservableCollection<string> AvailableSymbols { get; } = new();

    // ── Connection status ─────────────────────────────────────────────────────────

    private string _connectionStatusText = "Disconnected";
    public string ConnectionStatusText
    {
        get => _connectionStatusText;
        private set => SetProperty(ref _connectionStatusText, value);
    }

    private SolidColorBrush _connectionIndicatorFill = new(Color.FromRgb(139, 148, 158));
    public SolidColorBrush ConnectionIndicatorFill
    {
        get => _connectionIndicatorFill;
        private set => SetProperty(ref _connectionIndicatorFill, value);
    }

    // ── Visibility flags ──────────────────────────────────────────────────────────

    private bool _noDataVisible = true;
    public bool NoDataVisible { get => _noDataVisible; private set => SetProperty(ref _noDataVisible, value); }

    private bool _noTradesVisible = true;
    public bool NoTradesVisible { get => _noTradesVisible; private set => SetProperty(ref _noTradesVisible, value); }

    // ── Statistics ────────────────────────────────────────────────────────────────

    private string _bestBidText = "--";
    public string BestBidText { get => _bestBidText; private set => SetProperty(ref _bestBidText, value); }

    private string _bestAskText = "--";
    public string BestAskText { get => _bestAskText; private set => SetProperty(ref _bestAskText, value); }

    private string _midPriceText = "--";
    public string MidPriceText { get => _midPriceText; private set => SetProperty(ref _midPriceText, value); }

    private string _spreadText = "--";
    public string SpreadText { get => _spreadText; private set => SetProperty(ref _spreadText, value); }

    private string _spreadPercentText = string.Empty;
    public string SpreadPercentText { get => _spreadPercentText; private set => SetProperty(ref _spreadPercentText, value); }

    private string _bidVolumeText = "--";
    public string BidVolumeText { get => _bidVolumeText; private set => SetProperty(ref _bidVolumeText, value); }

    private string _askVolumeText = "--";
    public string AskVolumeText { get => _askVolumeText; private set => SetProperty(ref _askVolumeText, value); }

    private string _imbalanceText = "--";
    public string ImbalanceText { get => _imbalanceText; private set => SetProperty(ref _imbalanceText, value); }

    // ── Imbalance bar column widths ───────────────────────────────────────────────

    private GridLength _bidBarWidth = new(0.5, GridUnitType.Star);
    public GridLength BidBarWidth { get => _bidBarWidth; private set => SetProperty(ref _bidBarWidth, value); }

    private GridLength _askBarWidth = new(0.5, GridUnitType.Star);
    public GridLength AskBarWidth { get => _askBarWidth; private set => SetProperty(ref _askBarWidth, value); }

    // ── Symbol/level selection (called from code-behind event relays) ─────────────

    private string _selectedSymbol = string.Empty;
    private int _depthLevels = 10;

    // ─────────────────────────────────────────────────────────────────────────────

    public OrderBookViewModel(
        WpfServices.StatusService statusService,
        WpfServices.ConnectionService connectionService,
        WpfServices.LoggingService loggingService)
    {
        _statusService = statusService;
        _connectionService = connectionService;
        _loggingService = loggingService;
        _baseUrl = statusService.BaseUrl;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _refreshTimer.Tick += async (_, _) => await RefreshOrderBookAsync();
    }

    /// <summary>Subscribes to events, loads symbols, and starts the refresh timer.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        _connectionService.StateChanged += OnConnectionStateChanged;
        UpdateConnectionStatus();
        await LoadSymbolsAsync();
        _refreshTimer.Start();
    }

    /// <summary>Stops the timer and unsubscribes from events.</summary>
    public void Stop()
    {
        _connectionService.StateChanged -= OnConnectionStateChanged;
        _refreshTimer.Stop();
        _cts?.Cancel();
    }

    public void SetSymbol(string symbol)
    {
        _selectedSymbol = symbol;
        Bids.Clear();
        Asks.Clear();
        RecentTrades.Clear();
        NoDataVisible = true;
        NoTradesVisible = true;
    }

    public void SetDepthLevels(int levels)
    {
        _depthLevels = levels;
    }

    // ── Internal logic ────────────────────────────────────────────────────────────

    private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e) =>
        UpdateConnectionStatus();

    private void UpdateConnectionStatus()
    {
        var state = _connectionService.State;

        ConnectionStatusText = state switch
        {
            ConnectionState.Connected => "Connected",
            ConnectionState.Reconnecting => "Reconnecting...",
            _ => "Disconnected"
        };

        ConnectionIndicatorFill = new SolidColorBrush(state switch
        {
            ConnectionState.Connected => Color.FromRgb(63, 185, 80),
            ConnectionState.Reconnecting => Color.FromRgb(255, 193, 7),
            _ => Color.FromRgb(139, 148, 158)
        });
    }

    private async Task LoadSymbolsAsync(CancellationToken ct = default)
    {
        try
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            var status = await _statusService.GetStatusAsync(_cts.Token);
            if (status != null)
            {
                AvailableSymbols.Clear();
                foreach (var sym in new[] { "SPY", "AAPL", "MSFT", "GOOGL", "AMZN", "QQQ", "IWM", "DIA" })
                    AvailableSymbols.Add(sym);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled — ignore
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to load symbols", ex);
        }
    }

    private async Task RefreshOrderBookAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_selectedSymbol))
            return;

        try
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            var response = await _httpClient.GetAsync(
                $"{_baseUrl}/api/orderbook/{Uri.EscapeDataString(_selectedSymbol)}?levels={_depthLevels}",
                _cts.Token);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(_cts.Token);
                var data = JsonSerializer.Deserialize<JsonElement>(json);
                ProcessOrderBookData(data);
                NoDataVisible = false;
            }
            else if (Bids.Count == 0 && Asks.Count == 0)
            {
                LoadDemoOrderBook();
            }

            await RefreshRecentTradesAsync();
        }
        catch (OperationCanceledException)
        {
            // Cancelled — ignore
        }
        catch (HttpRequestException)
        {
            if (Bids.Count == 0 && Asks.Count == 0)
                LoadDemoOrderBook();
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to refresh order book", ex);
        }
    }

    private void ProcessOrderBookData(JsonElement data)
    {
        decimal maxSize = 0;

        var newBids = new List<OrderBookDisplayLevel>();
        if (data.TryGetProperty("bids", out var bids) && bids.ValueKind == JsonValueKind.Array)
        {
            decimal runningTotal = 0;
            foreach (var bid in bids.EnumerateArray().Take(_depthLevels))
            {
                var price = bid.TryGetProperty("price", out var p) ? p.GetDecimal() : 0;
                var size = bid.TryGetProperty("size", out var s) ? s.GetInt32() : 0;
                runningTotal += size;
                newBids.Add(new OrderBookDisplayLevel
                {
                    RawPrice = price,
                    Price = price.ToString("F2"),
                    RawSize = size,
                    Size = FormatSize(size),
                    RawTotal = runningTotal,
                    Total = FormatSize((int)runningTotal)
                });
                if (size > maxSize) maxSize = size;
            }
        }

        var newAsks = new List<OrderBookDisplayLevel>();
        if (data.TryGetProperty("asks", out var asks) && asks.ValueKind == JsonValueKind.Array)
        {
            decimal runningTotal = 0;
            foreach (var ask in asks.EnumerateArray().Take(_depthLevels))
            {
                var price = ask.TryGetProperty("price", out var p) ? p.GetDecimal() : 0;
                var size = ask.TryGetProperty("size", out var s) ? s.GetInt32() : 0;
                runningTotal += size;
                newAsks.Add(new OrderBookDisplayLevel
                {
                    RawPrice = price,
                    Price = price.ToString("F2"),
                    RawSize = size,
                    Size = FormatSize(size),
                    RawTotal = runningTotal,
                    Total = FormatSize((int)runningTotal)
                });
                if (size > maxSize) maxSize = size;
            }
        }

        var maxWidth = 200.0;
        foreach (var level in newBids.Concat(newAsks))
            level.DepthWidth = maxSize > 0 ? (double)level.RawSize / (double)maxSize * maxWidth : 0;

        Bids.Clear();
        foreach (var bid in newBids) Bids.Add(bid);

        Asks.Clear();
        foreach (var ask in newAsks.OrderByDescending(a => a.RawPrice)) Asks.Add(ask);

        UpdateStatistics(newBids, newAsks);
    }

    private void LoadDemoOrderBook()
    {
        var basePrice = _selectedSymbol switch
        {
            "SPY" => 485.50m,
            "AAPL" => 178.25m,
            "MSFT" => 405.75m,
            "GOOGL" => 142.30m,
            "AMZN" => 175.80m,
            _ => 100.00m
        };

        var random = new Random();
        var bidList = new List<OrderBookDisplayLevel>();
        var askList = new List<OrderBookDisplayLevel>();
        decimal bidTotal = 0, askTotal = 0, maxSize = 0;

        for (int i = 0; i < _depthLevels; i++)
        {
            var bidSize = random.Next(100, 5000);
            var askSize = random.Next(100, 5000);
            bidTotal += bidSize;
            askTotal += askSize;
            if (bidSize > maxSize) maxSize = bidSize;
            if (askSize > maxSize) maxSize = askSize;

            bidList.Add(new OrderBookDisplayLevel
            {
                RawPrice = basePrice - (i * 0.01m),
                Price = (basePrice - (i * 0.01m)).ToString("F2"),
                RawSize = bidSize,
                Size = FormatSize(bidSize),
                RawTotal = bidTotal,
                Total = FormatSize((int)bidTotal)
            });

            askList.Add(new OrderBookDisplayLevel
            {
                RawPrice = basePrice + 0.01m + (i * 0.01m),
                Price = (basePrice + 0.01m + (i * 0.01m)).ToString("F2"),
                RawSize = askSize,
                Size = FormatSize(askSize),
                RawTotal = askTotal,
                Total = FormatSize((int)askTotal)
            });
        }

        var maxWidth = 200.0;
        foreach (var level in bidList.Concat(askList))
            level.DepthWidth = maxSize > 0 ? (double)level.RawSize / (double)maxSize * maxWidth : 0;

        Bids.Clear();
        foreach (var bid in bidList) Bids.Add(bid);

        Asks.Clear();
        foreach (var ask in askList.OrderByDescending(a => a.RawPrice)) Asks.Add(ask);

        UpdateStatistics(bidList, askList);
        NoDataVisible = false;
        LoadDemoTrades(basePrice);
    }

    private void LoadDemoTrades(decimal basePrice)
    {
        var random = new Random();
        RecentTrades.Clear();

        for (int i = 0; i < 15; i++)
        {
            var isBuy = random.Next(2) == 0;
            var price = basePrice + (random.Next(-10, 11) * 0.01m);
            RecentTrades.Add(new RecentTradeModel
            {
                Time = DateTime.Now.AddSeconds(-i * random.Next(1, 5)).ToString("HH:mm:ss"),
                Price = price.ToString("F2"),
                Size = FormatSize(random.Next(10, 1000)),
                PriceColor = new SolidColorBrush(isBuy
                    ? Color.FromRgb(63, 185, 80)
                    : Color.FromRgb(244, 67, 54))
            });
        }

        NoTradesVisible = false;
    }

    private void UpdateStatistics(List<OrderBookDisplayLevel> bids, List<OrderBookDisplayLevel> asks)
    {
        if (bids.Count == 0 || asks.Count == 0)
        {
            BestBidText = "--";
            BestAskText = "--";
            MidPriceText = "--";
            SpreadText = "--";
            SpreadPercentText = string.Empty;
            BidVolumeText = "--";
            AskVolumeText = "--";
            ImbalanceText = "--";
            return;
        }

        var bestBid = bids.Max(b => b.RawPrice);
        var bestAsk = asks.Min(a => a.RawPrice);
        var spread = bestAsk - bestBid;
        var mid = (bestBid + bestAsk) / 2;
        var spreadPercent = mid > 0 ? spread / mid * 100 : 0;
        var bidVolume = bids.Sum(b => (decimal)b.RawSize);
        var askVolume = asks.Sum(a => (decimal)a.RawSize);
        var totalVolume = bidVolume + askVolume;
        var imbalance = totalVolume > 0 ? (bidVolume - askVolume) / totalVolume * 100 : 0;

        BestBidText = bestBid.ToString("F2");
        BestAskText = bestAsk.ToString("F2");
        MidPriceText = mid.ToString("F2");
        SpreadText = spread.ToString("F2");
        SpreadPercentText = $"({spreadPercent:F3}%)";
        BidVolumeText = FormatSize((int)bidVolume);
        AskVolumeText = FormatSize((int)askVolume);
        ImbalanceText = $"{imbalance:+0.0;-0.0;0.0}%";

        var bidRatio = totalVolume > 0 ? (double)bidVolume / (double)totalVolume : 0.5;
        BidBarWidth = new GridLength(bidRatio, GridUnitType.Star);
        AskBarWidth = new GridLength(1 - bidRatio, GridUnitType.Star);
    }

    private async Task RefreshRecentTradesAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{_baseUrl}/api/live/{Uri.EscapeDataString(_selectedSymbol)}/trades?limit=15",
                _cts!.Token);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(_cts.Token);
                var data = JsonSerializer.Deserialize<JsonElement>(json);

                if (data.ValueKind == JsonValueKind.Array)
                {
                    RecentTrades.Clear();

                    foreach (var trade in data.EnumerateArray())
                    {
                        var price = trade.TryGetProperty("price", out var p) ? p.GetDecimal() : 0;
                        var size = trade.TryGetProperty("size", out var s) ? s.GetInt32() : 0;
                        var timestamp = trade.TryGetProperty("timestamp", out var ts) ? ts.GetDateTime() : DateTime.UtcNow;
                        var side = trade.TryGetProperty("side", out var sd) ? sd.GetString() ?? "" : "";
                        var isBuy = side.Equals("buy", StringComparison.OrdinalIgnoreCase);

                        RecentTrades.Add(new RecentTradeModel
                        {
                            Time = timestamp.ToString("HH:mm:ss"),
                            Price = price.ToString("F2"),
                            Size = FormatSize(size),
                            PriceColor = new SolidColorBrush(isBuy
                                ? Color.FromRgb(63, 185, 80)
                                : Color.FromRgb(244, 67, 54))
                        });
                    }

                    NoTradesVisible = RecentTrades.Count == 0;
                }
            }
        }
        catch
        {
            // Ignore trade fetch errors — non-critical
        }
    }

    private static string FormatSize(int size)
    {
        if (size >= 1000000) return $"{size / 1000000.0:F1}M";
        if (size >= 1000) return $"{size / 1000.0:F1}K";
        return size.ToString("N0");
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _httpClient.Dispose();
    }
}

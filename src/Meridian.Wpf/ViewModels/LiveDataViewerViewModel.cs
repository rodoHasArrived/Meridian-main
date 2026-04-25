using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Live Data Viewer page.
/// Owns all state, timers, HTTP loading, and session statistics.
/// </summary>
public sealed class LiveDataViewerViewModel : BindableBase, IDisposable
{
    // P1: Static cached brushes — avoids allocating a new SolidColorBrush per market event.
    private static readonly SolidColorBrush BrushTrade = new(Color.FromRgb(63, 185, 80));
    private static readonly SolidColorBrush BrushQuote = new(Color.FromRgb(88, 166, 255));
    private static readonly SolidColorBrush BrushEventOther = new(Color.FromRgb(139, 148, 158));
    private static readonly SolidColorBrush BrushBuy = new(Color.FromRgb(63, 185, 80));
    private static readonly SolidColorBrush BrushSell = new(Color.FromRgb(244, 67, 54));
    private static readonly SolidColorBrush BrushConnected = new(Color.FromRgb(63, 185, 80));
    private static readonly SolidColorBrush BrushReconnecting = new(Color.FromRgb(255, 193, 7));
    private static readonly SolidColorBrush BrushDisconnected = new(Color.FromRgb(139, 148, 158));

    private readonly HttpClient _httpClient = new();
    private readonly WpfServices.StatusService _statusService;
    private readonly WpfServices.ConnectionService _connectionService;
    private readonly WpfServices.LoggingService _loggingService;
    private readonly WpfServices.NotificationService _notificationService;
    private readonly SymbolManagementService _symbolManagementService;
    private readonly WpfServices.TearOffPanelService _tearOffPanelService;
    private readonly WpfServices.ConfigService _configService;

    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _statsTimer;
    // P3: Two separate CancellationTokenSources so symbol loading and live-data polling
    //     cannot inadvertently cancel each other.
    private CancellationTokenSource? _loadSymbolsCts;
    private CancellationTokenSource? _liveDataCts;
    // P2: Persistent set of already-seen event IDs — updated incrementally instead of
    //     rebuilding a HashSet on every 500 ms timer tick.
    private readonly HashSet<string> _seenEventIds = new();
    private string _baseUrl;
    private string _selectedSymbol = string.Empty;
    private bool _isPaused;
    private int _eventsThisSecond;
    private int _totalEvents;
    private DateTime _lastStatsUpdate = DateTime.UtcNow;

    // Session statistics
    private decimal? _sessionHigh;
    private decimal? _sessionLow;
    private long _sessionVolume;
    private int _tradeCount;
    private decimal _vwapNumerator;
    private decimal _lastPrice;
    private decimal _bidPrice;
    private decimal _askPrice;
    private int _bidSize;
    private int _askSize;

    // ── Public collections ──────────────────────────────────────────────
    public ObservableCollection<LiveDataEventModel> LiveEvents { get; } = new();
    public List<string> AvailableSymbols { get; } = new();

    // ── Bindable properties ─────────────────────────────────────────────
    private string _connectionStatusText = "Disconnected";
    public string ConnectionStatusText { get => _connectionStatusText; private set => SetProperty(ref _connectionStatusText, value); }

    public string SelectedSymbol => string.IsNullOrWhiteSpace(_selectedSymbol) ? "No symbol" : _selectedSymbol;

    // P1: Initial value reuses the static cached brush instead of allocating a new instance.
    private SolidColorBrush _connectionIndicatorColor = BrushDisconnected;
    public SolidColorBrush ConnectionIndicatorColor { get => _connectionIndicatorColor; private set => SetProperty(ref _connectionIndicatorColor, value); }

    private bool _isPauseResumeEnabled;
    public bool IsPauseResumeEnabled { get => _isPauseResumeEnabled; private set => SetProperty(ref _isPauseResumeEnabled, value); }

    private string _pauseResumeButtonText = "Pause";
    public string PauseResumeButtonText { get => _pauseResumeButtonText; private set => SetProperty(ref _pauseResumeButtonText, value); }

    private bool _noDataVisible = true;
    public bool NoDataVisible { get => _noDataVisible; private set => SetProperty(ref _noDataVisible, value); }

    private string _totalEventsText = "0";
    public string TotalEventsText { get => _totalEventsText; private set => SetProperty(ref _totalEventsText, value); }

    private string _eventRateText = "0";
    public string EventRateText { get => _eventRateText; private set => SetProperty(ref _eventRateText, value); }

    private string _bidPriceText = "--";
    public string BidPriceText { get => _bidPriceText; private set => SetProperty(ref _bidPriceText, value); }

    private string _bidSizeText = "--";
    public string BidSizeText { get => _bidSizeText; private set => SetProperty(ref _bidSizeText, value); }

    private string _askPriceText = "--";
    public string AskPriceText { get => _askPriceText; private set => SetProperty(ref _askPriceText, value); }

    private string _askSizeText = "--";
    public string AskSizeText { get => _askSizeText; private set => SetProperty(ref _askSizeText, value); }

    private string _spreadText = "--";
    public string SpreadText { get => _spreadText; private set => SetProperty(ref _spreadText, value); }

    private string _midPriceText = "--";
    public string MidPriceText { get => _midPriceText; private set => SetProperty(ref _midPriceText, value); }

    private string _lastTradeText = "--";
    public string LastTradeText { get => _lastTradeText; private set => SetProperty(ref _lastTradeText, value); }

    private string _lastTradeTimeText = "--";
    public string LastTradeTimeText { get => _lastTradeTimeText; private set => SetProperty(ref _lastTradeTimeText, value); }

    private string _sessionHighText = "--";
    public string SessionHighText { get => _sessionHighText; private set => SetProperty(ref _sessionHighText, value); }

    private string _sessionLowText = "--";
    public string SessionLowText { get => _sessionLowText; private set => SetProperty(ref _sessionLowText, value); }

    private string _sessionVolumeText = "--";
    public string SessionVolumeText { get => _sessionVolumeText; private set => SetProperty(ref _sessionVolumeText, value); }

    private string _tradeCountText = "--";
    public string TradeCountText { get => _tradeCountText; private set => SetProperty(ref _tradeCountText, value); }

    private string _vwapText = "--";
    public string VwapText { get => _vwapText; private set => SetProperty(ref _vwapText, value); }

    // Fired when a new event is added and auto-scroll is desired
    public event EventHandler? AutoScrollRequested;

    /// <summary>Tears off the current symbol into a floating quote panel.</summary>
    public ICommand TearOffCommand { get; }

    public LiveDataViewerViewModel(
        WpfServices.StatusService statusService,
        WpfServices.ConnectionService connectionService,
        WpfServices.LoggingService loggingService,
        WpfServices.NotificationService notificationService,
        SymbolManagementService symbolManagementService,
        WpfServices.TearOffPanelService tearOffPanelService,
        WpfServices.ConfigService configService)
    {
        _statusService = statusService;
        _connectionService = connectionService;
        _loggingService = loggingService;
        _notificationService = notificationService;
        _symbolManagementService = symbolManagementService;
        _tearOffPanelService = tearOffPanelService;
        _configService = configService;
        _baseUrl = _statusService.BaseUrl;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _refreshTimer.Tick += async (_, _) => await RefreshLiveDataAsync();

        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statsTimer.Tick += (_, _) => UpdateStats();

        TearOffCommand = new RelayCommand(TearOffCurrentSymbol);
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _connectionService.StateChanged += OnConnectionStateChanged;
        UpdateConnectionStatus();
        await LoadSymbolsAsync();
        _refreshTimer.Start();
        _statsTimer.Start();
    }

    public void Stop()
    {
        _connectionService.StateChanged -= OnConnectionStateChanged;
        _refreshTimer.Stop();
        _statsTimer.Stop();
        _loadSymbolsCts?.Cancel();
        _loadSymbolsCts?.Dispose();
        _loadSymbolsCts = null;
        _liveDataCts?.Cancel();
        _liveDataCts?.Dispose();
        _liveDataCts = null;
    }

    public void PauseResume()
    {
        _isPaused = !_isPaused;
        PauseResumeButtonText = _isPaused ? "Resume" : "Pause";
        _notificationService.ShowNotification(
            _isPaused ? "Paused" : "Resumed",
            _isPaused ? "Live data feed has been paused." : "Live data feed has been resumed.",
            NotificationType.Info);
    }

    public void Clear()
    {
        LiveEvents.Clear();
        _seenEventIds.Clear();
        ResetSessionStats();
        NoDataVisible = true;
        _notificationService.ShowNotification("Cleared", "Live data feed has been cleared.", NotificationType.Info);
    }

    public void SelectSymbol(string symbol)
    {
        _selectedSymbol = symbol;
        RaisePropertyChanged(nameof(SelectedSymbol));
        ResetSessionStats();
        LiveEvents.Clear();
        _seenEventIds.Clear();
        NoDataVisible = true;
    }

    public void AddSymbolToList(string symbol)
    {
        if (!AvailableSymbols.Contains(symbol))
            AvailableSymbols.Add(symbol);
    }

    public bool ShowTrades { get; set; } = true;
    public bool ShowQuotes { get; set; } = true;
    public bool AutoScroll { get; set; } = true;

    private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(UpdateConnectionStatus);
    }

    // P1: Connection indicator brushes are cached as static fields — avoids allocation per state change.
    private void UpdateConnectionStatus()
    {
        var state = _connectionService.State;
        ConnectionStatusText = state switch
        {
            ConnectionState.Connected => "Connected",
            ConnectionState.Reconnecting => "Reconnecting...",
            _ => "Disconnected"
        };
        ConnectionIndicatorColor = state switch
        {
            ConnectionState.Connected => BrushConnected,
            ConnectionState.Reconnecting => BrushReconnecting,
            _ => BrushDisconnected
        };
        IsPauseResumeEnabled = state == ConnectionState.Connected;
    }

    // P3: Uses a dedicated CTS so that symbol loading never cancels an in-flight live-data request.
    private async Task LoadSymbolsAsync(CancellationToken ct = default)
    {
        try
        {
            _loadSymbolsCts?.Cancel();
            _loadSymbolsCts?.Dispose();
            _loadSymbolsCts = new CancellationTokenSource();

            var symbolService = _symbolManagementService;
            var result = await symbolService.GetAllSymbolsAsync(_loadSymbolsCts.Token);
            AvailableSymbols.Clear();

            if (result.Success && result.Symbols.Count > 0)
            {
                AvailableSymbols.AddRange(result.Symbols.Select(s => s.Symbol));
            }
            else
            {
                var configSymbols = await _configService.GetConfiguredSymbolsAsync(_loadSymbolsCts.Token);
                if (configSymbols.Length > 0)
                    AvailableSymbols.AddRange(configSymbols.Select(s => s.Symbol));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to load symbols from backend", ex);
        }
    }

    // P3: Uses a dedicated CTS so that live-data polling never cancels an in-flight symbol load.
    // P2: Uses the persistent _seenEventIds set — avoids rebuilding a HashSet on every 500 ms tick.
    private async Task RefreshLiveDataAsync(CancellationToken ct = default)
    {
        if (_isPaused || string.IsNullOrEmpty(_selectedSymbol))
            return;

        try
        {
            _liveDataCts?.Cancel();
            _liveDataCts?.Dispose();
            _liveDataCts = new CancellationTokenSource();

            var response = await _httpClient.GetAsync(
                $"{_baseUrl}/api/live/{Uri.EscapeDataString(_selectedSymbol)}/recent?limit=50",
                _liveDataCts.Token);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(_liveDataCts.Token);
                var data = JsonSerializer.Deserialize<JsonElement>(json);

                if (data.ValueKind == JsonValueKind.Array)
                {
                    var newEvents = new List<LiveDataEventModel>();
                    foreach (var item in data.EnumerateArray())
                    {
                        var eventModel = ParseLiveEvent(item);
                        if (eventModel != null)
                        {
                            newEvents.Add(eventModel);
                            UpdateSessionStats(eventModel);
                        }
                    }

                    // P2: Filter using the persistent set (O(1) per lookup, no per-tick allocation).
                    bool added = false;
                    foreach (var evt in newEvents.Where(e => _seenEventIds.Add(e.Id)).OrderBy(e => e.RawTimestamp))
                    {
                        if (ShouldShowEvent(evt))
                        {
                            LiveEvents.Add(evt);
                            _eventsThisSecond++;
                            _totalEvents++;
                            added = true;

                            while (LiveEvents.Count > 500)
                            {
                                _seenEventIds.Remove(LiveEvents[0].Id);
                                LiveEvents.RemoveAt(0);
                            }
                        }
                    }

                    if (added && AutoScroll)
                        AutoScrollRequested?.Invoke(this, EventArgs.Empty);

                    NoDataVisible = LiveEvents.Count == 0;
                }
            }

            await RefreshQuoteAsync();
        }
        catch (OperationCanceledException) { /* Cancellation is expected */ }
        catch (HttpRequestException ex)
        {
            _loggingService.LogError("HTTP error refreshing live data", ex);
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to refresh live data", ex);
        }
    }

    private async Task RefreshQuoteAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{_baseUrl}/api/live/{Uri.EscapeDataString(_selectedSymbol)}/quote",
                _liveDataCts!.Token);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(_liveDataCts.Token);
                var quote = JsonSerializer.Deserialize<JsonElement>(json);

                if (quote.TryGetProperty("bid", out var b))
                    _bidPrice = b.GetDecimal();
                if (quote.TryGetProperty("ask", out var a))
                    _askPrice = a.GetDecimal();
                if (quote.TryGetProperty("bidSize", out var bs))
                    _bidSize = bs.GetInt32();
                if (quote.TryGetProperty("askSize", out var as2))
                    _askSize = as2.GetInt32();

                UpdateQuoteDisplay();
            }
        }
        catch (OperationCanceledException) { /* Cancellation is expected */ }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LiveDataViewerViewModel] HTTP error refreshing quote: {ex.Message}");
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LiveDataViewerViewModel] JSON parse error in quote refresh: {ex.Message}");
        }
    }

    // P1: Returns static cached brushes — avoids allocating two SolidColorBrush objects per event.
    // E2: Logs parse failures instead of silently swallowing them.
    private LiveDataEventModel? ParseLiveEvent(JsonElement item)
    {
        try
        {
            var type = item.TryGetProperty("type", out var tp) ? tp.GetString() ?? "TRADE" : "TRADE";
            var symbol = item.TryGetProperty("symbol", out var sp) ? sp.GetString() ?? "" : "";
            var price = item.TryGetProperty("price", out var pp) ? pp.GetDecimal() : 0m;
            var size = item.TryGetProperty("size", out var szp) ? szp.GetInt32() :
                       item.TryGetProperty("volume", out var vp) ? vp.GetInt32() : 0;
            var exchange = item.TryGetProperty("exchange", out var ep) ? ep.GetString() ?? "" : "";
            var timestamp = item.TryGetProperty("timestamp", out var tsp) ? tsp.GetDateTime() : DateTime.UtcNow;
            var id = item.TryGetProperty("id", out var idp) ? idp.GetString() ?? "" :
                     $"{timestamp:HHmmssfffff}_{type}_{price}";
            var isBuy = type.Equals("TRADE", StringComparison.OrdinalIgnoreCase) &&
                        item.TryGetProperty("side", out var side) &&
                        side.GetString()?.Equals("buy", StringComparison.OrdinalIgnoreCase) == true;

            return new LiveDataEventModel
            {
                Id = id,
                RawTimestamp = timestamp,
                Timestamp = timestamp.ToString("HH:mm:ss.fff"),
                Type = type.ToUpperInvariant() switch
                {
                    "TRADE" => "TRD",
                    "QUOTE" => "QTE",
                    "BBO" => "BBO",
                    _ => type[..Math.Min(3, type.Length)].ToUpperInvariant()
                },
                Symbol = symbol,
                Price = price.ToString("F2"),
                RawPrice = price,
                Size = FormatSize(size),
                Exchange = exchange,
                TypeColor = type.ToUpperInvariant() switch
                {
                    "TRADE" => BrushTrade,
                    "QUOTE" or "BBO" => BrushQuote,
                    _ => BrushEventOther
                },
                PriceColor = isBuy ? BrushBuy : BrushSell
            };
        }
        catch (Exception ex)
        {
            _loggingService.LogWarning("Failed to parse live market event", ("Error", ex.Message));
            return null;
        }
    }

    private bool ShouldShowEvent(LiveDataEventModel evt)
    {
        if (evt.Type == "TRD" && !ShowTrades)
            return false;
        if ((evt.Type == "QTE" || evt.Type == "BBO") && !ShowQuotes)
            return false;
        return true;
    }

    private void UpdateSessionStats(LiveDataEventModel evt)
    {
        if (evt.Type != "TRD" || evt.RawPrice <= 0)
            return;
        _lastPrice = evt.RawPrice;
        if (!_sessionHigh.HasValue || evt.RawPrice > _sessionHigh)
            _sessionHigh = evt.RawPrice;
        if (!_sessionLow.HasValue || evt.RawPrice < _sessionLow)
            _sessionLow = evt.RawPrice;
        if (int.TryParse(evt.Size.Replace(",", ""), out var sz))
        {
            _sessionVolume += sz;
            _tradeCount++;
            _vwapNumerator += evt.RawPrice * sz;
        }
    }

    private void UpdateStats()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastStatsUpdate).TotalSeconds >= 1)
        {
            EventRateText = _eventsThisSecond.ToString();
            _eventsThisSecond = 0;
            _lastStatsUpdate = now;
        }
        TotalEventsText = FormatNumber(_totalEvents);
    }

    private void UpdateQuoteDisplay()
    {
        BidPriceText = _bidPrice > 0 ? _bidPrice.ToString("F2") : "--";
        BidSizeText = _bidSize > 0 ? FormatSize(_bidSize) : "--";
        AskPriceText = _askPrice > 0 ? _askPrice.ToString("F2") : "--";
        AskSizeText = _askSize > 0 ? FormatSize(_askSize) : "--";

        if (_bidPrice > 0 && _askPrice > 0)
        {
            SpreadText = (_askPrice - _bidPrice).ToString("F2");
            MidPriceText = ((_bidPrice + _askPrice) / 2).ToString("F2");
        }
        else
        {
            SpreadText = "--";
            MidPriceText = "--";
        }

        LastTradeText = _lastPrice > 0 ? _lastPrice.ToString("F2") : "--";
        LastTradeTimeText = _lastPrice > 0 ? DateTime.Now.ToString("HH:mm:ss") : "--";
        SessionHighText = _sessionHigh?.ToString("F2") ?? "--";
        SessionLowText = _sessionLow?.ToString("F2") ?? "--";
        SessionVolumeText = _sessionVolume > 0 ? FormatNumber(_sessionVolume) : "--";
        TradeCountText = _tradeCount > 0 ? FormatNumber(_tradeCount) : "--";
        VwapText = _sessionVolume > 0 ? (_vwapNumerator / _sessionVolume).ToString("F2") : "--";
    }

    private void ResetSessionStats()
    {
        _sessionHigh = null;
        _sessionLow = null;
        _sessionVolume = 0;
        _tradeCount = 0;
        _vwapNumerator = 0;
        _lastPrice = 0;
        _bidPrice = 0;
        _askPrice = 0;
        _bidSize = 0;
        _askSize = 0;
        _totalEvents = 0;
        UpdateQuoteDisplay();
        TotalEventsText = "0";
    }

    private static string FormatSize(int size) => size >= 1000 ? $"{size:N0}" : size.ToString();

    private static string FormatNumber(long num)
    {
        if (num >= 1_000_000)
            return $"{num / 1_000_000.0:F1}M";
        if (num >= 1_000)
            return $"{num / 1_000.0:F1}K";
        return num.ToString("N0");
    }

    public void Dispose() => Stop();

    private void TearOffCurrentSymbol()
    {
        if (!string.IsNullOrEmpty(_selectedSymbol))
            _tearOffPanelService.TearOff(_selectedSymbol);
    }
}

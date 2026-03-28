using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Provider Health page.
/// All state, HTTP loading, timer management, and connection-event tracking live here
/// so that the code-behind is thinned to lifecycle wiring only.
/// </summary>
public sealed class ProviderHealthViewModel : BindableBase, IDisposable, IPageActionBarProvider
{
    private readonly WpfServices.StatusService _statusService;
    private readonly WpfServices.ConnectionService _connectionService;
    private readonly WpfServices.LoggingService _loggingService;
    private readonly WpfServices.NotificationService _notificationService;

    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _staleCheckTimer;
    private PeriodicTimer? _sparklineTimer;
    private CancellationTokenSource? _cts;
    private DateTime? _lastRefreshTime;

    // ── Public collections ──────────────────────────────────────────────────
    public ObservableCollection<ProviderStatusModel> StreamingProviders { get; } = new();
    public ObservableCollection<BackfillProviderModel> BackfillProviders { get; } = new();
    public ObservableCollection<ConnectionEventModel> ConnectionHistory { get; } = new();

    // ── Bindable properties ─────────────────────────────────────────────────
    private string _connectedCount = "0";
    public string ConnectedCount { get => _connectedCount; private set => SetProperty(ref _connectedCount, value); }

    private string _disconnectedCount = "0";
    public string DisconnectedCount { get => _disconnectedCount; private set => SetProperty(ref _disconnectedCount, value); }

    private string _totalProviders = "0";
    public string TotalProviders { get => _totalProviders; private set => SetProperty(ref _totalProviders, value); }

    private string _avgLatency = "--";
    public string AvgLatency { get => _avgLatency; private set => SetProperty(ref _avgLatency, value); }

    private string _lastUpdateText = "Last updated: --";
    public string LastUpdateText { get => _lastUpdateText; private set => SetProperty(ref _lastUpdateText, value); }

    private bool _isLastUpdateStale;
    public bool IsLastUpdateStale { get => _isLastUpdateStale; private set => SetProperty(ref _isLastUpdateStale, value); }

    private bool _hasNoHistory = true;
    public bool HasNoHistory { get => _hasNoHistory; private set => SetProperty(ref _hasNoHistory, value); }

    // ── IPageActionBarProvider implementation ──────────────────────────────────────
    public string PageTitle => "Provider Health";
    public ObservableCollection<ActionEntry> Actions { get; } = new();

    public ProviderHealthViewModel(
        WpfServices.StatusService statusService,
        WpfServices.ConnectionService connectionService,
        WpfServices.LoggingService loggingService,
        WpfServices.NotificationService notificationService)
    {
        _statusService = statusService;
        _connectionService = connectionService;
        _loggingService = loggingService;
        _notificationService = notificationService;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _refreshTimer.Tick += async (_, _) => await RefreshDataAsync();

        _staleCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _staleCheckTimer.Tick += (_, _) => UpdateStaleIndicator();
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _connectionService.StateChanged += OnConnectionStateChanged;
        _connectionService.ConnectionHealthUpdated += OnConnectionHealthUpdated;

        // Populate action bar.
        Actions.Clear();
        Actions.Add(new ActionEntry("Refresh", new RelayCommand(() => _ = RefreshAsync()), "↻", "Refresh provider data", IsPrimary: true));
        Actions.Add(new ActionEntry("Reconnect All", new RelayCommand(() => _notificationService.NotifyInfo("Reconnecting", "Initiating provider reconnection...")), "🔗", "Reconnect all providers"));

        await RefreshDataAsync();

        _refreshTimer.Start();
        _staleCheckTimer.Start();
        StartSparklineTimer();
    }

    public void Stop()
    {
        _connectionService.StateChanged -= OnConnectionStateChanged;
        _connectionService.ConnectionHealthUpdated -= OnConnectionHealthUpdated;
        _refreshTimer.Stop();
        _staleCheckTimer.Stop();
        StopSparklineTimer();
        _cts?.Cancel();
        _cts?.Dispose();
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await RefreshDataAsync();
        _notificationService.ShowNotification(
            "Refreshed",
            "Provider health data has been refreshed.",
            NotificationType.Info);
    }

    public async Task ToggleProviderConnectionAsync(string providerId, CancellationToken ct = default)
    {
        var provider = StreamingProviders.FirstOrDefault(p => p.ProviderId == providerId);
        if (provider == null) return;

        if (provider.IsConnected)
        {
            await _connectionService.DisconnectAsync();
            AddConnectionEvent("Disconnected by user", provider.Name, EventType.Info);
            _notificationService.ShowNotification(
                "Disconnected",
                $"Disconnected from {provider.Name}.",
                NotificationType.Info);
        }
        else
        {
            await _connectionService.ConnectAsync(providerId);
            AddConnectionEvent("Connected by user", provider.Name, EventType.Success);
            _notificationService.ShowNotification(
                "Connected",
                $"Connected to {provider.Name}.",
                NotificationType.Success);
        }

        await RefreshDataAsync();
    }

    public string GetProviderDetails(string providerId)
    {
        var provider = StreamingProviders.FirstOrDefault(p => p.ProviderId == providerId);
        if (provider == null) return string.Empty;

        return $"Provider: {provider.Name}\n" +
               $"Status: {provider.StatusText}\n" +
               $"{provider.LatencyText}\n" +
               $"{provider.UptimeText}\n" +
               $"Reconnects: {_connectionService.TotalReconnects}";
    }

    public void ClearHistory()
    {
        ConnectionHistory.Clear();
        HasNoHistory = true;
        _notificationService.ShowNotification(
            "History Cleared",
            "Connection history has been cleared.",
            NotificationType.Info);
    }

    private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            AddConnectionEvent(
                e.NewState == ConnectionState.Connected ? "Connected" :
                e.NewState == ConnectionState.Disconnected ? "Disconnected" :
                e.NewState == ConnectionState.Reconnecting ? "Reconnecting" : "Unknown",
                e.Provider,
                e.NewState == ConnectionState.Connected ? EventType.Success :
                e.NewState == ConnectionState.Disconnected ? EventType.Error : EventType.Warning);

            if (e.NewState == ConnectionState.Disconnected && !string.IsNullOrEmpty(e.Provider))
                WpfServices.ToastNotificationService.Instance.ShowProviderDisconnected(e.Provider);
        });
    }

    private void OnConnectionHealthUpdated(object? sender, ConnectionHealthEventArgs e)
    {
        if (!e.IsHealthy)
        {
            _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                AddConnectionEvent(
                    $"Health check failed: {e.ErrorMessage}",
                    _connectionService.CurrentProvider,
                    EventType.Warning);
            });
        }
    }

    private async Task RefreshDataAsync(CancellationToken ct = default)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        try
        {
            await LoadStreamingProvidersAsync(_cts.Token);
            await LoadBackfillProvidersAsync(_cts.Token);
            UpdateSummaryStats();
            _lastRefreshTime = DateTime.UtcNow;
            UpdateStaleIndicator();
        }
        catch (OperationCanceledException)
        {
            // Cancelled — ignore
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to refresh provider health", ex);
        }
    }

    private async Task LoadStreamingProvidersAsync(CancellationToken ct)
    {
        StreamingProviders.Clear();

        var providers = await _statusService.GetAvailableProvidersAsync(ct);
        var streaming = providers.Where(p =>
            p.ProviderType == "Streaming" || p.ProviderType == "Hybrid").ToList();

        var connectionState = _connectionService.State;
        var currentProvider = _connectionService.CurrentProvider;
        var latency = _connectionService.LastLatencyMs;
        var uptime = _connectionService.Uptime;

        foreach (var provider in streaming)
        {
            var isActive = provider.ProviderId.Equals(currentProvider, StringComparison.OrdinalIgnoreCase);
            var isConnected = isActive && connectionState == ConnectionState.Connected;
            var isReconnecting = isActive && connectionState == ConnectionState.Reconnecting;

            StreamingProviders.Add(new ProviderStatusModel
            {
                ProviderId = provider.ProviderId,
                Name = provider.DisplayName,
                StatusText = isConnected ? "Connected" :
                             isReconnecting ? "Reconnecting..." :
                             isActive ? "Disconnected" : "Not Active",
                StatusColor = new SolidColorBrush(
                    isConnected ? Color.FromRgb(63, 185, 80) :
                    isReconnecting ? Color.FromRgb(255, 193, 7) :
                    Color.FromRgb(139, 148, 158)),
                LatencyText = isConnected ? $"Latency: {latency:F0}ms" : "Latency: --",
                UptimeText = isConnected && uptime.HasValue ?
                    $"Uptime: {FormatUptime(uptime.Value)}" : "Uptime: --",
                ActionText = isConnected ? "Disconnect" : "Connect",
                IsConnected = isConnected
            });
        }

        if (StreamingProviders.Count == 0)
        {
            StreamingProviders.Add(CreateDefaultProvider("alpaca", "Alpaca Markets"));
            StreamingProviders.Add(CreateDefaultProvider("ib", "Interactive Brokers"));
            StreamingProviders.Add(CreateDefaultProvider("polygon", "Polygon.io"));
        }
    }

    private static ProviderStatusModel CreateDefaultProvider(string id, string name) => new()
    {
        ProviderId = id,
        Name = name,
        StatusText = "Not Configured",
        StatusColor = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
        LatencyText = "Latency: --",
        UptimeText = "Uptime: --",
        ActionText = "Configure",
        IsConnected = false
    };

    private async Task LoadBackfillProvidersAsync(CancellationToken ct)
    {
        BackfillProviders.Clear();

        var providers = await _statusService.GetAvailableProvidersAsync(ct);
        var backfill = providers.Where(p =>
            p.ProviderType == "Backfill" || p.ProviderType == "Hybrid").ToList();

        foreach (var provider in backfill)
        {
            var hasCredentials = !provider.RequiresCredentials ||
                CheckCredentialsConfigured(provider.ProviderId);

            BackfillProviders.Add(new BackfillProviderModel
            {
                ProviderId = provider.ProviderId,
                Name = provider.DisplayName,
                StatusText = hasCredentials ? "Available" : "Not Configured",
                StatusColor = new SolidColorBrush(
                    hasCredentials ? Color.FromRgb(63, 185, 80) : Color.FromRgb(139, 148, 158)),
                RateLimitText = GetRateLimitText(provider.ProviderId),
                LastUsedText = "Last used: --"
            });
        }

        if (BackfillProviders.Count == 0)
        {
            BackfillProviders.Add(new BackfillProviderModel
            {
                ProviderId = "stooq", Name = "Stooq", StatusText = "Available",
                StatusColor = new SolidColorBrush(Color.FromRgb(63, 185, 80)),
                RateLimitText = "30 req/min", LastUsedText = "Last used: --"
            });
            BackfillProviders.Add(new BackfillProviderModel
            {
                ProviderId = "yahoo", Name = "Yahoo Finance", StatusText = "Available",
                StatusColor = new SolidColorBrush(Color.FromRgb(63, 185, 80)),
                RateLimitText = "100 req/min", LastUsedText = "Last used: --"
            });
        }
    }

    private static bool CheckCredentialsConfigured(string providerId)
    {
        var envVarName = providerId.ToUpperInvariant() switch
        {
            "ALPACA" => "ALPACA__KEYID",
            "POLYGON" => "POLYGON__APIKEY",
            "TIINGO" => "TIINGO__TOKEN",
            "FINNHUB" => "FINNHUB__APIKEY",
            "ALPHAVANTAGE" => "ALPHAVANTAGE__APIKEY",
            _ => null
        };
        if (envVarName == null) return true;
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(envVarName));
    }

    private static string GetRateLimitText(string providerId) =>
        providerId.ToLowerInvariant() switch
        {
            "alpaca" => "200 req/min",
            "polygon" => "5 req/min (free)",
            "tiingo" => "500 req/hour",
            "finnhub" => "60 req/min",
            "alphavantage" => "5 req/min",
            "stooq" => "30 req/min",
            "yahoo" => "100 req/min",
            _ => "Unknown"
        };

    private void UpdateStaleIndicator()
    {
        var secondsSince = _lastRefreshTime.HasValue
            ? (DateTime.UtcNow - _lastRefreshTime.Value).TotalSeconds
            : (double?)null;

        var indicator = FormatHelpers.FormatStaleIndicator(secondsSince, staleThresholdSeconds: 45);
        LastUpdateText = $"Last updated: {indicator.DisplayText}";
        IsLastUpdateStale = indicator.IsStale;
    }

    private void UpdateSummaryStats()
    {
        var connected = StreamingProviders.Count(p => p.IsConnected);
        var disconnected = StreamingProviders.Count(p => !p.IsConnected);
        ConnectedCount = connected.ToString();
        DisconnectedCount = disconnected.ToString();
        TotalProviders = (StreamingProviders.Count + BackfillProviders.Count).ToString();
        AvgLatency = _connectionService.State == ConnectionState.Connected
            ? $"{_connectionService.LastLatencyMs:F0}"
            : "--";
    }

    private void AddConnectionEvent(string message, string provider, EventType eventType)
    {
        ConnectionHistory.Insert(0, new ConnectionEventModel
        {
            Message = message,
            Provider = provider,
            Timestamp = DateTime.Now,
            TimeText = "Just now",
            EventColor = new SolidColorBrush(eventType switch
            {
                EventType.Success => Color.FromRgb(63, 185, 80),
                EventType.Warning => Color.FromRgb(255, 193, 7),
                EventType.Error => Color.FromRgb(244, 67, 54),
                _ => Color.FromRgb(139, 148, 158)
            })
        });

        while (ConnectionHistory.Count > 50)
            ConnectionHistory.RemoveAt(ConnectionHistory.Count - 1);

        HasNoHistory = ConnectionHistory.Count == 0;

        foreach (var evt in ConnectionHistory)
            evt.TimeText = FormatTimeAgo(evt.Timestamp);
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalMinutes < 1) return "< 1m";
        if (uptime.TotalHours < 1) return $"{(int)uptime.TotalMinutes}m";
        if (uptime.TotalDays < 1) return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        return $"{(int)uptime.TotalDays}d {uptime.Hours}h";
    }

    private static string FormatTimeAgo(DateTime timestamp)
    {
        var diff = DateTime.Now - timestamp;
        if (diff.TotalSeconds < 60) return "Just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        return timestamp.ToString("MMM d HH:mm");
    }

    public void Dispose()
    {
        Stop();
        _sparklineTimer?.Dispose();
    }

    private void StartSparklineTimer()
    {
        _sparklineTimer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        _ = RefreshSparklineDataAsync();
    }

    private void StopSparklineTimer()
    {
        _sparklineTimer?.Dispose();
        _sparklineTimer = null;
    }

    private async Task RefreshSparklineDataAsync()
    {
        try
        {
            while (_sparklineTimer is not null)
            {
                await _sparklineTimer.WaitForNextTickAsync();
                if (_sparklineTimer is null) break;

                _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(UpdateSparklineData);
            }
        }
        catch (OperationCanceledException)
        {
            // Timer disposed — ignore
        }
    }

    private void UpdateSparklineData()
    {
        foreach (var provider in StreamingProviders)
        {
            if (provider.SparklineItem == null)
            {
                provider.SparklineItem = new ProviderSparklineItem { ProviderName = provider.Name };
            }

            var latencySample = _connectionService.LastLatencyMs;
            provider.SparklineItem.AddLatencySample(latencySample);
            provider.SparklineItem.ReconnectCount = _connectionService.TotalReconnects;
        }
    }
}

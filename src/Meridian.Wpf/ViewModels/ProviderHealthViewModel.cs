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
    private static readonly Brush ReadyPostureBrush = CreateFrozenBrush(63, 185, 80);
    private static readonly Brush WarningPostureBrush = CreateFrozenBrush(255, 193, 7);
    private static readonly Brush CriticalPostureBrush = CreateFrozenBrush(244, 67, 54);
    private static readonly Brush NeutralPostureBrush = CreateFrozenBrush(139, 148, 158);
    private static readonly Brush ReadyPostureBackgroundBrush = CreateFrozenBrush(63, 185, 80, 31);
    private static readonly Brush WarningPostureBackgroundBrush = CreateFrozenBrush(255, 193, 7, 31);
    private static readonly Brush CriticalPostureBackgroundBrush = CreateFrozenBrush(244, 67, 54, 31);
    private static readonly Brush NeutralPostureBackgroundBrush = CreateFrozenBrush(139, 148, 158, 31);

    private readonly WpfServices.StatusService _statusService;
    private readonly WpfServices.ConnectionService _connectionService;
    private readonly WpfServices.LoggingService _loggingService;
    private readonly WpfServices.NotificationService _notificationService;

    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _staleCheckTimer;
    private PeriodicTimer? _sparklineTimer;
    private CancellationTokenSource? _cts;
    private DateTime? _lastRefreshTime;
    private bool _isActive;
    private bool _isDisposed;

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

    private string _providerPostureTitle = "Provider posture loading";
    public string ProviderPostureTitle { get => _providerPostureTitle; private set => SetProperty(ref _providerPostureTitle, value); }

    private string _providerPostureDetail = "Refresh provider health to compute the active streaming and backfill posture.";
    public string ProviderPostureDetail { get => _providerPostureDetail; private set => SetProperty(ref _providerPostureDetail, value); }

    private string _providerPostureActionText = "Refresh provider data";
    public string ProviderPostureActionText { get => _providerPostureActionText; private set => SetProperty(ref _providerPostureActionText, value); }

    private string _providerPostureTargetText = "Provider health";
    public string ProviderPostureTargetText { get => _providerPostureTargetText; private set => SetProperty(ref _providerPostureTargetText, value); }

    private string _providerPostureEvidenceText = "Awaiting provider snapshot";
    public string ProviderPostureEvidenceText { get => _providerPostureEvidenceText; private set => SetProperty(ref _providerPostureEvidenceText, value); }

    private string _providerPostureIcon = "\uE946";
    public string ProviderPostureIcon { get => _providerPostureIcon; private set => SetProperty(ref _providerPostureIcon, value); }

    private Brush _providerPostureAccentBrush = NeutralPostureBrush;
    public Brush ProviderPostureAccentBrush { get => _providerPostureAccentBrush; private set => SetProperty(ref _providerPostureAccentBrush, value); }

    private Brush _providerPostureBackgroundBrush = NeutralPostureBackgroundBrush;
    public Brush ProviderPostureBackgroundBrush { get => _providerPostureBackgroundBrush; private set => SetProperty(ref _providerPostureBackgroundBrush, value); }

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
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_isActive)
        {
            return;
        }

        _isActive = true;
        _connectionService.StateChanged += OnConnectionStateChanged;
        _connectionService.ConnectionHealthUpdated += OnConnectionHealthUpdated;

        // Populate action bar.
        Actions.Clear();
        Actions.Add(new ActionEntry("Refresh", new RelayCommand(() => _ = RefreshAsync()), "\uE72C", "Refresh provider data", IsPrimary: true));
        Actions.Add(new ActionEntry("Reconnect All", new RelayCommand(() => _notificationService.NotifyInfo("Reconnecting", "Initiating provider reconnection...")), "\uE71B", "Reconnect all providers"));

        await RefreshDataAsync();

        _refreshTimer.Start();
        _staleCheckTimer.Start();
        StartSparklineTimer();
    }

    public void Stop()
    {
        _isActive = false;
        _connectionService.StateChanged -= OnConnectionStateChanged;
        _connectionService.ConnectionHealthUpdated -= OnConnectionHealthUpdated;
        _refreshTimer.Stop();
        _staleCheckTimer.Stop();
        StopSparklineTimer();
        var refreshCts = Interlocked.Exchange(ref _cts, null);
        CancelAndDispose(refreshCts);
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
        if (provider == null)
            return;

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
        if (provider == null)
            return string.Empty;

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
        if (_isDisposed || !_isActive)
        {
            return;
        }

        var refreshCts = ct.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : new CancellationTokenSource();
        var previousRefreshCts = Interlocked.Exchange(ref _cts, refreshCts);
        CancelAndDispose(previousRefreshCts);

        try
        {
            if (_isDisposed || !_isActive)
            {
                return;
            }

            await LoadStreamingProvidersAsync(refreshCts.Token);
            await LoadBackfillProvidersAsync(refreshCts.Token);
            UpdateSummaryStats();
            _lastRefreshTime = DateTime.UtcNow;
            UpdateStaleIndicator();
        }
        catch (OperationCanceledException) when (refreshCts.IsCancellationRequested || ct.IsCancellationRequested)
        {
            // Cancelled — ignore
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to refresh provider health", ex);
        }
        finally
        {
            Interlocked.CompareExchange(ref _cts, null, refreshCts);
            CancelAndDispose(refreshCts, cancel: false);
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
            StreamingProviders.Add(CreateDefaultProvider("robinhood", "Robinhood"));
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
                ProviderId = "stooq",
                Name = "Stooq",
                StatusText = "Available",
                StatusColor = new SolidColorBrush(Color.FromRgb(63, 185, 80)),
                RateLimitText = "30 req/min",
                LastUsedText = "Last used: --"
            });
            BackfillProviders.Add(new BackfillProviderModel
            {
                ProviderId = "yahoo",
                Name = "Yahoo Finance",
                StatusText = "Available",
                StatusColor = new SolidColorBrush(Color.FromRgb(63, 185, 80)),
                RateLimitText = "100 req/min",
                LastUsedText = "Last used: --"
            });
        }
    }

    private static bool CheckCredentialsConfigured(string providerId)
    {
        var envVarName = providerId.ToUpperInvariant() switch
        {
            "ALPACA" => "ALPACA__KEYID",
            "POLYGON" => "POLYGON__APIKEY",
            "ROBINHOOD" => "ROBINHOOD_ACCESS_TOKEN",
            "TIINGO" => "TIINGO__TOKEN",
            "FINNHUB" => "FINNHUB__APIKEY",
            "ALPHAVANTAGE" => "ALPHAVANTAGE__APIKEY",
            _ => null
        };
        if (envVarName == null)
            return true;
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(envVarName));
    }

    private static string GetRateLimitText(string providerId) =>
        providerId.ToLowerInvariant() switch
        {
            "alpaca" => "200 req/min",
            "polygon" => "5 req/min (free)",
            "robinhood" => "Rate limited by broker session",
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
        UpdateProviderPosture();
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
        UpdateProviderPosture();
    }

    private void UpdateProviderPosture()
    {
        var connected = StreamingProviders.Count(p => p.IsConnected);
        var disconnected = StreamingProviders.Count(p => !p.IsConnected);
        var availableBackfill = BackfillProviders.Count(p =>
            string.Equals(p.StatusText, "Available", StringComparison.OrdinalIgnoreCase));

        var posture = BuildProviderPosture(
            connected,
            disconnected,
            StreamingProviders.Count,
            BackfillProviders.Count,
            availableBackfill,
            IsLastUpdateStale);

        ProviderPostureTitle = posture.Title;
        ProviderPostureDetail = posture.Detail;
        ProviderPostureActionText = posture.ActionText;
        ProviderPostureTargetText = posture.TargetText;
        ProviderPostureEvidenceText = posture.EvidenceText;
        ProviderPostureIcon = posture.Icon;
        ProviderPostureAccentBrush = GetPostureAccentBrush(posture.Tone);
        ProviderPostureBackgroundBrush = GetPostureBackgroundBrush(posture.Tone);
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
        if (uptime.TotalMinutes < 1)
            return "< 1m";
        if (uptime.TotalHours < 1)
            return $"{(int)uptime.TotalMinutes}m";
        if (uptime.TotalDays < 1)
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        return $"{(int)uptime.TotalDays}d {uptime.Hours}h";
    }

    private static string FormatTimeAgo(DateTime timestamp)
    {
        var diff = DateTime.Now - timestamp;
        if (diff.TotalSeconds < 60)
            return "Just now";
        if (diff.TotalMinutes < 60)
            return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24)
            return $"{(int)diff.TotalHours}h ago";
        return timestamp.ToString("MMM d HH:mm");
    }

    internal static ProviderHealthPostureState BuildProviderPosture(
        int connectedStreamingProviders,
        int disconnectedStreamingProviders,
        int streamingProviders,
        int backfillProviders,
        int availableBackfillProviders,
        bool isLastUpdateStale)
    {
        connectedStreamingProviders = Math.Max(0, connectedStreamingProviders);
        disconnectedStreamingProviders = Math.Max(0, disconnectedStreamingProviders);
        streamingProviders = Math.Max(0, streamingProviders);
        backfillProviders = Math.Max(0, backfillProviders);
        availableBackfillProviders = Math.Max(0, availableBackfillProviders);

        var evidence = $"{connectedStreamingProviders}/{streamingProviders} streaming connected; " +
            $"{availableBackfillProviders}/{backfillProviders} backfill available";

        if (streamingProviders == 0 && backfillProviders == 0)
        {
            return new ProviderHealthPostureState(
                "No providers discovered",
                "Provider discovery returned no streaming or backfill entries. Open provider setup before routing live data or backfills.",
                "Open provider setup",
                "Provider catalog",
                evidence,
                "\uE946",
                ProviderHealthPostureTone.Neutral);
        }

        if (isLastUpdateStale)
        {
            return new ProviderHealthPostureState(
                "Provider snapshot is stale",
                "The provider health snapshot has aged past the workstation freshness window. Refresh before acting on feed posture.",
                "Refresh provider posture",
                "Provider health snapshot",
                evidence,
                "\uE72C",
                ProviderHealthPostureTone.Warning);
        }

        if (streamingProviders > 0 && connectedStreamingProviders == 0)
        {
            return new ProviderHealthPostureState(
                "Provider session offline",
                "No streaming provider is connected. Reconnect the primary session before relying on live monitoring or paper-trading handoffs.",
                "Reconnect primary provider",
                "Streaming session",
                evidence,
                "\uE71B",
                ProviderHealthPostureTone.Critical);
        }

        if (disconnectedStreamingProviders > 0)
        {
            return new ProviderHealthPostureState(
                "Mixed provider posture",
                $"{connectedStreamingProviders} streaming provider(s) are connected while {disconnectedStreamingProviders} need review. Keep live handoffs in watch mode until the inactive feed is explained.",
                "Review disconnected providers",
                "Streaming provider grid",
                evidence,
                "\uE7BA",
                ProviderHealthPostureTone.Warning);
        }

        if (connectedStreamingProviders > 0 && backfillProviders > 0 && availableBackfillProviders == 0)
        {
            return new ProviderHealthPostureState(
                "Streaming ready; backfill blocked",
                "The live feed is connected, but no backfill provider is available. Historical repair and replay preparation need provider setup.",
                "Configure backfill provider",
                "Backfill provider grid",
                evidence,
                "\uE8B7",
                ProviderHealthPostureTone.Warning);
        }

        if (connectedStreamingProviders > 0)
        {
            return new ProviderHealthPostureState(
                "Provider posture ready",
                "Streaming and backfill surfaces have usable coverage. Continue monitoring reconnect badges and connection history for drift.",
                "Monitor connection history",
                "Connection history",
                evidence,
                "\uE73E",
                ProviderHealthPostureTone.Ready);
        }

        return new ProviderHealthPostureState(
            "Configure provider access",
            "Provider entries are present but none are ready for operator workflows. Finish credentials and connectivity before starting data operations.",
            "Configure provider access",
            "Provider credentials",
            evidence,
            "\uE946",
            ProviderHealthPostureTone.Neutral);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Stop();
        _sparklineTimer?.Dispose();
    }

    private static Brush GetPostureAccentBrush(ProviderHealthPostureTone tone) =>
        tone switch
        {
            ProviderHealthPostureTone.Ready => ReadyPostureBrush,
            ProviderHealthPostureTone.Warning => WarningPostureBrush,
            ProviderHealthPostureTone.Critical => CriticalPostureBrush,
            _ => NeutralPostureBrush
        };

    private static Brush GetPostureBackgroundBrush(ProviderHealthPostureTone tone) =>
        tone switch
        {
            ProviderHealthPostureTone.Ready => ReadyPostureBackgroundBrush,
            ProviderHealthPostureTone.Warning => WarningPostureBackgroundBrush,
            ProviderHealthPostureTone.Critical => CriticalPostureBackgroundBrush,
            _ => NeutralPostureBackgroundBrush
        };

    private static SolidColorBrush CreateFrozenBrush(byte red, byte green, byte blue, byte alpha = 255)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
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

    private static void CancelAndDispose(CancellationTokenSource? cts, bool cancel = true)
    {
        if (cts is null)
        {
            return;
        }

        if (cancel)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }

        try
        {
            cts.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task RefreshSparklineDataAsync()
    {
        try
        {
            while (_sparklineTimer is not null)
            {
                await _sparklineTimer.WaitForNextTickAsync();
                if (_sparklineTimer is null)
                    break;

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

internal enum ProviderHealthPostureTone
{
    Ready,
    Warning,
    Critical,
    Neutral
}

internal sealed record ProviderHealthPostureState(
    string Title,
    string Detail,
    string ActionText,
    string TargetText,
    string EvidenceText,
    string Icon,
    ProviderHealthPostureTone Tone);

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.Ui.Services;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Models;
using Meridian.Wpf.Workstation.Models;
using Meridian.Wpf.Workstation.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Provider Health page.
/// All state, HTTP loading, timer management, and connection-event tracking live here
/// so that the code-behind is thinned to lifecycle wiring only.
/// </summary>
public sealed class ProviderHealthViewModel : CommandHostViewModel, IPageActivationLifetime, IDisposable, IPageActionBarProvider
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
    private readonly ProviderHealthCollectionsSectionViewModel _collectionsSection = new();
    private readonly ProviderHealthPostureSectionViewModel _postureSection = new(NeutralPostureBrush, NeutralPostureBackgroundBrush);
    private readonly ProviderHealthManagementSectionViewModel _managementSection = new(
        BuildProviderManagementTable(new ObservableCollection<ProviderManagementRowModel>()),
        BuildEmptyInspector(),
        BuildEmptyDiagnostics(),
        BuildEmptyRoutingMatrix());

    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _staleCheckTimer;
    private PeriodicTimer? _sparklineTimer;
    private CancellationTokenSource? _activationCts;
    private CancellationTokenSource? _cts;
    private DateTime? _lastRefreshTime;
    private ProviderReadinessSummaryDto? _providerReadiness;
    private bool _isActive;
    private bool _isDisposed;

    // ── Public collections ──────────────────────────────────────────────────
    public ObservableCollection<ProviderStatusModel> StreamingProviders => _collectionsSection.StreamingProviders;
    public ObservableCollection<BackfillProviderModel> BackfillProviders => _collectionsSection.BackfillProviders;
    public ObservableCollection<ConnectionEventModel> ConnectionHistory => _collectionsSection.ConnectionHistory;
    public ObservableCollection<ProviderManagementRowModel> ProviderManagementRows => _collectionsSection.ProviderManagementRows;
    public ObservableCollection<ProviderManagementSummaryCardModel> ProviderManagementSummaryCards => _collectionsSection.ProviderManagementSummaryCards;
    public ObservableCollection<WorkstationMetricModel> ProviderMetricTiles => _collectionsSection.ProviderMetricTiles;

    // ── Bindable properties ─────────────────────────────────────────────────
    public string ConnectedCount
    {
        get => _postureSection.ConnectedCount;
        private set => SetProviderHealthSectionProperty(_postureSection.ConnectedCount, value, next => _postureSection.ConnectedCount = next);
    }

    public string DisconnectedCount
    {
        get => _postureSection.DisconnectedCount;
        private set => SetProviderHealthSectionProperty(_postureSection.DisconnectedCount, value, next => _postureSection.DisconnectedCount = next);
    }

    public string TotalProviders
    {
        get => _postureSection.TotalProviders;
        private set => SetProviderHealthSectionProperty(_postureSection.TotalProviders, value, next => _postureSection.TotalProviders = next);
    }

    public string AvgLatency
    {
        get => _postureSection.AvgLatency;
        private set => SetProviderHealthSectionProperty(_postureSection.AvgLatency, value, next => _postureSection.AvgLatency = next);
    }

    public string LastUpdateText
    {
        get => _postureSection.LastUpdateText;
        private set => SetProviderHealthSectionProperty(_postureSection.LastUpdateText, value, next => _postureSection.LastUpdateText = next);
    }

    public bool IsLastUpdateStale
    {
        get => _postureSection.IsLastUpdateStale;
        private set => SetProviderHealthSectionProperty(_postureSection.IsLastUpdateStale, value, next => _postureSection.IsLastUpdateStale = next);
    }

    public bool HasNoHistory
    {
        get => _postureSection.HasNoHistory;
        private set => SetProviderHealthSectionProperty(_postureSection.HasNoHistory, value, next => _postureSection.HasNoHistory = next);
    }

    public string ProviderPostureTitle
    {
        get => _postureSection.ProviderPostureTitle;
        private set => SetProviderHealthSectionProperty(_postureSection.ProviderPostureTitle, value, next => _postureSection.ProviderPostureTitle = next);
    }

    public string ProviderPostureDetail
    {
        get => _postureSection.ProviderPostureDetail;
        private set => SetProviderHealthSectionProperty(_postureSection.ProviderPostureDetail, value, next => _postureSection.ProviderPostureDetail = next);
    }

    public string ProviderPostureActionText
    {
        get => _postureSection.ProviderPostureActionText;
        private set => SetProviderHealthSectionProperty(_postureSection.ProviderPostureActionText, value, next => _postureSection.ProviderPostureActionText = next);
    }

    public string ProviderPostureTargetText
    {
        get => _postureSection.ProviderPostureTargetText;
        private set => SetProviderHealthSectionProperty(_postureSection.ProviderPostureTargetText, value, next => _postureSection.ProviderPostureTargetText = next);
    }

    public string ProviderPostureEvidenceText
    {
        get => _postureSection.ProviderPostureEvidenceText;
        private set => SetProviderHealthSectionProperty(_postureSection.ProviderPostureEvidenceText, value, next => _postureSection.ProviderPostureEvidenceText = next);
    }

    public string ProviderPostureIcon
    {
        get => _postureSection.ProviderPostureIcon;
        private set => SetProviderHealthSectionProperty(_postureSection.ProviderPostureIcon, value, next => _postureSection.ProviderPostureIcon = next);
    }

    public Brush ProviderPostureAccentBrush
    {
        get => _postureSection.ProviderPostureAccentBrush;
        private set => SetProviderHealthSectionProperty(_postureSection.ProviderPostureAccentBrush, value, next => _postureSection.ProviderPostureAccentBrush = next);
    }

    public Brush ProviderPostureBackgroundBrush
    {
        get => _postureSection.ProviderPostureBackgroundBrush;
        private set => SetProviderHealthSectionProperty(_postureSection.ProviderPostureBackgroundBrush, value, next => _postureSection.ProviderPostureBackgroundBrush = next);
    }

    public ProviderManagementRowModel? SelectedProviderManagementRow
    {
        get => _managementSection.SelectedProviderManagementRow;
        set
        {
            if (SetProviderHealthSectionProperty(_managementSection.SelectedProviderManagementRow, value, next => _managementSection.SelectedProviderManagementRow = next))
            {
                UpdateProviderManagementSummary();
            }
        }
    }

    public string ProviderManagementStatusText
    {
        get => _managementSection.ProviderManagementStatusText;
        private set => SetProviderHealthSectionProperty(_managementSection.ProviderManagementStatusText, value, next => _managementSection.ProviderManagementStatusText = next);
    }

    public string ProviderVerificationStatusText
    {
        get => _managementSection.ProviderVerificationStatusText;
        private set => SetProviderHealthSectionProperty(_managementSection.ProviderVerificationStatusText, value, next => _managementSection.ProviderVerificationStatusText = next);
    }

    public WorkstationStateModel ProviderPostureState
    {
        get => _postureSection.ProviderPostureState;
        private set => SetProviderHealthSectionProperty(_postureSection.ProviderPostureState, value, next => _postureSection.ProviderPostureState = next);
    }

    public WorkstationBadgeModel ProviderPostureBadge
    {
        get => _postureSection.ProviderPostureBadge;
        private set => SetProviderHealthSectionProperty(_postureSection.ProviderPostureBadge, value, next => _postureSection.ProviderPostureBadge = next);
    }

    public WorkstationCommandGroupModel ProviderManagementCommandGroup
    {
        get => _managementSection.ProviderManagementCommandGroup;
        private set => SetProviderHealthSectionProperty(_managementSection.ProviderManagementCommandGroup, value, next => _managementSection.ProviderManagementCommandGroup = next);
    }

    public WorkstationTableModel<ProviderManagementRowModel> ProviderManagementTable
    {
        get => _managementSection.ProviderManagementTable;
        private set => SetProviderHealthSectionProperty(_managementSection.ProviderManagementTable, value, next => _managementSection.ProviderManagementTable = next);
    }

    public InspectorPanelModel SelectedProviderInspector
    {
        get => _managementSection.SelectedProviderInspector;
        private set => SetProviderHealthSectionProperty(_managementSection.SelectedProviderInspector, value, next => _managementSection.SelectedProviderInspector = next);
    }

    public DiagnosticsChecklistModel SelectedProviderDiagnostics
    {
        get => _managementSection.SelectedProviderDiagnostics;
        private set => SetProviderHealthSectionProperty(_managementSection.SelectedProviderDiagnostics, value, next => _managementSection.SelectedProviderDiagnostics = next);
    }

    public RoutingMatrixModel SelectedProviderRoutingMatrix
    {
        get => _managementSection.SelectedProviderRoutingMatrix;
        private set => SetProviderHealthSectionProperty(_managementSection.SelectedProviderRoutingMatrix, value, next => _managementSection.SelectedProviderRoutingMatrix = next);
    }

    public AuditTimelineModel ProviderActivityTimeline { get; } = new()
    {
        Title = "Connection History",
        EmptyText = "No connection events recorded."
    };

    // ── IPageActionBarProvider implementation ──────────────────────────────────────
    public string PageTitle => "Provider Health";
    public ObservableCollection<ActionEntry> Actions => _collectionsSection.Actions;

    internal ProviderHealthCollectionsSectionViewModel CollectionsSection => _collectionsSection;

    internal ProviderHealthPostureSectionViewModel PostureSection => _postureSection;

    internal ProviderHealthManagementSectionViewModel ManagementSection => _managementSection;

    private bool SetProviderHealthSectionProperty<T>(
        T currentValue,
        T newValue,
        Action<T> assign,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(currentValue, newValue))
        {
            return false;
        }

        assign(newValue);
        RaisePropertyChanged(propertyName);
        return true;
    }

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

        ProviderManagementCommandGroup = BuildProviderManagementCommandGroup();
        CommandGroup = ProviderManagementCommandGroup;
        ProviderManagementTable = BuildProviderManagementTable(ProviderManagementRows);
        UpdateProviderMetricTiles();
    }

    public bool IsActive => _isActive;

    public CancellationToken ActivationToken => _activationCts?.Token ?? CancellationToken.None;

    public Task StartAsync(CancellationToken ct = default) => ActivateAsync(ct);

    public async Task ActivateAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_isActive)
        {
            return;
        }

        _isActive = true;
        _activationCts?.Dispose();
        _activationCts = ct.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : new CancellationTokenSource();

        _connectionService.StateChanged += OnConnectionStateChanged;
        _connectionService.ConnectionHealthUpdated += OnConnectionHealthUpdated;

        // Populate action bar.
        Actions.Clear();
        Actions.Add(new ActionEntry("Refresh", new RelayCommand(() => _ = RefreshAsync()), "\uE72C", "Refresh provider data", IsPrimary: true));
        Actions.Add(new ActionEntry("Run Diagnostics", new RelayCommand(RunSelectedProviderDiagnostics), "\uE9D9", "Run selected provider diagnostics"));
        Actions.Add(new ActionEntry("Reconnect All", new RelayCommand(() => _notificationService.NotifyInfo("Reconnecting", "Initiating provider reconnection...")), "\uE71B", "Reconnect all providers"));

        await RefreshDataAsync(ActivationToken);

        _refreshTimer.Start();
        _staleCheckTimer.Start();
        StartSparklineTimer();
    }

    public void Stop() => Deactivate();

    public void CancelActivation()
    {
        var refreshCts = Interlocked.Exchange(ref _cts, null);
        CancelAndDispose(refreshCts);
        var activationCts = Interlocked.Exchange(ref _activationCts, null);
        CancelAndDispose(activationCts);
    }

    public void Deactivate()
    {
        if (!_isActive)
        {
            return;
        }

        _isActive = false;
        _connectionService.StateChanged -= OnConnectionStateChanged;
        _connectionService.ConnectionHealthUpdated -= OnConnectionHealthUpdated;
        _refreshTimer.Stop();
        _staleCheckTimer.Stop();
        StopSparklineTimer();
        CancelActivation();
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
        RebuildProviderActivityTimeline();
        _notificationService.ShowNotification(
            "History Cleared",
            "Connection history has been cleared.",
            NotificationType.Info);
    }

    public async Task ExecuteProviderManagementCommandAsync(string commandId, CancellationToken ct = default)
    {
        switch (commandId)
        {
            case "AddProvider":
                OpenAddProviderWizard();
                break;
            case "RefreshStatus":
                await RefreshAsync(ct);
                break;
            case "RunDiagnostics":
                RunSelectedProviderDiagnostics();
                break;
            case "OpenSettings":
                OpenProviderSettings();
                break;
        }
    }

    public void RunSelectedProviderDiagnostics()
    {
        var selected = SelectedProviderManagementRow;
        if (selected is null)
        {
            ProviderVerificationStatusText = "Select a provider before running diagnostics.";
            _notificationService.ShowNotification(
                "No provider selected",
                "Select a provider before running diagnostics.",
                NotificationType.Warning);
            return;
        }

        ProviderVerificationStatusText =
            string.Equals(selected.VerificationStateText, "Verified", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(selected.VerificationStateText, "Not Required", StringComparison.OrdinalIgnoreCase)
                ? $"Credential verification passed for {selected.DisplayName}."
                : $"Credential verification needs review for {selected.DisplayName}.";

        _notificationService.ShowNotification(
            "Provider diagnostics",
            ProviderVerificationStatusText,
            string.Equals(selected.VerificationStateText, "Failed", StringComparison.OrdinalIgnoreCase)
                ? NotificationType.Warning
                : NotificationType.Info);
    }

    public void OpenAddProviderWizard() =>
        WpfServices.NavigationService.Instance.NavigateTo("AddProviderWizard");

    public void OpenProviderSettings() =>
        WpfServices.NavigationService.Instance.NavigateTo("Settings");

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
            _providerReadiness = await TryLoadProviderReadinessAsync(refreshCts.Token);
            UpdateSummaryStats();
            UpdateProviderManagementCenter();
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

    private async Task<ProviderReadinessSummaryDto?> TryLoadProviderReadinessAsync(CancellationToken ct)
    {
        try
        {
            return await SystemHealthService.Instance.GetProviderReadinessAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _loggingService.LogWarning($"Shared provider readiness endpoint unavailable; using local provider health projection. {ex.Message}");
            return null;
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
        UpdateProviderMetricTiles();
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
        ProviderPostureState = ToWorkstationState(posture);
        ProviderPostureBadge = new WorkstationBadgeModel(
            "Posture",
            posture.Tone.ToString(),
            posture.Icon,
            ToWorkspaceTone(posture.Tone));
    }

    private void UpdateProviderManagementCenter()
    {
        var previouslySelectedProviderId = SelectedProviderManagementRow?.ProviderId;
        var rows = _providerReadiness is not null
            ? BuildProviderManagementRows(_providerReadiness.Providers)
            : BuildProviderManagementRows(StreamingProviders, BackfillProviders, _lastRefreshTime);

        ProviderManagementRows.Clear();
        foreach (var row in rows)
        {
            ProviderManagementRows.Add(row);
        }

        SelectedProviderManagementRow = ProviderManagementRows.FirstOrDefault(row =>
                string.Equals(row.ProviderId, previouslySelectedProviderId, StringComparison.OrdinalIgnoreCase))
            ?? ProviderManagementRows.FirstOrDefault();

        ProviderManagementTable = BuildProviderManagementTable(ProviderManagementRows);
        UpdateProviderManagementSummary();
    }

    internal static IReadOnlyList<ProviderManagementRowModel> BuildProviderManagementRows(
        IReadOnlyList<ProviderReadinessRowDto> readinessRows)
        => readinessRows
            .Select(row =>
            {
                var health = HealthTextForReadiness(row.Status);
                var model = new ProviderManagementRowModel
                {
                    ProviderId = row.ProviderId,
                    DisplayName = row.DisplayName,
                    CapabilityText = CapabilityTextForReadiness(row.Capability),
                    HealthText = health,
                    CredentialStateText = StateText(row.CredentialState),
                    CredentialSourceText = $"Source: {StateText(row.CredentialSource)}",
                    VerificationStateText = StateText(row.VerificationState),
                    LastSuccessfulConnectionText = row.LastSuccessfulAt.HasValue
                        ? $"Last good: {row.LastSuccessfulAt.Value:yyyy-MM-dd HH:mm} UTC"
                        : row.IsConnected ? "Connected" : "Never",
                    LastVerifiedText = row.LastVerifiedAt.HasValue
                        ? $"Last verified: {row.LastVerifiedAt.Value:yyyy-MM-dd HH:mm} UTC"
                        : "Last verified: Never",
                    LastFailureText = row.LastFailureAt.HasValue
                        ? $"Last failure: {row.LastFailureAt.Value:yyyy-MM-dd HH:mm} UTC"
                        : "Last failure: None reported",
                    LastErrorText = string.IsNullOrWhiteSpace(row.LastError) ? "None reported" : row.LastError,
                    AffectedWorkflowsText = row.AffectedWorkflows.Count == 0 ? "Data readiness" : string.Join(", ", row.AffectedWorkflows),
                    RecommendedActionText = row.RecommendedAction,
                    ActionText = ActionForReadiness(row.Status),
                    EnvironmentText = string.IsNullOrWhiteSpace(row.Environment) ? "Not reported" : row.Environment,
                    MaskedKeyPreviewText = string.IsNullOrWhiteSpace(row.MaskedKeyPreview) ? "Not reported" : row.MaskedKeyPreview,
                    ExternalAccountIdText = string.IsNullOrWhiteSpace(row.ExternalAccountId) ? "Not reported" : row.ExternalAccountId,
                    FallbackText = row.FallbackActive ? "Fallback active" : "Fallback not active",
                    TrustExplanationText = BuildReadinessTrustExplanation(row),
                    IsFallbackActive = row.FallbackActive,
                    HealthBrush = BrushForHealth(health)
                };

                foreach (var diagnostic in BuildProviderDiagnostics(row, model))
                {
                    model.Diagnostics.Add(diagnostic);
                }

                return model;
            })
            .ToArray();

    private static string CapabilityTextForReadiness(ProviderConnectionCapabilityDto capability)
        => capability switch
        {
            ProviderConnectionCapabilityDto.DataAndBrokerage => "Data + Brokerage",
            ProviderConnectionCapabilityDto.AccountingSystem => "Accounting system",
            ProviderConnectionCapabilityDto.Brokerage => "Brokerage",
            _ => "Data"
        };

    private static string HealthTextForReadiness(ProviderReadinessStatusDto status)
        => status switch
        {
            ProviderReadinessStatusDto.Ready => "Healthy",
            ProviderReadinessStatusDto.Degraded => "Degraded",
            ProviderReadinessStatusDto.Blocked => "Blocked",
            ProviderReadinessStatusDto.Review => "Warning",
            _ => "Unknown"
        };

    private static string ActionForReadiness(ProviderReadinessStatusDto status)
        => status switch
        {
            ProviderReadinessStatusDto.Blocked => "Repair",
            ProviderReadinessStatusDto.Degraded => "Review",
            ProviderReadinessStatusDto.Review => "Configure",
            _ => "View Details"
        };

    private static string BuildReadinessTrustExplanation(ProviderReadinessRowDto row)
    {
        var degradation = row.DegradationScore.HasValue
            ? $"; degradation={row.DegradationScore.Value:P0}"
            : string.Empty;
        return $"Status={row.Status}; credentials={row.CredentialState}; verification={row.VerificationState}; connection={row.ConnectionHealth}{degradation}.";
    }

    private static IEnumerable<ProviderManagementDiagnosticModel> BuildProviderDiagnostics(
        ProviderReadinessRowDto readiness,
        ProviderManagementRowModel row)
    {
        foreach (var evidence in readiness.Evidence)
        {
            var statusText = evidence.Status switch
            {
                ProviderReadinessStatusDto.Ready => "Pass",
                ProviderReadinessStatusDto.Blocked => "Fail",
                ProviderReadinessStatusDto.Degraded => "Review",
                ProviderReadinessStatusDto.Review => "Review",
                _ => "Unknown"
            };

            yield return new ProviderManagementDiagnosticModel
            {
                Label = evidence.Label,
                StatusText = statusText,
                Detail = evidence.Detail,
                StatusBrush = BrushForHealth(HealthTextForReadiness(evidence.Status))
            };
        }

        if (readiness.Evidence.Count == 0)
        {
            foreach (var diagnostic in BuildProviderDiagnostics(row))
            {
                yield return diagnostic;
            }
        }
    }

    private static string StateText<T>(T value)
        where T : Enum
        => value.ToString() switch
        {
            "NotRequired" => "Not Required",
            "NotVerified" => "Not Verified",
            "LocalEncryptedStore" => "encrypted store",
            "ExternalVaultReference" => "external vault",
            var text => SplitPascalCase(text)
        };

    private static string SplitPascalCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = new List<char>(value.Length + 4) { value[0] };
        for (var i = 1; i < value.Length; i++)
        {
            if (char.IsUpper(value[i]) && !char.IsWhiteSpace(value[i - 1]))
            {
                chars.Add(' ');
            }

            chars.Add(value[i]);
        }

        return new string(chars.ToArray());
    }

    private void UpdateProviderManagementSummary()
    {
        var selected = SelectedProviderManagementRow;
        ProviderManagementSummaryCards.Clear();

        if (selected is null)
        {
            ProviderManagementSummaryCards.Add(new ProviderManagementSummaryCardModel
            {
                Label = "Connection Health",
                Value = "No provider",
                Detail = "Configure a provider before routing Data workspace workflows.",
                AccentBrush = CreateModelBrush(139, 148, 158)
            });
            ProviderManagementStatusText = "No configured provider is available for management.";
            SelectedProviderInspector = BuildEmptyInspector();
            SelectedProviderDiagnostics = BuildEmptyDiagnostics();
            SelectedProviderRoutingMatrix = BuildEmptyRoutingMatrix();
            return;
        }

        ProviderManagementSummaryCards.Add(new ProviderManagementSummaryCardModel
        {
            Label = "Connection Health",
            Value = selected.HealthText,
            Detail = selected.TrustExplanationText,
            AccentBrush = selected.HealthBrush
        });
        ProviderManagementSummaryCards.Add(new ProviderManagementSummaryCardModel
        {
            Label = "Credential State",
            Value = selected.CredentialStateText,
            Detail = selected.CredentialSourceText,
            AccentBrush = BrushForCredentialState(selected.CredentialStateText)
        });
        ProviderManagementSummaryCards.Add(new ProviderManagementSummaryCardModel
        {
            Label = "Verification State",
            Value = selected.VerificationStateText,
            Detail = "Verification remains separate from configured state.",
            AccentBrush = BrushForVerificationState(selected.VerificationStateText)
        });
        ProviderManagementSummaryCards.Add(new ProviderManagementSummaryCardModel
        {
            Label = "Latency / Last Response",
            Value = selected.LastSuccessfulConnectionText,
            Detail = selected.LastVerifiedText,
            AccentBrush = CreateModelBrush(31, 111, 235)
        });
        ProviderManagementSummaryCards.Add(new ProviderManagementSummaryCardModel
        {
            Label = "Affected Workflows",
            Value = selected.AffectedWorkflowsText,
            Detail = selected.FallbackText,
            AccentBrush = CreateModelBrush(139, 148, 158)
        });
        ProviderManagementSummaryCards.Add(new ProviderManagementSummaryCardModel
        {
            Label = "Recommended Action",
            Value = selected.ActionText,
            Detail = selected.RecommendedActionText,
            AccentBrush = selected.HealthBrush
        });

        ProviderManagementStatusText =
            $"{ProviderManagementRows.Count} provider(s) loaded; selected {selected.DisplayName}. " +
            "Routing and advanced diagnostics are read-only placeholders in the desktop provider workflow.";
        SelectedProviderInspector = BuildProviderInspector(selected);
        SelectedProviderDiagnostics = BuildProviderDiagnosticsChecklist(selected);
        SelectedProviderRoutingMatrix = BuildProviderRoutingMatrix(selected);
    }

    internal static WorkstationTableModel<ProviderManagementRowModel> BuildProviderManagementTable(
        ObservableCollection<ProviderManagementRowModel> rows)
        => new(
            rows,
            [
                new("Provider", nameof(ProviderManagementRowModel.DisplayName), 150),
                new("Capability", nameof(ProviderManagementRowModel.CapabilityText), 130),
                new("Status", nameof(ProviderManagementRowModel.HealthText), 90),
                new("Credential", nameof(ProviderManagementRowModel.CredentialStateText), 105),
                new("Verification", nameof(ProviderManagementRowModel.VerificationStateText), 110),
                new("Last good", nameof(ProviderManagementRowModel.LastSuccessfulConnectionText), 130),
                new("Workflows", nameof(ProviderManagementRowModel.AffectedWorkflowsText), 190),
                new("Action", nameof(ProviderManagementRowModel.ActionText), 90)
            ],
            "Provider management table",
            "No providers discovered",
            "Configure a provider before routing Data workspace workflows.");

    internal static WorkstationCommandGroupModel BuildProviderManagementCommandGroup()
        => new()
        {
            PrimaryCommands =
            [
                new("AddProvider", "Add Provider", "Add a provider configuration.", "\uE710", WorkspaceTone.Primary),
                new("RefreshStatus", "Refresh Status", "Refresh provider health and routing posture.", "\uE72C"),
                new("RunDiagnostics", "Run Diagnostics", "Run selected provider diagnostics.", "\uE9D9")
            ],
            SecondaryCommands =
            [
                new("OpenSettings", "Open Settings", "Open provider settings.", "\uE713")
            ]
        };

    internal static InspectorPanelModel BuildProviderInspector(ProviderManagementRowModel selected)
        => new()
        {
            Title = selected.DisplayName,
            Subtitle = selected.CapabilityText,
            Detail = selected.TrustExplanationText,
            Badge = new WorkstationBadgeModel("Health", selected.HealthText, "\uE946", ToneForHealth(selected.HealthText)),
            Facts =
            [
                new("Credential state", selected.CredentialStateText, selected.CredentialSourceText),
                new("Verification", selected.VerificationStateText, selected.LastVerifiedText),
                new("Connection", selected.LastSuccessfulConnectionText),
                new("Fallback", selected.FallbackText),
                new("Last failure", selected.LastFailureText),
                new("Last error", selected.LastErrorText),
                new("Masked preview", selected.MaskedKeyPreviewText),
                new("External account", selected.ExternalAccountIdText)
            ]
        };

    internal static DiagnosticsChecklistModel BuildProviderDiagnosticsChecklist(ProviderManagementRowModel selected)
        => new()
        {
            Title = "Diagnostics",
            Detail = "Credential checks stay separate from provider health so routing decisions remain explainable.",
            Items = selected.Diagnostics
                .Select(static diagnostic => new DiagnosticsChecklistItemModel(
                    diagnostic.Label,
                    diagnostic.StatusText,
                    diagnostic.Detail,
                    ToneForDiagnosticStatus(diagnostic.StatusText)))
                .ToArray()
        };

    internal static RoutingMatrixModel BuildProviderRoutingMatrix(ProviderManagementRowModel selected)
        => new()
        {
            Title = "Routing Matrix",
            Detail = selected.RecommendedActionText,
            Rows = selected.AffectedWorkflowsText
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(workflow => new RoutingMatrixRowModel(
                    workflow,
                    selected.DisplayName,
                    selected.HealthText,
                    selected.FallbackText,
                    ToneForHealth(selected.HealthText)))
                .ToArray()
        };

    private static InspectorPanelModel BuildEmptyInspector()
        => new()
        {
            Title = "No provider selected",
            Subtitle = "Provider management",
            Detail = "Select a provider to inspect credentials, routing, and diagnostics."
        };

    private static DiagnosticsChecklistModel BuildEmptyDiagnostics()
        => new()
        {
            Title = "Diagnostics",
            Detail = "Select a provider to review credential and route diagnostics."
        };

    private static RoutingMatrixModel BuildEmptyRoutingMatrix()
        => new()
        {
            Title = "Routing Matrix",
            Detail = "Select a provider to review affected workstation workflows."
        };

    internal static IReadOnlyList<ProviderManagementRowModel> BuildProviderManagementRows(
        IReadOnlyCollection<ProviderStatusModel> streamingProviders,
        IReadOnlyCollection<BackfillProviderModel> backfillProviders,
        DateTime? lastRefreshUtc)
    {
        var providerIds = streamingProviders.Select(provider => provider.ProviderId)
            .Concat(backfillProviders.Select(provider => provider.ProviderId))
            .Where(providerId => !string.IsNullOrWhiteSpace(providerId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(providerId => providerId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = new List<ProviderManagementRowModel>(providerIds.Count);
        foreach (var providerId in providerIds)
        {
            var streaming = streamingProviders.FirstOrDefault(provider =>
                string.Equals(provider.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
            var backfill = backfillProviders.FirstOrDefault(provider =>
                string.Equals(provider.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));

            var displayName = streaming?.Name ?? backfill?.Name ?? providerId;
            var isStreaming = streaming is not null;
            var isBackfill = backfill is not null;
            var isConnected = streaming?.IsConnected == true;
            var isBackfillAvailable = string.Equals(backfill?.StatusText, "Available", StringComparison.OrdinalIgnoreCase);
            var isExplicitlyNotConfigured =
                string.Equals(streaming?.StatusText, "Not Configured", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(backfill?.StatusText, "Not Configured", StringComparison.OrdinalIgnoreCase);
            var isConfigured = isConnected || isBackfillAvailable ||
                !isExplicitlyNotConfigured;

            var credentialState = CredentialStateForProvider(providerId, isConfigured, isBackfillAvailable, isExplicitlyNotConfigured);
            var verificationState = VerificationStateForProvider(providerId, isConnected, isBackfillAvailable, credentialState);
            var health = HealthForProvider(streaming, backfill, isConnected, isBackfillAvailable);
            var workflows = WorkflowsForProvider(providerId, isStreaming, isBackfill);
            var capability = isStreaming && isBackfill ? "Data + Backfill" : isStreaming ? "Live data" : "Historical backfill";
            var lastSuccessful = isConnected
                ? streaming?.LatencyText ?? "Connected"
                : isBackfillAvailable
                    ? backfill?.LastUsedText ?? "Available"
                    : "Never";

            var row = new ProviderManagementRowModel
            {
                ProviderId = providerId,
                DisplayName = displayName,
                CapabilityText = capability,
                HealthText = health,
                CredentialStateText = credentialState,
                CredentialSourceText = CredentialSourceForProvider(providerId, credentialState),
                VerificationStateText = verificationState,
                LastSuccessfulConnectionText = lastSuccessful,
                LastVerifiedText = lastRefreshUtc.HasValue ? $"Last verified: {lastRefreshUtc.Value:yyyy-MM-dd HH:mm} UTC" : "Last verified: Never",
                LastFailureText = health is "Healthy" ? "Last failure: None reported" : "Last failure: Not reported",
                LastErrorText = health is "Blocked" ? "Provider is not configured for dependent workflows." : "None reported",
                AffectedWorkflowsText = string.Join(", ", workflows),
                RecommendedActionText = RecommendedActionForProvider(health, credentialState, verificationState),
                ActionText = ActionForProvider(health, credentialState, verificationState),
                EnvironmentText = EnvironmentForProvider(providerId),
                MaskedKeyPreviewText = credentialState is "Not Required" ? "Not required" : "Stored values remain masked",
                ExternalAccountIdText = IsBrokerageProvider(providerId) ? "Not reported" : "Not applicable",
                FallbackText = isBackfillAvailable && !isConnected && isStreaming ? "Fallback active for historical repair only" : "Fallback not active",
                TrustExplanationText = TrustExplanationForProvider(health, credentialState, verificationState),
                IsFallbackActive = isBackfillAvailable && !isConnected && isStreaming,
                HealthBrush = BrushForHealth(health)
            };

            foreach (var diagnostic in BuildProviderDiagnostics(row))
            {
                row.Diagnostics.Add(diagnostic);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static IEnumerable<ProviderManagementDiagnosticModel> BuildProviderDiagnostics(ProviderManagementRowModel row)
    {
        yield return new ProviderManagementDiagnosticModel
        {
            Label = "Credential presence",
            StatusText = row.CredentialStateText is "Missing" or "Invalid" ? "Fail" : "Pass",
            Detail = row.CredentialStateText,
            StatusBrush = row.CredentialStateText is "Missing" or "Invalid" ? CreateModelBrush(244, 67, 54) : CreateModelBrush(63, 185, 80)
        };
        yield return new ProviderManagementDiagnosticModel
        {
            Label = "Credential verification",
            StatusText = row.VerificationStateText is "Failed" ? "Fail" : row.VerificationStateText is "Not Verified" ? "Review" : "Pass",
            Detail = row.VerificationStateText,
            StatusBrush = row.VerificationStateText is "Failed"
                ? CreateModelBrush(244, 67, 54)
                : row.VerificationStateText is "Not Verified"
                    ? CreateModelBrush(255, 193, 7)
                    : CreateModelBrush(63, 185, 80)
        };
        yield return new ProviderManagementDiagnosticModel
        {
            Label = "Endpoint reachable",
            StatusText = "Placeholder",
            Detail = "Provider-specific reachability checks are a follow-up phase for the desktop provider workflow.",
            StatusBrush = CreateModelBrush(139, 148, 158)
        };
        yield return new ProviderManagementDiagnosticModel
        {
            Label = "Sample quote test",
            StatusText = "Placeholder",
            Detail = "Quote diagnostics will use provider-specific checks when shared diagnostics are wired here.",
            StatusBrush = CreateModelBrush(139, 148, 158)
        };
        yield return new ProviderManagementDiagnosticModel
        {
            Label = "Sample backfill test",
            StatusText = "Placeholder",
            Detail = "Backfill diagnostics will use provider-specific checks when shared diagnostics are wired here.",
            StatusBrush = CreateModelBrush(139, 148, 158)
        };
    }

    private static string CredentialStateForProvider(
        string providerId,
        bool isConfigured,
        bool isBackfillAvailable,
        bool isExplicitlyNotConfigured)
    {
        if (IsCredentialFreeProvider(providerId))
        {
            return "Not Required";
        }

        if (isExplicitlyNotConfigured)
        {
            return "Missing";
        }

        return isConfigured || isBackfillAvailable || CheckCredentialsConfigured(providerId)
            ? "Configured"
            : "Missing";
    }

    private static string VerificationStateForProvider(string providerId, bool isConnected, bool isBackfillAvailable, string credentialState)
    {
        if (credentialState is "Not Required")
        {
            return "Not Required";
        }

        if (isConnected)
        {
            return "Verified";
        }

        if (isBackfillAvailable || credentialState is "Configured")
        {
            return "Not Verified";
        }

        return "Failed";
    }

    private static string HealthForProvider(
        ProviderStatusModel? streaming,
        BackfillProviderModel? backfill,
        bool isConnected,
        bool isBackfillAvailable)
    {
        if (isConnected || isBackfillAvailable)
        {
            return "Healthy";
        }

        if (string.Equals(streaming?.StatusText, "Reconnecting...", StringComparison.OrdinalIgnoreCase))
        {
            return "Warning";
        }

        return string.Equals(streaming?.StatusText, "Not Configured", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(backfill?.StatusText, "Not Configured", StringComparison.OrdinalIgnoreCase)
            ? "Blocked"
            : "Warning";
    }

    private static IReadOnlyList<string> WorkflowsForProvider(string providerId, bool isStreaming, bool isBackfill)
    {
        var workflows = new List<string>();
        if (isStreaming)
        {
            workflows.Add("Live quotes");
            workflows.Add(IsBrokerageProvider(providerId) ? "Paper trading" : "Strategy");
        }

        if (isBackfill)
        {
            workflows.Add("Backfill");
            workflows.Add("Strategy");
        }

        if (IsBrokerageProvider(providerId))
        {
            workflows.Add("Brokerage");
        }

        return workflows.Count > 0 ? workflows.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : ["Workflow impact not declared"];
    }

    private static string RecommendedActionForProvider(string health, string credentialState, string verificationState)
    {
        if (credentialState is "Missing" or "Invalid")
        {
            return "Add credentials in Settings before routing dependent workflows.";
        }

        if (verificationState is "Not Verified" or "Failed")
        {
            return "Run credential verification before routing dependent workflows.";
        }

        if (health is "Blocked")
        {
            return "Configure provider access before using this provider.";
        }

        if (health is "Warning")
        {
            return "Review provider posture before live handoffs.";
        }

        return "Keep provider active and monitor diagnostics.";
    }

    private static string ActionForProvider(string health, string credentialState, string verificationState)
    {
        if (credentialState is "Missing" or "Invalid")
        {
            return "Configure";
        }

        if (verificationState is "Not Verified" or "Failed")
        {
            return "Verify";
        }

        return health is "Blocked" ? "Repair" : "View Details";
    }

    private static string CredentialSourceForProvider(string providerId, string credentialState)
    {
        if (credentialState is "Not Required")
        {
            return "Source: not required";
        }

        return CheckCredentialsConfigured(providerId)
            ? "Source: environment or encrypted store"
            : "Source: missing";
    }

    private static string EnvironmentForProvider(string providerId) =>
        providerId.Equals("alpaca", StringComparison.OrdinalIgnoreCase) ? "Paper or live" :
        IsBrokerageProvider(providerId) ? "Live/sandbox" : "Default";

    private static string TrustExplanationForProvider(string health, string credentialState, string verificationState) =>
        $"Health={health}; credentials={credentialState}; verification={verificationState}; routing mutation not enabled in desktop MVP.";

    private static bool IsCredentialFreeProvider(string providerId) =>
        providerId.Equals("yahoo", StringComparison.OrdinalIgnoreCase) ||
        providerId.Equals("stooq", StringComparison.OrdinalIgnoreCase);

    private static bool IsBrokerageProvider(string providerId) =>
        providerId.Equals("alpaca", StringComparison.OrdinalIgnoreCase) ||
        providerId.Equals("ib", StringComparison.OrdinalIgnoreCase) ||
        providerId.Equals("interactivebrokers", StringComparison.OrdinalIgnoreCase) ||
        providerId.Equals("robinhood", StringComparison.OrdinalIgnoreCase);

    private static SolidColorBrush BrushForHealth(string health) =>
        health switch
        {
            "Healthy" => CreateModelBrush(63, 185, 80),
            "Warning" => CreateModelBrush(255, 193, 7),
            "Degraded" => CreateModelBrush(255, 193, 7),
            "Blocked" => CreateModelBrush(244, 67, 54),
            _ => CreateModelBrush(139, 148, 158)
        };

    private static SolidColorBrush BrushForCredentialState(string credentialState) =>
        credentialState switch
        {
            "Configured" or "Verified" or "Not Required" => CreateModelBrush(63, 185, 80),
            "Missing" or "Invalid" => CreateModelBrush(244, 67, 54),
            _ => CreateModelBrush(255, 193, 7)
        };

    private static SolidColorBrush BrushForVerificationState(string verificationState) =>
        verificationState switch
        {
            "Verified" or "Not Required" => CreateModelBrush(63, 185, 80),
            "Failed" => CreateModelBrush(244, 67, 54),
            _ => CreateModelBrush(255, 193, 7)
        };

    private static SolidColorBrush CreateModelBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private void UpdateProviderMetricTiles()
    {
        ProviderMetricTiles.Clear();
        ProviderMetricTiles.Add(new WorkstationMetricModel(
            "Connected",
            ConnectedCount,
            "Streaming sessions ready",
            "\uE73E",
            WorkspaceTone.Success,
            ReadyPostureBrush));
        ProviderMetricTiles.Add(new WorkstationMetricModel(
            "Disconnected",
            DisconnectedCount,
            "Streaming sessions needing review",
            "\uE783",
            WorkspaceTone.Danger,
            ErrorBrush()));
        ProviderMetricTiles.Add(new WorkstationMetricModel(
            "Avg Latency",
            AvgLatency,
            "Milliseconds for active feed",
            "\uE916",
            WorkspaceTone.Info,
            InfoBrush()));
        ProviderMetricTiles.Add(new WorkstationMetricModel(
            "Total Providers",
            TotalProviders,
            "Streaming plus backfill",
            "\uE968",
            WorkspaceTone.Neutral,
            NeutralPostureBrush));
    }

    private void RebuildProviderActivityTimeline()
    {
        ProviderActivityTimeline.Entries.Clear();
        foreach (var evt in ConnectionHistory)
        {
            ProviderActivityTimeline.Entries.Add(new AuditTimelineEntryModel(
                evt.Message,
                evt.Provider,
                evt.TimeText,
                WorkspaceTone.Info));
        }
    }

    private static WorkstationStateModel ToWorkstationState(ProviderHealthPostureState posture)
    {
        var kind = posture.Tone switch
        {
            ProviderHealthPostureTone.Ready => WorkstationStateKind.Ready,
            ProviderHealthPostureTone.Warning => WorkstationStateKind.Stale,
            ProviderHealthPostureTone.Critical => WorkstationStateKind.Blocked,
            _ => WorkstationStateKind.Empty
        };

        return new WorkstationStateModel(
            kind,
            posture.Title,
            posture.Detail,
            posture.ActionText,
            posture.TargetText,
            posture.EvidenceText,
            posture.Icon,
            ToWorkspaceTone(posture.Tone));
    }

    private static string ToWorkspaceTone(ProviderHealthPostureTone tone)
        => tone switch
        {
            ProviderHealthPostureTone.Ready => WorkspaceTone.Success,
            ProviderHealthPostureTone.Warning => WorkspaceTone.Warning,
            ProviderHealthPostureTone.Critical => WorkspaceTone.Danger,
            _ => WorkspaceTone.Neutral
        };

    private static string ToneForHealth(string health)
        => health switch
        {
            "Healthy" => WorkspaceTone.Success,
            "Warning" => WorkspaceTone.Warning,
            "Degraded" => WorkspaceTone.Warning,
            "Blocked" => WorkspaceTone.Danger,
            _ => WorkspaceTone.Neutral
        };

    private static string ToneForDiagnosticStatus(string status)
        => status switch
        {
            "Pass" => WorkspaceTone.Success,
            "Fail" => WorkspaceTone.Danger,
            "Review" => WorkspaceTone.Warning,
            _ => WorkspaceTone.Neutral
        };

    private static Brush InfoBrush() => CreateModelBrush(31, 111, 235);

    private static Brush ErrorBrush() => CreateModelBrush(244, 67, 54);

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

        RebuildProviderActivityTimeline();
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
            "Provider entries are present but none are ready for operator workflows. Finish credentials and connectivity before starting Data workspace workflows.",
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
        StopSparklineTimer();

        var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        _sparklineTimer = timer;
        _ = RefreshSparklineDataAsync(timer, ActivationToken);
    }

    private void StopSparklineTimer()
    {
        var timer = Interlocked.Exchange(ref _sparklineTimer, null);
        timer?.Dispose();
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

    private async Task RefreshSparklineDataAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            while (ReferenceEquals(_sparklineTimer, timer) && !ct.IsCancellationRequested)
            {
                if (!await timer.WaitForNextTickAsync(ct))
                {
                    break;
                }

                if (!ReferenceEquals(_sparklineTimer, timer) || _isDisposed || !_isActive)
                {
                    break;
                }

                _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(UpdateSparklineData);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
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

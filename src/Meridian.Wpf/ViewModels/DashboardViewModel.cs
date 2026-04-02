using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Dashboard page.
/// Holds all state, business logic, cached resources, timer management, and commands
/// so that the code-behind can be thinned to lifecycle wiring only (M1–M4, M6, M7).
/// Provides contextual commands for the command palette when activated.
/// </summary>
public sealed class DashboardViewModel : BindableBase, IDisposable, IPageActionBarProvider, ICommandContextProvider
{
    private const int MaxActivityItems = 25;
    private const int SparklineCapacity = 30;

    private readonly WpfServices.NavigationService _navigationService;
    private readonly WpfServices.ConnectionService _connectionService;
    private readonly WpfServices.StatusService _statusService;
    private readonly WpfServices.MessagingService _messagingService;
    private readonly WpfServices.NotificationService _notificationService;
    private readonly AlertService _alertService;
    private readonly ActivityFeedService _activityFeedService;
    private readonly WpfServices.TaskbarProgressService _taskbarProgressService;
    private readonly WpfServices.LoggingService _loggingService;
    private readonly CommandPaletteService _commandPaletteService;

    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _staleCheckTimer;
    private readonly DispatcherTimer _activityPollTimer;
    private readonly CancellationTokenSource _cts = new();   // P6: lifecycle cancellation

    // Sparkline history buffers – store last N data points for each metric card.
    private readonly List<double> _publishedHistory = new(SparklineCapacity);
    private readonly List<double> _droppedHistory = new(SparklineCapacity);
    private readonly List<double> _integrityHistory = new(SparklineCapacity);
    private readonly List<double> _historicalHistory = new(SparklineCapacity);
    private readonly List<double> _throughputHistory = new(SparklineCapacity);

    // Cached brush and icon resources – fetched once at construction time (P2 fix).
    private readonly Brush _successBrush;
    private readonly Brush _errorBrush;
    private readonly Brush _warningBrush;
    private readonly Brush _secondaryTextBrush;
    private readonly Brush _infoBrush;

    private readonly string _iconInfo;
    private readonly string _iconSuccess;
    private readonly string _iconPause;
    private readonly string _iconPlay;
    private readonly string _iconChevronDown;
    private readonly string _iconChevronUp;

    private bool _isCollectorPaused;
    private long _previousPublished;
    private DateTime _lastRateCalcTime = DateTime.UtcNow;

    // ── Collections ──────────────────────────────────────────────────────────────

    public ObservableCollection<DashboardActivityItem> ActivityItems { get; } = new();
    public ObservableCollection<SymbolPerformanceItem> SymbolPerformanceItems { get; } = new();
    public ObservableCollection<SymbolFreshnessItem> SymbolFreshnessItems { get; } = new();
    public ObservableCollection<IntegrityEventItem> IntegrityEventItems { get; } = new();

    // ── Metric-card properties ────────────────────────────────────────────────────

    private string _publishedCount = "0";
    public string PublishedCount { get => _publishedCount; private set => SetProperty(ref _publishedCount, value); }

    private string _publishedRateText = "0/s";
    public string PublishedRateText { get => _publishedRateText; private set => SetProperty(ref _publishedRateText, value); }

    private string _droppedCount = "0";
    public string DroppedCount { get => _droppedCount; private set => SetProperty(ref _droppedCount, value); }

    private string _droppedRateText = "0.00%";
    public string DroppedRateText { get => _droppedRateText; private set => SetProperty(ref _droppedRateText, value); }

    private string _integrityCount = "0";
    public string IntegrityCount { get => _integrityCount; private set => SetProperty(ref _integrityCount, value); }

    private string _integrityRateText = "0 gaps";
    public string IntegrityRateText { get => _integrityRateText; private set => SetProperty(ref _integrityRateText, value); }

    private string _historicalCount = "0";
    public string HistoricalCount { get => _historicalCount; private set => SetProperty(ref _historicalCount, value); }

    private string _historicalTrendText = "No data";
    public string HistoricalTrendText { get => _historicalTrendText; private set => SetProperty(ref _historicalTrendText, value); }

    // ── Collector status badge (header) ──────────────────────────────────────────

    private string _collectorStatusText = "Stopped";
    public string CollectorStatusText { get => _collectorStatusText; private set => SetProperty(ref _collectorStatusText, value); }

    private Brush _collectorStatusBadgeBackground;
    public Brush CollectorStatusBadgeBackground { get => _collectorStatusBadgeBackground; private set => SetProperty(ref _collectorStatusBadgeBackground, value); }

    // ── Alert summary badges (header) ────────────────────────────────────────────

    private bool _isAlertCriticalBadgeVisible;
    public bool IsAlertCriticalBadgeVisible { get => _isAlertCriticalBadgeVisible; private set => SetProperty(ref _isAlertCriticalBadgeVisible, value); }

    private string _alertCriticalCount = "0";
    public string AlertCriticalCount { get => _alertCriticalCount; private set => SetProperty(ref _alertCriticalCount, value); }

    private bool _isAlertWarningBadgeVisible;
    public bool IsAlertWarningBadgeVisible { get => _isAlertWarningBadgeVisible; private set => SetProperty(ref _isAlertWarningBadgeVisible, value); }

    private string _alertWarningCount = "0";
    public string AlertWarningCount { get => _alertWarningCount; private set => SetProperty(ref _alertWarningCount, value); }

    // ── Sparkline point collections (for metric card mini-charts) ─────────────────

    private PointCollection _publishedSparkline = new();
    public PointCollection PublishedSparkline { get => _publishedSparkline; private set => SetProperty(ref _publishedSparkline, value); }

    private PointCollection _droppedSparkline = new();
    public PointCollection DroppedSparkline { get => _droppedSparkline; private set => SetProperty(ref _droppedSparkline, value); }

    private PointCollection _integritySparkline = new();
    public PointCollection IntegritySparkline { get => _integritySparkline; private set => SetProperty(ref _integritySparkline, value); }

    private PointCollection _historicalSparkline = new();
    public PointCollection HistoricalSparkline { get => _historicalSparkline; private set => SetProperty(ref _historicalSparkline, value); }

    private PointCollection _throughputSparkline = new();
    public PointCollection ThroughputSparkline { get => _throughputSparkline; private set => SetProperty(ref _throughputSparkline, value); }

    // ── Sparkline area fills (closed polygon underneath the line) ──────────────
    private PointCollection _publishedSparklineFill = new();
    public PointCollection PublishedSparklineFill { get => _publishedSparklineFill; private set => SetProperty(ref _publishedSparklineFill, value); }

    private PointCollection _droppedSparklineFill = new();
    public PointCollection DroppedSparklineFill { get => _droppedSparklineFill; private set => SetProperty(ref _droppedSparklineFill, value); }

    private PointCollection _integritySparklineFill = new();
    public PointCollection IntegritySparklineFill { get => _integritySparklineFill; private set => SetProperty(ref _integritySparklineFill, value); }

    private PointCollection _historicalSparklineFill = new();
    public PointCollection HistoricalSparklineFill { get => _historicalSparklineFill; private set => SetProperty(ref _historicalSparklineFill, value); }

    // ── Throughput statistics ───────────────────────────────────────────────────

    private string _avgThroughputText = "--";
    public string AvgThroughputText { get => _avgThroughputText; private set => SetProperty(ref _avgThroughputText, value); }

    private string _peakThroughputText = "--";
    public string PeakThroughputText { get => _peakThroughputText; private set => SetProperty(ref _peakThroughputText, value); }

    // ── Throughput / data health ──────────────────────────────────────────────────

    private string _currentThroughputText = "--";
    public string CurrentThroughputText { get => _currentThroughputText; private set => SetProperty(ref _currentThroughputText, value); }

    private string _dataHealthText = "--";
    public string DataHealthText { get => _dataHealthText; private set => SetProperty(ref _dataHealthText, value); }

    private Brush _dataHealthForeground;
    public Brush DataHealthForeground { get => _dataHealthForeground; private set => SetProperty(ref _dataHealthForeground, value); }

    private string _dataHealthIconGlyph = string.Empty;
    public string DataHealthIconGlyph { get => _dataHealthIconGlyph; private set => SetProperty(ref _dataHealthIconGlyph, value); }

    private Brush _dataHealthIconForeground;
    public Brush DataHealthIconForeground { get => _dataHealthIconForeground; private set => SetProperty(ref _dataHealthIconForeground, value); }

    private string _dataQualityText = "--";
    public string DataQualityText { get => _dataQualityText; private set => SetProperty(ref _dataQualityText, value); }

    // ── Quick actions – uptime, pause button ─────────────────────────────────────

    private string _collectorUptimeText = "0h 0m 0s";
    public string CollectorUptimeText { get => _collectorUptimeText; private set => SetProperty(ref _collectorUptimeText, value); }

    private string _pauseButtonText = "Pause Collection";
    public string PauseButtonText { get => _pauseButtonText; private set => SetProperty(ref _pauseButtonText, value); }

    private string _pauseButtonIconGlyph = string.Empty;
    public string PauseButtonIconGlyph { get => _pauseButtonIconGlyph; private set => SetProperty(ref _pauseButtonIconGlyph, value); }

    // ── Connection status ─────────────────────────────────────────────────────────

    private Brush _connectionStatusFill;
    public Brush ConnectionStatusFill { get => _connectionStatusFill; private set => SetProperty(ref _connectionStatusFill, value); }

    private string _connectionStatusText = "Disconnected";
    public string ConnectionStatusText { get => _connectionStatusText; private set => SetProperty(ref _connectionStatusText, value); }

    private string _lastUpdateText = "Last update: --";
    public string LastUpdateText { get => _lastUpdateText; private set => SetProperty(ref _lastUpdateText, value); }

    private Brush _lastUpdateForeground;
    public Brush LastUpdateForeground { get => _lastUpdateForeground; private set => SetProperty(ref _lastUpdateForeground, value); }

    private string _uptimeText = "--:--:--";
    public string UptimeText { get => _uptimeText; private set => SetProperty(ref _uptimeText, value); }

    private string _latencyText = "-- ms";
    public string LatencyText { get => _latencyText; private set => SetProperty(ref _latencyText, value); }

    private string _avgLatencyText = "-- ms";
    public string AvgLatencyText { get => _avgLatencyText; private set => SetProperty(ref _avgLatencyText, value); }

    // ── Active provider ───────────────────────────────────────────────────────────

    private string _selectedDataSourceText = "Not Connected";
    public string SelectedDataSourceText { get => _selectedDataSourceText; private set => SetProperty(ref _selectedDataSourceText, value); }

    private string _providerDescriptionText = "Awaiting connection";
    public string ProviderDescriptionText { get => _providerDescriptionText; private set => SetProperty(ref _providerDescriptionText, value); }

    // ── Symbol performance header ─────────────────────────────────────────────────

    private string _symbolCount = "0";
    public string SymbolCount { get => _symbolCount; private set => SetProperty(ref _symbolCount, value); }

    // ── Data freshness ────────────────────────────────────────────────────────────

    private string _lastDataUpdateText = "Last update: --";
    public string LastDataUpdateText { get => _lastDataUpdateText; private set => SetProperty(ref _lastDataUpdateText, value); }

    // ── Quick stats summary ───────────────────────────────────────────────────────

    private string _totalEventsToday = "0";
    public string TotalEventsToday { get => _totalEventsToday; private set => SetProperty(ref _totalEventsToday, value); }

    private string _activeSymbolsCount = "0";
    public string ActiveSymbolsCount { get => _activeSymbolsCount; private set => SetProperty(ref _activeSymbolsCount, value); }

    // ── Activity feed empty state ─────────────────────────────────────────────────

    private bool _isNoActivityVisible = true;
    public bool IsNoActivityVisible { get => _isNoActivityVisible; private set => SetProperty(ref _isNoActivityVisible, value); }

    // ── Integrity panel ───────────────────────────────────────────────────────────

    private string _criticalAlertsCount = "0";
    public string CriticalAlertsCount { get => _criticalAlertsCount; private set => SetProperty(ref _criticalAlertsCount, value); }

    private string _warningAlertsCount = "0";
    public string WarningAlertsCount { get => _warningAlertsCount; private set => SetProperty(ref _warningAlertsCount, value); }

    private bool _isCriticalAlertsBadgeVisible;
    public bool IsCriticalAlertsBadgeVisible { get => _isCriticalAlertsBadgeVisible; private set => SetProperty(ref _isCriticalAlertsBadgeVisible, value); }

    private bool _isWarningAlertsBadgeVisible;
    public bool IsWarningAlertsBadgeVisible { get => _isWarningAlertsBadgeVisible; private set => SetProperty(ref _isWarningAlertsBadgeVisible, value); }

    private bool _isIntegrityDetailsPanelVisible;
    public bool IsIntegrityDetailsPanelVisible { get => _isIntegrityDetailsPanelVisible; private set => SetProperty(ref _isIntegrityDetailsPanelVisible, value); }

    private string _expandIntegrityText = "Show Details";
    public string ExpandIntegrityText { get => _expandIntegrityText; private set => SetProperty(ref _expandIntegrityText, value); }

    private string _expandIntegrityIconGlyph = string.Empty;
    public string ExpandIntegrityIconGlyph { get => _expandIntegrityIconGlyph; private set => SetProperty(ref _expandIntegrityIconGlyph, value); }

    private string _integrityTotalEventsText = "0";
    public string IntegrityTotalEventsText { get => _integrityTotalEventsText; private set => SetProperty(ref _integrityTotalEventsText, value); }

    private string _integrityLast24hText = "0";
    public string IntegrityLast24hText { get => _integrityLast24hText; private set => SetProperty(ref _integrityLast24hText, value); }

    private string _integrityUnacknowledgedText = "0";
    public string IntegrityUnacknowledgedText { get => _integrityUnacknowledgedText; private set => SetProperty(ref _integrityUnacknowledgedText, value); }

    private string _integrityMostAffectedText = "N/A";
    public string IntegrityMostAffectedText { get => _integrityMostAffectedText; private set => SetProperty(ref _integrityMostAffectedText, value); }

    private bool _isNoIntegrityEventsVisible = true;
    public bool IsNoIntegrityEventsVisible { get => _isNoIntegrityEventsVisible; private set => SetProperty(ref _isNoIntegrityEventsVisible, value); }

    // ── Quick add symbol ──────────────────────────────────────────────────────────

    private string _quickAddSymbolText = string.Empty;
    public string QuickAddSymbolText
    {
        get => _quickAddSymbolText;
        set => SetProperty(ref _quickAddSymbolText, value);
    }

    // ── Commands ──────────────────────────────────────────────────────────────────

    public IAsyncRelayCommand StartCollectorCommand { get; }
    public IAsyncRelayCommand StopCollectorCommand { get; }
    public IRelayCommand QuickPauseCollectorCommand { get; }
    public IRelayCommand ViewLogsCommand { get; }
    public IRelayCommand RunBackfillCommand { get; }
    public IAsyncRelayCommand RefreshStatusCommand { get; }
    public IRelayCommand ViewAllActivityCommand { get; }
    public IRelayCommand ClearIntegrityAlertsCommand { get; }
    public IRelayCommand ExpandIntegrityPanelCommand { get; }
    public IRelayCommand<int> AcknowledgeIntegrityEventCommand { get; }
    public IRelayCommand ViewAllIntegrityEventsCommand { get; }
    public IRelayCommand ExportIntegrityReportCommand { get; }
    public IRelayCommand QuickAddSymbolCommand { get; }

    // ── IPageActionBarProvider implementation ──────────────────────────────────────
    public string PageTitle => "Dashboard";
    public ObservableCollection<ActionEntry> Actions { get; } = new();

    // ─────────────────────────────────────────────────────────────────────────────

    public DashboardViewModel(
        WpfServices.NavigationService navigationService,
        WpfServices.ConnectionService connectionService,
        WpfServices.StatusService statusService,
        WpfServices.MessagingService messagingService,
        WpfServices.NotificationService notificationService,
        AlertService alertService,
        ActivityFeedService activityFeedService,
        WpfServices.TaskbarProgressService taskbarProgressService,
        WpfServices.LoggingService loggingService,
        CommandPaletteService commandPaletteService)
    {
        _navigationService = navigationService;
        _connectionService = connectionService;
        _statusService = statusService;
        _messagingService = messagingService;
        _notificationService = notificationService;
        _alertService = alertService;
        _activityFeedService = activityFeedService;
        _taskbarProgressService = taskbarProgressService;
        _loggingService = loggingService;
        _commandPaletteService = commandPaletteService;

        // Cache brush resources once at construction time so FindResource() is never called in hot paths (P2 fix).
        _successBrush = (Brush)System.Windows.Application.Current.Resources["SuccessColorBrush"];
        _errorBrush = (Brush)System.Windows.Application.Current.Resources["ErrorColorBrush"];
        _warningBrush = (Brush)System.Windows.Application.Current.Resources["WarningColorBrush"];
        _secondaryTextBrush = (Brush)System.Windows.Application.Current.Resources["ConsoleTextSecondaryBrush"];
        _infoBrush = (Brush)System.Windows.Application.Current.Resources["InfoColorBrush"];

        // Cache icon glyph strings.
        _iconInfo = (string)System.Windows.Application.Current.Resources["IconInfo"];
        _iconSuccess = (string)System.Windows.Application.Current.Resources["IconSuccess"];
        _iconPause = (string)System.Windows.Application.Current.Resources["IconPause"];
        _iconPlay = (string)System.Windows.Application.Current.Resources["IconPlay"];
        _iconChevronDown = (string)System.Windows.Application.Current.Resources["IconChevronDown"];
        _iconChevronUp = (string)System.Windows.Application.Current.Resources["IconChevronUp"];

        // Initialize brush-dependent properties with sensible defaults.
        _collectorStatusBadgeBackground = _errorBrush;
        _connectionStatusFill = _errorBrush;
        _lastUpdateForeground = _secondaryTextBrush;
        _dataHealthForeground = _successBrush;
        _dataHealthIconForeground = _successBrush;
        _dataHealthIconGlyph = _iconSuccess;
        _pauseButtonIconGlyph = _iconPause;
        _expandIntegrityIconGlyph = _iconChevronDown;

        // Wire commands.
        StartCollectorCommand = new AsyncRelayCommand(StartCollectorAsync);
        StopCollectorCommand = new AsyncRelayCommand(StopCollectorAsync);
        QuickPauseCollectorCommand = new RelayCommand(TogglePauseCollector);
        ViewLogsCommand = new RelayCommand(() => _navigationService.NavigateTo("ActivityLog"));
        RunBackfillCommand = new RelayCommand(() => _navigationService.NavigateTo("Backfill"));
        RefreshStatusCommand = new AsyncRelayCommand(() => RefreshStatusAsync());
        ViewAllActivityCommand = new RelayCommand(() => _navigationService.NavigateTo("ActivityLog"));
        ClearIntegrityAlertsCommand = new RelayCommand(ClearIntegrityAlerts);
        ExpandIntegrityPanelCommand = new RelayCommand(ToggleIntegrityPanel);
        AcknowledgeIntegrityEventCommand = new RelayCommand<int>(AcknowledgeIntegrityEvent);
        ViewAllIntegrityEventsCommand = new RelayCommand(() => _navigationService.NavigateTo("DataQuality"));
        ExportIntegrityReportCommand = new RelayCommand(() =>
            _notificationService.NotifyInfo("Report queued", "Integrity report export started."));
        QuickAddSymbolCommand = new RelayCommand(ExecuteQuickAddSymbol);

        // Populate action bar.
        Actions.Clear();
        Actions.Add(new ActionEntry("Refresh", RefreshStatusCommand, "\uE72C", "Refresh all data", IsPrimary: true));
        Actions.Add(new ActionEntry("View Logs", ViewLogsCommand, "\uE8FD", "View activity log"));
        Actions.Add(new ActionEntry("Data Quality", ViewAllIntegrityEventsCommand, "\uE73E", "View data quality metrics"));

        // Observe collection changes to keep empty-state flags up to date.
        ActivityItems.CollectionChanged += (_, _) => IsNoActivityVisible = ActivityItems.Count == 0;
        IntegrityEventItems.CollectionChanged += (_, _) => IsNoIntegrityEventsVisible = IntegrityEventItems.Count == 0;

        // Subscribe to service events.
        _messagingService.MessageReceived += OnMessageReceived;
        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
        _connectionService.LatencyUpdated += OnLatencyUpdated;
        _statusService.LiveStatusReceived += OnLiveStatusReceived;
        _statusService.BackendReachabilityChanged += OnBackendReachabilityChanged;

        // Create timers (DispatcherTimer must be created on the UI thread).
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += OnRefreshTimerTick;
        _staleCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _staleCheckTimer.Tick += OnStaleCheckTimerTick;
        _activityPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _activityPollTimer.Tick += OnActivityPollTimerTick;
    }

    // ── Lifecycle (called by the Page) ────────────────────────────────────────────

    public void Start()
    {
        _statusService.StartLiveMonitoring(intervalSeconds: 2);
        _refreshTimer.Start();
        _staleCheckTimer.Start();
        _activityPollTimer.Start();
        _ = RefreshStatusAsync();
        // Immediately fetch backend events so the feed is populated without waiting 10s.
        _ = _activityFeedService.FetchServerEventsAsync(_cts.Token);
    }

    public void Stop()
    {
        _refreshTimer.Stop();
        _staleCheckTimer.Stop();
        _activityPollTimer.Stop();
        _statusService.StopLiveMonitoring();
    }

    public void Dispose()
    {
        Stop();
        _cts.Cancel();
        _cts.Dispose();
        _messagingService.MessageReceived -= OnMessageReceived;
        _connectionService.ConnectionStateChanged -= OnConnectionStateChanged;
        _connectionService.LatencyUpdated -= OnLatencyUpdated;
        _statusService.LiveStatusReceived -= OnLiveStatusReceived;
        _statusService.BackendReachabilityChanged -= OnBackendReachabilityChanged;
    }

    // ── Service event handlers ────────────────────────────────────────────────────

    private void OnLiveStatusReceived(object? sender, LiveStatusEventArgs e)
    {
        // Use InvokeAsync (fire-and-forget) so the service thread is not blocked (P1 fix).
        _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (e.Status != null)
            {
                ApplyLiveStatus(e.Status);
            }

            UpdateStaleIndicator(e.IsStale);
        });
    }

    private void OnBackendReachabilityChanged(object? sender, bool isReachable)
    {
        _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (!isReachable)
            {
                AddActivityItem("Backend unreachable", "Cannot connect to the Meridian service");
            }
            else
            {
                AddActivityItem("Backend connected", "Successfully connected to the Meridian service");
            }
        });
    }

    private void OnConnectionStateChanged(object? sender, ConnectionStateEventArgs e)
    {
        _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var state = e.State == ConnectionState.Connected ? "Connected" : e.State.ToString();
            ConnectionStatusText = e.Provider ?? state;
            ConnectionStatusFill = e.State == ConnectionState.Connected ? _successBrush : _errorBrush;
            AddActivityItem($"Connection {state.ToLowerInvariant()}", $"Provider: {e.Provider ?? "Unknown"}");
            UpdateCollectorBadge();

            // Reflect collection activity on the taskbar icon.
            if (e.State == ConnectionState.Connected)
                _taskbarProgressService.SetIndeterminate();
            else
                _taskbarProgressService.Clear();
        });
    }

    private void OnLatencyUpdated(object? sender, int latencyMs)
    {
        _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            LatencyText = $"{latencyMs} ms";
            AvgLatencyText = $"{latencyMs} ms";
        });
    }

    private void OnMessageReceived(object? sender, string message)
    {
        if (message == "RefreshStatus")
        {
            _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() => _ = RefreshStatusAsync());
        }
    }

    private void OnRefreshTimerTick(object? sender, EventArgs e) => _ = RefreshStatusAsync();

    private void OnActivityPollTimerTick(object? sender, EventArgs e) =>
        _ = _activityFeedService.FetchServerEventsAsync(_cts.Token);

    private void OnStaleCheckTimerTick(object? sender, EventArgs e) =>
        UpdateStaleIndicator(_statusService.IsDataStale);

    // ── Business logic ────────────────────────────────────────────────────────────

    private async Task RefreshStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var status = await _statusService.GetStatusAsync(_cts.Token);  // P6: cancellable

            if (status != null)
            {
                ApplyLiveStatus(status);
            }

            UpdateConnectionInfo();
            UpdateCollectorBadge();
            UpdateAlertSummaryBadges();
        }
        catch (OperationCanceledException)
        {
            // Page was unloaded — no action needed.
        }
        catch (Exception ex)
        {
            _loggingService.LogWarning(
                "Failed to refresh dashboard status",
                ("Error", ex.Message));   // C1: structured logging instead of Debug.WriteLine
        }
    }

    private void ApplyLiveStatus(SimpleStatus status)
    {
        // Compute event rate from delta between polls.
        // P5 fix: capture the computed rate BEFORE updating _previousPublished so that the
        //         sparkline history append below uses the same non-zero delta value.
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastRateCalcTime).TotalSeconds;
        double currentRate = 0;
        if (elapsed > 0 && _previousPublished > 0)
        {
            currentRate = (status.Published - _previousPublished) / elapsed;
            PublishedRateText = currentRate >= 0 ? $"+{currentRate:N0}/s" : "0/s";
            CurrentThroughputText = $"{(int)currentRate:N0}/s";
        }

        _previousPublished = status.Published;
        _lastRateCalcTime = now;

        PublishedCount = FormatNumber(status.Published);
        DroppedCount = FormatNumber(status.Dropped);
        IntegrityCount = FormatNumber(status.Integrity);
        HistoricalCount = FormatNumber(status.Historical);

        TotalEventsToday = FormatNumber(status.Published);
        var symbolCount = SymbolPerformanceItems.Count.ToString("N0");
        ActiveSymbolsCount = symbolCount;
        SymbolCount = symbolCount;

        if (status.Dropped > 0 && status.Published > 0)
        {
            var dropRate = (double)status.Dropped / status.Published * 100;
            DroppedRateText = $"{dropRate:0.00}%";

            var dataHealth = Math.Max(0, 100 - dropRate);
            DataHealthText = $"{dataHealth:F1}%";
            DataQualityText = $"{dataHealth:F1}%";
            DataHealthForeground = dataHealth > 97 ? _successBrush : _warningBrush;
            DataHealthIconGlyph = dataHealth > 97 ? _iconSuccess : _iconInfo;
            DataHealthIconForeground = DataHealthForeground;
        }
        else
        {
            DroppedRateText = "0.00%";
        }

        IntegrityRateText = $"{status.Integrity} gaps";
        HistoricalTrendText = status.Historical > 0
            ? $"{FormatNumber(status.Historical)} bars"
            : "No data";

        // Update sparkline history buffers and regenerate point collections.
        AppendToHistory(_publishedHistory, status.Published);
        AppendToHistory(_droppedHistory, status.Dropped);
        AppendToHistory(_integrityHistory, status.Integrity);
        AppendToHistory(_historicalHistory, status.Historical);

        // P5 fix: use the already-computed rate (captured before _previousPublished was updated).
        //         Previously this block re-computed the delta after _previousPublished was set,
        //         which always produced a delta of 0.
        if (elapsed > 0 && currentRate > 0)
        {
            AppendToHistory(_throughputHistory, currentRate);
        }

        PublishedSparkline = BuildSparkline(_publishedHistory, 30);
        DroppedSparkline = BuildSparkline(_droppedHistory, 30);
        IntegritySparkline = BuildSparkline(_integrityHistory, 30);
        HistoricalSparkline = BuildSparkline(_historicalHistory, 30);
        ThroughputSparkline = BuildSparkline(_throughputHistory, 30);

        PublishedSparklineFill = BuildSparklineFill(_publishedHistory, 30);
        DroppedSparklineFill = BuildSparklineFill(_droppedHistory, 30);
        IntegritySparklineFill = BuildSparklineFill(_integrityHistory, 30);
        HistoricalSparklineFill = BuildSparklineFill(_historicalHistory, 30);

        // Update throughput statistics.
        if (_throughputHistory.Count > 0)
        {
            var avg = _throughputHistory.Average();
            var peak = _throughputHistory.Max();
            AvgThroughputText = $"{avg:N0}/s";
            PeakThroughputText = $"{peak:N0}/s";
        }

        if (status.Provider != null)
        {
            SelectedDataSourceText = status.Provider.ActiveProvider ?? "Not Connected";
            ProviderDescriptionText = status.Provider.DisplayStatus;
            ConnectionStatusText = status.Provider.DisplayStatus;
            ConnectionStatusFill = status.Provider.IsConnected ? _successBrush : _errorBrush;
        }

        LastUpdateText = $"Last update: {DateTime.Now:HH:mm:ss}";
        LastDataUpdateText = $"Last update: {DateTime.Now:HH:mm:ss}";
    }

    private void UpdateStaleIndicator(bool isStale)
    {
        if (_statusService.SecondsSinceLastUpdate is { } seconds)
        {
            var staleText = seconds < 5 ? "Just now" : $"{seconds:F0}s ago";
            LastUpdateText = $"Last update: {staleText}";
        }

        LastUpdateForeground = isStale ? _warningBrush : _secondaryTextBrush;
    }

    private void UpdateConnectionInfo()
    {
        var latency = _connectionService.LastLatencyMs;
        LatencyText = latency > 0 ? $"{latency:F0} ms" : "-- ms";
        AvgLatencyText = latency > 0 ? $"{latency:F0} ms" : "-- ms";

        var uptime = _connectionService.Uptime;
        if (uptime.HasValue)
        {
            UptimeText = $"{uptime.Value.Hours:D2}:{uptime.Value.Minutes:D2}:{uptime.Value.Seconds:D2}";
            CollectorUptimeText = $"{uptime.Value.Hours}h {uptime.Value.Minutes}m {uptime.Value.Seconds}s";
        }
        else
        {
            UptimeText = "--:--:--";
            CollectorUptimeText = "0h 0m 0s";
        }
    }

    private void UpdateCollectorBadge()
    {
        var isConnected = _connectionService.State == ConnectionState.Connected;
        CollectorStatusText = isConnected ? "Running" : "Stopped";
        CollectorStatusBadgeBackground = isConnected ? _successBrush : _errorBrush;
    }

    private void UpdateAlertSummaryBadges()
    {
        var summary = _alertService.GetSummary();
        var criticalAndError = summary.CriticalCount + summary.ErrorCount;

        IsAlertCriticalBadgeVisible = criticalAndError > 0;
        AlertCriticalCount = criticalAndError.ToString("N0");

        IsAlertWarningBadgeVisible = summary.WarningCount > 0;
        AlertWarningCount = summary.WarningCount.ToString("N0");
    }

    private void UpdateIntegrityBadges()
    {
        // Use Severity enum for counting – no fragile brush-reference comparison (P3 fix).
        var criticalCount = IntegrityEventItems.Count(i => i.Severity == IntegrityEventSeverity.Critical);
        var warningCount = IntegrityEventItems.Count(i => i.Severity == IntegrityEventSeverity.Warning);

        CriticalAlertsCount = criticalCount.ToString("N0");
        WarningAlertsCount = warningCount.ToString("N0");
        IsCriticalAlertsBadgeVisible = criticalCount > 0;
        IsWarningAlertsBadgeVisible = warningCount > 0;

        IntegrityTotalEventsText = IntegrityEventItems.Count.ToString("N0");
        IntegrityLast24hText = warningCount.ToString("N0");
        IntegrityUnacknowledgedText = IntegrityEventItems.Count(i => i.IsNotAcknowledged).ToString("N0");
    }

    private void AddActivityItem(string title, string description)
    {
        ActivityItems.Insert(0, new DashboardActivityItem
        {
            Title = title,
            Description = description,
            RelativeTime = "Just now",
            IconGlyph = _iconInfo,
            IconBackground = _infoBrush
        });

        while (ActivityItems.Count > MaxActivityItems)
        {
            ActivityItems.RemoveAt(ActivityItems.Count - 1);
        }
    }

    // ── Command implementations ───────────────────────────────────────────────────

    private async Task StartCollectorAsync(CancellationToken ct = default)
    {
        try
        {
            var provider = _connectionService.CurrentProvider ?? "default";
            var success = await _connectionService.ConnectAsync(provider);

            if (success)
            {
                AddActivityItem("Collector started", $"Provider: {provider}");
                _notificationService.NotifySuccess("Collector Started", "Data collection has started.");
            }
            else
            {
                AddActivityItem("Collector start failed", "Unable to connect to provider");
                _notificationService.ShowNotification("Start Failed", "Failed to start the data collector.", NotificationType.Error);
            }
        }
        catch (Exception ex)
        {
            AddActivityItem("Collector error", ex.Message);
            _notificationService.ShowNotification("Error", ex.Message, NotificationType.Error);
        }
    }

    private async Task StopCollectorAsync(CancellationToken ct = default)
    {
        try
        {
            await _connectionService.DisconnectAsync();
            AddActivityItem("Collector stopped", "Streaming paused");
            _notificationService.NotifyInfo("Collector Stopped", "Data collection has stopped.");
        }
        catch (Exception ex)
        {
            AddActivityItem("Stop failed", ex.Message);
            _notificationService.ShowNotification("Error", ex.Message, NotificationType.Error);
        }
    }

    private void TogglePauseCollector()
    {
        if (_isCollectorPaused)
        {
            _connectionService.ResumeAutoReconnect();
            _isCollectorPaused = false;
            PauseButtonText = "Pause Collection";
            PauseButtonIconGlyph = _iconPause;
            AddActivityItem("Collector resumed", "Auto-reconnect enabled");
        }
        else
        {
            _connectionService.PauseAutoReconnect();
            _isCollectorPaused = true;
            PauseButtonText = "Resume Collection";
            PauseButtonIconGlyph = _iconPlay;
            AddActivityItem("Collector paused", "Auto-reconnect disabled");
        }
    }

    private void ClearIntegrityAlerts()
    {
        IntegrityEventItems.Clear();
        UpdateIntegrityBadges();
    }

    private void ToggleIntegrityPanel()
    {
        IsIntegrityDetailsPanelVisible = !IsIntegrityDetailsPanelVisible;
        ExpandIntegrityText = IsIntegrityDetailsPanelVisible ? "Hide Details" : "Show Details";
        ExpandIntegrityIconGlyph = IsIntegrityDetailsPanelVisible ? _iconChevronUp : _iconChevronDown;
    }

    private void AcknowledgeIntegrityEvent(int id)
    {
        var item = IntegrityEventItems.FirstOrDefault(i => i.Id == id);
        if (item != null)
        {
            item.IsNotAcknowledged = false;
            UpdateIntegrityBadges();
        }
    }

    private void ExecuteQuickAddSymbol()
    {
        var symbol = QuickAddSymbolText.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return;
        }

        AddActivityItem("Symbol added", $"Added {symbol} to watchlist");
        QuickAddSymbolText = string.Empty;
    }

    // ── Sparkline helpers ──────────────────────────────────────────────────────────

    private static void AppendToHistory(List<double> history, double value)
    {
        history.Add(value);
        while (history.Count > SparklineCapacity)
        {
            history.RemoveAt(0);
        }
    }

    /// <summary>
    /// Builds a WPF <see cref="PointCollection"/> suitable for a Polyline from a history buffer.
    /// The points are normalised to fit within the given height.
    /// </summary>
    private static PointCollection BuildSparkline(List<double> history, double height)
    {
        var points = new PointCollection();
        if (history.Count < 2) return points;

        var max = history.Max();
        var min = history.Min();
        var range = max - min;
        if (range < 0.001) range = 1;

        var step = 200.0 / (history.Count - 1); // width assumed ~200px in the card canvas
        for (var i = 0; i < history.Count; i++)
        {
            var x = i * step;
            var y = height - ((history[i] - min) / range * (height - 4)) - 2;
            points.Add(new Point(x, y));
        }

        return points;
    }

    /// <summary>
    /// Builds a closed polygon that traces the sparkline and returns to the bottom,
    /// creating a filled area effect beneath the line.
    /// </summary>
    private static PointCollection BuildSparklineFill(List<double> history, double height)
    {
        var points = new PointCollection();
        if (history.Count < 2) return points;

        var max = history.Max();
        var min = history.Min();
        var range = max - min;
        if (range < 0.001) range = 1;

        var step = 200.0 / (history.Count - 1);

        // Top edge (same as sparkline)
        for (var i = 0; i < history.Count; i++)
        {
            var x = i * step;
            var y = height - ((history[i] - min) / range * (height - 4)) - 2;
            points.Add(new Point(x, y));
        }

        // Close along the bottom
        points.Add(new Point((history.Count - 1) * step, height));
        points.Add(new Point(0, height));

        return points;
    }

    // ── Static helpers ────────────────────────────────────────────────────────────

    private static string FormatNumber(long number) => number switch
    {
        >= 1_000_000_000 => $"{number / 1_000_000_000.0:F1}B",
        >= 1_000_000 => $"{number / 1_000_000.0:F1}M",
        >= 1_000 => $"{number / 1_000.0:F1}K",
        _ => number.ToString("N0")
    };

    // ── ICommandContextProvider implementation ──────────────────────────────

    public string ContextKey => "Dashboard";

    public IReadOnlyList<CommandEntry> GetContextualCommands()
    {
        var commands = new List<CommandEntry>();

        // Refresh Status command
        var refreshCommand = RefreshStatusCommand;
        commands.Add(new CommandEntry(
            "Refresh Dashboard",
            "Refresh all dashboard metrics and status",
            "Dashboard",
            refreshCommand,
            "F5"));

        // Start/Stop Collector command
        if (_isCollectorPaused || CollectorStatusText.Contains("Stopped"))
        {
            var startCommand = StartCollectorCommand;
            commands.Add(new CommandEntry(
                "Start Data Collector",
                "Start collecting market data from enabled providers",
                "Dashboard",
                startCommand));
        }
        else
        {
            var stopCommand = StopCollectorCommand;
            commands.Add(new CommandEntry(
                "Stop Data Collector",
                "Stop the data collector",
                "Dashboard",
                stopCommand));
        }

        // View Provider Health command
        var healthCommand = new RelayCommand(() =>
            _navigationService.NavigateTo("ProviderHealth"));
        commands.Add(new CommandEntry(
            "View Provider Health",
            "Open provider status and health monitoring",
            "Dashboard",
            healthCommand));

        // View Data Quality command
        var qualityCommand = new RelayCommand(() =>
            _navigationService.NavigateTo("DataQuality"));
        commands.Add(new CommandEntry(
            "View Data Quality",
            "Open data quality metrics and alerts",
            "Dashboard",
            qualityCommand));

        // View Activity Log command
        var logCommand = ViewLogsCommand;
        commands.Add(new CommandEntry(
            "View Activity Log",
            "View recent activity and events",
            "Dashboard",
            logCommand));

        // Run Backfill command
        var backfillCommand = RunBackfillCommand;
        commands.Add(new CommandEntry(
            "Run Backfill",
            "Start a backfill operation for missing data",
            "Dashboard",
            backfillCommand));

        return commands.AsReadOnly();
    }

    public void OnActivated()
    {
        var paletteService = _commandPaletteService;
        paletteService.RegisterContextualProvider(ContextKey, GetContextualCommands);
        paletteService.SetActiveContext(ContextKey);
    }

    public void OnDeactivated()
    {
        var paletteService = _commandPaletteService;
        paletteService.ClearActiveContext();
        paletteService.UnregisterContextualProvider(ContextKey);
    }
}

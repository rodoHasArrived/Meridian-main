using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Workstation.Models;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the System Health page.
/// Owns the auto-refresh timer, all async data-load logic, and display state so that
/// the code-behind is thinned to lifecycle wiring only.
/// </summary>
public sealed class SystemHealthViewModel : BindableBase, IDisposable
{
    private readonly SystemHealthService _healthService;
    private readonly WpfServices.LoggingService _loggingService;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DateTime _startTime = DateTime.UtcNow;
    private int _refreshInFlight;
    private ProviderHealthItem? _selectedProvider;
    private SystemEventItem? _selectedEvent;
    private InspectorPanelModel _systemTriageInspector = BuildEmptyTriageInspector();
    private InspectorPanelModel _selectedProviderInspector = BuildEmptyProviderInspector();
    private InspectorPanelModel _selectedEventInspector = BuildEmptyEventInspector();

    // ── Cached theme brushes — initialised lazily from Application resources ──────────
    private Brush? _errorBrush;
    private Brush? _warningBrush;
    private Brush? _successBrush;
    private Brush? _infoBrush;
    private Brush? _mutedBrush;

    private Brush ErrorBrush => _errorBrush ??= GetResource("ErrorColorBrush", Brushes.Red);
    private Brush WarningBrush => _warningBrush ??= GetResource("WarningColorBrush", Brushes.Orange);
    private Brush SuccessBrush => _successBrush ??= GetResource("SuccessColorBrush", Brushes.LimeGreen);
    private Brush InfoBrush => _infoBrush ??= GetResource("InfoColorBrush", Brushes.CornflowerBlue);
    private Brush MutedBrush => _mutedBrush ??= GetResource("ConsoleTextMutedBrush", Brushes.Gray);

    // ── Public collections ────────────────────────────────────────────────────────────
    public ObservableCollection<ProviderHealthItem> Providers { get; } = new();
    public ObservableCollection<SystemEventItem> Events { get; } = new();

    public WorkstationTableModel<ProviderHealthItem> ProvidersTable { get; }
    public WorkstationTableModel<SystemEventItem> EventsTable { get; }

    private int _providerCount;
    private int _unhealthyProviderCount;
    private bool _hasProviderSnapshot;
    private bool _hasStorageSnapshot;
    private double _diskUsagePercent;
    private int _storageIssueCount;
    private int _storageCorruptedFiles;
    private int _storageOrphanedFiles;
    private int _eventCount;
    private int _errorEventCount;
    private int _warningEventCount;
    private bool _hasEventSnapshot;

    // ── Metric properties ─────────────────────────────────────────────────────────────
    private string _cpuText = "--";
    public string CpuText { get => _cpuText; private set => SetProperty(ref _cpuText, value); }

    private Brush _cpuForeground = Brushes.Gray;
    public Brush CpuForeground { get => _cpuForeground; private set => SetProperty(ref _cpuForeground, value); }

    private string _memoryText = "--";
    public string MemoryText { get => _memoryText; private set => SetProperty(ref _memoryText, value); }

    private string _threadsText = "--";
    public string ThreadsText { get => _threadsText; private set => SetProperty(ref _threadsText, value); }

    private string _uptimeText = "--";
    public string UptimeText { get => _uptimeText; private set => SetProperty(ref _uptimeText, value); }

    // ── Overall status ────────────────────────────────────────────────────────────────
    private string _overallStatusText = "Healthy";
    public string OverallStatusText { get => _overallStatusText; private set => SetProperty(ref _overallStatusText, value); }

    private Brush _overallStatusBackground = Brushes.LimeGreen;
    public Brush OverallStatusBackground { get => _overallStatusBackground; private set => SetProperty(ref _overallStatusBackground, value); }

    // ── System triage briefing ───────────────────────────────────────────────────────
    private string _systemTriageTitle = "Waiting for health scan";
    public string SystemTriageTitle { get => _systemTriageTitle; private set => SetProperty(ref _systemTriageTitle, value); }

    private string _systemTriageDetail = "Refresh health data to summarize provider, storage, and event posture.";
    public string SystemTriageDetail { get => _systemTriageDetail; private set => SetProperty(ref _systemTriageDetail, value); }

    private string _systemTriageActionText = "Refresh health data";
    public string SystemTriageActionText { get => _systemTriageActionText; private set => SetProperty(ref _systemTriageActionText, value); }

    private string _systemTriageTargetText = "Health snapshot";
    public string SystemTriageTargetText { get => _systemTriageTargetText; private set => SetProperty(ref _systemTriageTargetText, value); }

    private string _systemTriageEvidenceText = "Providers waiting; storage waiting; events waiting";
    public string SystemTriageEvidenceText { get => _systemTriageEvidenceText; private set => SetProperty(ref _systemTriageEvidenceText, value); }

    private string _systemTriageProviderSummaryText = "Providers waiting";
    public string SystemTriageProviderSummaryText { get => _systemTriageProviderSummaryText; private set => SetProperty(ref _systemTriageProviderSummaryText, value); }

    private string _systemTriageStorageSummaryText = "Storage waiting";
    public string SystemTriageStorageSummaryText { get => _systemTriageStorageSummaryText; private set => SetProperty(ref _systemTriageStorageSummaryText, value); }

    private string _systemTriageEventSummaryText = "Events waiting";
    public string SystemTriageEventSummaryText { get => _systemTriageEventSummaryText; private set => SetProperty(ref _systemTriageEventSummaryText, value); }

    private string _systemTriageIcon = "\uE946";
    public string SystemTriageIcon { get => _systemTriageIcon; private set => SetProperty(ref _systemTriageIcon, value); }

    private Brush _systemTriageAccentBrush = Brushes.CornflowerBlue;
    public Brush SystemTriageAccentBrush { get => _systemTriageAccentBrush; private set => SetProperty(ref _systemTriageAccentBrush, value); }

    private string _providerEmptyStateTitle = "Provider scan pending";
    public string ProviderEmptyStateTitle { get => _providerEmptyStateTitle; private set => SetProperty(ref _providerEmptyStateTitle, value); }

    private string _providerEmptyStateDetail = "Refresh health data to populate provider posture before relying on live diagnostics.";
    public string ProviderEmptyStateDetail { get => _providerEmptyStateDetail; private set => SetProperty(ref _providerEmptyStateDetail, value); }

    private string _eventEmptyStateTitle = "Event scan pending";
    public string EventEmptyStateTitle { get => _eventEmptyStateTitle; private set => SetProperty(ref _eventEmptyStateTitle, value); }

    private string _eventEmptyStateDetail = "Refresh health data to confirm whether desktop workstation events are available.";
    public string EventEmptyStateDetail { get => _eventEmptyStateDetail; private set => SetProperty(ref _eventEmptyStateDetail, value); }

    // ── Provider section ──────────────────────────────────────────────────────────────
    private bool _hasProviders;
    public bool HasProviders { get => _hasProviders; private set => SetProperty(ref _hasProviders, value); }

    private bool _hasNoProviders = true;
    public bool HasNoProviders { get => _hasNoProviders; private set => SetProperty(ref _hasNoProviders, value); }

    public ProviderHealthItem? SelectedProvider
    {
        get => _selectedProvider;
        set
        {
            if (SetProperty(ref _selectedProvider, value))
            {
                SelectedProviderInspector = value is null
                    ? BuildEmptyProviderInspector()
                    : BuildProviderInspector(value);
                OnPropertyChanged(nameof(HasSelectedProvider));
            }
        }
    }

    public bool HasSelectedProvider => SelectedProvider is not null;

    public InspectorPanelModel SelectedProviderInspector
    {
        get => _selectedProviderInspector;
        private set => SetProperty(ref _selectedProviderInspector, value);
    }

    // ── Storage health ────────────────────────────────────────────────────────────────
    private string _storageTotalText = "--";
    public string StorageTotalText { get => _storageTotalText; private set => SetProperty(ref _storageTotalText, value); }

    private string _storageFilesText = "--";
    public string StorageFilesText { get => _storageFilesText; private set => SetProperty(ref _storageFilesText, value); }

    private string _diskUsageText = "--";
    public string DiskUsageText { get => _diskUsageText; private set => SetProperty(ref _diskUsageText, value); }

    private double _diskUsageValue;
    public double DiskUsageValue { get => _diskUsageValue; private set => SetProperty(ref _diskUsageValue, value); }

    private Brush _diskUsageForeground = Brushes.CornflowerBlue;
    public Brush DiskUsageForeground { get => _diskUsageForeground; private set => SetProperty(ref _diskUsageForeground, value); }

    // ── Events section ────────────────────────────────────────────────────────────────
    private bool _hasEvents;
    public bool HasEvents { get => _hasEvents; private set => SetProperty(ref _hasEvents, value); }

    private bool _hasNoEvents = true;
    public bool HasNoEvents { get => _hasNoEvents; private set => SetProperty(ref _hasNoEvents, value); }

    public SystemEventItem? SelectedEvent
    {
        get => _selectedEvent;
        set
        {
            if (SetProperty(ref _selectedEvent, value))
            {
                SelectedEventInspector = value is null
                    ? BuildEmptyEventInspector()
                    : BuildEventInspector(value);
                OnPropertyChanged(nameof(HasSelectedEvent));
            }
        }
    }

    public bool HasSelectedEvent => SelectedEvent is not null;

    public InspectorPanelModel SelectedEventInspector
    {
        get => _selectedEventInspector;
        private set => SetProperty(ref _selectedEventInspector, value);
    }

    public InspectorPanelModel SystemTriageInspector
    {
        get => _systemTriageInspector;
        private set => SetProperty(ref _systemTriageInspector, value);
    }

    // ── Button state ──────────────────────────────────────────────────────────────────
    private bool _isRefreshEnabled = true;
    public bool IsRefreshEnabled
    {
        get => _isRefreshEnabled;
        private set
        {
            if (SetProperty(ref _isRefreshEnabled, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(RefreshTooltip));
                OnPropertyChanged(nameof(HealthActionStateTitle));
                OnPropertyChanged(nameof(HealthActionStateDetail));
            }
        }
    }

    private bool _isDiagnosticsEnabled = true;
    public bool IsDiagnosticsEnabled
    {
        get => _isDiagnosticsEnabled;
        private set
        {
            if (SetProperty(ref _isDiagnosticsEnabled, value))
            {
                GenerateDiagnosticsCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(DiagnosticsTooltip));
                OnPropertyChanged(nameof(HealthActionStateTitle));
                OnPropertyChanged(nameof(HealthActionStateDetail));
            }
        }
    }

    public string RefreshTooltip => IsRefreshEnabled
        ? "Refresh system metrics, provider posture, storage health, and recent events."
        : "Health refresh is already running.";

    public string DiagnosticsTooltip => IsDiagnosticsEnabled
        ? "Generate a diagnostic bundle for the current support posture."
        : "Diagnostic bundle generation is already running.";

    public string HealthActionStateTitle
        => IsRefreshEnabled && IsDiagnosticsEnabled ? SystemTriageActionText : "Health action running";

    public string HealthActionStateDetail
        => IsRefreshEnabled && IsDiagnosticsEnabled
            ? $"{SystemTriageTargetText}: {SystemTriageDetail}"
            : "Wait for the current health action to finish before starting another support task.";

    // ── Commands ──────────────────────────────────────────────────────────────────────
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand GenerateDiagnosticsCommand { get; }

    public SystemHealthViewModel(
        SystemHealthService healthService,
        WpfServices.LoggingService loggingService)
    {
        _healthService = healthService;
        _loggingService = loggingService;

        ProvidersTable = new WorkstationTableModel<ProviderHealthItem>(
            Providers,
            [
                new("Provider", nameof(ProviderHealthItem.Name), 180),
                new("Status", nameof(ProviderHealthItem.Status), 110),
                new("Latency", nameof(ProviderHealthItem.LatencyText), 90),
                new("Events", nameof(ProviderHealthItem.EventsText), 90)
            ],
            "Provider health",
            "No providers reported",
            "Refresh health data to populate provider posture.");

        EventsTable = new WorkstationTableModel<SystemEventItem>(
            Events,
            [
                new("Source", nameof(SystemEventItem.Source), 130),
                new("Severity", nameof(SystemEventItem.Severity), 90),
                new("Message", nameof(SystemEventItem.Message), 320),
                new("Time", nameof(SystemEventItem.TimeText), 110)
            ],
            "Recent events",
            "No recent events",
            "Refresh health data to confirm whether desktop workstation events are available.");

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += OnRefreshTimerTick;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => IsRefreshEnabled);
        GenerateDiagnosticsCommand = new AsyncRelayCommand(GenerateDiagnosticsAsync, () => IsDiagnosticsEnabled);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────────────

    public async Task StartAsync()
    {
        await LoadDataAsync();
        _refreshTimer.Start();
    }

    public void Stop() => _refreshTimer.Stop();

    // ── Commands ──────────────────────────────────────────────────────────────────────

    public async Task RefreshAsync()
    {
        IsRefreshEnabled = false;
        try
        { await LoadDataAsync(); }
        finally { IsRefreshEnabled = true; }
    }

    public async Task GenerateDiagnosticsAsync()
    {
        IsDiagnosticsEnabled = false;
        try
        {
            var bundle = await _healthService.GenerateDiagnosticBundleAsync();
            var message = bundle != null
                ? $"Diagnostic bundle created:\n{bundle.FilePath}\nSize: {FormatHelpers.FormatBytes(bundle.FileSizeBytes)}"
                : "Diagnostic bundle generated successfully.";

            MessageBox.Show(message, "Diagnostics", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to generate diagnostics", ex);
            MessageBox.Show($"Failed to generate diagnostics: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsDiagnosticsEnabled = true;
        }
    }

    // ── Data loading ──────────────────────────────────────────────────────────────────

    private async Task LoadDataAsync()
    {
        if (Interlocked.Exchange(ref _refreshInFlight, 1) == 1)
            return;

        try
        {
            await Task.WhenAll(
                LoadMetricsAsync(),
                LoadProviderHealthAsync(),
                LoadStorageHealthAsync(),
                LoadEventsAsync());
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to load health data", ex);
        }
        finally
        {
            Volatile.Write(ref _refreshInFlight, 0);
        }
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        await LoadDataAsync();
    }

    private async Task LoadMetricsAsync()
    {
        try
        {
            var metrics = await _healthService.GetSystemMetricsAsync();
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (metrics != null)
                {
                    CpuText = $"{metrics.CpuUsagePercent:F0}%";
                    CpuForeground = metrics.CpuUsagePercent > 80 ? ErrorBrush
                        : metrics.CpuUsagePercent > 50 ? WarningBrush
                        : SuccessBrush;
                    MemoryText = FormatHelpers.FormatBytes(metrics.MemoryUsedBytes);
                    ThreadsText = metrics.ThreadCount.ToString("N0");
                }
                else
                {
                    var process = Process.GetCurrentProcess();
                    CpuText = "--";
                    MemoryText = FormatHelpers.FormatBytes(process.WorkingSet64);
                    ThreadsText = process.Threads.Count.ToString("N0");
                }

                UptimeText = FormatUptime(DateTime.UtcNow - _startTime);
            });
        }
        catch (Exception ex)
        {
            // Silently swallow per-metric failures to keep other panels updating
            global::Meridian.Wpf.Services.LoggingService.Instance.LogDebug(
                "System metric refresh failed; other panels continue updating.",
                ("exception", ex.GetType().Name),
                ("message", ex.Message));
        }
    }

    private async Task LoadProviderHealthAsync()
    {
        try
        {
            var providers = await _healthService.GetProviderHealthAsync();
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Providers.Clear();
                SelectedProvider = null;
                _providerCount = providers?.Count ?? 0;
                _unhealthyProviderCount = 0;
                _hasProviderSnapshot = true;
                if (providers is { Count: > 0 })
                {
                    var hasUnhealthy = false;
                    foreach (var p in providers)
                    {
                        var isHealthy = string.Equals(p.Status, "Connected", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(p.Status, "Healthy", StringComparison.OrdinalIgnoreCase);
                        if (!isHealthy)
                        {
                            hasUnhealthy = true;
                            _unhealthyProviderCount++;
                        }

                        Providers.Add(new ProviderHealthItem
                        {
                            Name = p.Provider,
                            Status = p.Status,
                            StatusColor = isHealthy ? SuccessBrush : ErrorBrush,
                            LatencyText = $"{p.LatencyMs:F0}ms",
                            EventsText = $"{p.EventsPerSecond:F1}/s"
                        });
                    }

                    SelectedProvider = Providers.FirstOrDefault();
                    UpdateOverallStatus(hasUnhealthy);
                    HasProviders = true;
                    HasNoProviders = false;
                }
                else
                {
                    HasProviders = false;
                    HasNoProviders = true;
                }

                UpdateSystemTriage();
            });
        }
        catch (Exception ex)
        {
            // Silently swallow per-section failures
            global::Meridian.Wpf.Services.LoggingService.Instance.LogDebug(
                "System health section refresh failed.",
                ("exception", ex.GetType().Name),
                ("message", ex.Message));
        }
    }

    private async Task LoadStorageHealthAsync()
    {
        try
        {
            var storage = await _healthService.GetStorageHealthAsync();
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (storage == null)
                    return;

                StorageTotalText = FormatHelpers.FormatBytes(storage.TotalBytes);
                StorageFilesText = storage.TotalFiles.ToString("N0");
                _hasStorageSnapshot = true;
                _storageCorruptedFiles = Math.Max(0, storage.CorruptedFiles);
                _storageOrphanedFiles = Math.Max(0, storage.OrphanedFiles);
                _storageIssueCount = Math.Max(0, storage.Issues?.Count ?? 0) + _storageCorruptedFiles + _storageOrphanedFiles;

                if (storage.TotalBytes > 0 && storage.AvailableBytes > 0)
                {
                    var usedPercent = (double)(storage.TotalBytes - storage.AvailableBytes) / storage.TotalBytes * 100;
                    DiskUsageText = $"{usedPercent:F1}%";
                    DiskUsageValue = usedPercent;
                    _diskUsagePercent = usedPercent;
                    DiskUsageForeground = usedPercent > 90 ? ErrorBrush
                        : usedPercent > 75 ? WarningBrush
                        : InfoBrush;
                }
                else
                {
                    DiskUsageText = "--";
                    DiskUsageValue = 0;
                    _diskUsagePercent = Math.Max(0, storage.UsedPercent);
                }

                UpdateSystemTriage();
            });
        }
        catch (Exception ex)
        {
            // Silently swallow per-section failures
            global::Meridian.Wpf.Services.LoggingService.Instance.LogDebug(
                "System health section refresh failed.",
                ("exception", ex.GetType().Name),
                ("message", ex.Message));
        }
    }

    private async Task LoadEventsAsync()
    {
        try
        {
            var events = await _healthService.GetRecentEventsAsync(20);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Events.Clear();
                SelectedEvent = null;
                _eventCount = events?.Count ?? 0;
                _errorEventCount = 0;
                _warningEventCount = 0;
                _hasEventSnapshot = true;
                if (events is { Count: > 0 })
                {
                    foreach (var evt in events)
                    {
                        if (IsErrorSeverity(evt.Severity))
                            _errorEventCount++;
                        else if (IsWarningSeverity(evt.Severity))
                            _warningEventCount++;

                        Events.Add(new SystemEventItem
                        {
                            Source = evt.Source,
                            Message = evt.Message,
                            Severity = evt.Severity,
                            SeverityColor = GetSeverityBrush(evt.Severity),
                            TimeText = FormatTimestamp(evt.Timestamp)
                        });
                    }
                    SelectedEvent = Events.FirstOrDefault();
                    HasEvents = true;
                    HasNoEvents = false;
                }
                else
                {
                    HasEvents = false;
                    HasNoEvents = true;
                }

                UpdateSystemTriage();
            });
        }
        catch (Exception ex)
        {
            // Silently swallow per-section failures
            global::Meridian.Wpf.Services.LoggingService.Instance.LogDebug(
                "System health section refresh failed.",
                ("exception", ex.GetType().Name),
                ("message", ex.Message));
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────

    private void UpdateOverallStatus(bool hasUnhealthy)
    {
        OverallStatusText = hasUnhealthy ? "Degraded" : "Healthy";
        OverallStatusBackground = hasUnhealthy ? WarningBrush : SuccessBrush;
    }

    private void UpdateSystemTriage()
    {
        var state = BuildSystemTriage(
            _providerCount,
            _unhealthyProviderCount,
            _hasProviderSnapshot,
            _hasStorageSnapshot,
            _diskUsagePercent,
            _storageIssueCount,
            _storageCorruptedFiles,
            _storageOrphanedFiles,
            _eventCount,
            _errorEventCount,
            _warningEventCount,
            _hasEventSnapshot);

        SystemTriageTitle = state.Title;
        SystemTriageDetail = state.Detail;
        SystemTriageActionText = state.ActionText;
        SystemTriageTargetText = state.TargetText;
        SystemTriageEvidenceText = state.EvidenceText;
        SystemTriageProviderSummaryText = state.ProviderSummaryText;
        SystemTriageStorageSummaryText = state.StorageSummaryText;
        SystemTriageEventSummaryText = state.EventSummaryText;
        SystemTriageIcon = state.Tone switch
        {
            SystemHealthTriageTone.Critical => "\uE7BA",
            SystemHealthTriageTone.Warning => "\uE7BA",
            SystemHealthTriageTone.Ready => "\uE73E",
            _ => "\uE946"
        };
        SystemTriageAccentBrush = state.Tone switch
        {
            SystemHealthTriageTone.Critical => ErrorBrush,
            SystemHealthTriageTone.Warning => WarningBrush,
            SystemHealthTriageTone.Ready => SuccessBrush,
            _ => InfoBrush
        };
        SystemTriageInspector = BuildTriageInspector(state);
        OnPropertyChanged(nameof(HealthActionStateTitle));
        OnPropertyChanged(nameof(HealthActionStateDetail));

        UpdateEmptyStateCopy();
    }

    private void UpdateEmptyStateCopy()
    {
        var providerEmptyState = BuildProviderEmptyState(_hasProviderSnapshot);
        ProviderEmptyStateTitle = providerEmptyState.Title;
        ProviderEmptyStateDetail = providerEmptyState.Detail;

        var eventEmptyState = BuildEventEmptyState(_hasEventSnapshot);
        EventEmptyStateTitle = eventEmptyState.Title;
        EventEmptyStateDetail = eventEmptyState.Detail;
    }

    public static SystemHealthEmptyState BuildProviderEmptyState(bool hasProviderSnapshot) =>
        hasProviderSnapshot
            ? new(
                "No providers reported",
                "Connect or enable a provider, then refresh health data before relying on live diagnostics.")
            : new(
                "Provider scan pending",
                "Refresh health data to populate provider posture before relying on live diagnostics.");

    public static SystemHealthEmptyState BuildEventEmptyState(bool hasEventSnapshot) =>
        hasEventSnapshot
            ? new(
                "No recent desktop events",
                "The latest health window has no desktop workstation events. Continue monitoring or generate diagnostics if symptoms persist.")
            : new(
                "Event scan pending",
                "Refresh health data to confirm whether desktop workstation events are available.");

    public static SystemHealthTriageState BuildSystemTriage(
        int providerCount,
        int unhealthyProviderCount,
        bool hasProviderSnapshot,
        bool hasStorageSnapshot,
        double diskUsagePercent,
        int storageIssueCount,
        int storageCorruptedFiles,
        int storageOrphanedFiles,
        int eventCount,
        int errorEventCount,
        int warningEventCount,
        bool hasEventSnapshot)
    {
        providerCount = Math.Max(0, providerCount);
        unhealthyProviderCount = Math.Clamp(unhealthyProviderCount, 0, providerCount);
        diskUsagePercent = double.IsFinite(diskUsagePercent) ? Math.Clamp(diskUsagePercent, 0, 100) : 0;
        storageIssueCount = Math.Max(0, storageIssueCount);
        storageCorruptedFiles = Math.Max(0, storageCorruptedFiles);
        storageOrphanedFiles = Math.Max(0, storageOrphanedFiles);
        eventCount = Math.Max(0, eventCount);
        errorEventCount = Math.Clamp(errorEventCount, 0, eventCount);
        warningEventCount = Math.Clamp(warningEventCount, 0, eventCount - errorEventCount);

        var providerSummary = !hasProviderSnapshot
            ? "Providers waiting"
            : providerCount == 0
                ? "No providers reported"
                : $"{providerCount - unhealthyProviderCount}/{providerCount} providers healthy";
        var storageSummary = hasStorageSnapshot
            ? $"{diskUsagePercent:F1}% disk used; {storageIssueCount} storage issue(s)"
            : "Storage waiting";
        var eventSummary = !hasEventSnapshot
            ? "Events waiting"
            : eventCount == 0
                ? "No recent events"
                : $"{eventCount} recent events; {errorEventCount} errors; {warningEventCount} warnings";
        var evidence = string.Join("; ", providerSummary, storageSummary, eventSummary);

        if (unhealthyProviderCount > 0)
        {
            return new SystemHealthTriageState(
                SystemHealthTriageTone.Critical,
                "Provider health needs attention",
                $"{unhealthyProviderCount} provider(s) are disconnected or degraded. Inspect provider posture before relying on live diagnostics.",
                "Inspect unhealthy providers",
                "Provider health list",
                evidence,
                providerSummary,
                storageSummary,
                eventSummary);
        }

        if (storageCorruptedFiles > 0 || storageOrphanedFiles > 0 || storageIssueCount > 0 || diskUsagePercent >= 90)
        {
            var detail = storageCorruptedFiles > 0 || storageOrphanedFiles > 0
                ? $"{storageCorruptedFiles} corrupted and {storageOrphanedFiles} orphaned storage file(s) need review before collection evidence is trusted."
                : diskUsagePercent >= 90
                    ? $"Disk usage is {diskUsagePercent:F1}%, which can block diagnostics and retained evidence writes."
                    : $"{storageIssueCount} storage issue(s) are reported by the latest storage health snapshot.";

            return new SystemHealthTriageState(
                SystemHealthTriageTone.Critical,
                "Storage posture needs review",
                detail,
                "Generate diagnostics bundle",
                "Storage health panel",
                evidence,
                providerSummary,
                storageSummary,
                eventSummary);
        }

        if (errorEventCount > 0)
        {
            return new SystemHealthTriageState(
                SystemHealthTriageTone.Warning,
                "Recent errors need triage",
                $"{errorEventCount} error event(s) were retained in the latest health window.",
                "Review recent events",
                "Recent events list",
                evidence,
                providerSummary,
                storageSummary,
                eventSummary);
        }

        if (warningEventCount > 0 || diskUsagePercent >= 75)
        {
            var detail = warningEventCount > 0
                ? $"{warningEventCount} warning event(s) are present; confirm they are expected before closing the support loop."
                : $"Disk usage is {diskUsagePercent:F1}%, so storage headroom should be monitored.";

            return new SystemHealthTriageState(
                SystemHealthTriageTone.Warning,
                "Health posture is watchlisted",
                detail,
                "Monitor support signals",
                warningEventCount > 0 ? "Recent events list" : "Storage health panel",
                evidence,
                providerSummary,
                storageSummary,
                eventSummary);
        }

        if (!hasProviderSnapshot || !hasStorageSnapshot || !hasEventSnapshot)
        {
            return new SystemHealthTriageState(
                SystemHealthTriageTone.Waiting,
                "Waiting for health scan",
                "Waiting for the remaining provider, storage, or event snapshot before declaring support posture ready.",
                "Refresh health data",
                "Health snapshot",
                evidence,
                providerSummary,
                storageSummary,
                eventSummary);
        }

        return new SystemHealthTriageState(
            SystemHealthTriageTone.Ready,
            "System posture ready",
            "Providers, storage, and retained health events do not show an active blocker.",
            "Continue monitoring",
            "System health dashboard",
            evidence,
            providerSummary,
            storageSummary,
            eventSummary);
    }

    private Brush GetSeverityBrush(string severity) =>
        severity?.ToLowerInvariant() switch
        {
            "error" or "critical" => ErrorBrush,
            "warning" => WarningBrush,
            "info" or "information" => InfoBrush,
            _ => MutedBrush
        };

    private static bool IsErrorSeverity(string severity) =>
        string.Equals(severity, "error", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(severity, "critical", StringComparison.OrdinalIgnoreCase);

    private static bool IsWarningSeverity(string severity) =>
        string.Equals(severity, "warning", StringComparison.OrdinalIgnoreCase);

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h";
        if (uptime.TotalHours >= 1)
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        return $"{(int)uptime.TotalMinutes}m";
    }

    private static string FormatTimestamp(DateTime timestamp)
    {
        var elapsed = DateTime.UtcNow - timestamp;
        return elapsed.TotalSeconds switch
        {
            < 60 => "Just now",
            < 3600 => $"{(int)elapsed.TotalMinutes}m ago",
            < 86400 => $"{(int)elapsed.TotalHours}h ago",
            _ => timestamp.ToString("MMM dd, HH:mm")
        };
    }

    private static Brush GetResource(string key, Brush fallback) =>
        System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;

    public void Dispose() => _refreshTimer.Stop();

    internal static InspectorPanelModel BuildTriageInspector(SystemHealthTriageState state)
        => new()
        {
            Title = state.Title,
            Subtitle = "System health triage",
            Detail = state.Detail,
            Badge = new WorkstationBadgeModel("Posture", state.Tone.ToString(), "\uE946", ToneForTriage(state.Tone)),
            Facts =
            [
                new("Action", state.ActionText),
                new("Target", state.TargetText),
                new("Providers", state.ProviderSummaryText),
                new("Storage", state.StorageSummaryText),
                new("Events", state.EventSummaryText),
                new("Evidence", state.EvidenceText)
            ]
        };

    private static InspectorPanelModel BuildEmptyTriageInspector()
        => new()
        {
            Title = "Waiting for health scan",
            Subtitle = "System health triage",
            Detail = "Refresh health data to summarize provider, storage, and event posture.",
            Badge = new WorkstationBadgeModel("Posture", "Waiting", "\uE946", WorkspaceTone.Neutral)
        };

    internal static InspectorPanelModel BuildProviderInspector(ProviderHealthItem selected)
        => new()
        {
            Title = selected.Name,
            Subtitle = "Provider health",
            Detail = "Review provider connection state, latency, and event throughput before relying on live diagnostics.",
            Badge = new WorkstationBadgeModel("Status", selected.Status, "\uE774", ToneForProviderStatus(selected.Status)),
            Facts =
            [
                new("Status", selected.Status),
                new("Latency", selected.LatencyText),
                new("Events", selected.EventsText),
                new("Source", "System health provider scan")
            ]
        };

    private static InspectorPanelModel BuildEmptyProviderInspector()
        => new()
        {
            Title = "No provider selected",
            Subtitle = "Provider health inspector",
            Detail = "Select a provider health row to inspect connection status, latency, and event throughput."
        };

    internal static InspectorPanelModel BuildEventInspector(SystemEventItem selected)
        => new()
        {
            Title = selected.Source,
            Subtitle = "Recent desktop event",
            Detail = selected.Message,
            Badge = new WorkstationBadgeModel("Severity", selected.Severity, "\uE7BA", ToneForSeverity(selected.Severity)),
            Facts =
            [
                new("Source", selected.Source),
                new("Severity", selected.Severity),
                new("Time", selected.TimeText),
                new("Message", selected.Message)
            ]
        };

    private static InspectorPanelModel BuildEmptyEventInspector()
        => new()
        {
            Title = "No recent event selected",
            Subtitle = "Recent event inspector",
            Detail = "Select a retained desktop event row to inspect source, severity, message, and timing."
        };

    private static string ToneForTriage(SystemHealthTriageTone tone)
        => tone switch
        {
            SystemHealthTriageTone.Critical => WorkspaceTone.Danger,
            SystemHealthTriageTone.Warning => WorkspaceTone.Warning,
            SystemHealthTriageTone.Ready => WorkspaceTone.Success,
            _ => WorkspaceTone.Neutral
        };

    private static string ToneForProviderStatus(string status)
        => string.Equals(status, "Connected", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(status, "Healthy", StringComparison.OrdinalIgnoreCase)
            ? WorkspaceTone.Success
            : WorkspaceTone.Danger;

    private static string ToneForSeverity(string severity)
    {
        if (IsErrorSeverity(severity))
        {
            return WorkspaceTone.Danger;
        }

        if (IsWarningSeverity(severity))
        {
            return WorkspaceTone.Warning;
        }

        return WorkspaceTone.Info;
    }

    // ── Nested display models ─────────────────────────────────────────────────────────

    public sealed class ProviderHealthItem
    {
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public Brush StatusColor { get; set; } = Brushes.Gray;
        public string LatencyText { get; set; } = string.Empty;
        public string EventsText { get; set; } = string.Empty;
    }

    public sealed class SystemEventItem
    {
        public string Source { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public Brush SeverityColor { get; set; } = Brushes.Gray;
        public string TimeText { get; set; } = string.Empty;
    }
}

public enum SystemHealthTriageTone
{
    Waiting,
    Ready,
    Warning,
    Critical
}

public sealed record SystemHealthTriageState(
    SystemHealthTriageTone Tone,
    string Title,
    string Detail,
    string ActionText,
    string TargetText,
    string EvidenceText,
    string ProviderSummaryText,
    string StorageSummaryText,
    string EventSummaryText);

public sealed record SystemHealthEmptyState(
    string Title,
    string Detail);

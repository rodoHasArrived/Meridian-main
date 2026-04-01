using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services;
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

    // ── Provider section ──────────────────────────────────────────────────────────────
    private bool _hasProviders;
    public bool HasProviders { get => _hasProviders; private set => SetProperty(ref _hasProviders, value); }

    private bool _hasNoProviders = true;
    public bool HasNoProviders { get => _hasNoProviders; private set => SetProperty(ref _hasNoProviders, value); }

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

    // ── Button state ──────────────────────────────────────────────────────────────────
    private bool _isRefreshEnabled = true;
    public bool IsRefreshEnabled { get => _isRefreshEnabled; private set => SetProperty(ref _isRefreshEnabled, value); }

    private bool _isDiagnosticsEnabled = true;
    public bool IsDiagnosticsEnabled { get => _isDiagnosticsEnabled; private set => SetProperty(ref _isDiagnosticsEnabled, value); }

    // ── Commands ──────────────────────────────────────────────────────────────────────
    public IRelayCommand RefreshCommand { get; }
    public IRelayCommand GenerateDiagnosticsCommand { get; }

    public SystemHealthViewModel(
        SystemHealthService healthService,
        WpfServices.LoggingService loggingService)
    {
        _healthService = healthService;
        _loggingService = loggingService;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += OnRefreshTimerTick;

        RefreshCommand = new RelayCommand(() => _ = RefreshAsync());
        GenerateDiagnosticsCommand = new RelayCommand(() => _ = GenerateDiagnosticsAsync());
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
        try { await LoadDataAsync(); }
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
        catch (Exception)
        {
            // Silently swallow per-metric failures to keep other panels updating
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
                if (providers is { Count: > 0 })
                {
                    var hasUnhealthy = false;
                    foreach (var p in providers)
                    {
                        var isHealthy = string.Equals(p.Status, "Connected", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(p.Status, "Healthy", StringComparison.OrdinalIgnoreCase);
                        if (!isHealthy) hasUnhealthy = true;

                        Providers.Add(new ProviderHealthItem
                        {
                            Name = p.Provider,
                            Status = p.Status,
                            StatusColor = isHealthy ? SuccessBrush : ErrorBrush,
                            LatencyText = $"{p.LatencyMs:F0}ms",
                            EventsText = $"{p.EventsPerSecond:F1}/s"
                        });
                    }

                    UpdateOverallStatus(hasUnhealthy);
                    HasProviders = true;
                    HasNoProviders = false;
                }
                else
                {
                    HasProviders = false;
                    HasNoProviders = true;
                }
            });
        }
        catch (Exception)
        {
            // Silently swallow per-section failures
        }
    }

    private async Task LoadStorageHealthAsync()
    {
        try
        {
            var storage = await _healthService.GetStorageHealthAsync();
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (storage == null) return;

                StorageTotalText = FormatHelpers.FormatBytes(storage.TotalBytes);
                StorageFilesText = storage.TotalFiles.ToString("N0");

                if (storage.TotalBytes > 0 && storage.AvailableBytes > 0)
                {
                    var usedPercent = (double)(storage.TotalBytes - storage.AvailableBytes) / storage.TotalBytes * 100;
                    DiskUsageText = $"{usedPercent:F1}%";
                    DiskUsageValue = usedPercent;
                    DiskUsageForeground = usedPercent > 90 ? ErrorBrush
                        : usedPercent > 75 ? WarningBrush
                        : InfoBrush;
                }
                else
                {
                    DiskUsageText = "--";
                    DiskUsageValue = 0;
                }
            });
        }
        catch (Exception)
        {
            // Silently swallow per-section failures
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
                if (events is { Count: > 0 })
                {
                    foreach (var evt in events)
                    {
                        Events.Add(new SystemEventItem
                        {
                            Source = evt.Source,
                            Message = evt.Message,
                            SeverityColor = GetSeverityBrush(evt.Severity),
                            TimeText = FormatTimestamp(evt.Timestamp)
                        });
                    }
                    HasEvents = true;
                    HasNoEvents = false;
                }
                else
                {
                    HasEvents = false;
                    HasNoEvents = true;
                }
            });
        }
        catch (Exception)
        {
            // Silently swallow per-section failures
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────

    private void UpdateOverallStatus(bool hasUnhealthy)
    {
        OverallStatusText = hasUnhealthy ? "Degraded" : "Healthy";
        OverallStatusBackground = hasUnhealthy ? WarningBrush : SuccessBrush;
    }

    private Brush GetSeverityBrush(string severity) =>
        severity?.ToLowerInvariant() switch
        {
            "error" or "critical" => ErrorBrush,
            "warning" => WarningBrush,
            "info" or "information" => InfoBrush,
            _ => MutedBrush
        };

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1) return $"{(int)uptime.TotalDays}d {uptime.Hours}h";
        if (uptime.TotalHours >= 1) return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
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
        public string Message { get; set; } = string.Empty;
        public Brush SeverityColor { get; set; } = Brushes.Gray;
        public string TimeText { get; set; } = string.Empty;
    }
}

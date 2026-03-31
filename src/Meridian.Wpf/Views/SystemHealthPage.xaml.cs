using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Meridian.Ui.Services;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// System health monitoring page showing real-time metrics, provider health,
/// storage status, and recent system events with auto-refresh.
/// </summary>
public partial class SystemHealthPage : Page
{
    private readonly SystemHealthService _healthService;
    private readonly WpfServices.LoggingService _loggingService;
    private readonly ObservableCollection<ProviderHealthItem> _providers = new();
    private readonly ObservableCollection<SystemEventItem> _events = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly DateTime _startTime = DateTime.UtcNow;

    // Cached brush lookups — initialized in OnPageLoaded to avoid per-refresh FindResource calls [P1]
    private Brush _errorBrush = Brushes.Red;
    private Brush _warningBrush = Brushes.Orange;
    private Brush _successBrush = Brushes.Green;
    private Brush _infoBrush = Brushes.LightBlue;
    private Brush _mutedBrush = Brushes.Gray;

    public SystemHealthPage()
    {
        InitializeComponent();

        _healthService = SystemHealthService.Instance;
        _loggingService = WpfServices.LoggingService.Instance;

        ProvidersList.ItemsSource = _providers;
        EventsList.ItemsSource = _events;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += async (_, _) => await LoadDataAsync();
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        // Cache resource brushes once — avoids repeated dictionary lookups on every refresh [P1]
        _errorBrush   = (Brush)FindResource("ErrorColorBrush");
        _warningBrush = (Brush)FindResource("WarningColorBrush");
        _successBrush = (Brush)FindResource("SuccessColorBrush");
        _infoBrush    = (Brush)FindResource("InfoColorBrush");
        _mutedBrush   = (Brush)FindResource("ConsoleTextMutedBrush");

        await LoadDataAsync();
        _refreshTimer.Start();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _refreshTimer.Stop();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false;
        await LoadDataAsync();
        RefreshButton.IsEnabled = true;
    }

    private async void GenerateDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        DiagnosticsButton.IsEnabled = false;
        try
        {
            var bundle = await _healthService.GenerateDiagnosticBundleAsync();
            if (bundle != null)
            {
                MessageBox.Show(
                    $"Diagnostic bundle created:\n{bundle.FilePath}\nSize: {FormatHelpers.FormatBytes(bundle.FileSizeBytes)}",
                    "Diagnostics",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    "Diagnostic bundle generated successfully.",
                    "Diagnostics",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to generate diagnostics", ex);
            MessageBox.Show(
                $"Failed to generate diagnostics: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            DiagnosticsButton.IsEnabled = true;
        }
    }

    private async System.Threading.Tasks.Task LoadDataAsync()
    {
        try
        {
            await System.Threading.Tasks.Task.WhenAll(
                LoadMetricsAsync(),
                LoadProviderHealthAsync(),
                LoadStorageHealthAsync(),
                LoadEventsAsync());
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to load health data", ex);
        }
    }

    private async System.Threading.Tasks.Task LoadMetricsAsync()
    {
        try
        {
            var metrics = await _healthService.GetSystemMetricsAsync();
            if (metrics != null)
            {
                // InvokeAsync is non-blocking — does not block the thread-pool thread completing the await [P2]
                await Dispatcher.InvokeAsync(() =>
                {
                    CpuText.Text = $"{metrics.CpuUsagePercent:F0}%";
                    CpuText.Foreground = metrics.CpuUsagePercent > 80
                        ? _errorBrush
                        : metrics.CpuUsagePercent > 50
                            ? _warningBrush
                            : _successBrush;

                    MemoryText.Text = FormatHelpers.FormatBytes(metrics.MemoryUsedBytes);
                    ThreadsText.Text = metrics.ThreadCount.ToString("N0");
                    UptimeText.Text = FormatUptime(DateTime.UtcNow - _startTime);
                });
            }
            else
            {
                var process = Process.GetCurrentProcess();
                var uptime = DateTime.UtcNow - _startTime;

                await Dispatcher.InvokeAsync(() =>
                {
                    CpuText.Text = "--";
                    MemoryText.Text = FormatHelpers.FormatBytes(process.WorkingSet64);
                    ThreadsText.Text = process.Threads.Count.ToString("N0");
                    UptimeText.Text = FormatUptime(uptime);
                });
            }
        }
        catch (Exception ex)
        {
        }
    }

    private async System.Threading.Tasks.Task LoadProviderHealthAsync()
    {
        try
        {
            var providers = await _healthService.GetProviderHealthAsync();
            await Dispatcher.InvokeAsync(() =>
            {
                _providers.Clear();
                if (providers != null && providers.Count > 0)
                {
                    var hasUnhealthy = false;
                    foreach (var p in providers)
                    {
                        var isHealthy = string.Equals(p.Status, "Connected", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(p.Status, "Healthy", StringComparison.OrdinalIgnoreCase);
                        if (!isHealthy) hasUnhealthy = true;

                        _providers.Add(new ProviderHealthItem
                        {
                            Name = p.Provider,
                            Status = p.Status,
                            StatusColor = isHealthy ? _successBrush : _errorBrush,
                            LatencyText = $"{p.LatencyMs:F0}ms",
                            EventsText = $"{p.EventsPerSecond:F1}/s"
                        });
                    }

                    UpdateOverallStatus(hasUnhealthy);
                    NoProvidersPanel.Visibility = Visibility.Collapsed;
                    ProvidersList.Visibility = Visibility.Visible;
                }
                else
                {
                    NoProvidersPanel.Visibility = Visibility.Visible;
                    ProvidersList.Visibility = Visibility.Collapsed;
                }
            });
        }
        catch (Exception ex)
        {
        }
    }

    private async System.Threading.Tasks.Task LoadStorageHealthAsync()
    {
        try
        {
            var storage = await _healthService.GetStorageHealthAsync();
            await Dispatcher.InvokeAsync(() =>
            {
                if (storage != null)
                {
                    StorageTotalText.Text = FormatHelpers.FormatBytes(storage.TotalBytes);
                    StorageFilesText.Text = storage.TotalFiles.ToString("N0");

                    if (storage.TotalBytes > 0 && storage.AvailableBytes > 0)
                    {
                        var usedPercent = (double)(storage.TotalBytes - storage.AvailableBytes) / storage.TotalBytes * 100;
                        DiskUsageText.Text = $"{usedPercent:F1}%";
                        DiskUsageBar.Value = usedPercent;
                        DiskUsageBar.Foreground = usedPercent > 90
                            ? _errorBrush
                            : usedPercent > 75
                                ? _warningBrush
                                : _infoBrush;
                    }
                    else
                    {
                        DiskUsageText.Text = "--";
                        DiskUsageBar.Value = 0;
                    }
                }
            });
        }
        catch (Exception ex)
        {
        }
    }

    private async System.Threading.Tasks.Task LoadEventsAsync()
    {
        try
        {
            var events = await _healthService.GetRecentEventsAsync(20);
            await Dispatcher.InvokeAsync(() =>
            {
                _events.Clear();
                if (events != null && events.Count > 0)
                {
                    foreach (var evt in events)
                    {
                        _events.Add(new SystemEventItem
                        {
                            Source = evt.Source,
                            Message = evt.Message,
                            SeverityColor = GetSeverityBrush(evt.Severity),
                            TimeText = FormatTimestamp(evt.Timestamp)
                        });
                    }

                    NoEventsPanel.Visibility = Visibility.Collapsed;
                    EventsList.Visibility = Visibility.Visible;
                }
                else
                {
                    NoEventsPanel.Visibility = Visibility.Visible;
                    EventsList.Visibility = Visibility.Collapsed;
                }
            });
        }
        catch (Exception ex)
        {
        }
    }

    private void UpdateOverallStatus(bool hasUnhealthy)
    {
        if (hasUnhealthy)
        {
            OverallStatusText.Text = "Degraded";
            OverallStatusBadge.Background = _warningBrush;
        }
        else
        {
            OverallStatusText.Text = "Healthy";
            OverallStatusBadge.Background = _successBrush;
        }
    }

    private Brush GetSeverityBrush(string severity)
    {
        return severity?.ToLowerInvariant() switch
        {
            "error" or "critical" => _errorBrush,
            "warning" => _warningBrush,
            "info" or "information" => _infoBrush,
            _ => _mutedBrush
        };
    }


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

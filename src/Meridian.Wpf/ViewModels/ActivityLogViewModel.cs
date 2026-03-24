using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Activity Log page.
/// Holds all state, HTTP loading, filtering, and timer management so that the code-behind
/// is thinned to lifecycle wiring and the single UI-level action (auto-scroll).
/// </summary>
public sealed class ActivityLogViewModel : BindableBase, IDisposable
{
    private const int MaxLogEntries = 1000;

    private readonly HttpClient _httpClient = new();
    private readonly WpfServices.LoggingService _loggingService;
    private readonly WpfServices.NotificationService _notificationService;

    private readonly ObservableCollection<LogEntryModel> _allLogs = new();
    private readonly DispatcherTimer _refreshTimer;
    private CancellationTokenSource? _cts;

    private string _baseUrl;
    private string _levelFilter = "All";
    private string _categoryFilter = "All";
    private string _searchText = string.Empty;
    private bool _offlineIndicatorShown;

    // ── Public collections ────────────────────────────────────────────────────────

    /// <summary>The filtered and sorted log entries bound to the list view.</summary>
    public ObservableCollection<LogEntryModel> FilteredLogs { get; } = new();

    // ── Bindable properties ───────────────────────────────────────────────────────

    private string _logCount = "0 entries";
    public string LogCount { get => _logCount; private set => SetProperty(ref _logCount, value); }

    private bool _noLogsVisible = true;
    public bool NoLogsVisible { get => _noLogsVisible; private set => SetProperty(ref _noLogsVisible, value); }

    private bool _isAutoScrollEnabled = true;
    public bool IsAutoScrollEnabled { get => _isAutoScrollEnabled; set => SetProperty(ref _isAutoScrollEnabled, value); }

    // ── Event raised to signal the view to scroll ─────────────────────────────────

    /// <summary>Raised when a new log entry is prepended and auto-scroll is enabled.</summary>
    public event EventHandler? ScrollToTopRequested;

    // ── Commands ──────────────────────────────────────────────────────────────────

    public IRelayCommand ClearCommand { get; }

    // ─────────────────────────────────────────────────────────────────────────────

    public ActivityLogViewModel(
        WpfServices.StatusService statusService,
        WpfServices.LoggingService loggingService,
        WpfServices.NotificationService notificationService)
    {
        _loggingService = loggingService;
        _notificationService = notificationService;
        _baseUrl = statusService.BaseUrl;

        ClearCommand = new RelayCommand(ExecuteClear);

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += async (_, _) => await LoadLogsAsync();
    }

    /// <summary>Starts the background refresh timer and subscribes to log events.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        _loggingService.LogWritten += OnLogEntryAdded;

        await LoadLogsAsync();

        _refreshTimer.Start();
    }

    /// <summary>Stops the timer and unsubscribes from events.</summary>
    public void Stop()
    {
        _loggingService.LogWritten -= OnLogEntryAdded;
        _refreshTimer.Stop();
        _cts?.Cancel();
    }

    // ── Filter/search surface (called from code-behind event handlers) ────────────

    public void UpdateLevelFilter(string level)
    {
        _levelFilter = level;
        ApplyFilters();
    }

    public void UpdateCategoryFilter(string category)
    {
        _categoryFilter = category;
        ApplyFilters();
    }

    public void UpdateSearch(string text)
    {
        _searchText = text;
        ApplyFilters();
    }

    // ── Export (called from code-behind after file dialog) ────────────────────────

    public void ExportAsCsv(System.IO.Stream stream)
    {
        using var writer = new System.IO.StreamWriter(stream, leaveOpen: false);
        writer.WriteLine("Timestamp,Level,Category,Message");
        foreach (var log in FilteredLogs)
        {
            var escapedMessage = $"\"{log.Message.Replace("\"", "\"\"")}\"";
            writer.WriteLine($"{log.RawTimestamp:O},{log.Level},{log.Category},{escapedMessage}");
        }
    }

    public void ExportAsText(System.IO.Stream stream)
    {
        using var writer = new System.IO.StreamWriter(stream, leaveOpen: false);
        foreach (var log in FilteredLogs)
        {
            writer.WriteLine($"[{log.RawTimestamp:O}] [{log.Level}] [{log.Category}] {log.Message}");
        }
    }

    public void ShowExportNotification(string filePath)
    {
        _notificationService.ShowNotification(
            "Export Complete",
            $"Exported {FilteredLogs.Count} log entries to {System.IO.Path.GetFileName(filePath)}",
            NotificationType.Success);
    }

    public void ShowExportError(Exception ex)
    {
        _loggingService.LogError("Failed to export activity log", ex);
        _notificationService.ShowNotification(
            "Export Failed",
            "An error occurred while exporting the activity log.",
            NotificationType.Error);
    }

    public int FilteredCount => FilteredLogs.Count;

    // ── Internal logic ────────────────────────────────────────────────────────────

    private void OnLogEntryAdded(object? sender, LogEntryEventArgs e)
    {
        var category = "System";
        foreach (var (key, value) in e.Properties)
        {
            if (string.Equals(key, "category", StringComparison.OrdinalIgnoreCase))
            {
                category = value;
                break;
            }
        }

        var level = e.Level.ToString();
        var entry = new LogEntryModel
        {
            RawTimestamp = e.Timestamp,
            Timestamp = e.Timestamp.ToString("HH:mm:ss"),
            Level = level,
            Category = category,
            Message = e.Message,
            LevelBackground = GetLevelBackground(level),
            LevelForeground = GetLevelForeground(level)
        };

        _allLogs.Insert(0, entry);

        while (_allLogs.Count > MaxLogEntries)
        {
            _allLogs.RemoveAt(_allLogs.Count - 1);
        }

        ApplyFilters();

        if (IsAutoScrollEnabled && FilteredLogs.Count > 0)
        {
            ScrollToTopRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task LoadLogsAsync(CancellationToken ct = default)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        try
        {
            var response = await _httpClient.GetAsync(
                $"{_baseUrl}/api/logs?limit=500", _cts.Token);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(_cts.Token);
                var data = JsonSerializer.Deserialize<JsonElement>(json);

                if (data.ValueKind == JsonValueKind.Array)
                {
                    var existingIds = _allLogs.Select(l => $"{l.RawTimestamp:O}_{l.Message}").ToHashSet();

                    foreach (var item in data.EnumerateArray())
                    {
                        var timestamp = item.TryGetProperty("timestamp", out var ts) ? ts.GetDateTime() : DateTime.UtcNow;
                        var level = item.TryGetProperty("level", out var lv) ? lv.GetString() ?? "Info" : "Info";
                        var category = item.TryGetProperty("category", out var cat) ? cat.GetString() ?? "System" : "System";
                        var message = item.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : "";

                        var id = $"{timestamp:O}_{message}";
                        if (!existingIds.Contains(id))
                        {
                            _allLogs.Add(new LogEntryModel
                            {
                                RawTimestamp = timestamp,
                                Timestamp = timestamp.ToString("HH:mm:ss"),
                                Level = level,
                                Category = category,
                                Message = message,
                                LevelBackground = GetLevelBackground(level),
                                LevelForeground = GetLevelForeground(level)
                            });
                        }
                    }
                }

                ApplyFilters();
            }
            else if (_allLogs.Count == 0)
            {
                ShowOfflineIndicator("Backend returned non-success status. Showing local logs only.");
            }
        }
        catch (HttpRequestException)
        {
            if (_allLogs.Count == 0)
            {
                ShowOfflineIndicator("Backend is unreachable. Showing local logs only.");
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled — ignore
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to load activity logs", ex);
        }
    }

    private void ShowOfflineIndicator(string reason)
    {
        if (_offlineIndicatorShown) return;
        _offlineIndicatorShown = true;

        _notificationService.ShowNotification(
            "Offline Mode",
            $"{reason} Connect the backend service to see live activity logs.",
            NotificationType.Warning);

        var now = DateTime.Now;
        _allLogs.Add(new LogEntryModel
        {
            RawTimestamp = now,
            Timestamp = now.ToString("HH:mm:ss"),
            Level = "Warning",
            Category = "System",
            Message = $"[Offline] {reason} Local UI events will still appear here.",
            LevelBackground = GetLevelBackground("Warning"),
            LevelForeground = GetLevelForeground("Warning")
        });

        ApplyFilters();
    }

    private void ApplyFilters()
    {
        FilteredLogs.Clear();

        var filtered = _allLogs.AsEnumerable();

        if (_levelFilter != "All")
        {
            filtered = filtered.Where(l => l.Level.Equals(_levelFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (_categoryFilter != "All")
        {
            filtered = filtered.Where(l => l.Category.Equals(_categoryFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            filtered = filtered.Where(l =>
                l.Message.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                l.Category.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var log in filtered.OrderByDescending(l => l.RawTimestamp))
        {
            FilteredLogs.Add(log);
        }

        LogCount = $"{FilteredLogs.Count} entries";
        NoLogsVisible = FilteredLogs.Count == 0;
    }

    private void ExecuteClear()
    {
        _allLogs.Clear();
        FilteredLogs.Clear();
        LogCount = "0 entries";
        NoLogsVisible = true;

        _notificationService.ShowNotification(
            "Cleared",
            "Activity log has been cleared.",
            NotificationType.Info);
    }

    private static SolidColorBrush GetLevelBackground(string level) =>
        new(level.ToUpperInvariant() switch
        {
            "ERROR" => Color.FromArgb(40, 244, 67, 54),
            "WARNING" or "WARN" => Color.FromArgb(40, 255, 193, 7),
            "INFO" => Color.FromArgb(40, 88, 166, 255),
            "DEBUG" => Color.FromArgb(40, 139, 148, 158),
            _ => Color.FromArgb(40, 139, 148, 158)
        });

    private static SolidColorBrush GetLevelForeground(string level) =>
        new(level.ToUpperInvariant() switch
        {
            "ERROR" => Color.FromRgb(244, 67, 54),
            "WARNING" or "WARN" => Color.FromRgb(255, 193, 7),
            "INFO" => Color.FromRgb(88, 166, 255),
            "DEBUG" => Color.FromRgb(139, 148, 158),
            _ => Color.FromRgb(139, 148, 158)
        });

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _httpClient.Dispose();
    }
}

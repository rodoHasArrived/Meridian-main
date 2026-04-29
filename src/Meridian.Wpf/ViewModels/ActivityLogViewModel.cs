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
using Meridian.Ui.Services.Services;
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

    private static readonly SolidColorBrush ErrorLevelBackground = CreateFrozenBrush(Color.FromArgb(40, 244, 67, 54));
    private static readonly SolidColorBrush WarningLevelBackground = CreateFrozenBrush(Color.FromArgb(40, 255, 193, 7));
    private static readonly SolidColorBrush InfoLevelBackground = CreateFrozenBrush(Color.FromArgb(40, 88, 166, 255));
    private static readonly SolidColorBrush DebugLevelBackground = CreateFrozenBrush(Color.FromArgb(40, 139, 148, 158));
    private static readonly SolidColorBrush ErrorLevelForeground = CreateFrozenBrush(Color.FromRgb(244, 67, 54));
    private static readonly SolidColorBrush WarningLevelForeground = CreateFrozenBrush(Color.FromRgb(255, 193, 7));
    private static readonly SolidColorBrush InfoLevelForeground = CreateFrozenBrush(Color.FromRgb(88, 166, 255));
    private static readonly SolidColorBrush DebugLevelForeground = CreateFrozenBrush(Color.FromRgb(139, 148, 158));

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
    private bool _suppressFilterRefresh;

    // ── Public collections ────────────────────────────────────────────────────────

    /// <summary>The filtered and sorted log entries bound to the list view.</summary>
    public ObservableCollection<LogEntryModel> FilteredLogs { get; } = new();

    // ── Bindable properties ───────────────────────────────────────────────────────

    private string _logCount = "0 entries";
    public string LogCount { get => _logCount; private set => SetProperty(ref _logCount, value); }

    private string _visibleLogCountText = "0 visible";
    public string VisibleLogCountText { get => _visibleLogCountText; private set => SetProperty(ref _visibleLogCountText, value); }

    private string _errorLogCountText = "0 errors";
    public string ErrorLogCountText { get => _errorLogCountText; private set => SetProperty(ref _errorLogCountText, value); }

    private string _warningLogCountText = "0 warnings";
    public string WarningLogCountText { get => _warningLogCountText; private set => SetProperty(ref _warningLogCountText, value); }

    private string _latestLogTimeText = "--";
    public string LatestLogTimeText { get => _latestLogTimeText; private set => SetProperty(ref _latestLogTimeText, value); }

    private string _latestLogSummary = "No activity captured yet.";
    public string LatestLogSummary { get => _latestLogSummary; private set => SetProperty(ref _latestLogSummary, value); }

    private string _activityPostureTitle = "Waiting for activity";
    public string ActivityPostureTitle { get => _activityPostureTitle; private set => SetProperty(ref _activityPostureTitle, value); }

    private string _activityPostureDetail = "Connect the backend or trigger a desktop workflow to populate retained activity.";
    public string ActivityPostureDetail { get => _activityPostureDetail; private set => SetProperty(ref _activityPostureDetail, value); }

    private string _activeFilterSummary = "Showing every retained entry";
    public string ActiveFilterSummary { get => _activeFilterSummary; private set => SetProperty(ref _activeFilterSummary, value); }

    private bool _noLogsVisible = true;
    public bool NoLogsVisible { get => _noLogsVisible; private set => SetProperty(ref _noLogsVisible, value); }

    private bool _isAutoScrollEnabled = true;
    public bool IsAutoScrollEnabled { get => _isAutoScrollEnabled; set => SetProperty(ref _isAutoScrollEnabled, value); }

    public string LevelFilter
    {
        get => _levelFilter;
        set
        {
            if (SetProperty(ref _levelFilter, NormalizeFilterValue(value)) && !_suppressFilterRefresh)
            {
                ApplyFilters();
            }
        }
    }

    public string CategoryFilter
    {
        get => _categoryFilter;
        set
        {
            if (SetProperty(ref _categoryFilter, NormalizeFilterValue(value)) && !_suppressFilterRefresh)
            {
                ApplyFilters();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty) && !_suppressFilterRefresh)
            {
                ApplyFilters();
            }
        }
    }

    public bool HasLogHistory => _allLogs.Count > 0;

    public bool HasActiveFilters =>
        !string.Equals(LevelFilter, "All", StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(CategoryFilter, "All", StringComparison.OrdinalIgnoreCase) ||
        !string.IsNullOrWhiteSpace(SearchText);

    public bool HasFilterRecoveryAction => HasLogHistory && HasActiveFilters;

    public bool CanClearLog => HasLogHistory;

    public bool CanExportVisibleLogs => FilteredLogs.Count > 0;

    public string EmptyStateTitle => HasFilterRecoveryAction
        ? "No log entries match the current filters"
        : "No log entries to display";

    public string EmptyStateDetail => HasFilterRecoveryAction
        ? "Reset filters to return to the retained activity window."
        : "Connect the backend or trigger a desktop workflow to populate retained activity.";

    // ── Event raised to signal the view to scroll ─────────────────────────────────

    /// <summary>Raised when a new log entry is prepended and auto-scroll is enabled.</summary>
    public event EventHandler? ScrollToTopRequested;

    // ── Commands ──────────────────────────────────────────────────────────────────

    public IRelayCommand ClearCommand { get; }
    public IRelayCommand ClearFiltersCommand { get; }

    // ─────────────────────────────────────────────────────────────────────────────

    public ActivityLogViewModel(
        WpfServices.StatusService statusService,
        WpfServices.LoggingService loggingService,
        WpfServices.NotificationService notificationService)
    {
        _loggingService = loggingService;
        _notificationService = notificationService;
        _baseUrl = statusService.BaseUrl;

        ClearCommand = new RelayCommand(ExecuteClear, () => CanClearLog);
        ClearFiltersCommand = new RelayCommand(ClearFilters, () => HasActiveFilters);

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
        LevelFilter = level;
    }

    public void UpdateCategoryFilter(string category)
    {
        CategoryFilter = category;
    }

    public void UpdateSearch(string text)
    {
        SearchText = text;
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

    internal void AddLocalLogEntry(
        LogLevel level,
        string message,
        string category = "System",
        DateTime? timestamp = null)
    {
        OnLogEntryAdded(
            this,
            new LogEntryEventArgs(
                level,
                timestamp ?? DateTime.UtcNow,
                message,
                null,
                [("category", category)]));
    }

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
        if (_offlineIndicatorShown)
            return;
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

        LogCount = FilteredLogs.Count == _allLogs.Count
            ? FormatEntryCount(FilteredLogs.Count)
            : $"{FilteredLogs.Count} of {FormatEntryCount(_allLogs.Count)}";
        NoLogsVisible = FilteredLogs.Count == 0;
        UpdateTriageState();
        RaiseFilterStateChanged();
    }

    private void ExecuteClear()
    {
        _allLogs.Clear();
        ApplyFilters();

        _notificationService.ShowNotification(
            "Cleared",
            "Activity log has been cleared.",
            NotificationType.Info);
    }

    private void ClearFilters()
    {
        if (!HasActiveFilters)
            return;

        _suppressFilterRefresh = true;
        try
        {
            LevelFilter = "All";
            CategoryFilter = "All";
            SearchText = string.Empty;
        }
        finally
        {
            _suppressFilterRefresh = false;
        }

        ApplyFilters();
    }

    private void UpdateTriageState()
    {
        var totalCount = _allLogs.Count;
        var visibleCount = FilteredLogs.Count;
        var errorCount = _allLogs.Count(log => IsLevel(log.Level, "Error"));
        var warningCount = _allLogs.Count(log => IsLevel(log.Level, "Warning") || IsLevel(log.Level, "Warn"));
        var latest = _allLogs.OrderByDescending(log => log.RawTimestamp).FirstOrDefault();

        VisibleLogCountText = $"{visibleCount} visible";
        ErrorLogCountText = $"{errorCount} error{(errorCount == 1 ? string.Empty : "s")}";
        WarningLogCountText = $"{warningCount} warning{(warningCount == 1 ? string.Empty : "s")}";
        LatestLogTimeText = latest?.Timestamp ?? "--";
        LatestLogSummary = latest is null
            ? "No activity captured yet."
            : $"{latest.Level} / {latest.Category}: {latest.Message}";
        ActiveFilterSummary = BuildActiveFilterSummary();

        if (totalCount == 0)
        {
            ActivityPostureTitle = "Waiting for activity";
            ActivityPostureDetail = "Connect the backend or trigger a desktop workflow to populate retained activity.";
        }
        else if (errorCount > 0)
        {
            ActivityPostureTitle = "Errors need review";
            ActivityPostureDetail = $"{errorCount} retained error{(errorCount == 1 ? " is" : "s are")} visible in the local activity window. Start with the latest error, then export the trace if support needs it.";
        }
        else if (warningCount > 0)
        {
            ActivityPostureTitle = "Warnings present";
            ActivityPostureDetail = $"{warningCount} retained warning{(warningCount == 1 ? " is" : "s are")} available for triage. Check provider, storage, or backfill categories before clearing the log.";
        }
        else
        {
            ActivityPostureTitle = "Activity is steady";
            ActivityPostureDetail = "No retained errors or warnings are present in the current activity window.";
        }
    }

    private string BuildActiveFilterSummary()
    {
        var parts = new List<string>();

        if (!string.Equals(_levelFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add($"{_levelFilter} level");
        }

        if (!string.Equals(_categoryFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add($"{_categoryFilter} category");
        }

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            parts.Add($"search \"{_searchText.Trim()}\"");
        }

        return parts.Count == 0
            ? "Showing every retained entry"
            : $"Filters: {string.Join(" | ", parts)}";
    }

    private static string FormatEntryCount(int count) =>
        $"{count} entr{(count == 1 ? "y" : "ies")}";

    private void RaiseFilterStateChanged()
    {
        OnPropertyChanged(nameof(HasLogHistory));
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(HasFilterRecoveryAction));
        OnPropertyChanged(nameof(CanClearLog));
        OnPropertyChanged(nameof(CanExportVisibleLogs));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateDetail));
        ClearCommand.NotifyCanExecuteChanged();
        ClearFiltersCommand.NotifyCanExecuteChanged();
    }

    private static string NormalizeFilterValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "All" : value;

    private static SolidColorBrush GetLevelBackground(string level) =>
        level.ToUpperInvariant() switch
        {
            "ERROR" => ErrorLevelBackground,
            "WARNING" or "WARN" => WarningLevelBackground,
            "INFO" => InfoLevelBackground,
            "DEBUG" => DebugLevelBackground,
            _ => DebugLevelBackground
        };

    private static SolidColorBrush GetLevelForeground(string level) =>
        level.ToUpperInvariant() switch
        {
            "ERROR" => ErrorLevelForeground,
            "WARNING" or "WARN" => WarningLevelForeground,
            "INFO" => InfoLevelForeground,
            "DEBUG" => DebugLevelForeground,
            _ => DebugLevelForeground
        };

    private static bool IsLevel(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _httpClient.Dispose();
    }
}

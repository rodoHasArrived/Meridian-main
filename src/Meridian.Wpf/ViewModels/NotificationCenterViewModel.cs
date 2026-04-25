using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Notification Center page.
/// Owns notification collection state, filtering, counters, and timer management.
/// CreateNotificationItem is kept here but uses Application.Current.Resources for brushes/icons.
/// </summary>
public sealed class NotificationCenterViewModel : BindableBase, IDisposable
{
    private readonly WpfServices.NotificationService _notificationService;
    private readonly AlertService _alertService;
    private DispatcherTimer? _alertRefreshTimer;
    private bool _suppressFilterRefresh;

    public ObservableCollection<NotificationItem> AllNotifications { get; } = new();
    public ObservableCollection<NotificationItem> FilteredNotifications { get; } = new();

    private int _unreadCount;
    public int UnreadCount { get => _unreadCount; private set => SetProperty(ref _unreadCount, value); }

    private int _totalCount;
    public int TotalCount { get => _totalCount; private set => SetProperty(ref _totalCount, value); }

    private bool _noNotificationsVisible = true;
    public bool NoNotificationsVisible { get => _noNotificationsVisible; private set => SetProperty(ref _noNotificationsVisible, value); }

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ApplyFilters();
        }
    }

    private bool _showUnreadOnly;
    public bool ShowUnreadOnly
    {
        get => _showUnreadOnly;
        set
        {
            if (SetProperty(ref _showUnreadOnly, value))
                ApplyFilters();
        }
    }

    private bool _showErrors = true;
    public bool ShowErrors
    {
        get => _showErrors;
        private set
        {
            if (SetProperty(ref _showErrors, value) && !_suppressFilterRefresh)
                ApplyFilters();
        }
    }

    private bool _showWarnings = true;
    public bool ShowWarnings
    {
        get => _showWarnings;
        private set
        {
            if (SetProperty(ref _showWarnings, value) && !_suppressFilterRefresh)
                ApplyFilters();
        }
    }

    private bool _showInfo = true;
    public bool ShowInfo
    {
        get => _showInfo;
        private set
        {
            if (SetProperty(ref _showInfo, value) && !_suppressFilterRefresh)
                ApplyFilters();
        }
    }

    private bool _showSuccess = true;
    public bool ShowSuccess
    {
        get => _showSuccess;
        private set
        {
            if (SetProperty(ref _showSuccess, value) && !_suppressFilterRefresh)
                ApplyFilters();
        }
    }

    public bool HasUnreadNotifications => UnreadCount > 0;
    public bool CanMarkAllRead => UnreadCount > 0;
    public bool HasNotificationHistory => AllNotifications.Count > 0;

    public string HistorySummaryText => HasActiveFilters
        ? $"Showing {TotalCount} of {AllNotifications.Count} notification{(AllNotifications.Count == 1 ? string.Empty : "s")}"
        : $"{TotalCount} notification{(TotalCount == 1 ? string.Empty : "s")}";

    public string EmptyStateTitle => HasNotificationHistory
        ? "No notifications match the current filters"
        : "No notifications yet";

    public string EmptyStateDescription => HasNotificationHistory
        ? "Clear the search term or widen the filters to see more notification history."
        : "Notifications about connection status, data quality, and system events will appear here.";

    /// <summary>
    /// Fires when the grouped alerts display needs to be refreshed.
    /// Code-behind handles RefreshGroupedAlerts/RefreshAlertSummary (require FindResource).
    /// </summary>
    public event EventHandler? AlertsRefreshRequested;

    public NotificationCenterViewModel(
        WpfServices.NotificationService notificationService,
        AlertService alertService)
    {
        _notificationService = notificationService;
        _alertService = alertService;
    }

    public void Start()
    {
        _notificationService.NotificationReceived += OnNotificationReceived;
        _alertService.AlertRaised += OnAlertChanged;
        _alertService.AlertResolved += OnAlertChanged;

        LoadNotifications();

        _alertRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _alertRefreshTimer.Tick += (_, _) => AlertsRefreshRequested?.Invoke(this, EventArgs.Empty);
        _alertRefreshTimer.Start();
    }

    public void Stop()
    {
        _notificationService.NotificationReceived -= OnNotificationReceived;
        _alertService.AlertRaised -= OnAlertChanged;
        _alertService.AlertResolved -= OnAlertChanged;
        _alertRefreshTimer?.Stop();
        _alertRefreshTimer = null;
    }

    public void LoadNotifications()
    {
        AllNotifications.Clear();

        var history = _notificationService.GetHistory();
        for (var index = 0; index < history.Count; index++)
        {
            var historyItem = history[index];
            var item = CreateNotificationItem(
                historyItem.Title,
                historyItem.Message,
                historyItem.Type,
                historyItem.Timestamp);
            item.IsRead = historyItem.IsRead;
            item.HistoryIndex = index;
            AllNotifications.Add(item);
        }

        ApplyFilters();
    }

    private void ApplyFilters()
    {
        FilteredNotifications.Clear();

        var filteredItems = AllNotifications
            .Where(MatchesTypeFilter)
            .Where(item => !ShowUnreadOnly || !item.IsRead)
            .Where(MatchesSearch)
            .OrderBy(item => item.IsRead)
            .ThenByDescending(item => item.RawTimestamp);

        foreach (var item in filteredItems)
        {
            FilteredNotifications.Add(item);
        }

        NoNotificationsVisible = FilteredNotifications.Count == 0;
        UpdateCounters();
    }

    /// <summary>
    /// Applies filters based on individual type checkboxes (Errors/Warnings/Info/Success).
    /// </summary>
    public void ApplyCheckboxFilters(bool showErrors, bool showWarnings, bool showInfo, bool showSuccess)
    {
        _suppressFilterRefresh = true;
        ShowErrors = showErrors;
        ShowWarnings = showWarnings;
        ShowInfo = showInfo;
        ShowSuccess = showSuccess;
        _suppressFilterRefresh = false;

        ApplyFilters();
    }

    public void UpdateCounters()
    {
        UnreadCount = AllNotifications.Count(n => !n.IsRead);
        TotalCount = FilteredNotifications.Count;
        RaiseHistoryStateChanged();
    }

    public void MarkAllRead()
    {
        foreach (var item in AllNotifications)
            item.IsRead = true;

        var history = _notificationService.GetHistory();
        for (var i = 0; i < history.Count; i++)
            _notificationService.MarkAsRead(i);

        ApplyFilters();
    }

    public void MarkRead(NotificationItem? item)
    {
        if (item is null || item.IsRead)
            return;

        item.IsRead = true;
        if (item.HistoryIndex >= 0)
            _notificationService.MarkAsRead(item.HistoryIndex);

        ApplyFilters();
    }

    public void ClearAll()
    {
        AllNotifications.Clear();
        _notificationService.ClearHistory();
        FilteredNotifications.Clear();
        NoNotificationsVisible = true;
        UpdateCounters();
    }

    public NotificationItem CreateNotificationItem(
        string title,
        string message,
        NotificationType type,
        DateTime timestamp)
    {
        var resources = System.Windows.Application.Current?.Resources;

        string GetIcon(string key) =>
            resources?[key] as string ?? "•";
        Brush GetBrush(string key) =>
            resources?[key] as Brush ?? Brushes.Gray;

        var (icon, iconColor, iconBackground, typeBackground, typeName) = type switch
        {
            NotificationType.Error => (
                GetIcon("IconError"),
                GetBrush("ErrorColorBrush"),
                GetBrush("ErrorColorBrush"),
                GetBrush("ConsoleAccentRedAlpha10Brush"),
                "Error"),
            NotificationType.Warning => (
                GetIcon("IconWarning"),
                GetBrush("WarningColorBrush"),
                GetBrush("WarningColorBrush"),
                GetBrush("ConsoleAccentOrangeAlpha10Brush"),
                "Warning"),
            NotificationType.Success => (
                GetIcon("IconSuccess"),
                GetBrush("SuccessColorBrush"),
                GetBrush("SuccessColorBrush"),
                GetBrush("ConsoleAccentGreenAlpha10Brush"),
                "Success"),
            _ => (
                GetIcon("IconInfo"),
                GetBrush("InfoColorBrush"),
                GetBrush("InfoColorBrush"),
                GetBrush("ConsoleAccentBlueAlpha10Brush"),
                "Info")
        };

        return new NotificationItem
        {
            Icon = icon,
            IconColor = iconColor,
            IconBackground = iconBackground,
            TypeBackground = typeBackground,
            Title = title,
            Message = message,
            Timestamp = FormatTimestamp(timestamp),
            Type = typeName,
            RawTimestamp = timestamp,
            NotificationType = type
        };
    }

    private void OnNotificationReceived(object? sender, NotificationEventArgs e)
    {
        _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            var item = CreateNotificationItem(e.Title, e.Message, e.Type, DateTime.Now);
            foreach (var existingItem in AllNotifications)
                existingItem.HistoryIndex++;

            item.HistoryIndex = 0;
            AllNotifications.Insert(0, item);
            ApplyFilters();
        });
    }

    private void OnAlertChanged(object? sender, AlertEventArgs e)
    {
        _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            AlertsRefreshRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    private bool MatchesTypeFilter(NotificationItem item) =>
        item.NotificationType switch
        {
            NotificationType.Error => ShowErrors,
            NotificationType.Warning => ShowWarnings,
            NotificationType.Success => ShowSuccess,
            NotificationType.Info => ShowInfo,
            _ => ShowInfo
        };

    private bool MatchesSearch(NotificationItem item)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        return item.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || item.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || item.Type.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private bool HasActiveFilters =>
        ShowUnreadOnly
        || !string.IsNullOrWhiteSpace(SearchText)
        || !(ShowErrors && ShowWarnings && ShowInfo && ShowSuccess);

    private void RaiseHistoryStateChanged()
    {
        OnPropertyChanged(nameof(HasUnreadNotifications));
        OnPropertyChanged(nameof(CanMarkAllRead));
        OnPropertyChanged(nameof(HasNotificationHistory));
        OnPropertyChanged(nameof(HistorySummaryText));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateDescription));
    }

    private static string FormatTimestamp(DateTime timestamp)
    {
        var elapsed = DateTime.Now - timestamp;
        return elapsed.TotalSeconds switch
        {
            < 60 => "Just now",
            < 3600 => $"{(int)elapsed.TotalMinutes}m ago",
            < 86400 => $"{(int)elapsed.TotalHours}h ago",
            < 172800 => "Yesterday",
            _ => timestamp.ToString("MMM dd, HH:mm")
        };
    }

    public void Dispose() => Stop();
}

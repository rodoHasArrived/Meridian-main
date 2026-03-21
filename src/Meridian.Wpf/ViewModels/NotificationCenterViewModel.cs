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

    public ObservableCollection<NotificationItem> AllNotifications { get; } = new();
    public ObservableCollection<NotificationItem> FilteredNotifications { get; } = new();

    // ── Bindable counters ───────────────────────────────────────────────────
    private int _unreadCount;
    public int UnreadCount { get => _unreadCount; private set => SetProperty(ref _unreadCount, value); }

    private int _totalCount;
    public int TotalCount { get => _totalCount; private set => SetProperty(ref _totalCount, value); }

    private bool _noNotificationsVisible = true;
    public bool NoNotificationsVisible { get => _noNotificationsVisible; private set => SetProperty(ref _noNotificationsVisible, value); }

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
        foreach (var historyItem in history)
        {
            var item = CreateNotificationItem(
                historyItem.Title,
                historyItem.Message,
                historyItem.Type,
                historyItem.Timestamp);
            item.IsRead = historyItem.IsRead;
            AllNotifications.Add(item);
        }

        ApplyFilters();
        UpdateCounters();
    }

    public void ApplyFilters(string typeFilter = "All", bool unreadOnly = false)
    {
        FilteredNotifications.Clear();

        foreach (var item in AllNotifications)
        {
            if (unreadOnly && item.IsRead) continue;

            var shouldShow = typeFilter switch
            {
                "Errors" => item.NotificationType == NotificationType.Error,
                "Warnings" => item.NotificationType == NotificationType.Warning,
                "Info" => item.NotificationType == NotificationType.Info,
                "Success" => item.NotificationType == NotificationType.Success,
                _ => true
            };

            if (shouldShow)
                FilteredNotifications.Add(item);
        }

        NoNotificationsVisible = FilteredNotifications.Count == 0;
    }

    /// <summary>
    /// Applies filters based on individual type checkboxes (Errors/Warnings/Info/Success).
    /// </summary>
    public void ApplyCheckboxFilters(bool showErrors, bool showWarnings, bool showInfo, bool showSuccess)
    {
        FilteredNotifications.Clear();

        foreach (var item in AllNotifications)
        {
            var shouldShow = item.NotificationType switch
            {
                NotificationType.Error => showErrors,
                NotificationType.Warning => showWarnings,
                NotificationType.Success => showSuccess,
                NotificationType.Info => showInfo,
                _ => showInfo
            };

            if (shouldShow)
                FilteredNotifications.Add(item);
        }

        NoNotificationsVisible = FilteredNotifications.Count == 0;
    }

    public void UpdateCounters()
    {
        UnreadCount = AllNotifications.Count(n => !n.IsRead);
        TotalCount = FilteredNotifications.Count;
    }

    public void MarkAllRead()
    {
        foreach (var item in AllNotifications)
            item.IsRead = true;

        var history = _notificationService.GetHistory();
        for (var i = 0; i < history.Count; i++)
            _notificationService.MarkAsRead(i);

        UpdateCounters();
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
        var resources = Application.Current?.Resources;

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
        _ = Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            var item = CreateNotificationItem(e.Title, e.Message, e.Type, DateTime.Now);
            AllNotifications.Insert(0, item);
            ApplyFilters();
            UpdateCounters();
        });
    }

    private void OnAlertChanged(object? sender, AlertEventArgs e)
    {
        _ = Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            AlertsRefreshRequested?.Invoke(this, EventArgs.Empty);
        });
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

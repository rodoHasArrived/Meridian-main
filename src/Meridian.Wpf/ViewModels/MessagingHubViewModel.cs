using System;
using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Messaging Hub page.
/// Owns the refresh timer, message subscription, statistics counters, and all collections.
/// </summary>
public sealed class MessagingHubViewModel : BindableBase, IDisposable
{
    private readonly WpfServices.MessagingService _messagingService;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DateTime _pageLoadedAt = DateTime.UtcNow;

    private IDisposable? _messageSubscription;
    private int _totalMessages;
    private int _failedMessages;

    // ── Cached brushes ──────────────────────────────────────────────────────
    private readonly Brush _infoBrush;
    private readonly Brush _successBrush;
    private readonly Brush _errorBrush;

    // ── Collections ─────────────────────────────────────────────────────────
    public ObservableCollection<MessageTypeItem> MessageTypes { get; } = new();
    public ObservableCollection<ActivityItem> ActivityItems { get; } = new();

    // ── Bindable properties ─────────────────────────────────────────────────
    private string _totalMessagesText = "0";
    public string TotalMessagesText { get => _totalMessagesText; private set => SetProperty(ref _totalMessagesText, value); }

    private string _subscribersText = "0";
    public string SubscribersText { get => _subscribersText; private set => SetProperty(ref _subscribersText, value); }

    private string _failedMessagesText = "0";
    public string FailedMessagesText { get => _failedMessagesText; private set => SetProperty(ref _failedMessagesText, value); }

    private string _messageRateText = "0/s";
    public string MessageRateText { get => _messageRateText; private set => SetProperty(ref _messageRateText, value); }

    private string _connectionStatusText = "Checking...";
    public string ConnectionStatusText { get => _connectionStatusText; private set => SetProperty(ref _connectionStatusText, value); }

    private Brush _connectionBadgeBackground;
    public Brush ConnectionBadgeBackground { get => _connectionBadgeBackground; private set => SetProperty(ref _connectionBadgeBackground, value); }

    private bool _noActivityVisible = true;
    public bool NoActivityVisible { get => _noActivityVisible; private set => SetProperty(ref _noActivityVisible, value); }

    public MessagingHubViewModel(
        WpfServices.MessagingService messagingService,
        Brush infoBrush,
        Brush successBrush,
        Brush errorBrush)
    {
        _messagingService = messagingService;
        _infoBrush = infoBrush;
        _successBrush = successBrush;
        _errorBrush = errorBrush;
        _connectionBadgeBackground = errorBrush;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _refreshTimer.Tick += (_, _) => RefreshStatistics();
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────

    public void Start()
    {
        _messageSubscription = _messagingService.SubscribeAll(OnMessageReceived);
        LoadMessageTypes();
        RefreshStatistics();
        UpdateConnectionStatus(true);
        _refreshTimer.Start();
    }

    public void Stop()
    {
        _refreshTimer.Stop();
        _messageSubscription?.Dispose();
        _messageSubscription = null;
    }

    // ── Public commands ─────────────────────────────────────────────────────

    public void Refresh()
    {
        LoadMessageTypes();
        RefreshStatistics();
    }

    public void ClearActivity()
    {
        ActivityItems.Clear();
        _totalMessages = 0;
        _failedMessages = 0;
        RefreshStatistics();
        UpdateActivityVisibility();
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private void OnMessageReceived(string message)
    {
        _totalMessages++;

        ActivityItems.Insert(0, new ActivityItem
        {
            DirectionIcon = "\u2192",
            DirectionColor = _infoBrush,
            MessageType = message.Length > 30 ? message[..30] + "..." : message,
            Detail = "Delivered",
            TimeText = "Just now"
        });

        while (ActivityItems.Count > 50)
            ActivityItems.RemoveAt(ActivityItems.Count - 1);

        RefreshStatistics();
        UpdateActivityVisibility();
    }

    private void LoadMessageTypes()
    {
        MessageTypes.Clear();

        var knownTypes = new[]
        {
            (WpfServices.MessageTypes.SymbolsUpdated, "Symbols Updated"),
            (WpfServices.MessageTypes.ConfigurationChanged, "Configuration Changed"),
            (WpfServices.MessageTypes.ConnectionStatusChanged, "Connection Status"),
            (WpfServices.MessageTypes.BackfillStarted, "Backfill Started"),
            (WpfServices.MessageTypes.BackfillCompleted, "Backfill Completed"),
            (WpfServices.MessageTypes.BackfillProgress, "Backfill Progress"),
            (WpfServices.MessageTypes.DataQualityAlert, "Data Quality Alert"),
            (WpfServices.MessageTypes.StorageWarning, "Storage Warning"),
            (WpfServices.MessageTypes.ProviderHealthChanged, "Provider Health"),
            (WpfServices.MessageTypes.ThemeChanged, "Theme Changed"),
            (WpfServices.MessageTypes.RefreshRequested, "Refresh Requested"),
            (WpfServices.MessageTypes.NavigationRequested, "Navigation Requested"),
            (WpfServices.MessageTypes.WatchlistUpdated, "Watchlist Updated"),
            (WpfServices.MessageTypes.ScheduleUpdated, "Schedule Updated")
        };

        foreach (var (type, displayName) in knownTypes)
        {
            var count = _messagingService.GetSubscriptionCount(type);
            MessageTypes.Add(new MessageTypeItem
            {
                TypeName = displayName,
                CountText = $"{count} subscriber{(count == 1 ? "" : "s")}"
            });
        }
    }

    private void RefreshStatistics()
    {
        TotalMessagesText = _totalMessages.ToString("N0");
        FailedMessagesText = _failedMessages.ToString("N0");

        var totalSubscribers = 0;
        var knownTypes = new[]
        {
            WpfServices.MessageTypes.SymbolsUpdated,
            WpfServices.MessageTypes.ConfigurationChanged,
            WpfServices.MessageTypes.ConnectionStatusChanged,
            WpfServices.MessageTypes.BackfillStarted,
            WpfServices.MessageTypes.BackfillCompleted,
            WpfServices.MessageTypes.BackfillProgress,
            WpfServices.MessageTypes.DataQualityAlert,
            WpfServices.MessageTypes.StorageWarning,
            WpfServices.MessageTypes.ProviderHealthChanged,
            WpfServices.MessageTypes.RefreshRequested,
            WpfServices.MessageTypes.NavigationRequested
        };

        foreach (var type in knownTypes)
            totalSubscribers += _messagingService.GetSubscriptionCount(type);

        SubscribersText = totalSubscribers.ToString("N0");

        var elapsed = (DateTime.UtcNow - _pageLoadedAt).TotalSeconds;
        MessageRateText = elapsed > 0 ? $"{_totalMessages / elapsed:F1}/s" : "0/s";
    }

    private void UpdateConnectionStatus(bool isConnected)
    {
        ConnectionStatusText = isConnected ? "Active" : "Inactive";
        ConnectionBadgeBackground = isConnected ? _successBrush : _errorBrush;
    }

    private void UpdateActivityVisibility() =>
        NoActivityVisible = ActivityItems.Count == 0;

    public void Dispose() => Stop();

    // ── Nested display item types ────────────────────────────────────────────
    public sealed class MessageTypeItem
    {
        public string TypeName { get; set; } = string.Empty;
        public string CountText { get; set; } = string.Empty;
    }

    public sealed class ActivityItem
    {
        public string DirectionIcon { get; set; } = string.Empty;
        public Brush DirectionColor { get; set; } = Brushes.Gray;
        public string MessageType { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string TimeText { get; set; } = string.Empty;
    }
}

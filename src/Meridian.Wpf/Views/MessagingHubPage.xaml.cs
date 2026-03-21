using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Messaging Hub page for monitoring inter-service messaging activity,
/// registered message types, and recent message delivery status.
/// </summary>
public partial class MessagingHubPage : Page
{
    private readonly WpfServices.MessagingService _messagingService;
    private readonly WpfServices.LoggingService _loggingService;
    private readonly ObservableCollection<MessageTypeItem> _messageTypes = new();
    private readonly ObservableCollection<ActivityItem> _activityItems = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly DateTime _pageLoadedAt = DateTime.UtcNow;
    private IDisposable? _messageSubscription;
    private int _totalMessages;
    private int _failedMessages;
    private Brush _infoBrush = null!;
    private Brush _successBrush = null!;
    private Brush _errorBrush = null!;

    public MessagingHubPage()
    {
        InitializeComponent();

        _messagingService = WpfServices.MessagingService.Instance;
        _loggingService = WpfServices.LoggingService.Instance;

        MessageTypesList.ItemsSource = _messageTypes;
        ActivityList.ItemsSource = _activityItems;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _refreshTimer.Tick += (_, _) => RefreshStatistics();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        // Cache brushes once to avoid resource dictionary walks on every message
        _infoBrush = (Brush)FindResource("InfoColorBrush");
        _successBrush = (Brush)FindResource("SuccessColorBrush");
        _errorBrush = (Brush)FindResource("ErrorColorBrush");

        _messageSubscription = _messagingService.SubscribeAll(OnMessageReceived);

        LoadMessageTypes();
        RefreshStatistics();
        UpdateConnectionStatus(true);

        _refreshTimer.Start();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _refreshTimer.Stop();
        _messageSubscription?.Dispose();
        _messageSubscription = null;
    }

    private void OnMessageReceived(string message)
    {
        Dispatcher.Invoke(() =>
        {
            _totalMessages++;

            _activityItems.Insert(0, new ActivityItem
            {
                DirectionIcon = "\u2192",
                DirectionColor = _infoBrush,
                MessageType = message.Length > 30 ? message[..30] + "..." : message,
                Detail = "Delivered",
                TimeText = "Just now"
            });

            while (_activityItems.Count > 50)
            {
                _activityItems.RemoveAt(_activityItems.Count - 1);
            }

            RefreshStatistics();
            UpdateActivityVisibility();
        });
    }

    private void LoadMessageTypes()
    {
        _messageTypes.Clear();

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
            _messageTypes.Add(new MessageTypeItem
            {
                TypeName = displayName,
                CountText = $"{count} subscriber{(count == 1 ? "" : "s")}"
            });
        }
    }

    private void RefreshStatistics()
    {
        TotalMessagesText.Text = _totalMessages.ToString("N0");
        FailedMessagesText.Text = _failedMessages.ToString("N0");

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
        {
            totalSubscribers += _messagingService.GetSubscriptionCount(type);
        }

        SubscribersText.Text = totalSubscribers.ToString("N0");

        var elapsed = (DateTime.UtcNow - _pageLoadedAt).TotalSeconds;
        MessageRateText.Text = elapsed > 0 ? $"{_totalMessages / elapsed:F1}/s" : "0/s";
    }

    private void UpdateConnectionStatus(bool isConnected)
    {
        if (isConnected)
        {
            ConnectionStatusText.Text = "Active";
            ConnectionBadge.Background = _successBrush;
        }
        else
        {
            ConnectionStatusText.Text = "Inactive";
            ConnectionBadge.Background = _errorBrush;
        }
    }

    private void UpdateActivityVisibility()
    {
        NoActivityPanel.Visibility = _activityItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ActivityList.Visibility = _activityItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        LoadMessageTypes();
        RefreshStatistics();
    }

    private void ClearActivity_Click(object sender, RoutedEventArgs e)
    {
        _activityItems.Clear();
        _totalMessages = 0;
        _failedMessages = 0;
        RefreshStatistics();
        UpdateActivityVisibility();
    }

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

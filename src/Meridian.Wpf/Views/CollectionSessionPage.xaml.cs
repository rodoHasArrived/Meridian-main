using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Wpf.Services;
using CollectionSessionService = Meridian.Ui.Services.CollectionSessionService;

namespace Meridian.Wpf.Views;

public partial class CollectionSessionPage : Page
{
    private readonly NavigationService _navigationService;
    private readonly NotificationService _notificationService;
    private readonly CollectionSessionService _sessionService;

    public CollectionSessionPage(
        NavigationService navigationService,
        NotificationService notificationService)
    {
        InitializeComponent();

        _navigationService = navigationService;
        _notificationService = notificationService;
        _sessionService = CollectionSessionService.Instance;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await LoadSessionsAsync();
    }

    private async void RefreshSessions_Click(object sender, RoutedEventArgs e)
    {
        await LoadSessionsAsync();
    }

    private async void CreateDailySession_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var session = await _sessionService.CreateDailySessionAsync();
            _notificationService.NotifySuccess("Session Created", $"Daily session '{session.Name}' created.");
            await LoadSessionsAsync();
        }
        catch (Exception ex)
        {
            _notificationService.ShowNotification("Error", $"Failed to create session: {ex.Message}", NotificationType.Error);
        }
    }

    private async void PauseSession_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var activeSession = await _sessionService.GetActiveSessionAsync();
            if (activeSession == null) return;

            await _sessionService.PauseSessionAsync(activeSession.Id);
            _notificationService.NotifyInfo("Session Paused", $"Session '{activeSession.Name}' has been paused.");
            await LoadSessionsAsync();
        }
        catch (Exception ex)
        {
            _notificationService.ShowNotification("Error", $"Failed to pause session: {ex.Message}", NotificationType.Error);
        }
    }

    private async void StopSession_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var activeSession = await _sessionService.GetActiveSessionAsync();
            if (activeSession == null) return;

            await _sessionService.StopSessionAsync(activeSession.Id);
            _notificationService.NotifySuccess("Session Stopped", $"Session '{activeSession.Name}' has been completed.");
            await LoadSessionsAsync();
        }
        catch (Exception ex)
        {
            _notificationService.ShowNotification("Error", $"Failed to stop session: {ex.Message}", NotificationType.Error);
        }
    }

    private async System.Threading.Tasks.Task LoadSessionsAsync()
    {
        try
        {
            // Load active session
            var activeSession = await _sessionService.GetActiveSessionAsync();
            if (activeSession != null)
            {
                ActiveSessionPanel.Visibility = Visibility.Visible;
                NoActiveSessionText.Visibility = Visibility.Collapsed;

                ActiveSessionName.Text = activeSession.Name;
                ActiveSessionStatus.Text = activeSession.Status;

                if (activeSession.StartedAt.HasValue)
                {
                    var duration = DateTime.UtcNow - activeSession.StartedAt.Value;
                    ActiveSessionDuration.Text = $"Running for {FormatDuration(duration)}";
                }

                if (activeSession.Statistics != null)
                {
                    ActiveSessionEvents.Text = activeSession.Statistics.TotalEvents.ToString("N0");
                    ActiveSessionRate.Text = $"{activeSession.Statistics.EventsPerSecond:F1}/s";
                }
            }
            else
            {
                ActiveSessionPanel.Visibility = Visibility.Collapsed;
                NoActiveSessionText.Visibility = Visibility.Visible;
            }

            // Load session history
            var sessions = await _sessionService.GetSessionsAsync();
            var sortedSessions = sessions.OrderByDescending(s => s.CreatedAt).ToArray();

            if (sortedSessions.Length > 0)
            {
                SessionHistoryStatus.Text = $"{sortedSessions.Length} session(s) found";
                SessionHistoryList.ItemsSource = sortedSessions;

                // Show summary for latest completed session
                var latestCompleted = sortedSessions.FirstOrDefault(s => s.Status == "Completed");
                if (latestCompleted != null)
                {
                    SessionSummaryText.Text = _sessionService.GenerateSessionSummary(latestCompleted);
                }
            }
            else
            {
                SessionHistoryStatus.Text = "No sessions found. Create a daily session to get started.";
                SessionHistoryList.ItemsSource = null;
            }
        }
        catch (Exception ex)
        {
            SessionHistoryStatus.Text = $"Failed to load sessions: {ex.Message}";
            SessionHistoryStatus.Foreground = (Brush)FindResource("ErrorColorBrush");
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";
        if (duration.TotalMinutes >= 1)
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        return $"{duration.Seconds}s";
    }
}

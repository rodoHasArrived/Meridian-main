using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Meridian.Ui.Services;
using Meridian.Wpf.Services;
using NotificationService = Meridian.Wpf.Services.NotificationService;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Collection Session page. Exposes observable session state and
/// delegates all session lifecycle operations to <see cref="CollectionSessionService"/>.
/// </summary>
public sealed class CollectionSessionViewModel : BindableBase
{
    private readonly CollectionSessionService _sessionService;
    private readonly NotificationService _notificationService;

    private bool _hasActiveSession;
    private string _activeSessionName = string.Empty;
    private string _activeSessionStatus = string.Empty;
    private string _activeSessionDuration = string.Empty;
    private string _activeSessionEvents = string.Empty;
    private string _activeSessionRate = string.Empty;
    private string _sessionHistoryStatus = "Loading sessions...";
    private bool _hasSessionHistoryError;
    private IEnumerable? _sessions;
    private string _sessionSummaryText = "Select a completed session to view its summary.";

    public CollectionSessionViewModel(NotificationService notificationService)
    {
        _sessionService = CollectionSessionService.Instance;
        _notificationService = notificationService;
    }

    public bool HasActiveSession
    {
        get => _hasActiveSession;
        private set => SetProperty(ref _hasActiveSession, value);
    }

    public string ActiveSessionName
    {
        get => _activeSessionName;
        private set => SetProperty(ref _activeSessionName, value);
    }

    public string ActiveSessionStatus
    {
        get => _activeSessionStatus;
        private set => SetProperty(ref _activeSessionStatus, value);
    }

    public string ActiveSessionDuration
    {
        get => _activeSessionDuration;
        private set => SetProperty(ref _activeSessionDuration, value);
    }

    public string ActiveSessionEvents
    {
        get => _activeSessionEvents;
        private set => SetProperty(ref _activeSessionEvents, value);
    }

    public string ActiveSessionRate
    {
        get => _activeSessionRate;
        private set => SetProperty(ref _activeSessionRate, value);
    }

    public string SessionHistoryStatus
    {
        get => _sessionHistoryStatus;
        private set => SetProperty(ref _sessionHistoryStatus, value);
    }

    public bool HasSessionHistoryError
    {
        get => _hasSessionHistoryError;
        private set => SetProperty(ref _hasSessionHistoryError, value);
    }

    public IEnumerable? Sessions
    {
        get => _sessions;
        private set => SetProperty(ref _sessions, value);
    }

    public string SessionSummaryText
    {
        get => _sessionSummaryText;
        private set => SetProperty(ref _sessionSummaryText, value);
    }

    public async Task LoadSessionsAsync()
    {
        HasSessionHistoryError = false;
        try
        {
            var activeSession = await _sessionService.GetActiveSessionAsync();
            if (activeSession != null)
            {
                HasActiveSession = true;
                ActiveSessionName = activeSession.Name;
                ActiveSessionStatus = activeSession.Status;

                if (activeSession.StartedAt.HasValue)
                {
                    var duration = DateTime.UtcNow - activeSession.StartedAt.Value;
                    ActiveSessionDuration = $"Running for {FormatDuration(duration)}";
                }

                if (activeSession.Statistics != null)
                {
                    ActiveSessionEvents = activeSession.Statistics.TotalEvents.ToString("N0");
                    ActiveSessionRate = $"{activeSession.Statistics.EventsPerSecond:F1}/s";
                }
            }
            else
            {
                HasActiveSession = false;
            }

            var sessions = await _sessionService.GetSessionsAsync();
            var sortedSessions = sessions.OrderByDescending(s => s.CreatedAt).ToArray();

            if (sortedSessions.Length > 0)
            {
                SessionHistoryStatus = $"{sortedSessions.Length} session(s) found";
                Sessions = sortedSessions;

                var latestCompleted = sortedSessions.FirstOrDefault(s => s.Status == "Completed");
                if (latestCompleted != null)
                {
                    SessionSummaryText = _sessionService.GenerateSessionSummary(latestCompleted);
                }
            }
            else
            {
                SessionHistoryStatus = "No sessions found. Create a daily session to get started.";
                Sessions = null;
            }
        }
        catch (Exception ex)
        {
            SessionHistoryStatus = $"Failed to load sessions: {ex.Message}";
            HasSessionHistoryError = true;
        }
    }

    public async Task CreateDailySessionAsync()
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

    public async Task PauseSessionAsync()
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

    public async Task StopSessionAsync()
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

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";
        if (duration.TotalMinutes >= 1)
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        return $"{duration.Seconds}s";
    }
}

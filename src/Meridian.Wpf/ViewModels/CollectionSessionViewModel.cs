using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Session;
using Meridian.Ui.Services;
using Meridian.Wpf.Services;
using NotificationService = Meridian.Wpf.Services.NotificationService;

namespace Meridian.Wpf.ViewModels;

public interface ICollectionSessionClient
{
    Task<CollectionSession?> GetActiveSessionAsync(CancellationToken ct = default);

    Task<CollectionSession[]> GetSessionsAsync(CancellationToken ct = default);

    Task<CollectionSession> CreateDailySessionAsync(CancellationToken ct = default);

    Task PauseSessionAsync(string sessionId, CancellationToken ct = default);

    Task StopSessionAsync(string sessionId, bool generateManifest = true, CancellationToken ct = default);

    string GenerateSessionSummary(CollectionSession session);
}

public sealed class CollectionSessionServiceClient : ICollectionSessionClient
{
    private readonly CollectionSessionService _sessionService;

    public CollectionSessionServiceClient()
        : this(CollectionSessionService.Instance)
    {
    }

    public CollectionSessionServiceClient(CollectionSessionService sessionService)
    {
        _sessionService = sessionService;
    }

    public Task<CollectionSession?> GetActiveSessionAsync(CancellationToken ct = default)
        => _sessionService.GetActiveSessionAsync(ct);

    public Task<CollectionSession[]> GetSessionsAsync(CancellationToken ct = default)
        => _sessionService.GetSessionsAsync(ct);

    public Task<CollectionSession> CreateDailySessionAsync(CancellationToken ct = default)
        => _sessionService.CreateDailySessionAsync(ct);

    public Task PauseSessionAsync(string sessionId, CancellationToken ct = default)
        => _sessionService.PauseSessionAsync(sessionId, ct);

    public Task StopSessionAsync(string sessionId, bool generateManifest = true, CancellationToken ct = default)
        => _sessionService.StopSessionAsync(sessionId, generateManifest, ct);

    public string GenerateSessionSummary(CollectionSession session)
        => _sessionService.GenerateSessionSummary(session);
}

public interface ICollectionSessionNotifier
{
    void Success(string title, string message);

    void Info(string title, string message);

    void Error(string title, string message);
}

public sealed class CollectionSessionNotifier : ICollectionSessionNotifier
{
    private readonly NotificationService _notificationService;

    public CollectionSessionNotifier(NotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void Success(string title, string message) => _notificationService.NotifySuccess(title, message);

    public void Info(string title, string message) => _notificationService.NotifyInfo(title, message);

    public void Error(string title, string message)
        => _notificationService.ShowNotification(title, message, NotificationType.Error);
}

/// <summary>
/// ViewModel for the Collection Session page. Exposes observable session state and
/// delegates all session lifecycle operations to <see cref="CollectionSessionService"/>.
/// </summary>
public sealed class CollectionSessionViewModel : BindableBase
{
    private readonly ICollectionSessionClient _sessionClient;
    private readonly ICollectionSessionNotifier _notifier;

    private bool _hasActiveSession;
    private string _activeSessionName = string.Empty;
    private string _activeSessionStatus = string.Empty;
    private string _activeSessionDuration = string.Empty;
    private string _activeSessionEvents = string.Empty;
    private string _activeSessionRate = string.Empty;
    private string _sessionHistoryStatus = "Load sessions to inspect capture history.";
    private bool _hasSessionHistoryError;
    private string _sessionSummaryText = "Select a completed session to view its summary.";
    private string _activeSessionId = string.Empty;
    private string _sessionActionTitle = "Session state not loaded";
    private string _sessionActionDetail = "Refresh sessions before creating, pausing, or stopping collection work.";
    private string _sessionActionScope = "No session snapshot loaded";
    private bool _isBusy;
    private Visibility _loadingVisibility = Visibility.Collapsed;
    private Visibility _sessionHistoryVisibility = Visibility.Collapsed;
    private Visibility _emptySessionHistoryVisibility = Visibility.Visible;

    public CollectionSessionViewModel(NotificationService notificationService)
        : this(new CollectionSessionServiceClient(), new CollectionSessionNotifier(notificationService))
    {
    }

    public CollectionSessionViewModel(ICollectionSessionClient sessionClient, ICollectionSessionNotifier notifier)
    {
        _sessionClient = sessionClient ?? throw new ArgumentNullException(nameof(sessionClient));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));

        LoadSessionsCommand = new AsyncRelayCommand(() => LoadSessionsAsync(), CanRunSessionAction);
        RefreshSessionsCommand = new AsyncRelayCommand(() => LoadSessionsAsync(), CanRunSessionAction);
        CreateDailySessionCommand = new AsyncRelayCommand(() => CreateDailySessionAsync(), CanRunSessionAction);
        PauseSessionCommand = new AsyncRelayCommand(() => PauseSessionAsync(), CanRunActiveSessionAction);
        StopSessionCommand = new AsyncRelayCommand(() => StopSessionAsync(), CanRunActiveSessionAction);
    }

    public IAsyncRelayCommand LoadSessionsCommand { get; }

    public IAsyncRelayCommand RefreshSessionsCommand { get; }

    public IAsyncRelayCommand CreateDailySessionCommand { get; }

    public IAsyncRelayCommand PauseSessionCommand { get; }

    public IAsyncRelayCommand StopSessionCommand { get; }

    public ObservableCollection<CollectionSession> SessionItems { get; } = new();

    public bool HasActiveSession
    {
        get => _hasActiveSession;
        private set
        {
            if (SetProperty(ref _hasActiveSession, value))
            {
                RefreshCommandStates();
            }
        }
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
        get => SessionItems;
    }

    public string SessionSummaryText
    {
        get => _sessionSummaryText;
        private set => SetProperty(ref _sessionSummaryText, value);
    }

    public string SessionActionTitle
    {
        get => _sessionActionTitle;
        private set => SetProperty(ref _sessionActionTitle, value);
    }

    public string SessionActionDetail
    {
        get => _sessionActionDetail;
        private set => SetProperty(ref _sessionActionDetail, value);
    }

    public string SessionActionScope
    {
        get => _sessionActionScope;
        private set => SetProperty(ref _sessionActionScope, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                LoadingVisibility = value ? Visibility.Visible : Visibility.Collapsed;
                RefreshCommandStates();
                RefreshSessionActionState();
            }
        }
    }

    public Visibility LoadingVisibility
    {
        get => _loadingVisibility;
        private set => SetProperty(ref _loadingVisibility, value);
    }

    public Visibility SessionHistoryVisibility
    {
        get => _sessionHistoryVisibility;
        private set => SetProperty(ref _sessionHistoryVisibility, value);
    }

    public Visibility EmptySessionHistoryVisibility
    {
        get => _emptySessionHistoryVisibility;
        private set => SetProperty(ref _emptySessionHistoryVisibility, value);
    }

    public bool CanRunSessionAction() => !IsBusy;

    public bool CanRunActiveSessionAction() => !IsBusy && HasActiveSession && !string.IsNullOrWhiteSpace(_activeSessionId);

    public async Task LoadSessionsAsync()
    {
        IsBusy = true;
        HasSessionHistoryError = false;
        SessionHistoryStatus = "Loading collection sessions...";
        RefreshSessionActionState();

        try
        {
            var activeSession = await _sessionClient.GetActiveSessionAsync();
            if (activeSession != null)
            {
                _activeSessionId = activeSession.Id;
                HasActiveSession = true;
                ActiveSessionName = activeSession.Name;
                ActiveSessionStatus = activeSession.Status;
                ActiveSessionDuration = activeSession.StartedAt.HasValue
                    ? $"Running for {FormatDuration(DateTime.UtcNow - activeSession.StartedAt.Value)}"
                    : "Duration unavailable";
                ActiveSessionEvents = activeSession.Statistics?.TotalEvents.ToString("N0") ?? "0";
                ActiveSessionRate = activeSession.Statistics != null
                    ? $"{activeSession.Statistics.EventsPerSecond:F1}/s"
                    : "0.0/s";
            }
            else
            {
                _activeSessionId = string.Empty;
                HasActiveSession = false;
                ActiveSessionName = string.Empty;
                ActiveSessionStatus = string.Empty;
                ActiveSessionDuration = string.Empty;
                ActiveSessionEvents = "0";
                ActiveSessionRate = "0.0/s";
            }

            var sessions = await _sessionClient.GetSessionsAsync();
            var sortedSessions = sessions.OrderByDescending(s => s.CreatedAt).ToArray();
            SessionItems.Clear();
            foreach (var session in sortedSessions)
            {
                SessionItems.Add(session);
            }

            if (sortedSessions.Length > 0)
            {
                SessionHistoryStatus = $"{sortedSessions.Length} session(s) found";

                var latestCompleted = sortedSessions.FirstOrDefault(s => s.Status == "Completed");
                if (latestCompleted != null)
                {
                    SessionSummaryText = _sessionClient.GenerateSessionSummary(latestCompleted);
                }
                else
                {
                    SessionSummaryText = "No completed sessions are available for summary yet.";
                }
            }
            else
            {
                SessionHistoryStatus = "No sessions found. Create a daily session to get started.";
                SessionSummaryText = "No completed sessions are available for summary yet.";
            }
        }
        catch (Exception ex)
        {
            SessionHistoryStatus = $"Failed to load sessions: {ex.Message}";
            HasSessionHistoryError = true;
        }
        finally
        {
            IsBusy = false;
            RefreshSessionActionState();
            RefreshCommandStates();
        }
    }

    public async Task CreateDailySessionAsync()
    {
        if (!CanRunSessionAction())
        {
            return;
        }

        IsBusy = true;
        SessionHistoryStatus = "Creating daily collection session...";
        try
        {
            var session = await _sessionClient.CreateDailySessionAsync();
            _notifier.Success("Session Created", $"Daily session '{session.Name}' created.");
            await LoadSessionsAsync();
        }
        catch (Exception ex)
        {
            SessionHistoryStatus = $"Failed to create session: {ex.Message}";
            HasSessionHistoryError = true;
            _notifier.Error("Error", $"Failed to create session: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            RefreshSessionActionState();
        }
    }

    public async Task PauseSessionAsync()
    {
        if (!CanRunActiveSessionAction())
        {
            return;
        }

        IsBusy = true;
        SessionHistoryStatus = $"Pausing '{ActiveSessionName}'...";
        try
        {
            var sessionName = ActiveSessionName;
            await _sessionClient.PauseSessionAsync(_activeSessionId);
            _notifier.Info("Session Paused", $"Session '{sessionName}' has been paused.");
            await LoadSessionsAsync();
        }
        catch (Exception ex)
        {
            SessionHistoryStatus = $"Failed to pause session: {ex.Message}";
            HasSessionHistoryError = true;
            _notifier.Error("Error", $"Failed to pause session: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            RefreshSessionActionState();
        }
    }

    public async Task StopSessionAsync()
    {
        if (!CanRunActiveSessionAction())
        {
            return;
        }

        IsBusy = true;
        SessionHistoryStatus = $"Stopping '{ActiveSessionName}'...";
        try
        {
            var sessionName = ActiveSessionName;
            await _sessionClient.StopSessionAsync(_activeSessionId);
            _notifier.Success("Session Stopped", $"Session '{sessionName}' has been completed.");
            await LoadSessionsAsync();
        }
        catch (Exception ex)
        {
            SessionHistoryStatus = $"Failed to stop session: {ex.Message}";
            HasSessionHistoryError = true;
            _notifier.Error("Error", $"Failed to stop session: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            RefreshSessionActionState();
        }
    }

    private void RefreshSessionActionState()
    {
        var count = SessionItems.Count;
        SessionHistoryVisibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptySessionHistoryVisibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (IsBusy)
        {
            SessionActionTitle = "Session action running";
            SessionActionDetail = SessionHistoryStatus;
            SessionActionScope = "Lifecycle controls are temporarily disabled.";
            return;
        }

        if (HasSessionHistoryError)
        {
            SessionActionTitle = "Session state needs attention";
            SessionActionDetail = SessionHistoryStatus;
            SessionActionScope = "Refresh to retry the collection-session snapshot.";
            return;
        }

        if (HasActiveSession)
        {
            SessionActionTitle = "Capture session active";
            SessionActionDetail = $"{ActiveSessionName} is {ActiveSessionStatus.ToLowerInvariant()} with pause and stop controls ready.";
            SessionActionScope = $"{ActiveSessionEvents} events captured at {ActiveSessionRate}; {ActiveSessionDuration}";
            return;
        }

        if (count == 0)
        {
            SessionActionTitle = "No collection sessions";
            SessionActionDetail = "Create a daily session to start tracking capture history and manifest quality.";
            SessionActionScope = "No active capture session is running.";
            return;
        }

        var latest = SessionItems.OrderByDescending(session => session.CreatedAt).First();
        SessionActionTitle = "Session history ready";
        SessionActionDetail = $"{count} session(s) are available for review. Create a new daily session when capture begins.";
        SessionActionScope = $"Latest: {latest.Name} ({latest.Status})";
    }

    private void RefreshCommandStates()
    {
        LoadSessionsCommand.NotifyCanExecuteChanged();
        RefreshSessionsCommand.NotifyCanExecuteChanged();
        CreateDailySessionCommand.NotifyCanExecuteChanged();
        PauseSessionCommand.NotifyCanExecuteChanged();
        StopSessionCommand.NotifyCanExecuteChanged();
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

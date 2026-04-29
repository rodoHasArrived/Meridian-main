using System.Windows;
using Meridian.Contracts.Session;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class CollectionSessionViewModelTests
{
    [Fact]
    public async Task LoadSessionsAsync_WithNoSessions_ProjectsEmptyStateAndCreateAction()
    {
        var client = new FakeCollectionSessionClient();
        var notifier = new FakeCollectionSessionNotifier();
        var viewModel = new CollectionSessionViewModel(client, notifier);

        await viewModel.LoadSessionsAsync();

        viewModel.HasActiveSession.Should().BeFalse();
        viewModel.SessionItems.Should().BeEmpty();
        viewModel.SessionHistoryStatus.Should().Be("No sessions found. Create a daily session to get started.");
        viewModel.SessionActionTitle.Should().Be("No collection sessions");
        viewModel.SessionActionDetail.Should().Contain("Create a daily session");
        viewModel.EmptySessionHistoryVisibility.Should().Be(Visibility.Visible);
        viewModel.SessionHistoryVisibility.Should().Be(Visibility.Collapsed);
        viewModel.CreateDailySessionCommand.CanExecute(null).Should().BeTrue();
        viewModel.PauseSessionCommand.CanExecute(null).Should().BeFalse();
        viewModel.StopSessionCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task LoadSessionsAsync_WithActiveSession_ProjectsLifecycleReadiness()
    {
        var active = new CollectionSession
        {
            Id = "active-1",
            Name = "2026-04-29-regular-hours",
            Status = SessionStatus.Active,
            StartedAt = DateTime.UtcNow.AddMinutes(-12),
            CreatedAt = DateTime.UtcNow.AddMinutes(-12),
            Statistics = new CollectionSessionStatistics
            {
                TotalEvents = 1200,
                EventsPerSecond = 48.5f
            }
        };
        var completed = new CollectionSession
        {
            Id = "completed-1",
            Name = "2026-04-28-regular-hours",
            Status = SessionStatus.Completed,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            Statistics = new CollectionSessionStatistics { TotalEvents = 900 }
        };
        var client = new FakeCollectionSessionClient
        {
            ActiveSession = active,
            Sessions = [completed, active],
            Summary = "Completed capture summary"
        };
        var viewModel = new CollectionSessionViewModel(client, new FakeCollectionSessionNotifier());

        await viewModel.LoadSessionsAsync();

        viewModel.HasActiveSession.Should().BeTrue();
        viewModel.ActiveSessionName.Should().Be(active.Name);
        viewModel.ActiveSessionStatus.Should().Be(SessionStatus.Active);
        viewModel.ActiveSessionEvents.Should().Be("1,200");
        viewModel.ActiveSessionRate.Should().Be("48.5/s");
        viewModel.SessionItems.Select(session => session.Name).Should().Equal(active.Name, completed.Name);
        viewModel.SessionActionTitle.Should().Be("Capture session active");
        viewModel.SessionActionScope.Should().Contain("1,200 events captured at 48.5/s");
        viewModel.SessionHistoryVisibility.Should().Be(Visibility.Visible);
        viewModel.PauseSessionCommand.CanExecute(null).Should().BeTrue();
        viewModel.StopSessionCommand.CanExecute(null).Should().BeTrue();
        viewModel.SessionSummaryText.Should().Be("Completed capture summary");
    }

    [Fact]
    public async Task CreateDailySessionAsync_CreatesSessionRefreshesStateAndNotifies()
    {
        var client = new FakeCollectionSessionClient();
        var notifier = new FakeCollectionSessionNotifier();
        var viewModel = new CollectionSessionViewModel(client, notifier);

        await viewModel.CreateDailySessionAsync();

        client.CreatedDailySessionCount.Should().Be(1);
        viewModel.SessionItems.Should().ContainSingle(session => session.Name == "2026-04-29-regular-hours");
        viewModel.SessionActionTitle.Should().Be("Session history ready");
        viewModel.SessionActionScope.Should().Contain("2026-04-29-regular-hours");
        notifier.Messages.Should().Contain("success:Session Created:Daily session '2026-04-29-regular-hours' created.");
    }

    [Fact]
    public async Task PauseSessionAsync_UsesLoadedActiveSessionAndRefreshes()
    {
        var active = new CollectionSession
        {
            Id = "active-1",
            Name = "daily-capture",
            Status = SessionStatus.Active,
            StartedAt = DateTime.UtcNow.AddMinutes(-5),
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            Statistics = new CollectionSessionStatistics { TotalEvents = 42, EventsPerSecond = 2.5f }
        };
        var client = new FakeCollectionSessionClient
        {
            ActiveSession = active,
            Sessions = [active]
        };
        var notifier = new FakeCollectionSessionNotifier();
        var viewModel = new CollectionSessionViewModel(client, notifier);
        await viewModel.LoadSessionsAsync();

        await viewModel.PauseSessionAsync();

        client.PausedSessionIds.Should().Equal("active-1");
        notifier.Messages.Should().Contain("info:Session Paused:Session 'daily-capture' has been paused.");
        viewModel.SessionHistoryStatus.Should().Be("1 session(s) found");
    }

    [Fact]
    public async Task LoadSessionsAsync_WhenServiceFails_ProjectsRecoverableErrorState()
    {
        var client = new FakeCollectionSessionClient { ThrowOnGetSessions = true };
        var viewModel = new CollectionSessionViewModel(client, new FakeCollectionSessionNotifier());

        await viewModel.LoadSessionsAsync();

        viewModel.HasSessionHistoryError.Should().BeTrue();
        viewModel.SessionHistoryStatus.Should().Be("Failed to load sessions: session store unavailable");
        viewModel.SessionActionTitle.Should().Be("Session state needs attention");
        viewModel.SessionActionScope.Should().Be("Refresh to retry the collection-session snapshot.");
        viewModel.RefreshSessionsCommand.CanExecute(null).Should().BeTrue();
        viewModel.EmptySessionHistoryVisibility.Should().Be(Visibility.Visible);
    }

    [Fact]
    public void CollectionSessionPageSource_BindsLifecycleActionsThroughViewModel()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\CollectionSessionPage.xaml"));
        var codeBehind = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\CollectionSessionPage.xaml.cs"));

        xaml.Should().Contain("CollectionSessionReadinessCard");
        xaml.Should().Contain("Command=\"{Binding CreateDailySessionCommand}\"");
        xaml.Should().Contain("Command=\"{Binding RefreshSessionsCommand}\"");
        xaml.Should().Contain("Command=\"{Binding PauseSessionCommand}\"");
        xaml.Should().Contain("Command=\"{Binding StopSessionCommand}\"");
        xaml.Should().Contain("{Binding LoadingVisibility}");
        xaml.Should().Contain("{Binding EmptySessionHistoryVisibility}");
        xaml.Should().Contain("{Binding SessionHistoryVisibility}");
        xaml.Should().NotContain("Click=\"RefreshSessions_Click\"");
        xaml.Should().NotContain("Click=\"CreateDailySession_Click\"");
        xaml.Should().NotContain("Click=\"PauseSession_Click\"");
        xaml.Should().NotContain("Click=\"StopSession_Click\"");

        codeBehind.Should().Contain("_viewModel.LoadSessionsAsync");
        codeBehind.Should().NotContain("private async void RefreshSessions_Click");
        codeBehind.Should().NotContain("private async void CreateDailySession_Click");
        codeBehind.Should().NotContain("private async void PauseSession_Click");
        codeBehind.Should().NotContain("private async void StopSession_Click");
    }

    private sealed class FakeCollectionSessionClient : ICollectionSessionClient
    {
        public CollectionSession? ActiveSession { get; set; }

        public CollectionSession[] Sessions { get; set; } = [];

        public string Summary { get; set; } = "Generated session summary";

        public bool ThrowOnGetSessions { get; set; }

        public int CreatedDailySessionCount { get; private set; }

        public List<string> PausedSessionIds { get; } = [];

        public List<string> StoppedSessionIds { get; } = [];

        public Task<CollectionSession?> GetActiveSessionAsync(CancellationToken ct = default)
            => Task.FromResult(ActiveSession);

        public Task<CollectionSession[]> GetSessionsAsync(CancellationToken ct = default)
        {
            if (ThrowOnGetSessions)
            {
                throw new InvalidOperationException("session store unavailable");
            }

            return Task.FromResult(Sessions);
        }

        public Task<CollectionSession> CreateDailySessionAsync(CancellationToken ct = default)
        {
            CreatedDailySessionCount++;
            var session = new CollectionSession
            {
                Id = $"daily-{CreatedDailySessionCount}",
                Name = "2026-04-29-regular-hours",
                Status = SessionStatus.Pending,
                CreatedAt = new DateTime(2026, 4, 29, 14, 0, 0, DateTimeKind.Utc),
                Statistics = new CollectionSessionStatistics()
            };
            Sessions = Sessions.Append(session).ToArray();
            return Task.FromResult(session);
        }

        public Task PauseSessionAsync(string sessionId, CancellationToken ct = default)
        {
            PausedSessionIds.Add(sessionId);
            if (ActiveSession?.Id == sessionId)
            {
                ActiveSession.Status = SessionStatus.Paused;
            }

            return Task.CompletedTask;
        }

        public Task StopSessionAsync(string sessionId, bool generateManifest = true, CancellationToken ct = default)
        {
            StoppedSessionIds.Add(sessionId);
            if (ActiveSession?.Id == sessionId)
            {
                ActiveSession.Status = SessionStatus.Completed;
                ActiveSession = null;
            }

            return Task.CompletedTask;
        }

        public string GenerateSessionSummary(CollectionSession session) => Summary;
    }

    private sealed class FakeCollectionSessionNotifier : ICollectionSessionNotifier
    {
        public List<string> Messages { get; } = [];

        public void Success(string title, string message) => Messages.Add($"success:{title}:{message}");

        public void Info(string title, string message) => Messages.Add($"info:{title}:{message}");

        public void Error(string title, string message) => Messages.Add($"error:{title}:{message}");
    }
}

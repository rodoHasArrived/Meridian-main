using System.Windows;
using FluentAssertions;
using Meridian.Ui.Services.Services;
using Meridian.Contracts.Lifecycle;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

[Collection("DesktopAuthenticationEnvironment")]
public sealed class StartupWindowViewModelTests
{
    [Fact]
    public async Task SignInCommand_WithValidCredentials_ShouldCompleteStartupAndClearPassword()
    {
        using var env = new DesktopAuthenticationSessionTests.EnvironmentVariableScope()
            .Set("MDC_USERS", DesktopAuthenticationSessionTests.HashedDesktopAdminUsersJson())
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", null);
        var session = DesktopAuthenticationSessionTests.CreateSession("Production");
        var viewModel = CreateViewModel(session, "Production");
        var completed = false;
        var resetRequested = false;
        viewModel.StartupCompleted += (_, _) => completed = true;
        viewModel.PasswordResetRequested += (_, _) => resetRequested = true;

        viewModel.SignInCommand.CanExecute(null).Should().BeFalse();
        viewModel.Username = "desktop-admin";
        viewModel.Password = "pw";

        viewModel.SignInCommand.CanExecute(null).Should().BeTrue();
        await viewModel.SignInCommand.ExecuteAsync(null);

        completed.Should().BeTrue();
        resetRequested.Should().BeTrue();
        session.CurrentActor.Should().Be("desktop-admin");
        viewModel.Password.Should().BeEmpty();
        viewModel.StatusVisibility.Should().Be(Visibility.Visible);
        viewModel.StatusText.Should().Contain("Signed in");
    }

    [Fact]
    public async Task SignInCommand_WithInvalidCredentials_ShouldShowErrorAndKeepStartupOpen()
    {
        using var env = new DesktopAuthenticationSessionTests.EnvironmentVariableScope()
            .Set("MDC_USERS", DesktopAuthenticationSessionTests.HashedDesktopAdminUsersJson())
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", null);
        var session = DesktopAuthenticationSessionTests.CreateSession("Production");
        var viewModel = CreateViewModel(session, "Production");
        var completed = false;
        viewModel.StartupCompleted += (_, _) => completed = true;

        viewModel.Username = "desktop-admin";
        viewModel.Password = "wrong";
        await viewModel.SignInCommand.ExecuteAsync(null);

        completed.Should().BeFalse();
        session.IsAuthenticated.Should().BeFalse();
        viewModel.Password.Should().BeEmpty();
        viewModel.SignInCommand.CanExecute(null).Should().BeFalse();
        viewModel.StatusVisibility.Should().Be(Visibility.Visible);
        viewModel.StatusText.Should().Contain("does not match");
    }

    [Fact]
    public void ContinueWithoutCredentialsCommand_WhenDevelopmentUnconfigured_ShouldCompleteStartup()
    {
        using var env = new DesktopAuthenticationSessionTests.EnvironmentVariableScope()
            .Set("MDC_USERS", null)
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", "optional");
        var session = DesktopAuthenticationSessionTests.CreateSession("Development");
        var viewModel = CreateViewModel(session, "Development");
        var completed = false;
        viewModel.StartupCompleted += (_, _) => completed = true;

        viewModel.LoginFormVisibility.Should().Be(Visibility.Collapsed);
        viewModel.MissingConfigurationVisibility.Should().Be(Visibility.Visible);
        viewModel.ContinueWithoutCredentialsVisibility.Should().Be(Visibility.Visible);
        viewModel.ContinueWithoutCredentialsCommand.Execute(null);

        completed.Should().BeTrue();
        session.IsAnonymousDevelopmentSession.Should().BeTrue();
        viewModel.StatusText.Should().Contain("Continuing without credentials");
    }

    [Fact]
    public async Task RefreshLifecycleAsync_GatesSignInUntilHostAcceptsWork()
    {
        using var env = new DesktopAuthenticationSessionTests.EnvironmentVariableScope()
            .Set("MDC_USERS", DesktopAuthenticationSessionTests.HashedDesktopAdminUsersJson())
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", null);
        var session = DesktopAuthenticationSessionTests.CreateSession("Production");
        var lifecycleClient = new StubLifecycleControlClient(NotReadySnapshot());
        var viewModel = new StartupWindowViewModel(
            session,
            FixtureModeDetector.Instance,
            new DesktopAuthenticationSessionTests.FakeHostEnvironment("Production"),
            lifecycleClient)
        {
            Username = "desktop-admin",
            Password = "pw"
        };

        viewModel.SignInCommand.CanExecute(null).Should().BeFalse();
        await viewModel.RefreshLifecycleAsync();
        viewModel.SignInCommand.CanExecute(null).Should().BeFalse();
        viewModel.LifecycleHeadingText.Should().Be("Runtime lifecycle: Starting");

        lifecycleClient.Snapshot = ReadySnapshot();
        await viewModel.RefreshLifecycleAsync();

        viewModel.SignInCommand.CanExecute(null).Should().BeTrue();
        viewModel.LifecycleHeadingText.Should().Be("Runtime lifecycle: Ready");
        viewModel.LifecycleDetailText.Should().Contain("accepting work");
    }

    [Fact]
    public async Task SignInCommand_WhenHostAuthenticationRejectsCredentials_FailsClosed()
    {
        using var env = new DesktopAuthenticationSessionTests.EnvironmentVariableScope()
            .Set("MDC_USERS", DesktopAuthenticationSessionTests.HashedDesktopAdminUsersJson())
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", null);
        var session = DesktopAuthenticationSessionTests.CreateSession("Production");
        var lifecycleClient = new StubLifecycleControlClient(ReadySnapshot())
        {
            AuthenticationAccepted = false
        };
        var viewModel = new StartupWindowViewModel(
            session,
            FixtureModeDetector.Instance,
            new DesktopAuthenticationSessionTests.FakeHostEnvironment("Production"),
            lifecycleClient)
        {
            Username = "desktop-admin",
            Password = "pw"
        };

        await viewModel.RefreshLifecycleAsync();
        await viewModel.SignInCommand.ExecuteAsync(null);

        session.IsAuthenticated.Should().BeFalse();
        viewModel.StatusText.Should().Contain("host rejected");
        viewModel.Password.Should().BeEmpty();
    }

    private static RuntimeLifecycleSnapshotDto NotReadySnapshot() => new()
    {
        SessionId = "startup-session",
        State = RuntimeLifecycleState.EvaluatingReadiness,
        Readiness = RuntimeReadinessStatus.Starting,
        StartedAtUtc = DateTimeOffset.UtcNow,
        StateChangedAtUtc = DateTimeOffset.UtcNow,
        ActivePhase = "Evaluating database readiness",
        AcceptingWork = false,
        ShutdownRequested = false,
        UptimeSeconds = 2
    };

    private static RuntimeLifecycleSnapshotDto ReadySnapshot()
        => NotReadySnapshot() with
        {
            State = RuntimeLifecycleState.Ready,
            Readiness = RuntimeReadinessStatus.Ready,
            ActivePhase = "Serving",
            AcceptingWork = true,
            UptimeSeconds = 8
        };

    private sealed class StubLifecycleControlClient : ILifecycleControlClient
    {
        public StubLifecycleControlClient(RuntimeLifecycleSnapshotDto snapshot)
        {
            Snapshot = snapshot;
        }

        public RuntimeLifecycleSnapshotDto Snapshot { get; set; }

        public bool AuthenticationAccepted { get; set; } = true;

        public Task<RuntimeLifecycleSnapshotDto?> GetStartupSnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<RuntimeLifecycleSnapshotDto?>(Snapshot);

        public Task<bool> AuthenticateAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AuthenticationAccepted);

        public Task<RuntimeLifecycleSnapshotDto?> GetSnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<RuntimeLifecycleSnapshotDto?>(Snapshot);

        public Task<LifecycleShutdownReceiptDto?> GetLatestReceiptAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<LifecycleShutdownReceiptDto?>(null);

        public Task<LifecycleShutdownAcceptedDto?> RequestShutdownAsync(
            LifecycleShutdownRequestDto request,
            CancellationToken cancellationToken = default)
            => Task.FromResult<LifecycleShutdownAcceptedDto?>(null);
    }

    private static StartupWindowViewModel CreateViewModel(DesktopAuthenticationSession session, string environmentName)
        => new(
            session,
            FixtureModeDetector.Instance,
            new DesktopAuthenticationSessionTests.FakeHostEnvironment(environmentName));
}

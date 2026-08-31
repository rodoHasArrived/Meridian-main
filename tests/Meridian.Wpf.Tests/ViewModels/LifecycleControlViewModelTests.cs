using System.Windows;
using System.Net.Http;
using Meridian.Contracts.Lifecycle;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class LifecycleControlViewModelTests
{
    [Fact]
    public async Task RefreshAsync_ProjectsRuntimeChecksAndLatestReceipt()
    {
        var client = new StubLifecycleControlClient
        {
            Snapshot = ReadySnapshot(),
            Receipt = ShutdownReceipt()
        };
        var viewModel = new LifecycleControlViewModel(client);

        await viewModel.RefreshAsync();

        viewModel.ReadinessText.Should().Be("Ready");
        viewModel.StateText.Should().Be("Ready");
        viewModel.PhaseText.Should().Be("Serving");
        viewModel.UptimeText.Should().Be("1h 2m");
        viewModel.Checks.Should().ContainSingle();
        viewModel.Checks[0].DisplayName.Should().Be("Dedicated database");
        viewModel.LatestReceiptText.Should().Contain("Succeeded");
        viewModel.BeginRestartCommand.CanExecute(null).Should().BeTrue();
        viewModel.BeginShutdownCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task ConfirmActionCommand_SubmitsTypedRestartAndDisablesNewLifecycleActions()
    {
        var client = new StubLifecycleControlClient
        {
            Snapshot = ReadySnapshot(),
            Accepted = new LifecycleShutdownAcceptedDto
            {
                Accepted = true,
                OperationId = "operation-1234567890",
                OperationUri = "/api/system/shutdown/operation-1234567890",
                State = RuntimeLifecycleState.ShutdownRequested,
                RequestedAtUtc = DateTimeOffset.UtcNow
            }
        };
        var viewModel = new LifecycleControlViewModel(client);
        await viewModel.RefreshAsync();

        viewModel.BeginRestartCommand.Execute(null);

        viewModel.ConfirmationVisibility.Should().Be(Visibility.Visible);
        viewModel.ConfirmationTitle.Should().Be("Restart Meridian?");
        await viewModel.ConfirmActionCommand.ExecuteAsync(null);

        client.LastRequest.Should().NotBeNull();
        client.LastRequest!.Reason.Should().Be(LifecycleShutdownReason.Restart);
        client.LastRequest.RequestedBy.Should().Be("wpf-workstation");
        viewModel.StatusText.Should().Contain("Restart accepted");
        viewModel.StateText.Should().Be("ShutdownRequested");
        viewModel.ConfirmationVisibility.Should().Be(Visibility.Collapsed);
        viewModel.BeginRestartCommand.CanExecute(null).Should().BeFalse();
        viewModel.BeginShutdownCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task RefreshAsync_WhenLifecycleIsUnavailable_FailsClosedAndCanRecover()
    {
        var client = new StubLifecycleControlClient { SnapshotException = new HttpRequestException("offline") };
        var viewModel = new LifecycleControlViewModel(client);

        await viewModel.RefreshAsync();

        viewModel.ReadinessText.Should().Be("Unavailable");
        viewModel.StatusText.Should().Contain("offline");
        viewModel.BeginShutdownCommand.CanExecute(null).Should().BeFalse();

        client.SnapshotException = null;
        client.Snapshot = ReadySnapshot();
        await viewModel.RefreshAsync();

        viewModel.ReadinessText.Should().Be("Ready");
        viewModel.BeginShutdownCommand.CanExecute(null).Should().BeTrue();
    }

    private static RuntimeLifecycleSnapshotDto ReadySnapshot() => new()
    {
        SessionId = "session-0123456789abcdef",
        State = RuntimeLifecycleState.Ready,
        Readiness = RuntimeReadinessStatus.Ready,
        StartedAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
        StateChangedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
        ActivePhase = "Serving",
        AcceptingWork = true,
        ShutdownRequested = false,
        UptimeSeconds = 3723,
        Checks =
        [
            new RuntimeLifecycleCheckDto
            {
                Id = "database",
                DisplayName = "Dedicated database",
                Requirement = LifecycleCheckRequirement.Required,
                Status = LifecycleCheckStatus.Passing,
                Message = "PostgreSQL accepted the readiness probe.",
                CheckedAtUtc = DateTimeOffset.UtcNow,
                DurationMilliseconds = 8
            }
        ]
    };

    private static LifecycleShutdownReceiptDto ShutdownReceipt() => new()
    {
        SessionId = "previous-session",
        OperationId = "previous-operation",
        Reason = LifecycleShutdownReason.Operator,
        Outcome = LifecycleShutdownOutcome.Succeeded,
        StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2),
        CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
        ForcedTermination = false
    };

    private sealed class StubLifecycleControlClient : ILifecycleControlClient
    {
        public RuntimeLifecycleSnapshotDto? Snapshot { get; set; }

        public LifecycleShutdownReceiptDto? Receipt { get; set; }

        public LifecycleShutdownAcceptedDto? Accepted { get; set; }

        public Exception? SnapshotException { get; set; }

        public LifecycleShutdownRequestDto? LastRequest { get; private set; }

        public Task<RuntimeLifecycleSnapshotDto?> GetStartupSnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Snapshot);

        public Task<bool> AuthenticateAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<RuntimeLifecycleSnapshotDto?> GetSnapshotAsync(CancellationToken cancellationToken = default)
            => SnapshotException is null
                ? Task.FromResult(Snapshot)
                : Task.FromException<RuntimeLifecycleSnapshotDto?>(SnapshotException);

        public Task<LifecycleShutdownReceiptDto?> GetLatestReceiptAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Receipt);

        public Task<LifecycleShutdownAcceptedDto?> RequestShutdownAsync(
            LifecycleShutdownRequestDto request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(Accepted);
        }
    }
}

using FluentAssertions;
using Meridian.Contracts.Lifecycle;
using Xunit;

namespace Meridian.LifecycleSupervisor.Tests;

public sealed class LifecycleSupervisorRuntimeTests
{
    [Fact]
    public void ClassifyHostExit_ReceiptBackedOperatorShutdownIsExpectedStop()
    {
        var action = LifecycleSupervisorRuntime.ClassifyHostExit(
            Lifecycle(shutdownRequested: true, nameof(LifecycleShutdownReason.Operator)),
            Receipt(LifecycleShutdownReason.Operator));

        action.Should().Be(SupervisorAction.Stop);
    }

    [Fact]
    public void ClassifyHostExit_RestartReceiptRequestsNewSession()
    {
        var action = LifecycleSupervisorRuntime.ClassifyHostExit(
            Lifecycle(shutdownRequested: true, nameof(LifecycleShutdownReason.Restart)),
            Receipt(LifecycleShutdownReason.Restart));

        action.Should().Be(SupervisorAction.Restart);
    }

    [Fact]
    public void ClassifyHostExit_UnexpectedExitRemainsFailure()
    {
        var action = LifecycleSupervisorRuntime.ClassifyHostExit(
            Lifecycle(shutdownRequested: false, reason: null),
            receipt: null);

        action.Should().Be(SupervisorAction.HostExited);
    }

    [Theory]
    [InlineData(RuntimeLifecycleState.Degraded, RuntimeReadinessStatus.Degraded, true, false)]
    [InlineData(RuntimeLifecycleState.Ready, RuntimeReadinessStatus.Ready, false, false)]
    [InlineData(RuntimeLifecycleState.Ready, RuntimeReadinessStatus.Ready, true, true)]
    public void IsReadyForBrowser_RequiresExactReadyAndAcceptingWork(
        RuntimeLifecycleState state,
        RuntimeReadinessStatus readiness,
        bool acceptingWork,
        bool expected)
    {
        var lifecycle = new RuntimeLifecycleSnapshotDto
        {
            SessionId = "host-session",
            State = state,
            Readiness = readiness,
            StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            StateChangedAtUtc = DateTimeOffset.UtcNow,
            ActivePhase = readiness.ToString(),
            AcceptingWork = acceptingWork,
            ShutdownRequested = false
        };

        LifecycleSupervisorRuntime.IsReadyForBrowser(lifecycle).Should().Be(expected);
    }

    private static RuntimeLifecycleSnapshotDto Lifecycle(bool shutdownRequested, string? reason)
        => new()
        {
            SessionId = "host-session",
            State = shutdownRequested ? RuntimeLifecycleState.ShutdownRequested : RuntimeLifecycleState.Ready,
            Readiness = shutdownRequested ? RuntimeReadinessStatus.Stopping : RuntimeReadinessStatus.Ready,
            StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            StateChangedAtUtc = DateTimeOffset.UtcNow,
            ActivePhase = shutdownRequested ? "Stopping" : "Serving",
            AcceptingWork = !shutdownRequested,
            ShutdownRequested = shutdownRequested,
            ShutdownReason = reason
        };

    private static LifecycleShutdownReceiptDto Receipt(LifecycleShutdownReason reason)
        => new()
        {
            SessionId = "host-session",
            OperationId = "operation",
            Reason = reason,
            Outcome = LifecycleShutdownOutcome.Succeeded,
            StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
            CompletedAtUtc = DateTimeOffset.UtcNow,
            ForcedTermination = false
        };
}

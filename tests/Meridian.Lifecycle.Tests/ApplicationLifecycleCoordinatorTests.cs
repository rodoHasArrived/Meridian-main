using FluentAssertions;
using Meridian.Application.Composition.Startup;
using Meridian.Contracts.Lifecycle;
using Serilog;
using Xunit;

namespace Meridian.Tests.Application.Composition.Startup;

public sealed class ApplicationLifecycleCoordinatorTests
{
    private readonly ILogger _log = new LoggerConfiguration().CreateLogger();

    [Fact]
    public void Snapshot_InitializesCreatedAndNotAcceptingWork()
    {
        using var lifecycle = ApplicationLifecycleCoordinator.Create(_log);

        var snapshot = lifecycle.Snapshot;

        snapshot.SessionId.Should().NotBeNullOrWhiteSpace();
        snapshot.State.Should().Be(RuntimeLifecycleState.Created);
        snapshot.Readiness.Should().Be(RuntimeReadinessStatus.Starting);
        snapshot.AcceptingWork.Should().BeFalse();
        snapshot.ShutdownRequested.Should().BeFalse();
    }

    [Fact]
    public void UpdateReadiness_RequiredChecksPassing_MarksRuntimeReady()
    {
        using var lifecycle = ApplicationLifecycleCoordinator.Create(_log);
        var checks = new[]
        {
            CreateCheck("configuration", LifecycleCheckRequirement.Required, LifecycleCheckStatus.Passing)
        };

        lifecycle.UpdateReadiness(RuntimeReadinessStatus.Ready, checks);

        lifecycle.Snapshot.Should().BeEquivalentTo(new
        {
            State = RuntimeLifecycleState.Ready,
            Readiness = RuntimeReadinessStatus.Ready,
            AcceptingWork = true,
            Checks = checks
        });
    }

    [Fact]
    public async Task RequestShutdownAsync_DuplicateRequests_ReturnSameOperationAndSeparateTermination()
    {
        using var lifecycle = ApplicationLifecycleCoordinator.Create(_log);
        lifecycle.UpdateReadiness(
            RuntimeReadinessStatus.Ready,
            [CreateCheck("configuration", LifecycleCheckRequirement.Required, LifecycleCheckStatus.Passing)]);

        var first = await lifecycle.RequestShutdownAsync(new LifecycleShutdownRequestDto
        {
            Reason = LifecycleShutdownReason.Operator,
            RequestedBy = "test"
        });
        var duplicate = await lifecycle.RequestShutdownAsync(new LifecycleShutdownRequestDto
        {
            Reason = LifecycleShutdownReason.Restart,
            RequestedBy = "duplicate"
        });

        duplicate.OperationId.Should().Be(first.OperationId);
        lifecycle.StopWorkToken.IsCancellationRequested.Should().BeTrue();
        lifecycle.TerminationToken.IsCancellationRequested.Should().BeFalse();
        lifecycle.Snapshot.AcceptingWork.Should().BeFalse();
        lifecycle.Snapshot.State.Should().Be(RuntimeLifecycleState.ShutdownRequested);

        lifecycle.SignalTermination();
        lifecycle.TerminationToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteShutdown_RecordsMatchingReceipt()
    {
        using var lifecycle = ApplicationLifecycleCoordinator.Create(_log);
        var accepted = await lifecycle.RequestShutdownAsync(new LifecycleShutdownRequestDto
        {
            Reason = LifecycleShutdownReason.Supervisor
        });
        lifecycle.AdvanceShutdown(LifecycleShutdownStage.Draining);
        lifecycle.AdvanceShutdown(LifecycleShutdownStage.Flushing);
        var completedAt = DateTimeOffset.UtcNow;
        var receipt = new LifecycleShutdownReceiptDto
        {
            SessionId = lifecycle.Snapshot.SessionId,
            OperationId = accepted.OperationId,
            Reason = LifecycleShutdownReason.Supervisor,
            Outcome = LifecycleShutdownOutcome.Succeeded,
            StartedAtUtc = accepted.RequestedAtUtc,
            CompletedAtUtc = completedAt,
            ForcedTermination = false
        };

        lifecycle.CompleteShutdown(receipt);

        lifecycle.LatestShutdownReceipt.Should().Be(receipt);
        lifecycle.ActiveShutdownOperation.Should().BeEquivalentTo(new
        {
            CurrentStage = LifecycleShutdownStage.Completed,
            Outcome = LifecycleShutdownOutcome.Succeeded,
            CompletedAtUtc = (DateTimeOffset?)completedAt
        }, options => options.ExcludingMissingMembers());
    }

    private static RuntimeLifecycleCheckDto CreateCheck(
        string id,
        LifecycleCheckRequirement requirement,
        LifecycleCheckStatus status)
        => new()
        {
            Id = id,
            DisplayName = id,
            Requirement = requirement,
            Status = status,
            Message = status.ToString(),
            CheckedAtUtc = DateTimeOffset.UtcNow
        };
}

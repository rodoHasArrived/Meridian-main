using FluentAssertions;
using Meridian.Application.Coordination;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Meridian.Tests.Application.Coordination;

public sealed class ClusterCoordinatorServiceTests
{
    private static Mock<ILeaseManager> MakeManager(string instanceId = "inst-a", bool enabled = true)
    {
        var mock = new Mock<ILeaseManager>(MockBehavior.Strict);
        mock.Setup(m => m.Enabled).Returns(enabled);
        mock.Setup(m => m.InstanceId).Returns(instanceId);
        return mock;
    }

    [Fact]
    public async Task TryBecomeLeaderAsync_WhenDisabled_ReturnsFalse()
    {
        var manager = MakeManager(enabled: false);
        var svc = new ClusterCoordinatorService(manager.Object, NullLogger<ClusterCoordinatorService>.Instance);

        var result = await svc.TryBecomeLeaderAsync();

        result.Should().BeFalse();
        svc.IsLeader.Should().BeFalse();
    }

    [Fact]
    public async Task TryBecomeLeaderAsync_WhenLeaseAcquired_BecomesLeader()
    {
        var manager = MakeManager("inst-a");
        manager.Setup(m => m.HoldsLease(ClusterCoordinatorService.CoordinatorLeaseId)).Returns(false);
        manager.Setup(m => m.TryAcquireAsync(
                ClusterCoordinatorService.CoordinatorLeaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeaseAcquireResult(
                Acquired: true,
                TakenOver: false,
                Lease: new LeaseRecord(
                    ClusterCoordinatorService.CoordinatorLeaseId,
                    "inst-a", 1,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow.AddSeconds(30),
                    DateTimeOffset.UtcNow),
                CurrentOwner: null,
                CurrentExpiryUtc: null,
                Reason: null));

        var svc = new ClusterCoordinatorService(manager.Object, NullLogger<ClusterCoordinatorService>.Instance);

        var result = await svc.TryBecomeLeaderAsync();

        result.Should().BeTrue();
        svc.IsLeader.Should().BeTrue();
    }

    [Fact]
    public async Task TryBecomeLeaderAsync_WhenLeaseConflict_RemainsFollower()
    {
        var manager = MakeManager("inst-a");
        manager.Setup(m => m.HoldsLease(ClusterCoordinatorService.CoordinatorLeaseId)).Returns(false);
        manager.Setup(m => m.TryAcquireAsync(
                ClusterCoordinatorService.CoordinatorLeaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeaseAcquireResult(
                Acquired: false,
                TakenOver: false,
                Lease: null,
                CurrentOwner: "inst-b",
                CurrentExpiryUtc: DateTimeOffset.UtcNow.AddSeconds(25),
                Reason: "Held by another instance"));

        var svc = new ClusterCoordinatorService(manager.Object, NullLogger<ClusterCoordinatorService>.Instance);

        var result = await svc.TryBecomeLeaderAsync();

        result.Should().BeFalse();
        svc.IsLeader.Should().BeFalse();
    }

    [Fact]
    public async Task TryBecomeLeaderAsync_WhenAlreadyHoldsLease_SkipsAcquireCall()
    {
        var manager = MakeManager("inst-a");
        manager.Setup(m => m.HoldsLease(ClusterCoordinatorService.CoordinatorLeaseId)).Returns(true);
        // TryAcquireAsync should NOT be called when the lease is already held.

        var svc = new ClusterCoordinatorService(manager.Object, NullLogger<ClusterCoordinatorService>.Instance);

        var result = await svc.TryBecomeLeaderAsync();

        result.Should().BeTrue();
        svc.IsLeader.Should().BeTrue();
        manager.Verify(
            m => m.TryAcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task StepDownAsync_WhenLeader_ReleasesLeaseAndBecomesFollower()
    {
        var manager = MakeManager("inst-a");
        // First acquire the lease.
        manager.Setup(m => m.HoldsLease(ClusterCoordinatorService.CoordinatorLeaseId))
            .Returns(false);
        manager.Setup(m => m.TryAcquireAsync(
                ClusterCoordinatorService.CoordinatorLeaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeaseAcquireResult(
                Acquired: true, TakenOver: false,
                Lease: new LeaseRecord(
                    ClusterCoordinatorService.CoordinatorLeaseId,
                    "inst-a", 1,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow.AddSeconds(30),
                    DateTimeOffset.UtcNow),
                CurrentOwner: null, CurrentExpiryUtc: null, Reason: null));
        manager.Setup(m => m.ReleaseAsync(
                ClusterCoordinatorService.CoordinatorLeaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var svc = new ClusterCoordinatorService(manager.Object, NullLogger<ClusterCoordinatorService>.Instance);
        await svc.TryBecomeLeaderAsync();
        svc.IsLeader.Should().BeTrue();

        // Re-setup HoldsLease for the StepDown path.
        manager.Setup(m => m.HoldsLease(ClusterCoordinatorService.CoordinatorLeaseId)).Returns(true);

        await svc.StepDownAsync();

        svc.IsLeader.Should().BeFalse();
        manager.Verify(
            m => m.ReleaseAsync(
                ClusterCoordinatorService.CoordinatorLeaseId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LeadershipChanged_Event_FiresOnTransition()
    {
        var manager = MakeManager("inst-a");
        manager.Setup(m => m.HoldsLease(ClusterCoordinatorService.CoordinatorLeaseId)).Returns(false);
        manager.Setup(m => m.TryAcquireAsync(
                ClusterCoordinatorService.CoordinatorLeaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeaseAcquireResult(
                Acquired: true, TakenOver: false,
                Lease: new LeaseRecord(
                    ClusterCoordinatorService.CoordinatorLeaseId,
                    "inst-a", 1,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow.AddSeconds(30),
                    DateTimeOffset.UtcNow),
                CurrentOwner: null, CurrentExpiryUtc: null, Reason: null));

        var svc = new ClusterCoordinatorService(manager.Object, NullLogger<ClusterCoordinatorService>.Instance);

        LeadershipChangedEventArgs? captured = null;
        svc.LeadershipChanged += (_, args) => captured = args;

        await svc.TryBecomeLeaderAsync();

        captured.Should().NotBeNull();
        captured!.IsLeader.Should().BeTrue();
        captured.InstanceId.Should().Be("inst-a");
    }

    [Fact]
    public void InstanceId_DelegatestoLeaseManager()
    {
        var manager = MakeManager("my-node-42");
        var svc = new ClusterCoordinatorService(manager.Object, NullLogger<ClusterCoordinatorService>.Instance);

        svc.InstanceId.Should().Be("my-node-42");
    }
}

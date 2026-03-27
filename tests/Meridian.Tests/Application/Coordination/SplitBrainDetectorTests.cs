using FluentAssertions;
using Meridian.Application.Coordination;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Meridian.Tests.Application.Coordination;

public sealed class SplitBrainDetectorTests
{
    private static LeaseRecord MakeLease(string resourceId, string instanceId, bool expired = false) =>
        new(resourceId, instanceId, 1,
            DateTimeOffset.UtcNow.AddSeconds(-10),
            expired
                ? DateTimeOffset.UtcNow.AddSeconds(-5)   // already expired
                : DateTimeOffset.UtcNow.AddSeconds(25),  // still valid
            DateTimeOffset.UtcNow.AddSeconds(-5));

    private static (SplitBrainDetector Detector, Mock<ICoordinationStore> Store, Mock<IClusterCoordinator> Coordinator)
        Build(string instanceId = "inst-a", bool isLeader = false)
    {
        var store = new Mock<ICoordinationStore>(MockBehavior.Strict);
        var coordinator = new Mock<IClusterCoordinator>(MockBehavior.Strict);
        coordinator.Setup(c => c.InstanceId).Returns(instanceId);
        coordinator.Setup(c => c.IsLeader).Returns(isLeader);

        var detector = new SplitBrainDetector(
            store.Object,
            coordinator.Object,
            NullLogger<SplitBrainDetector>.Instance);

        return (detector, store, coordinator);
    }

    [Fact]
    public async Task DetectAndHeal_SingleCoordinatorLease_DoesNotStepDown()
    {
        var (detector, store, coordinator) = Build("inst-a", isLeader: true);

        store.Setup(s => s.GetAllLeasesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                MakeLease(ClusterCoordinatorService.CoordinatorLeaseId, "inst-a"),
            });

        // No step-down should occur with a single healthy coordinator lease.
        await detector.DetectAndHealAsync(CancellationToken.None);

        coordinator.Verify(c => c.StepDownAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DetectAndHeal_NoCoordinatorLeases_DoesNotStepDown()
    {
        var (detector, store, coordinator) = Build("inst-a", isLeader: false);

        store.Setup(s => s.GetAllLeasesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<LeaseRecord>());

        await detector.DetectAndHealAsync(CancellationToken.None);

        coordinator.Verify(c => c.StepDownAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DetectAndHeal_TwoCoordinatorLeases_LowerInstanceIdYields()
    {
        // "inst-a" < "inst-b" lexicographically → inst-a should yield
        var (detector, store, coordinator) = Build("inst-a", isLeader: true);

        store.Setup(s => s.GetAllLeasesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                MakeLease(ClusterCoordinatorService.CoordinatorLeaseId, "inst-a"),
                MakeLease(ClusterCoordinatorService.CoordinatorLeaseId, "inst-b"),
            });
        coordinator.Setup(c => c.StepDownAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await detector.DetectAndHealAsync(CancellationToken.None);

        coordinator.Verify(c => c.StepDownAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DetectAndHeal_TwoCoordinatorLeases_HigherInstanceIdDoesNotYield()
    {
        // "inst-b" > "inst-a" → inst-b should NOT yield (inst-a does)
        var (detector, store, coordinator) = Build("inst-b", isLeader: true);

        store.Setup(s => s.GetAllLeasesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                MakeLease(ClusterCoordinatorService.CoordinatorLeaseId, "inst-a"),
                MakeLease(ClusterCoordinatorService.CoordinatorLeaseId, "inst-b"),
            });

        await detector.DetectAndHealAsync(CancellationToken.None);

        coordinator.Verify(c => c.StepDownAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DetectAndHeal_ExpiredCoordinatorLeases_AreIgnored()
    {
        // One expired coordinator lease + one valid for this instance → no split brain
        var (detector, store, coordinator) = Build("inst-a", isLeader: true);

        store.Setup(s => s.GetAllLeasesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                MakeLease(ClusterCoordinatorService.CoordinatorLeaseId, "inst-a"),
                MakeLease(ClusterCoordinatorService.CoordinatorLeaseId, "inst-b", expired: true),
            });

        await detector.DetectAndHealAsync(CancellationToken.None);

        coordinator.Verify(c => c.StepDownAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DetectAndHeal_SplitBrainButNotLeader_DoesNotStepDown()
    {
        // This instance thinks it is NOT the leader — even during split-brain, it shouldn't yield
        // (the actual leader instance will handle this).
        var (detector, store, coordinator) = Build("inst-a", isLeader: false);

        store.Setup(s => s.GetAllLeasesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                MakeLease(ClusterCoordinatorService.CoordinatorLeaseId, "inst-a"),
                MakeLease(ClusterCoordinatorService.CoordinatorLeaseId, "inst-b"),
            });

        await detector.DetectAndHealAsync(CancellationToken.None);

        coordinator.Verify(c => c.StepDownAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DetectAndHeal_SymbolLeaseWithSingleInstance_DoesNotWarn()
    {
        // Single-owner symbol leases are normal — no double-subscription warning.
        var (detector, store, coordinator) = Build("inst-a", isLeader: true);

        store.Setup(s => s.GetAllLeasesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                MakeLease(ClusterCoordinatorService.CoordinatorLeaseId, "inst-a"),
                MakeLease("symbols/polygon/trades/SPY", "inst-a"),
                MakeLease("symbols/polygon/trades/AAPL", "inst-a"),
            });

        // Should complete without throwing (double-subscription check is a log-only side-effect).
        await detector.DetectAndHealAsync(CancellationToken.None);
    }
}

using Meridian.Contracts.Coordination;
using Meridian.Core.Config;
using Meridian.Platform.Coordination;
using Meridian.Storage.Coordination;
using Moq;

namespace Meridian.Tests.Platform.Coordination;

/// <summary>Guards ETL side effects against duplicate starts, transfer races, and host shutdown.</summary>
[Trait("Category", "Integration")]
public sealed class ExecutionLeaseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"meridian-execution-lease-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_ExcludesAcquisition_AndCancellingContenderDoesNotCancelOwner(bool sharedMode)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var config = Config(sharedMode);
        var store = new SharedStorageCoordinationStore(config, _root);
        await using var manager = new LeaseManager(config, store);
        await using var contender = new LeaseManager(config with { InstanceId = "successor" }, store);
        var acquired = await manager.TryAcquireExecutionAsync("jobs/etl/import", timeout.Token);
        await using var lease = Assert.IsAssignableFrom<IExecutionLease>(acquired.Lease);
        var snapshot = await manager.GetSnapshotAsync(timeout.Token);
        Assert.Equal(1, snapshot.JobLeaseCount);
        Assert.Equal(lease.ResourceId, Assert.Single(snapshot.HeldLeases).ResourceId);
        Assert.False((await manager.TryAcquireExecutionAsync(lease.ResourceId, timeout.Token)).Acquired);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var action = lease.ExecuteAsync(async ct =>
        {
            entered.SetResult();
            await resume.Task.WaitAsync(ct);
        }, timeout.Token);
        try
        {
            await entered.Task.WaitAsync(timeout.Token);
            using var contenderTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            var acquisition = contender.TryAcquireExecutionAsync(lease.ResourceId, contenderTimeout.Token);
            Assert.False(acquisition.IsCompleted, "Acquisition must wait for the action's transfer lock.");
            contenderTimeout.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acquisition);
            Assert.False(action.IsCompleted);
        }
        finally
        {
            resume.TrySetResult();
            await action;
        }
        await lease.DisposeAsync();
        var next = await contender.TryAcquireExecutionAsync(lease.ResourceId, timeout.Token);
        await using var nextLease = Assert.IsAssignableFrom<IExecutionLease>(next.Lease);
        var calls = 0;
        await nextLease.ExecuteAsync(_ => { calls++; return Task.CompletedTask; }, timeout.Token);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ExecuteAsync_AfterTransfer_RejectsStaleActionAndPreservesSuccessorOnDisposal()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var config = Config(true);
        var store = new SharedStorageCoordinationStore(config, _root);
        await using var manager = new LeaseManager(config, store);
        var acquired = await manager.TryAcquireExecutionAsync("jobs/etl/import", timeout.Token);
        await using var stale = Assert.IsAssignableFrom<IExecutionLease>(acquired.Lease);
        var retained = (await store.GetLeaseAsync(stale.ResourceId, timeout.Token))!;
        Assert.True(await store.RenewLeaseAsync(stale.ResourceId, retained.InstanceId, TimeSpan.FromSeconds(-1), timeout.Token));
        var next = await manager.TryAcquireExecutionAsync(stale.ResourceId, timeout.Token);
        await using var successor = Assert.IsAssignableFrom<IExecutionLease>(next.Lease);
        var nextOwner = (await store.GetLeaseAsync(stale.ResourceId, timeout.Token))!;
        var calls = 0;
        await Assert.ThrowsAsync<ExecutionLeaseLostException>(() => stale.ExecuteAsync(
            _ => { calls++; return Task.CompletedTask; }, timeout.Token));
        Assert.Equal(0, calls);
        await stale.DisposeAsync();
        Assert.Equal(nextOwner, await store.GetLeaseAsync(successor.ResourceId, timeout.Token));
        await successor.ExecuteAsync(_ => { calls++; return Task.CompletedTask; }, timeout.Token);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DisposeAsync_ManagerShutdown_CancelsActiveActionAndReleasesRunOwnership()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var config = Config(true);
        var store = new SharedStorageCoordinationStore(config, _root);
        await using var manager = new LeaseManager(config, store);
        var acquired = await manager.TryAcquireExecutionAsync("jobs/etl/import", timeout.Token);
        var lease = Assert.IsAssignableFrom<IExecutionLease>(acquired.Lease);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var action = lease.ExecuteAsync(async ct =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }, timeout.Token);
        await entered.Task.WaitAsync(timeout.Token);
        await manager.DisposeAsync().AsTask().WaitAsync(timeout.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => action);
        Assert.Null(await store.GetLeaseAsync(lease.ResourceId, timeout.Token));
    }

    [Fact]
    public async Task DisposeAsync_ReleaseStoreFails_PreservesCommittedResultAndRejectsFurtherActions()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var store = new Mock<ICoordinationStore>();
        store.Setup(s => s.TryAcquireLeaseAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string resource, string owner, TimeSpan ttl, TimeSpan delay, CancellationToken ct) =>
                new LeaseAcquireResult(true, false,
                    new LeaseRecord(resource, owner, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow + ttl, DateTimeOffset.UtcNow),
                    null, null, null));
        store.Setup(s => s.ExecuteUnderLeaseAsync(It.IsAny<LeaseRecord>(),
                It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns(async (LeaseRecord lease, Func<CancellationToken, Task> action, CancellationToken ct) =>
            {
                await action(ct);
                return true;
            });
        store.Setup(s => s.ReleaseLeaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("lease volume unavailable after commit"));
        await using var manager = new LeaseManager(Config(true), store.Object);
        var acquired = await manager.TryAcquireExecutionAsync("jobs/etl/import", timeout.Token);
        var lease = Assert.IsAssignableFrom<IExecutionLease>(acquired.Lease);
        var committed = await lease.ExecuteAsync(_ => Task.FromResult("retained-success"), timeout.Token);
        await lease.DisposeAsync();
        Assert.Equal("retained-success", committed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => lease.ExecuteAsync(_ => Task.CompletedTask, timeout.Token));
        store.Verify(s => s.ReleaseLeaseAsync(lease.ResourceId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecutionLease_IdleParserInterval_RenewsRetainedOwnership()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var config = Config(true) with { RenewIntervalSeconds = 1 };
        var store = new SharedStorageCoordinationStore(config, _root);
        await using var manager = new LeaseManager(config, store);
        var acquired = await manager.TryAcquireExecutionAsync("jobs/etl/import", timeout.Token);
        await using var lease = Assert.IsAssignableFrom<IExecutionLease>(acquired.Lease);
        var initial = (await store.GetLeaseAsync(lease.ResourceId, timeout.Token))!;
        LeaseRecord? renewed;
        do
        {
            // Observe the lifecycle-owned timer; no action or explicit renewal may trigger the write.
            await Task.Delay(20, timeout.Token);
            renewed = await store.GetLeaseAsync(lease.ResourceId, timeout.Token);
        } while (renewed is not null && renewed.LastRenewedAtUtc == initial.LastRenewedAtUtc);
        Assert.NotNull(renewed);
        Assert.Equal(initial.InstanceId, renewed.InstanceId);
        Assert.True(renewed.ExpiresAtUtc > initial.ExpiresAtUtc);
        Assert.Equal(renewed, Assert.Single((await manager.GetSnapshotAsync(timeout.Token)).HeldLeases));
    }

    [Fact]
    public async Task TryAcquireExecutionAsync_StoreCompletesAfterShutdown_ReleasesLateOwnership()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var grant = new TaskCompletionSource<LeaseAcquireResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        LeaseRecord? granted = null;
        var store = new Mock<ICoordinationStore>();
        store.Setup(s => s.TryAcquireLeaseAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns((string resource, string owner, TimeSpan ttl, TimeSpan delay, CancellationToken ct) =>
            {
                granted = new LeaseRecord(resource, owner, 1, DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow + ttl, DateTimeOffset.UtcNow);
                entered.SetResult();
                return grant.Task;
            });
        store.Setup(s => s.ReleaseLeaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        await using var manager = new LeaseManager(Config(true), store.Object);
        var acquisition = manager.TryAcquireExecutionAsync("jobs/etl/import", timeout.Token);
        await entered.Task.WaitAsync(timeout.Token);
        await manager.DisposeAsync();
        grant.SetResult(new LeaseAcquireResult(true, false, granted, null, null, null));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => acquisition);
        store.Verify(s => s.ReleaseLeaseAsync(granted!.ResourceId, granted.InstanceId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private CoordinationConfig Config(bool shared) => new(
        Enabled: shared, Mode: shared ? CoordinationMode.SharedStorage : CoordinationMode.SingleInstance,
        InstanceId: "runner", LeaseTtlSeconds: 60, RenewIntervalSeconds: 3600,
        TakeoverDelaySeconds: 0, RootPath: _root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}

using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Application.Coordination;
using Xunit;

namespace Meridian.Tests.Application.Coordination;

public sealed class LeaseManagerTests
{
    [Fact]
    public async Task LeaseManager_RenewReleaseAndReacquire_WorkAcrossInstances()
    {
        var tempDir = CreateTempDir();
        try
        {
            var config = CreateConfig(tempDir, leaseTtlSeconds: 5, renewIntervalSeconds: 30, takeoverDelaySeconds: 1);
            var store1 = new SharedStorageCoordinationStore(config, tempDir);
            var store2 = new SharedStorageCoordinationStore(config, tempDir);

            await using var manager1 = new LeaseManager(config with { InstanceId = "instance-a" }, store1);
            await using var manager2 = new LeaseManager(config with { InstanceId = "instance-b" }, store2);

            const string resourceId = "symbols/polygon/trades/SPY";

            var acquired = await manager1.TryAcquireAsync(resourceId);
            acquired.Acquired.Should().BeTrue();

            var initialLease = await store1.GetLeaseAsync(resourceId);
            initialLease.Should().NotBeNull();

            await Task.Delay(1100);
            var renewed = await manager1.RenewAsync(resourceId);
            renewed.Should().BeTrue();

            var renewedLease = await store1.GetLeaseAsync(resourceId);
            renewedLease.Should().NotBeNull();
            renewedLease!.ExpiresAtUtc.Should().BeAfter(initialLease!.ExpiresAtUtc);

            var denied = await manager2.TryAcquireAsync(resourceId);
            denied.Acquired.Should().BeFalse();
            denied.CurrentOwner.Should().Be("instance-a");

            var released = await manager1.ReleaseAsync(resourceId);
            released.Should().BeTrue();

            var reacquired = await manager2.TryAcquireAsync(resourceId);
            reacquired.Acquired.Should().BeTrue();
            reacquired.TakenOver.Should().BeFalse();
        }
        finally
        {
            DeleteTempDir(tempDir);
        }
    }

    [Fact]
    public async Task SharedStorageStore_AllowsTakeoverAfterExpiryAndDelay()
    {
        var tempDir = CreateTempDir();
        try
        {
            var config = CreateConfig(tempDir, leaseTtlSeconds: 1, renewIntervalSeconds: 30, takeoverDelaySeconds: 1);
            var store = new SharedStorageCoordinationStore(config, tempDir);
            const string resourceId = "jobs/job-123";

            var first = await store.TryAcquireLeaseAsync(
                resourceId,
                "instance-a",
                TimeSpan.FromSeconds(config.LeaseTtlSeconds),
                TimeSpan.FromSeconds(config.TakeoverDelaySeconds));

            first.Acquired.Should().BeTrue();

            await Task.Delay(TimeSpan.FromSeconds(config.LeaseTtlSeconds + config.TakeoverDelaySeconds + 1));

            var takeover = await store.TryAcquireLeaseAsync(
                resourceId,
                "instance-b",
                TimeSpan.FromSeconds(config.LeaseTtlSeconds),
                TimeSpan.FromSeconds(config.TakeoverDelaySeconds));

            takeover.Acquired.Should().BeTrue();
            takeover.TakenOver.Should().BeTrue();
            takeover.CurrentOwner.Should().Be("instance-a");
        }
        finally
        {
            DeleteTempDir(tempDir);
        }
    }

    [Fact]
    public async Task SharedStorageStore_ReportsAndRecoversCorruptedLeaseFiles()
    {
        var tempDir = CreateTempDir();
        try
        {
            var config = CreateConfig(tempDir, leaseTtlSeconds: 5, renewIntervalSeconds: 30, takeoverDelaySeconds: 1);
            var store = new SharedStorageCoordinationStore(config, tempDir);
            const string resourceId = "symbols/polygon/depth/MSFT";

            var leasePath = Path.Combine(
                store.RootPath,
                "symbols",
                "polygon",
                "depth",
                "MSFT.lease.json");

            Directory.CreateDirectory(Path.GetDirectoryName(leasePath)!);
            await File.WriteAllTextAsync(leasePath, "{ not-valid-json");

            var corrupted = await store.GetCorruptedLeaseFilesAsync();
            corrupted.Should().Contain(leasePath);

            var recovered = await store.TryAcquireLeaseAsync(
                resourceId,
                "instance-b",
                TimeSpan.FromSeconds(config.LeaseTtlSeconds),
                TimeSpan.FromSeconds(config.TakeoverDelaySeconds));

            recovered.Acquired.Should().BeTrue();

            var remainingCorrupted = await store.GetCorruptedLeaseFilesAsync();
            remainingCorrupted.Should().NotContain(leasePath);

            var lease = await store.GetLeaseAsync(resourceId);
            lease.Should().NotBeNull();
            lease!.InstanceId.Should().Be("instance-b");
        }
        finally
        {
            DeleteTempDir(tempDir);
        }
    }

    [Fact]
    public async Task LeaseManager_RemovesHeldLease_WhenRenewThrowsInBackgroundLoop()
    {
        var config = new CoordinationConfig(
            Enabled: true,
            Mode: CoordinationMode.SharedStorage,
            InstanceId: "instance-a",
            LeaseTtlSeconds: 30,
            RenewIntervalSeconds: 1,
            TakeoverDelaySeconds: 1,
            RootPath: Path.Combine(Path.GetTempPath(), $"meridian_coord_{Guid.NewGuid():N}"));
        var store = new ThrowingRenewStore();

        await using var manager = new LeaseManager(config, store);
        const string resourceId = "leader/cluster-coordinator";

        var acquired = await manager.TryAcquireAsync(resourceId);
        acquired.Acquired.Should().BeTrue();
        manager.HoldsLease(resourceId).Should().BeTrue();

        await Task.Delay(TimeSpan.FromSeconds(2));

        manager.HoldsLease(resourceId).Should().BeFalse();
    }

    private static CoordinationConfig CreateConfig(
        string dataRoot,
        int leaseTtlSeconds,
        int renewIntervalSeconds,
        int takeoverDelaySeconds)
        => new(
            Enabled: true,
            Mode: CoordinationMode.SharedStorage,
            InstanceId: "instance-default",
            LeaseTtlSeconds: leaseTtlSeconds,
            RenewIntervalSeconds: renewIntervalSeconds,
            TakeoverDelaySeconds: takeoverDelaySeconds,
            RootPath: Path.Combine(dataRoot, "_coordination"));

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meridian_coord_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDir(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, true);
    }

    private sealed class ThrowingRenewStore : ICoordinationStore
    {
        private LeaseRecord? _lease;

        public string RootPath => Path.GetTempPath();

        public Task<LeaseAcquireResult> TryAcquireLeaseAsync(
            string resourceId,
            string instanceId,
            TimeSpan leaseTtl,
            TimeSpan takeoverDelay,
            CancellationToken ct = default)
        {
            _lease = new LeaseRecord(
                resourceId,
                instanceId,
                LeaseVersion: 1,
                AcquiredAtUtc: DateTimeOffset.UtcNow,
                ExpiresAtUtc: DateTimeOffset.UtcNow.Add(leaseTtl),
                LastRenewedAtUtc: DateTimeOffset.UtcNow);

            return Task.FromResult(new LeaseAcquireResult(true, false, _lease, null, null, null));
        }

        public Task<bool> RenewLeaseAsync(string resourceId, string instanceId, TimeSpan leaseTtl, CancellationToken ct = default)
            => throw new IOException("Simulated renew failure");

        public Task<bool> ReleaseLeaseAsync(string resourceId, string instanceId, CancellationToken ct = default)
        {
            _lease = null;
            return Task.FromResult(true);
        }

        public Task<LeaseRecord?> GetLeaseAsync(string resourceId, CancellationToken ct = default)
            => Task.FromResult(_lease);

        public Task<IReadOnlyList<LeaseRecord>> GetAllLeasesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LeaseRecord>>(_lease is null ? Array.Empty<LeaseRecord>() : [_lease]);

        public Task<IReadOnlyList<string>> GetCorruptedLeaseFilesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }
}

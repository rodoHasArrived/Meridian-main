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
}

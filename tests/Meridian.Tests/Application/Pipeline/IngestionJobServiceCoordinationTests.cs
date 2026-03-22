using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Application.Coordination;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Pipeline;
using Xunit;

namespace Meridian.Tests.Application.Pipeline;

public sealed class IngestionJobServiceCoordinationTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"meridian_ingcoord_{Guid.NewGuid():N}");

    public IngestionJobServiceCoordinationTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task TransitionAsync_OnlyOneInstanceCanEnterRunningStateForSameJob()
    {
        var coordination = new CoordinationConfig(
            Enabled: true,
            Mode: CoordinationMode.SharedStorage,
            LeaseTtlSeconds: 30,
            RenewIntervalSeconds: 10,
            TakeoverDelaySeconds: 5,
            RootPath: Path.Combine(_tempDir, "_coordination"));

        await using var leaseManager1 = new LeaseManager(
            coordination with { InstanceId = "instance-a" },
            new SharedStorageCoordinationStore(coordination with { InstanceId = "instance-a" }, _tempDir));
        await using var leaseManager2 = new LeaseManager(
            coordination with { InstanceId = "instance-b" },
            new SharedStorageCoordinationStore(coordination with { InstanceId = "instance-b" }, _tempDir));

        var ownership1 = new ScheduledWorkOwnershipService(leaseManager1);
        var ownership2 = new ScheduledWorkOwnershipService(leaseManager2);

        using var service1 = new IngestionJobService(_tempDir, ownership1);
        using var service2 = new IngestionJobService(_tempDir, ownership2);

        var job = await service1.CreateJobAsync(
            IngestionWorkloadType.Historical,
            ["SPY"],
            "polygon");

        await service2.LoadJobsAsync();

        await service1.TransitionAsync(job.JobId, IngestionJobState.Queued);
        await service2.TransitionAsync(job.JobId, IngestionJobState.Queued);

        var run1 = await service1.TransitionAsync(job.JobId, IngestionJobState.Running);
        var run2 = await service2.TransitionAsync(job.JobId, IngestionJobState.Running);

        (run1 || run2).Should().BeTrue();
        (run1 && run2).Should().BeFalse();

        var runningService = run1 ? service1 : service2;
        var waitingService = run1 ? service2 : service1;

        await runningService.TransitionAsync(job.JobId, IngestionJobState.Completed);

        var retried = await waitingService.TransitionAsync(job.JobId, IngestionJobState.Running);
        retried.Should().BeTrue();
    }
}

using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Meridian.Documents;
using Meridian.ProcessTestHelper;

namespace Meridian.Tests.Documents;

public sealed class EvidenceStorageQuotaCoordinatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-quota-core-" + Guid.NewGuid().ToString("N"));
    private string Journal => Path.Combine(_root, "workstation", "evidence-quota");

    [Fact]
    public async Task LiveLease_IsNotRecoveredRegardlessOfAgeAndOtherInstancesRespectItsBudget()
    {
        var first = Coordinator();
        await using var active = await first.ReserveAsync("tenant", 80, 1);
        File.SetLastWriteTimeUtc(Path.Combine(Journal, active.Id + ".json"), DateTime.UtcNow.AddYears(-1));
        var liveTemporaryFile = Path.Combine(Journal, $".{active.Id}.json.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(liveTemporaryFile, "in progress");
        var second = Coordinator();
        (await second.RecoverAbandonedAsync()).Should().Be(0);
        await AssertQuotaAsync(() => second.ReserveAsync("TENANT", 21, 1), "tenant-bytes");
        await using var independentTenant = await second.ReserveAsync("other", 80, 1);
        File.Exists(Path.Combine(Journal, active.Id + ".json")).Should().BeTrue();
        File.Exists(liveTemporaryFile).Should().BeTrue();
    }

    [Fact]
    public async Task FailedCleanup_RetainsCapacityUntilLaterRecoverySucceeds()
    {
        var allowCleanup = false;
        var coordinator = Coordinator(recover: (_, _) => Task.FromResult(allowCleanup));
        var reservation = await coordinator.ReserveAsync("tenant", 80, 1);
        await reservation.DisposeAsync();
        File.Exists(Path.Combine(Journal, reservation.Id + ".json")).Should().BeTrue();
        await AssertQuotaAsync(() => coordinator.ReserveAsync("tenant", 21, 1), "tenant-bytes");
        allowCleanup = true;
        (await Coordinator().RecoverAbandonedAsync()).Should().Be(1);
        await using var retry = await coordinator.ReserveAsync("tenant", 100, 1);
    }

    [Fact]
    public async Task AbandonedReservation_CleansOnlyItsAttemptAndAllowsRetry()
    {
        var abandoned = Guid.NewGuid().ToString("N");
        await SeedReservationAsync(abandoned, 100, 60);
        var abandonedTemporaryFile = Path.Combine(Journal, $".{abandoned}.json.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(abandonedTemporaryFile, "interrupted journal update");
        var owned = Path.Combine(_root, abandoned);
        var published = Path.Combine(_root, "published.dat");
        Directory.CreateDirectory(owned);
        await File.WriteAllTextAsync(Path.Combine(owned, "partial.dat"), "partial");
        await File.WriteAllTextAsync(published, "retained evidence");
        var coordinator = Coordinator(recover: (id, _) =>
        {
            if (Directory.Exists(Path.Combine(_root, id)))
            {
                Directory.Delete(Path.Combine(_root, id), recursive: true);
            }
            return Task.FromResult(true);
        });
        await using var admitted = await coordinator.ReserveAsync("tenant", 100, 1);
        Directory.Exists(owned).Should().BeFalse();
        (await File.ReadAllTextAsync(published)).Should().Be("retained evidence");
        File.Exists(Path.Combine(Journal, abandoned + ".json")).Should().BeFalse();
        File.Exists(abandonedTemporaryFile).Should().BeFalse();
    }

    [Fact]
    public async Task Publication_ReconcilesEstimateAndCleansStageBeforeReleasingReservation()
    {
        var publishedBytes = 0L;
        var cleaned = new List<string>();
        var coordinator = Coordinator(published: _ => publishedBytes, recover: (id, _) =>
        {
            cleaned.Add(id);
            return Task.FromResult(true);
        });
        await using var reservation = await coordinator.ReserveAsync("tenant", 80, 1);
        await reservation.BeforeWriteAsync(20);
        await reservation.AfterWriteAsync(20);
        await reservation.PublishAsync(_ =>
        {
            publishedBytes = 20;
            return Task.CompletedTask;
        });
        cleaned.Should().ContainSingle().Which.Should().Be(reservation.Id);
        File.Exists(Path.Combine(Journal, reservation.Id + ".json")).Should().BeFalse();
        await using var remaining = await coordinator.ReserveAsync("tenant", 80, 1);
        await AssertQuotaAsync(() => coordinator.ReserveAsync("tenant", 1, 0), "tenant-bytes");
    }

    [Fact]
    public async Task DiskAccounting_ChargesUnwrittenCapacityAndKeepsInflightWritesReserved()
    {
        var freeBytes = 100L;
        var options = Options();
        options.MinimumDiskHeadroomBytes = 10;
        var coordinator = Coordinator(options, disk: _ => freeBytes);
        await using var first = await coordinator.ReserveAsync("tenant", 80, 1);
        await first.BeforeWriteAsync(60);
        await AssertQuotaAsync(() => coordinator.ReserveAsync("other", 11, 1), "disk-headroom");
        freeBytes = 40; // The completed write is now reflected by the filesystem probe.
        await first.AfterWriteAsync(60);
        await using var fitting = await coordinator.ReserveAsync("other", 10, 1);
        await AssertQuotaAsync(() => coordinator.ReserveAsync("third", 1, 0), "disk-headroom");
    }

    [Fact]
    public async Task CancellationWhileWaitingForGate_DoesNotCreateAReservation()
    {
        Directory.CreateDirectory(Journal);
        await using var gate = new FileStream(Path.Combine(Journal, "quota.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var reserve = () => Coordinator().ReserveAsync("tenant", 50, 1, cancellation.Token);
        await reserve.Should().ThrowAsync<OperationCanceledException>();
        Directory.EnumerateFiles(Journal, "*.json").Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KilledProcess_ReclaimsItsReservationAndPreservesPublishedEvidence(bool publishBeforeCrash)
    {
        Directory.CreateDirectory(_root);
        var ready = Path.Combine(_root, "ready");
        var published = Path.Combine(_root, "published.dat");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "exec", "--depsfile", Path.Combine(AppContext.BaseDirectory, "Meridian.Tests.deps.json"),
            "--runtimeconfig", Path.Combine(AppContext.BaseDirectory, "Meridian.Tests.runtimeconfig.json"),
            typeof(ProcessTestHelperMarker).Assembly.Location,
            "evidence-reserve-and-wait", _root, ready, publishBeforeCrash.ToString()
        })
        {
            start.ArgumentList.Add(argument);
        }
        using var writer = Process.Start(start) ?? throw new InvalidOperationException("Evidence quota writer did not start.");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (!File.Exists(ready))
            {
                if (writer.HasExited)
                {
                    throw new InvalidOperationException(await writer.StandardError.ReadToEndAsync(timeout.Token));
                }
                await Task.Delay(20, timeout.Token);
            }
            if (!publishBeforeCrash)
            {
                (await Coordinator().RecoverAbandonedAsync(timeout.Token)).Should().Be(0);
                await AssertQuotaAsync(() => Coordinator().ReserveAsync("tenant", 21, 1, timeout.Token), "tenant-bytes");
            }
            writer.Kill(entireProcessTree: true);
            await writer.WaitForExitAsync(timeout.Token);
            var coordinator = Coordinator(
                published: _ => File.Exists(published) ? new FileInfo(published).Length : 0,
                recover: (id, _) =>
                {
                    var stage = Path.Combine(_root, "stage", id);
                    if (Directory.Exists(stage))
                    {
                        Directory.Delete(stage, recursive: true);
                    }
                    return Task.FromResult(true);
                });
            (await coordinator.RecoverAbandonedAsync(timeout.Token)).Should().Be(1);
            Directory.EnumerateFiles(Journal, "*.json").Should().BeEmpty();
            if (publishBeforeCrash)
            {
                File.ReadAllBytes(published).Should().Equal(Enumerable.Repeat((byte)0x5a, 20));
                await AssertQuotaAsync(() => coordinator.ReserveAsync("tenant", 81, 1), "tenant-bytes");
            }
            await using var retry = await coordinator.ReserveAsync("tenant", publishBeforeCrash ? 80 : 100, 1);
        }
        finally
        {
            if (!writer.HasExited)
            {
                writer.Kill(entireProcessTree: true);
                await writer.WaitForExitAsync();
            }
        }
    }

    private EvidenceStorageQuotaCoordinator Coordinator(
        EvidenceStorageQuotaOptions? options = null,
        Func<string, long>? published = null,
        Func<string, long>? disk = null,
        Func<string, CancellationToken, Task<bool>>? recover = null) =>
        new(_root, options ?? Options(), published ?? (_ => 0), disk ?? (_ => long.MaxValue), recover);

    private static EvidenceStorageQuotaOptions Options() => new()
    {
        MaxPackageBytes = 100,
        DefaultTenantBudgetBytes = 100,
        MinimumDiskHeadroomBytes = 0
    };

    private async Task SeedReservationAsync(string id, long reserved, long written)
    {
        Directory.CreateDirectory(Journal);
        await File.WriteAllTextAsync(Path.Combine(Journal, id + ".json"), JsonSerializer.Serialize(new
        {
            id,
            tenantId = "tenant",
            reservedBytes = reserved,
            writtenBytes = written,
            preparedBytes = 0,
            artifactCount = 1
        }));
    }

    private static async Task AssertQuotaAsync(Func<Task<EvidenceStorageReservation>> action, string reason)
    {
        var thrown = await action.Should().ThrowAsync<EvidenceStorageQuotaExceededException>();
        thrown.Which.Reason.Should().Be(reason);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

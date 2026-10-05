using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Documents;
using Meridian.Ui.Shared.Evidence;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui.Evidence;

/// <summary>
/// Guards report-pack retention when concurrent operators approach tenant or disk capacity,
/// and when an interrupted or underestimated export must release its unpublished reservation.
/// </summary>
public sealed class FileEvidenceArtifactStoreQuotaTests : IDisposable
{
    private const int KiB = 1024;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-quota", Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(30));
    private static readonly EvidencePacketExportRequest Request = new("controller", "retain report-pack evidence")
    {
        TenantId = "tenant-a",
        Scope = "company-a"
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentRequests_NearTenantLimit_RejectSecondBeforeItCopies(bool secondIsIntake)
    {
        var source = await SourceAsync("report.dat", 256 * KiB);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = Options();
        var firstStore = Store(options, path => new BlockingStream(File.OpenRead(path), entered, release));
        var first = firstStore.WriteManifestAsync(Packet(Artifact("first", source)), Request, _timeout.Token);
        await entered.Task.WaitAsync(_timeout.Token);

        var sourceOpened = false;
        var secondStore = Store(options, path =>
        {
            sourceOpened = true;
            return File.OpenRead(path);
        });
        try
        {
            Func<Task> second = secondIsIntake
                ? async () => await secondStore.WriteIntakeArtifactAsync(Intake(256 * KiB), _timeout.Token)
                : async () => await secondStore.WriteManifestAsync(Packet(Artifact("second", source)), Request, _timeout.Token);
            (await second.Should().ThrowAsync<EvidenceStorageQuotaExceededException>())
                .Which.Reason.Should().Be("tenant-bytes");
            sourceOpened.Should().BeFalse();
            PublishedFiles().Should().BeEmpty("a live reservation must not publish or be reclaimed by another store");
        }
        finally
        {
            release.TrySetResult();
            await first.WaitAsync(_timeout.Token);
        }

        var identity = (await first).VaultIdentity!;
        (await Store(options).TryGetVaultIdentityAsync(identity.VaultId, Request.TenantId!, Request.Scope!, _timeout.Token))
            .Should().NotBeNull();
        StagingFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task TenantBudgets_PublishedPackageInAnotherTenant_DoesNotConsumeOwnBudget()
    {
        var source = await SourceAsync("report.dat", 256 * KiB);
        var first = await Store(Options()).WriteManifestAsync(Packet(Artifact("first", source)), Request, _timeout.Token);
        var otherRequest = Request with { TenantId = "tenant-b" };
        var second = await Store(Options()).WriteManifestAsync(Packet(Artifact("second", source)), otherRequest, _timeout.Token);

        first.VaultIdentity!.TenantId.Should().Be("tenant-a");
        second.VaultIdentity!.TenantId.Should().Be("tenant-b");
        new FileInfo(Path.Combine(_root, first.VaultIdentity.Artifacts.Single().RelativePath)).Length.Should().Be(256 * KiB);
        new FileInfo(Path.Combine(_root, second.VaultIdentity.Artifacts.Single().RelativePath)).Length.Should().Be(256 * KiB);
    }

    [Fact]
    public async Task TenantOverride_ZeroDefaultBudget_OnlyConfiguredTenantCanRetainIntake()
    {
        var options = Options(tenantBytes: 0);
        options.TenantBudgetBytes["tenant-a"] = 400 * KiB;
        var retained = await Store(options).WriteIntakeArtifactAsync(Intake(16 * KiB), _timeout.Token);
        var rejected = () => Store(options).WriteIntakeArtifactAsync(Intake(16 * KiB) with { TenantId = "tenant-b" }, _timeout.Token);

        (await rejected.Should().ThrowAsync<EvidenceStorageQuotaExceededException>()).Which.Reason.Should().Be("tenant-bytes");
        retained.SizeBytes.Should().Be(16 * KiB);
        StagingFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task ArtifactCountLimit_TooManyReportAttachments_RejectsBeforeOpeningSources()
    {
        var source = await SourceAsync("report.dat", 1);
        var options = Options(artifactCount: 1);
        var sourceOpened = false;
        var store = Store(options, path =>
        {
            sourceOpened = true;
            return File.OpenRead(path);
        });
        var export = () => store.WriteManifestAsync(Packet(Artifact("first", source), Artifact("second", source)), Request, _timeout.Token);

        (await export.Should().ThrowAsync<EvidenceStorageQuotaExceededException>()).Which.Reason.Should().Be("package-count");
        sourceOpened.Should().BeFalse();
        PublishedFiles().Should().BeEmpty();
        StagingFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task PackageByteLimit_SummedAttachmentsExceedCapacity_RejectsBeforeOpeningSources()
    {
        var source = await SourceAsync("report.dat", 128 * KiB);
        var sourceOpened = false;
        var store = Store(Options(packageBytes: 200 * KiB), path =>
        {
            sourceOpened = true;
            return File.OpenRead(path);
        });
        var export = () => store.WriteManifestAsync(Packet(Artifact("first", source), Artifact("second", source)), Request, _timeout.Token);

        (await export.Should().ThrowAsync<EvidenceStorageQuotaExceededException>()).Which.Reason.Should().Be("package-bytes");
        sourceOpened.Should().BeFalse();
        PublishedFiles().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfiguredArtifactLimit_OversizedEvidence_RejectsExportAndIntake(bool intake)
    {
        var options = Options(artifactBytes: 8 * KiB);
        var source = await SourceAsync("report.dat", 8 * KiB + 1);
        var store = Store(options);
        Func<Task> operation = intake
            ? async () => await store.WriteIntakeArtifactAsync(Intake(8 * KiB + 1), _timeout.Token)
            : async () => await store.WriteManifestAsync(Packet(Artifact("large", source)), Request, _timeout.Token);

        (await operation.Should().ThrowAsync<EvidenceStorageQuotaExceededException>()).Which.Reason.Should().Be("artifact-bytes");
        PublishedFiles().Should().BeEmpty();
        StagingFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task UnderestimatedSource_ActualBytesFit_ExtendsReservationAndRetainsExactDigest()
    {
        var source = await SourceAsync("growing.dat", 1);
        var bytes = Enumerable.Repeat((byte)0x5a, 256 * KiB).ToArray();
        var response = await Store(Options(), _ => new MemoryStream(bytes, writable: false))
            .WriteManifestAsync(Packet(Artifact("growing", source)), Request, _timeout.Token);

        var artifact = response.VaultIdentity!.Artifacts.Single();
        artifact.SizeBytes.Should().Be(bytes.LongLength);
        artifact.ContentHashSha256.Should().Be(Sha256Digest.Compute(bytes));
        (await File.ReadAllBytesAsync(Path.Combine(_root, artifact.RelativePath), _timeout.Token)).Should().Equal(bytes);

        var nextSource = await SourceAsync("next.dat", 200 * KiB);
        var next = () => Store(Options()).WriteManifestAsync(Packet(Artifact("next", nextSource)), Request, _timeout.Token);
        (await next.Should().ThrowAsync<EvidenceStorageQuotaExceededException>()).Which.Reason.Should().Be("tenant-bytes");
    }

    [Theory]
    [InlineData("tenant-bytes")]
    [InlineData("package-bytes")]
    public async Task UnderestimatedSource_ActualBytesExceedCapacity_CleansOnlyAttemptAndAllowsRetry(string limit)
    {
        var source = await SourceAsync("report.dat", 8 * KiB);
        var existing = await Store(Options()).WriteManifestAsync(Packet(Artifact("published", source)), Request, _timeout.Token);
        var before = PublishedFiles().ToDictionary(path => path, File.ReadAllBytes);
        var options = Options(packageBytes: limit == "package-bytes" ? 200 * KiB : 1024 * KiB);
        var failing = Store(options, _ => new MemoryStream(new byte[512 * KiB], writable: false));
        var export = () => failing.WriteManifestAsync(Packet(Artifact("growing", source)), Request, _timeout.Token);

        (await export.Should().ThrowAsync<EvidenceStorageQuotaExceededException>()).Which.Reason.Should().Be(limit);
        AssertPublishedUnchanged(before);
        StagingFiles().Should().BeEmpty();

        var retry = await Store(options).WriteManifestAsync(Packet(Artifact("retry", source)), Request, _timeout.Token);
        retry.Retained.Should().BeTrue();
        (await Store(options).TryGetVaultIdentityAsync(existing.VaultIdentity!.VaultId, Request.TenantId!, Request.Scope!, _timeout.Token))
            .Should().NotBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiskPressure_InsufficientInitialHeadroom_RejectsWithoutStagingEvidence(bool intake)
    {
        var source = await SourceAsync("report.dat", 16 * KiB);
        var options = Options(diskHeadroom: 64 * KiB);
        var sourceOpened = false;
        var store = Store(options, path =>
        {
            sourceOpened = true;
            return File.OpenRead(path);
        }, _ => 64 * KiB);
        Func<Task> operation = intake
            ? async () => await store.WriteIntakeArtifactAsync(Intake(16 * KiB), _timeout.Token)
            : async () => await store.WriteManifestAsync(Packet(Artifact("report", source)), Request, _timeout.Token);

        (await operation.Should().ThrowAsync<EvidenceStorageQuotaExceededException>()).Which.Reason.Should().Be("disk-headroom");
        sourceOpened.Should().BeFalse();
        PublishedFiles().Should().BeEmpty();
        StagingFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task DiskPressure_DropsDuringStreaming_StopsAndReleasesReservationForRetry()
    {
        var source = await SourceAsync("report.dat", 64 * KiB);
        long availableBytes = 10 * 1024 * KiB;
        var store = Store(Options(diskHeadroom: 64 * KiB), _ => new ReadHookStream(64 * KiB, read =>
        {
            if (read == 2)
            {
                availableBytes = 0;
            }
        }), _ => availableBytes);
        var export = () => store.WriteManifestAsync(Packet(Artifact("report", source)), Request, _timeout.Token);

        (await export.Should().ThrowAsync<EvidenceStorageQuotaExceededException>()).Which.Reason.Should().Be("disk-headroom");
        PublishedFiles().Should().BeEmpty();
        StagingFiles().Should().BeEmpty();
        availableBytes = 10 * 1024 * KiB;
        var retry = await Store(Options(diskHeadroom: 64 * KiB), availableDiskBytes: _ => availableBytes)
            .WriteManifestAsync(Packet(Artifact("retry", source)), Request, _timeout.Token);
        retry.Retained.Should().BeTrue();
    }

    [Fact]
    public async Task Cancellation_ExportWaitingOnSource_ReleasesCapacityAndPreservesPublishedEvidence()
    {
        var priorSource = await SourceAsync("prior.dat", 8 * KiB);
        await Store(Options()).WriteManifestAsync(Packet(Artifact("prior", priorSource)), Request, _timeout.Token);
        var before = PublishedFiles().ToDictionary(path => path, File.ReadAllBytes);
        var source = await SourceAsync("report.dat", 256 * KiB);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_timeout.Token);
        var store = Store(Options(), path => new BlockingStream(File.OpenRead(path), entered, release));
        var pending = store.WriteManifestAsync(Packet(Artifact("cancelled", source)), Request, cancellation.Token);
        await entered.Task.WaitAsync(_timeout.Token);
        cancellation.Cancel();

        Func<Task> cancelled = async () => await pending;
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        AssertPublishedUnchanged(before);
        StagingFiles().Should().BeEmpty();
        var retry = await Store(Options()).WriteManifestAsync(Packet(Artifact("retry", source)), Request, _timeout.Token);
        retry.VaultIdentity!.Artifacts.Single().SizeBytes.Should().Be(256 * KiB);
    }

    [Fact]
    public async Task Cancellation_IntakeAfterArtifactWrite_CleansStagingAndReleasesCapacity()
    {
        await Store(Options()).WriteIntakeArtifactAsync(Intake(8 * KiB), _timeout.Token);
        var before = PublishedFiles().ToDictionary(path => path, File.ReadAllBytes);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_timeout.Token);
        var checks = 0;
        var store = Store(Options(), availableDiskBytes: _ =>
        {
            // Admission and artifact write have completed before the metadata reservation.
            if (++checks == 3)
            {
                StagingFiles().Should().ContainSingle();
                cancellation.Cancel();
            }
            return 100L * 1024 * 1024;
        });
        var intake = () => store.WriteIntakeArtifactAsync(Intake(256 * KiB), cancellation.Token);
        await intake.Should().ThrowAsync<OperationCanceledException>();
        AssertPublishedUnchanged(before);
        StagingFiles().Should().BeEmpty();
        var retry = await Store(Options()).WriteIntakeArtifactAsync(Intake(256 * KiB), _timeout.Token);
        retry.SizeBytes.Should().Be(256 * KiB);
    }

    [Fact]
    public async Task Restart_OverestimatedExport_ChargesActualRetainedBytesInsteadOfOriginalReservation()
    {
        var source = await SourceAsync("shrinking.dat", 256 * KiB);
        var first = await Store(Options(), _ => new MemoryStream(new byte[16 * KiB], writable: false))
            .WriteManifestAsync(Packet(Artifact("shrinking", source)), Request, _timeout.Token);
        first.VaultIdentity!.Artifacts.Single().SizeBytes.Should().Be(16 * KiB);

        var second = await Store(Options()).WriteManifestAsync(Packet(Artifact("second", source)), Request, _timeout.Token);
        second.VaultIdentity!.Artifacts.Single().SizeBytes.Should().Be(256 * KiB);
        StagingFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Restart_MetadataBytesAreCharged_RejectsNextPayloadBeforeOpeningIt()
    {
        var source = await SourceAsync("report.dat", 64 * KiB);
        var response = await Store(Options(tenantBytes: 1024 * KiB)).WriteManifestAsync(
            Packet(Artifact("published", source)), Request with { Reason = new string('\u00e9', 6000) }, _timeout.Token);
        var identity = response.VaultIdentity!;
        var actualBytes = PublishedFiles().Sum(path => new FileInfo(path).Length);
        actualBytes.Should().BeGreaterThan(identity.Artifacts.Sum(artifact => artifact.SizeBytes) + 32 * KiB);
        var nextSource = await SourceAsync("next.dat", 128 * KiB);
        var sourceOpened = false;
        var restarted = Store(Options(tenantBytes: actualBytes + 128 * KiB - 1), path =>
        {
            sourceOpened = true;
            return File.OpenRead(path);
        });
        var export = () => restarted.WriteManifestAsync(Packet(Artifact("next", nextSource)), Request, _timeout.Token);

        (await export.Should().ThrowAsync<EvidenceStorageQuotaExceededException>()).Which.Reason.Should().Be("tenant-bytes");
        sourceOpened.Should().BeFalse();
        PublishedFiles().Sum(path => new FileInfo(path).Length).Should().Be(actualBytes);
        (await restarted.TryGetVaultIdentityAsync(identity.VaultId, Request.TenantId!, Request.Scope!, _timeout.Token))
            .Should().NotBeNull();
    }

    [Fact]
    public async Task Restart_AbandonedPartiallyMovedPackage_ReclaimsCapacityAndOnlyFailedAttemptFiles()
    {
        var source = await SourceAsync("published.dat", 8 * KiB);
        await Store(Options()).WriteManifestAsync(Packet(Artifact("published", source)), Request, _timeout.Token);
        var before = PublishedFiles().ToDictionary(path => path, File.ReadAllBytes);
        var abandonedVaultId = "ev-111111111111111111111111";
        var packagePath = Path.Combine(EvidenceRoot, "_vault", abandonedVaultId);
        var manifestPath = Path.Combine(EvidenceRoot, "report-pack", "interrupted", "manifest.json");
        Directory.CreateDirectory(Path.Combine(packagePath, "artifacts"));
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        await File.WriteAllTextAsync(Path.Combine(packagePath, "artifacts", "partial.dat"), "unpublished bytes", _timeout.Token);
        await File.WriteAllTextAsync(manifestPath, "unpublished manifest", _timeout.Token);
        var attemptId = await SeedAbandonedAttemptAsync(packagePath, manifestPath);

        var admitted = await Store(Options()).WriteIntakeArtifactAsync(Intake(16 * KiB), _timeout.Token);

        admitted.SizeBytes.Should().Be(16 * KiB);
        Directory.Exists(packagePath).Should().BeFalse();
        File.Exists(manifestPath).Should().BeFalse();
        File.Exists(JournalPath(attemptId)).Should().BeFalse();
        Directory.Exists(Path.Combine(EvidenceRoot, "_staging", attemptId)).Should().BeFalse();
        foreach (var pair in before)
        {
            File.ReadAllBytes(pair.Key).Should().Equal(pair.Value);
        }
        StagingFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Restart_AbandonedReservationAfterIndexPublication_PreservesAllPublishedEvidence()
    {
        var source = await SourceAsync("published.dat", 8 * KiB);
        var published = await Store(Options()).WriteManifestAsync(Packet(Artifact("published", source)), Request, _timeout.Token);
        var identity = published.VaultIdentity!;
        var before = PublishedFiles().ToDictionary(path => path, File.ReadAllBytes);
        var packagePath = Path.Combine(EvidenceRoot, "_vault", identity.VaultId);
        var attemptId = await SeedAbandonedAttemptAsync(packagePath, Path.Combine(_root, identity.ManifestPath));

        var restarted = Store(Options());
        await restarted.WriteIntakeArtifactAsync(Intake(16 * KiB), _timeout.Token);

        foreach (var pair in before)
        {
            File.ReadAllBytes(pair.Key).Should().Equal(pair.Value);
        }
        File.Exists(JournalPath(attemptId)).Should().BeFalse();
        StagingFiles().Should().BeEmpty();
        (await restarted.TryGetVaultIdentityAsync(identity.VaultId, Request.TenantId!, Request.Scope!, _timeout.Token))
            .Should().NotBeNull();
    }

    private string JournalPath(string attemptId) => Path.Combine(_root, "workstation", "evidence-quota", $"{attemptId}.json");

    private async Task<string> SeedAbandonedAttemptAsync(string packagePath, string manifestPath)
    {
        // Model a killed writer: persisted charge and publication intent survive, while no
        // process holds its lifetime lease. Admission must recover through the real adapter.
        var attemptId = Guid.NewGuid().ToString("N");
        var stage = Path.Combine(EvidenceRoot, "_staging", attemptId);
        Directory.CreateDirectory(stage);
        await File.WriteAllTextAsync(Path.Combine(stage, "partial.dat"), "abandoned staged bytes", _timeout.Token);
        await File.WriteAllTextAsync(Path.Combine(stage, "publication.json"), JsonSerializer.Serialize(new
        {
            PackagePath = packagePath,
            ManifestPath = manifestPath,
            IndexPath = packagePath + ".json"
        }), _timeout.Token);
        Directory.CreateDirectory(Path.GetDirectoryName(JournalPath(attemptId))!);
        await File.WriteAllTextAsync(JournalPath(attemptId), JsonSerializer.Serialize(new
        {
            id = attemptId,
            tenantId = Request.TenantId,
            reservedBytes = 400 * KiB,
            writtenBytes = 16 * KiB,
            preparedBytes = 0,
            artifactCount = 1
        }), _timeout.Token);
        return attemptId;
    }

    private FileEvidenceArtifactStore Store(EvidenceStorageQuotaOptions options,
        Func<string, Stream>? sourceFactory = null, Func<string, long>? availableDiskBytes = null) => new(
        _root, NullLogger<FileEvidenceArtifactStore>.Instance, 256L * 1024 * 1024,
        exportArtifactSourceFactory: sourceFactory, quotaOptions: options,
        availableDiskBytes: availableDiskBytes ?? (_ => 100L * 1024 * 1024));

    private static EvidenceStorageQuotaOptions Options(long tenantBytes = 400 * KiB, long packageBytes = 1024 * KiB,
        int artifactCount = 8, long artifactBytes = 1024 * KiB, long diskHeadroom = 0) => new()
        {
            DefaultTenantBudgetBytes = tenantBytes,
            MaxPackageBytes = packageBytes,
            MaxArtifactsPerPackage = artifactCount,
            MaxArtifactBytes = artifactBytes,
            MinimumDiskHeadroomBytes = diskHeadroom
        };

    private async Task<string> SourceAsync(string name, int size)
    {
        var directory = Path.Combine(_root, "source");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        await File.WriteAllBytesAsync(path, new byte[size], _timeout.Token);
        return path;
    }

    private string EvidenceRoot => Path.Combine(_root, "workstation", "evidence");
    private string[] PublishedFiles() => Directory.Exists(EvidenceRoot)
        ? Directory.GetFiles(EvidenceRoot, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(EvidenceRoot, path).StartsWith($"_staging{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order().ToArray()
        : [];
    private string[] StagingFiles() => Directory.Exists(Path.Combine(EvidenceRoot, "_staging"))
        ? Directory.GetFiles(Path.Combine(EvidenceRoot, "_staging"), "*", SearchOption.AllDirectories)
        : [];
    private void AssertPublishedUnchanged(Dictionary<string, byte[]> before)
    {
        PublishedFiles().Should().Equal(before.Keys.Order());
        foreach (var pair in before)
        {
            File.ReadAllBytes(pair.Key).Should().Equal(pair.Value);
        }
    }

    private static EvidenceVaultIntakeRequestDto Intake(int bytes) => new(
        "report-pack", "quota-report", "api", "report.dat", Convert.ToBase64String(new byte[bytes]))
    {
        TenantId = Request.TenantId,
        Scope = Request.Scope
    };

    private static EvidenceArtifactRefDto Artifact(string id, string path) => new(
        id, "source-document", path, null, DateTimeOffset.UtcNow, null, true, "report-pack", "quota-report");

    private static EvidencePacketDto Packet(params EvidenceArtifactRefDto[] artifacts)
    {
        var subject = new EvidenceSubjectDto("quota-report", "report-pack", "Quota report", "Reporting", "/reporting", "ReportPack");
        var node = new EvidenceNodeDto("source", subject, "source-document", EvidenceStatusDto.Ready,
            new EvidenceFreshnessDto(DateTimeOffset.UtcNow, false, null), "test", "Report evidence", artifacts, []);
        return new(subject, DateTimeOffset.UtcNow, [node], [],
            new EvidenceCompletenessDto(100, EvidenceStatusDto.Ready, ["source"], ["source"], [], [], []), [], []);
    }

    public void Dispose()
    {
        _timeout.Cancel();
        _timeout.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class BlockingStream(Stream inner, TaskCompletionSource entered, TaskCompletionSource release) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return await inner.ReadAsync(buffer, ct);
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }
            base.Dispose(disposing);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ReadHookStream(int length, Action<int> onRead) : Stream
    {
        private int _position;
        private int _reads;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            onRead(++_reads);
            var count = Math.Min(Math.Min(buffer.Length, 8 * KiB), length - _position);
            buffer.Span[..count].Clear();
            _position += count;
            return ValueTask.FromResult(count);
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

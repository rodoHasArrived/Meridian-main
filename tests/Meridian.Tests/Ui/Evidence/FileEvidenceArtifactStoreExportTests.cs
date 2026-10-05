using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Evidence;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui.Evidence;

public sealed class FileEvidenceArtifactStoreExportTests : IDisposable
{
    private const long ArtifactLimit = 100L * 1024 * 1024;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-export", Guid.NewGuid().ToString("N"));
    private static readonly EvidencePacketExportRequest Request = new("operator", "retained export")
    {
        TenantId = "tenant-export",
        Scope = "company-export"
    };

    [Fact]
    public async Task NearLimitArtifact_RetainsExactBytesAndDigestAfterRestart()
    {
        var source = Source("near-limit.dat");
        await using (var file = File.Create(source))
        {
            file.SetLength(ArtifactLimit - 1);
        }

        string expectedHash;
        await using (var file = File.OpenRead(source))
        {
            expectedHash = await Sha256Digest.ComputeAsync(file);
        }
        var response = await Store().WriteManifestAsync(Packet(Artifact("near-limit", source, expectedHash)), Request);
        var identity = response.VaultIdentity!;
        var retained = identity.Artifacts.Should().ContainSingle().Which;
        retained.SizeBytes.Should().Be(ArtifactLimit - 1);
        retained.ContentHashSha256.Should().Be(expectedHash);
        var retainedPath = Path.Combine(_root, retained.RelativePath);
        new FileInfo(retainedPath).Length.Should().Be(ArtifactLimit - 1);
        await using (var file = File.OpenRead(retainedPath))
        {
            (await Sha256Digest.ComputeAsync(file)).Should().Be(expectedHash);
        }

        var restarted = Store();
        (await restarted.TryGetVaultIdentityAsync(identity.VaultId, Request.TenantId!, Request.Scope!))
            .Should().NotBeNull();
        var manifest = await restarted.TryOpenManifestByVaultIdAsync(identity.VaultId, Request.TenantId!, Request.Scope!);
        manifest.Should().NotBeNull();
        await manifest!.Content.DisposeAsync();
        StagingFiles().Should().BeEmpty();
    }

    [Theory]
    [InlineData(100L * 1024 * 1024)]
    [InlineData(100L * 1024 * 1024 - 17)]
    public async Task Copy_UsesBoundedReadsWithNonSeekableInput(long length)
    {
        using var source = new GeneratedStream(length);
        using var target = new CountingStream();
        var result = await FileEvidenceArtifactStore.CopyExportArtifactAsync(source, target, "bounded");
        result.SizeBytes.Should().Be(length);
        target.BytesWritten.Should().Be(length);
        source.LargestReadRequest.Should().BeLessThanOrEqualTo(FileEvidenceArtifactStore.ExportCopyBufferBytes);
        Sha256Digest.IsCanonical(result.ContentHashSha256).Should().BeTrue();
    }

    [Fact]
    public async Task SourceGrowsDuringCopy_ActualReadLimitBlocksPublicationAndRetrySucceeds()
    {
        var sourcePath = Source("growing.dat");
        await File.WriteAllTextAsync(sourcePath, "initially small");
        GeneratedStream? growing = null;
        var store = Store(_ => growing = new GeneratedStream(ArtifactLimit - 4, firstRead: stream => stream.AvailableLength = ArtifactLimit + 1));
        var packet = Packet(Artifact("growing", sourcePath));
        var export = () => store.WriteManifestAsync(packet, Request);
        await export.Should().ThrowAsync<InvalidOperationException>().WithMessage("*100 MB*");
        growing!.BytesRead.Should().BeGreaterThan(ArtifactLimit);
        growing.LargestReadRequest.Should().BeLessThanOrEqualTo(FileEvidenceArtifactStore.ExportCopyBufferBytes);
        EvidenceFiles().Should().BeEmpty();

        var retry = await Store().WriteManifestAsync(packet, Request);
        retry.Retained.Should().BeTrue();
        retry.VaultIdentity!.Artifacts.Single().SizeBytes.Should().Be(new FileInfo(sourcePath).Length);
        StagingFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task FileGrowsAfterInitialLengthCheck_RejectsTheAdditionalBytesAndCleansStage()
    {
        var sourcePath = Source("growing-file.dat");
        await using (var file = File.Create(sourcePath))
        {
            file.SetLength(1024 * 1024);
        }
        var store = Store(path => new ReadHookStream(
            new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                FileEvidenceArtifactStore.ExportCopyBufferBytes, FileOptions.Asynchronous),
            () =>
            {
                using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                writer.SetLength(ArtifactLimit + 1);
            }));
        var export = () => store.WriteManifestAsync(Packet(Artifact("growing-file", sourcePath)), Request);
        await export.Should().ThrowAsync<InvalidOperationException>().WithMessage("*100 MB*");
        new FileInfo(sourcePath).Length.Should().Be(ArtifactLimit + 1);
        EvidenceFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task HashMismatch_CleansAttemptAndPreservesPublishedPackage()
    {
        var sourcePath = Source("hash.dat");
        await File.WriteAllTextAsync(sourcePath, "approved evidence");
        var existing = await Store().WriteManifestAsync(Packet(Artifact("existing", sourcePath)), Request);
        var before = EvidenceFiles().ToDictionary(path => path, File.ReadAllBytes);
        var export = () => Store().WriteManifestAsync(Packet(Artifact("mismatch", sourcePath, new string('0', 64))), Request);
        await export.Should().ThrowAsync<InvalidOperationException>().WithMessage("*hash does not match*");
        AssertUnchanged(before);
        (await Store().TryGetVaultIdentityAsync(existing.VaultIdentity!.VaultId, Request.TenantId!, Request.Scope!))
            .Should().NotBeNull();
        StagingFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task CancellationMidwayThroughSecondArtifact_RemovesEarlierStagedArtifactAndCanRetry()
    {
        var first = Source("first.dat");
        var second = Source("second.dat");
        await File.WriteAllTextAsync(first, "first evidence");
        await File.WriteAllTextAsync(second, "second evidence");
        using var cancellation = new CancellationTokenSource();
        var store = Store(path => path == second
            ? new GeneratedStream(1024 * 1024, firstRead: _ => cancellation.Cancel())
            : File.OpenRead(path));
        var packet = Packet(Artifact("first", first), Artifact("second", second));
        var export = () => store.WriteManifestAsync(packet, Request, cancellation.Token);
        await export.Should().ThrowAsync<OperationCanceledException>();
        EvidenceFiles().Should().BeEmpty();
        var response = await Store().WriteManifestAsync(packet, Request);
        response.VaultIdentity!.Artifacts.Should().HaveCount(2);
        StagingFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task LaterArtifactReadFailure_RemovesWholeFailedAttempt()
    {
        var first = Source("first.dat");
        var second = Source("failed.dat");
        await File.WriteAllTextAsync(first, "first evidence");
        await File.WriteAllTextAsync(second, "second evidence");
        var store = Store(path => path == second
            ? new GeneratedStream(1024 * 1024, failAfterBytes: 8192)
            : File.OpenRead(path));
        var export = () => store.WriteManifestAsync(Packet(Artifact("first", first), Artifact("failed", second)), Request);
        await export.Should().ThrowAsync<IOException>().WithMessage("Injected source read failure.");
        EvidenceFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task ManifestDirectoryFailure_CleansStageAndPreservesOtherFiles()
    {
        var source = Source("publication.dat");
        await File.WriteAllTextAsync(source, "source evidence");
        var subjectDirectory = Path.Combine(EvidenceRoot, "report-pack", "export-test");
        Directory.CreateDirectory(Path.GetDirectoryName(subjectDirectory)!);
        await File.WriteAllTextAsync(subjectDirectory, "existing unrelated file blocks directory creation");
        var export = () => Store().WriteManifestAsync(Packet(Artifact("publication", source)), Request);
        await export.Should().ThrowAsync<IOException>();
        (await File.ReadAllTextAsync(subjectDirectory)).Should().Be("existing unrelated file blocks directory creation");
        Directory.EnumerateFiles(EvidenceRoot, "*.json", SearchOption.AllDirectories).Should().BeEmpty();
        StagingFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task IndexPublicationCollision_RollsBackMovedManifestAndArtifactsWithoutReplacingExistingIndex()
    {
        const string vaultId = "ev-012345678901234567890123";
        var manifestPath = Path.Combine(EvidenceRoot, "report-pack", "export-test", "candidate-manifest.json");
        var indexPath = Path.Combine(EvidenceRoot, "_vault", $"{vaultId}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        await File.WriteAllTextAsync(indexPath, "previously published index");
        await using (var publication = new FileEvidenceArtifactStore.ExportPublication(Store(), vaultId, manifestPath))
        {
            await File.WriteAllTextAsync(Path.Combine(publication.ArtifactDirectory, "owned.dat"), "staged artifact");
            var publish = () => publication.PublishAsync("{}", "{}", CancellationToken.None);
            await publish.Should().ThrowAsync<IOException>();
        }

        (await File.ReadAllTextAsync(indexPath)).Should().Be("previously published index");
        File.Exists(manifestPath).Should().BeFalse();
        Directory.Exists(Path.Combine(EvidenceRoot, "_vault", vaultId)).Should().BeFalse();
        StagingFiles().Should().BeEmpty();
        EvidenceFiles().Should().Equal(indexPath);
    }

    [Fact]
    public async Task IncompleteCleanup_PreventsAnotherAttemptFromReusingItsManifestPath()
    {
        var manifest = Path.Combine(EvidenceRoot, "report-pack", "export-test", "shared-manifest.json");
        var oldPackage = Path.Combine(EvidenceRoot, "_vault", "ev-old");
        var oldStage = Path.Combine(EvidenceRoot, "_staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(oldStage);
        var intent = System.Text.Json.JsonSerializer.Serialize(new
        {
            PackagePath = oldPackage,
            ManifestPath = manifest,
            IndexPath = oldPackage + ".json"
        });
        // Final-path cleanup succeeded, but deletion of this attempt's stage previously failed.
        // A later recovery must never be able to delete a new owner's manifest at the same path.
        await File.WriteAllTextAsync(Path.Combine(oldStage, "publication.json"), intent);
        await using (var publication = new FileEvidenceArtifactStore.ExportPublication(Store(), "ev-new", manifest))
        {
            var publish = () => publication.PublishAsync("{}", "{}", CancellationToken.None);
            await publish.Should().ThrowAsync<IOException>().WithMessage("*earlier attempt*");
        }
        File.Exists(manifest).Should().BeFalse();
        Directory.Exists(Path.Combine(EvidenceRoot, "_vault", "ev-new")).Should().BeFalse();
        (await File.ReadAllTextAsync(Path.Combine(oldStage, "publication.json"))).Should().Be(intent);
    }

    [Fact]
    public async Task OversizedFileBeforeCopy_BlocksWithoutPublishedOrStagedFiles()
    {
        var source = Source("oversized.dat");
        await using (var file = File.Create(source))
        {
            file.SetLength(ArtifactLimit + 1);
        }
        var export = () => Store().WriteManifestAsync(Packet(Artifact("oversized", source)), Request);
        await export.Should().ThrowAsync<InvalidOperationException>().WithMessage("*100 MB*");
        EvidenceFiles().Should().BeEmpty();
    }

    private FileEvidenceArtifactStore Store(Func<string, Stream>? factory = null) => new(
        _root, NullLogger<FileEvidenceArtifactStore>.Instance, 256L * 1024 * 1024,
        exportArtifactSourceFactory: factory);

    private string EvidenceRoot => Path.Combine(_root, "workstation", "evidence");
    private string Source(string fileName)
    {
        Directory.CreateDirectory(Path.Combine(_root, "source"));
        return Path.Combine(_root, "source", fileName);
    }

    private string[] EvidenceFiles() => Directory.Exists(EvidenceRoot)
        ? Directory.GetFiles(EvidenceRoot, "*", SearchOption.AllDirectories).Order().ToArray()
        : [];
    private string[] StagingFiles() => Directory.Exists(Path.Combine(EvidenceRoot, "_staging"))
        ? Directory.GetFiles(Path.Combine(EvidenceRoot, "_staging"), "*", SearchOption.AllDirectories)
        : [];
    private void AssertUnchanged(Dictionary<string, byte[]> before)
    {
        EvidenceFiles().Should().Equal(before.Keys.Order());
        foreach (var pair in before)
        {
            File.ReadAllBytes(pair.Key).Should().Equal(pair.Value);
        }
    }

    private static EvidenceArtifactRefDto Artifact(string id, string path, string? hash = null) => new(
        id, "source-document", path, null, DateTimeOffset.UtcNow, hash, true, "report-pack", "export-test");

    private static EvidencePacketDto Packet(params EvidenceArtifactRefDto[] artifacts)
    {
        var subject = new EvidenceSubjectDto("export-test", "report-pack", "Export test", "Reporting", "/reporting", "ReportPack");
        var node = new EvidenceNodeDto("source", subject, "source-document", EvidenceStatusDto.Ready,
            new EvidenceFreshnessDto(DateTimeOffset.UtcNow, false, null), "test", "Export source", artifacts, []);
        return new(subject, DateTimeOffset.UtcNow, [node], [],
            new EvidenceCompletenessDto(100, EvidenceStatusDto.Ready, ["source"], ["source"], [], [], []), [], []);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class GeneratedStream(long length, Action<GeneratedStream>? firstRead = null, long? failAfterBytes = null) : Stream
    {
        public long AvailableLength { get; set; } = length;
        public long BytesRead { get; private set; }
        public int LargestReadRequest { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            LargestReadRequest = Math.Max(LargestReadRequest, buffer.Length);
            if (failAfterBytes is { } failure && BytesRead >= failure)
            {
                throw new IOException("Injected source read failure.");
            }
            var count = (int)Math.Min(buffer.Length, AvailableLength - BytesRead);
            buffer.Span[..count].Fill(0x5a);
            var isFirst = BytesRead == 0;
            BytesRead += count;
            if (isFirst)
            {
                firstRead?.Invoke(this);
            }
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

    private sealed class CountingStream : Stream
    {
        public long BytesWritten { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => BytesWritten;
        public override long Position { get => BytesWritten; set => throw new NotSupportedException(); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            BytesWritten += buffer.Length;
            return ValueTask.CompletedTask;
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => BytesWritten += count;
    }

    private sealed class ReadHookStream(Stream inner, Action firstRead) : Stream
    {
        private bool _read;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var count = await inner.ReadAsync(buffer, ct);
            if (!_read)
            {
                _read = true;
                firstRead();
            }
            return count;
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
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

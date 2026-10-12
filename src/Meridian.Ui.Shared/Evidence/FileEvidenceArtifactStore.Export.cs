using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Documents;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Evidence;

public sealed partial class FileEvidenceArtifactStore
{
    internal const int ExportCopyBufferBytes = 64 * 1024;
    internal sealed record ExportArtifactCopyResult(string ContentHashSha256, long SizeBytes);

    private static Stream OpenExportArtifact(string sourcePath) => new FileStream(
        sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
        ExportCopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);

    internal static async Task<ExportArtifactCopyResult> CopyExportArtifactAsync(
        Stream source, Stream destination, string artifactId, CancellationToken ct = default,
        EvidenceStorageReservation? reservation = null, long artifactByteLimit = MaxRetainedArtifactBytes)
    {
        // The canonical digest consumes a bounded tee: every byte it hashes is also written to
        // the private staged artifact. Neither the source length nor a pre-copy stat is authority.
        using var copy = new ExportCopyStream(source, destination, artifactId, reservation, artifactByteLimit);
        var hash = await Sha256Digest.ComputeAsync(copy, ct).ConfigureAwait(false);
        return new ExportArtifactCopyResult(hash, copy.BytesCopied);
    }

    private sealed class ExportCopyStream(Stream source, Stream destination, string artifactId,
        EvidenceStorageReservation? reservation, long artifactByteLimit) : Stream
    {
        public long BytesCopied { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesCopied; set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var count = await source.ReadAsync(buffer[..Math.Min(buffer.Length, ExportCopyBufferBytes)], ct)
                .ConfigureAwait(false);
            if (count > MaxRetainedArtifactBytes - BytesCopied)
            {
                throw new InvalidOperationException($"Retained artifact '{artifactId}' exceeds the 100 MB vault artifact limit.");
            }

            if (count > artifactByteLimit - BytesCopied)
            {
                throw new EvidenceStorageQuotaExceededException("artifact-bytes", "Export exceeds the configured artifact byte limit.");
            }
            if (count == 0)
            {
                return 0;
            }
            if (reservation is not null)
            {
                await reservation.BeforeWriteAsync(count, ct).ConfigureAwait(false);
            }
            await destination.WriteAsync(buffer[..count], ct).ConfigureAwait(false);
            if (reservation is not null)
            {
                // FileStream may buffer a short read in user space. Make its disk allocation
                // visible to the headroom probe before crediting these bytes as written.
                await destination.FlushAsync(ct).ConfigureAwait(false);
                await reservation.AfterWriteAsync(count, CancellationToken.None).ConfigureAwait(false);
            }
            BytesCopied += count;
            return count;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("Export copying requires asynchronous reads.");
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// Owns one unpublished attempt. Publication intent is durable before any final-path move;
    /// the quota coordinator holds the root lock until the index-last transaction finishes.
    /// </summary>
    internal sealed class ExportPublication : IAsyncDisposable
    {
        private readonly FileEvidenceArtifactStore _store;
        private readonly EvidenceStorageReservation? _reservation;
        private readonly string _attemptId;
        private readonly string _stagingDirectory;
        private readonly string _stagedPackageDirectory;
        private readonly string _packageDirectory;
        private readonly string _manifestPath;
        private readonly string _indexPath;

        public ExportPublication(FileEvidenceArtifactStore store, string vaultId, string manifestPath,
            EvidenceStorageReservation? reservation = null)
        {
            _store = store;
            _reservation = reservation;
            _attemptId = reservation?.Id ?? Guid.NewGuid().ToString("N");
            _stagingDirectory = Path.Combine(store._rootDirectory, "_staging", _attemptId);
            _stagedPackageDirectory = Path.Combine(_stagingDirectory, "package");
            _packageDirectory = Path.Combine(store._rootDirectory, "_vault", vaultId);
            _manifestPath = manifestPath;
            _indexPath = Path.Combine(store._rootDirectory, "_vault", $"{vaultId}.json");
            ArtifactDirectory = Path.Combine(_stagedPackageDirectory, "artifacts");
            Directory.CreateDirectory(ArtifactDirectory);
        }

        public string ArtifactDirectory { get; }

        public async Task PublishAsync(string manifestJson, string identityJson, CancellationToken ct)
        {
            var stagedManifest = Path.Combine(_stagingDirectory, "manifest.json");
            var stagedIndex = Path.Combine(_stagingDirectory, "index.json");
            await WriteMetadataAsync(stagedManifest, manifestJson, ct).ConfigureAwait(false);
            await WriteMetadataAsync(stagedIndex, identityJson, ct).ConfigureAwait(false);
            if (_reservation is null)
            {
                await PublishFilesAsync(ct).ConfigureAwait(false);
            }
            else
            {
                await _reservation.PublishAsync(PublishFilesAsync, ct).ConfigureAwait(false);
            }
        }

        private async Task WriteMetadataAsync(string path, string json, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetByteCount(json);
            if (_reservation is not null)
            {
                await _reservation.BeforeWriteAsync(bytes, ct).ConfigureAwait(false);
            }
            await AtomicFileWriter.WriteAsync(path, json, ct).ConfigureAwait(false);
            if (_reservation is not null)
            {
                await _reservation.AfterWriteAsync(bytes, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private async Task PublishFilesAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            // All targets must be absent before durable intent grants this attempt ownership.
            // This check and all moves run under the same cross-process quota lock. Recovery runs
            // under that lock too, before another operation can claim an abandoned target.
            _store.EnsurePublicationTargetsUnclaimed(_attemptId, _packageDirectory, _manifestPath, _indexPath);
            if (Path.Exists(_packageDirectory) || Path.Exists(_manifestPath) || Path.Exists(_indexPath))
            {
                throw new IOException("Evidence publication would replace an existing package, manifest, or index.");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_packageDirectory)!);
            if (Path.GetDirectoryName(_manifestPath) != _packageDirectory)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_manifestPath)!);
            }
            await AtomicFileWriter.WriteAsync(Path.Combine(_stagingDirectory, "publication.json"),
                JsonSerializer.Serialize(new StoragePublicationIntent(_packageDirectory, _manifestPath, _indexPath),
                    StoragePublicationJsonContext.Default.StoragePublicationIntent), ct)
                .ConfigureAwait(false);
            Directory.Move(_stagedPackageDirectory, _packageDirectory);
            await AtomicFileWriter.SyncDirectoryAsync(Path.GetDirectoryName(_packageDirectory)!, CancellationToken.None)
                .ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            File.Move(Path.Combine(_stagingDirectory, "manifest.json"), _manifestPath, overwrite: false);
            await AtomicFileWriter.SyncDirectoryAsync(Path.GetDirectoryName(_manifestPath)!, CancellationToken.None)
                .ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            File.Move(Path.Combine(_stagingDirectory, "index.json"), _indexPath, overwrite: false);
            // Once visible, preserve evidence even if flushing or the reservation commit fails.
            await AtomicFileWriter.SyncDirectoryAsync(Path.GetDirectoryName(_indexPath)!, CancellationToken.None)
                .ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            // Reservations run recovery under the root lock and retain the charge if cleanup
            // fails. The compatibility constructor is used only by publication primitive tests.
            if (_reservation is null)
            {
                await _store.RecoverStorageAttemptAsync(_attemptId, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}

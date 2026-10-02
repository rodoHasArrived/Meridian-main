using Meridian.Contracts.Integrity;
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
        Stream source, Stream destination, string artifactId, CancellationToken ct = default)
    {
        // The canonical digest consumes a bounded tee: every byte it hashes is also written to
        // the private staged artifact. Neither the source length nor a pre-copy stat is authority.
        using var copy = new ExportCopyStream(source, destination, artifactId);
        var hash = await Sha256Digest.ComputeAsync(copy, ct).ConfigureAwait(false);
        return new ExportArtifactCopyResult(hash, copy.BytesCopied);
    }

    private sealed class ExportCopyStream(Stream source, Stream destination, string artifactId) : Stream
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

            await destination.WriteAsync(buffer[..count], ct).ConfigureAwait(false);
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
    /// Owns one unpublished attempt. Staged files are invisible to scoped vault readers, which
    /// require a top-level index and its matching manifest. The index is published last.
    /// </summary>
    internal sealed class ExportPublication : IAsyncDisposable
    {
        private readonly FileEvidenceArtifactStore _store;
        private readonly string _stagingDirectory;
        private readonly string _stagedPackageDirectory;
        private readonly string _packageDirectory;
        private readonly string _manifestPath;
        private readonly string _indexPath;
        private bool _ownsPackage;
        private bool _ownsManifest;
        private bool _committed;

        public ExportPublication(FileEvidenceArtifactStore store, string vaultId, string manifestPath)
        {
            _store = store;
            _stagingDirectory = Path.Combine(store._rootDirectory, "_staging", Guid.NewGuid().ToString("N"));
            _stagedPackageDirectory = Path.Combine(_stagingDirectory, "package");
            _packageDirectory = Path.Combine(store._rootDirectory, "_vault", vaultId);
            _manifestPath = manifestPath;
            _indexPath = Path.Combine(store._rootDirectory, "_vault", $"{vaultId}.json");
            ArtifactDirectory = Path.Combine(_stagedPackageDirectory, "artifacts");
            try
            {
                Directory.CreateDirectory(ArtifactDirectory);
            }
            catch
            {
                Cleanup(_stagingDirectory, directory: true);
                throw;
            }
        }

        public string ArtifactDirectory { get; }

        public async Task PublishAsync(string manifestJson, string identityJson, CancellationToken ct)
        {
            var stagedManifest = Path.Combine(_stagingDirectory, "manifest.json");
            var stagedIndex = Path.Combine(_stagingDirectory, "index.json");
            await AtomicFileWriter.WriteAsync(stagedManifest, manifestJson, ct)
                .ConfigureAwait(false);
            await AtomicFileWriter.WriteAsync(stagedIndex, identityJson, ct)
                .ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(_packageDirectory)!);
            Directory.CreateDirectory(Path.GetDirectoryName(_manifestPath)!);

            // No overwrite at any publication step: a colliding attempt cannot replace retained
            // evidence, and rollback owns only paths whose move actually succeeded.
            Directory.Move(_stagedPackageDirectory, _packageDirectory);
            _ownsPackage = true;
            await AtomicFileWriter.SyncDirectoryAsync(Path.GetDirectoryName(_packageDirectory)!, CancellationToken.None)
                .ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            File.Move(stagedManifest, _manifestPath, overwrite: false);
            _ownsManifest = true;
            await AtomicFileWriter.SyncDirectoryAsync(Path.GetDirectoryName(_manifestPath)!, CancellationToken.None)
                .ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            File.Move(stagedIndex, _indexPath, overwrite: false);
            _committed = true;
            // Once the readable index exists, caller cancellation cannot turn success into a
            // failed retry, and cleanup cannot dismantle the complete readable package.
            // Directory durability uses the same primitive as AtomicFileWriter.
            await AtomicFileWriter.SyncDirectoryAsync(Path.GetDirectoryName(_indexPath)!, CancellationToken.None)
                .ConfigureAwait(false);
        }

        public ValueTask DisposeAsync()
        {
            if (!_committed)
            {
                if (_ownsManifest)
                {
                    Cleanup(_manifestPath, directory: false);
                }
                if (_ownsPackage)
                {
                    Cleanup(_packageDirectory, directory: true);
                }
            }

            Cleanup(_stagingDirectory, directory: true);
            return ValueTask.CompletedTask;
        }

        private void Cleanup(string path, bool directory)
        {
            // All directories here are either our private GUID stage or a directory moved from
            // that stage without overwrite. Never sweep the subject or shared _vault directory.
            try
            {
                if (directory)
                {
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, recursive: true);
                    }
                }
                else
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _store._logger.LogWarning(ex, "Failed evidence export cleanup could not remove owned path {Path}.", path);
            }
        }
    }
}

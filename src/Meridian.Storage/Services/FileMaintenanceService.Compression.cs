using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using Meridian.Contracts.Integrity;
using Meridian.Storage.Archival;

namespace Meridian.Storage.Services;

public sealed partial class FileMaintenanceService
{
    /// <summary>Losslessly gzip healthy plain JSONL archives without claiming to repair their contents.</summary>
    public async Task<FileCompressionResult> CompressAsync(FileCompressionOptions options, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var recoveryCopies = new List<string>();
        var files = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var paths = options.Paths is { Length: > 0 } && options.Paths[0] != "*"
            ? options.Paths : [_options.RootPath];
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                             .Where(file => file.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)))
                    files.Add(Path.GetFullPath(file));
            }
            else if (File.Exists(path) && path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
                files.Add(Path.GetFullPath(path));
            else
                errors.Add($"Compression target is not a readable directory or plain .jsonl file: {path}");
        }

        var healthyFiles = new List<string>();
        foreach (var file in files.Order(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < options.MinimumQuietPeriod)
                {
                    errors.Add($"Compression deferred for recently written file {file}; it may still be open for appends.");
                    continue;
                }
                var issues = new ConcurrentBag<HealthIssue>();
                await CheckFileHealthAsync(new FileInfo(file), new HealthCheckOptions(), issues, ct);
                var blockers = issues.Where(issue => issue.Severity >= IssueSeverity.Warning).ToList();
                if (blockers.Count > 0)
                    errors.Add($"Compression refused for unhealthy file {file}: {string.Join(", ", blockers.Select(issue => issue.Type))}. Repair or restore its source evidence first.");
                else
                    healthyFiles.Add(file);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"Compression health check failed for {file}: {ex.Message}");
            }
        }

        if (options.DryRun)
            return new FileCompressionResult(files.Count, 0, errors, true,
                healthyFiles.Select(file => $"gzip: {file}").ToList());

        if (!await CreateRequiredBackupsAsync(healthyFiles, options.BackupBeforeCompression,
                options.BackupPath, "compression", recoveryCopies, errors, ct))
            return new FileCompressionResult(files.Count, 0, errors, false) { RecoveryCopies = recoveryCopies };

        var compressed = 0;
        long bytesBefore = 0;
        long bytesAfter = 0;
        foreach (var file in healthyFiles)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var sourceLength = new FileInfo(file).Length;
                if (!await RecompressAsync(file, ct))
                {
                    errors.Add($"Compression made no change for {file}.");
                    continue;
                }
                compressed++;
                bytesBefore += sourceLength;
                bytesAfter += new FileInfo(file + ".gz").Length;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"Compression failed for {file}: {ex.Message}");
            }
        }
        return new FileCompressionResult(files.Count, compressed, errors, false)
        { RecoveryCopies = recoveryCopies, BytesBefore = bytesBefore, BytesAfter = bytesAfter };
    }

    private static async Task<bool> CreateRequiredBackupsAsync(
        IEnumerable<string> paths, bool required, string? backupPath, string operation,
        List<string> recoveryCopies, List<string> errors, CancellationToken ct)
    {
        if (!required)
            return true;
        // Complete every required backup before any source mutation. Each attempt and source
        // has a distinct create-only destination, including the original checksum evidence.
        var errorCount = errors.Count;
        var operationId = Guid.NewGuid().ToString("N");
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                foreach (var source in File.Exists(path + ".sha256") ? new[] { path, path + ".sha256" } : new[] { path })
                {
                    string backupFile;
                    if (!string.IsNullOrWhiteSpace(backupPath))
                    {
                        var directory = Path.Combine(backupPath, operationId);
                        Directory.CreateDirectory(directory);
                        var pathDigest = Sha256Digest.ComputeUtf8(Path.GetFullPath(source));
                        backupFile = Path.Combine(directory, $"{Path.GetFileName(source)}.{pathDigest}.bak");
                    }
                    else
                        backupFile = source + $".pre-{operation}.{operationId}.bak";
                    File.Copy(source, backupFile, overwrite: false);
                    using (var stream = new FileStream(backupFile, FileMode.Open, FileAccess.Write, FileShare.Read))
                        stream.Flush(flushToDisk: true);
                    await AtomicFileWriter.SyncDirectoryAsync(Path.GetDirectoryName(backupFile)!, CancellationToken.None);
                    recoveryCopies.Add(backupFile);
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Backup failed for {path}; no source files were changed: {ex.Message}");
            }
        }
        return errors.Count == errorCount;
    }

    // Per-instance test seam; never shared across maintenance operations or configured by input.
    internal Action<CompressionPublicationStage>? CompressionCheckpoint { get; init; }

    private async Task<bool> RecompressAsync(string filePath, CancellationToken ct)
    {
        if (!filePath.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
            return false;
        var outputPath = filePath + ".gz";
        var outputSidecar = outputPath + ".sha256";
        if (File.Exists(outputSidecar) && !File.Exists(outputPath))
            throw new IOException($"Refusing to replace existing compression checksum without its archive: {outputSidecar}");

        // Fence live writers for the whole verify-publish-delete window. JsonlStorageSink reopens a
        // closed path on demand, so the source must be proven unchanged before publication and
        // again before deletion; a late append keeps the source and withdraws our new archive.
        FileStream source;
        try
        {
            // Windows refuses this open while a writer holds the file and blocks new writers for the
            // fence's lifetime; readers may share it. Unix locks cannot tell readers from writers, so
            // there the quiet period and the unchanged-source checks below are the fence.
            source = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        }
        catch (IOException ex)
        {
            throw new IOException($"Refusing to compress a file that is open for writing: {filePath}", ex);
        }

        await using (source)
        {
            var sourceChecksum = await ComputeChecksumAsync(source, ct);
            var sourceStamp = SourceStamp(filePath);
            var retainSidecar = File.Exists(filePath + ".sha256");
            if (File.Exists(outputPath))
            {
                // A crash after create-only publication can leave both complete representations.
                // Resume cleanup only after proving equality; never overwrite a retained destination.
                try
                {
                    await using var retained = new FileStream(outputPath, FileMode.Open,
                        FileAccess.ReadWrite, FileShare.Read);
                    await VerifyCompressedSourceAsync(filePath, source, retained, sourceChecksum, ct);
                    retained.Flush(flushToDisk: true);
                    if (File.Exists(outputSidecar))
                        await VerifyChecksumSidecarAsync(outputSidecar, retained, ct);
                    await AtomicFileWriter.SyncDirectoryAsync(Path.GetDirectoryName(outputPath)!, CancellationToken.None);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new IOException($"Refusing to replace existing compression destination; recovery verification failed: {outputPath}", ex);
                }
                CompressionCheckpoint?.Invoke(CompressionPublicationStage.PublicationSynced);
                if (retainSidecar && !File.Exists(outputSidecar))
                    await WriteChecksumSidecarAsync(outputPath, ct);
                RequireUnchangedSource(filePath, sourceStamp);
                await CompleteCompressionCleanupAsync(filePath);
                return true;
            }
            var stagedPath = outputPath + $".{Guid.NewGuid():N}.recompress.tmp";
            try
            {
                await AtomicFileWriter.WriteStreamAsync(stagedPath, async output =>
                {
                    source.Position = 0;
                    await using var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true);
                    await source.CopyToAsync(gzip, ct);
                }, ct);
                // Verify the actual decompressed bytes before publishing and removing the source.
                await using (var staged = File.OpenRead(stagedPath))
                    await VerifyCompressedSourceAsync(filePath, source, staged, sourceChecksum, ct);
                CompressionCheckpoint?.Invoke(CompressionPublicationStage.StagedVerified);
                ct.ThrowIfCancellationRequested();
                RequireUnchangedSource(filePath, sourceStamp);
                File.Move(stagedPath, outputPath, overwrite: false);
                CompressionCheckpoint?.Invoke(CompressionPublicationStage.Published);
                await AtomicFileWriter.SyncDirectoryAsync(Path.GetDirectoryName(outputPath)!, CancellationToken.None);
                CompressionCheckpoint?.Invoke(CompressionPublicationStage.PublicationSynced);
            }
            finally
            {
                File.Delete(stagedPath);
            }
            // Carry integrity evidence to the archive before the source's sidecar is removed;
            // health checks verify a file only against its own sidecar.
            if (retainSidecar)
                await WriteChecksumSidecarAsync(outputPath, ct);
            if (SourceStamp(filePath) != sourceStamp)
            {
                // Appended after verification: the archive we just published is incomplete.
                File.Delete(outputSidecar);
                File.Delete(outputPath);
                await AtomicFileWriter.SyncDirectoryAsync(Path.GetDirectoryName(outputPath)!, CancellationToken.None);
                throw new IOException($"Source changed during compression; kept the source and withdrew the archive: {filePath}");
            }
            await CompleteCompressionCleanupAsync(filePath);
            return true;
        }
    }

    private static (long Length, DateTime LastWriteUtc) SourceStamp(string filePath)
    {
        var info = new FileInfo(filePath);
        return (info.Length, info.LastWriteTimeUtc);
    }

    private static void RequireUnchangedSource(string filePath, (long Length, DateTime LastWriteUtc) expected)
    {
        if (SourceStamp(filePath) != expected)
            throw new IOException($"Source changed during compression; it was not replaced: {filePath}");
    }

    private static async Task VerifyCompressedSourceAsync(
        string filePath, FileStream source, Stream compressed, string sourceChecksum, CancellationToken ct)
    {
        await using var gzip = new GZipStream(compressed, CompressionMode.Decompress, leaveOpen: true);
        var readbackChecksum = await Sha256Digest.ComputeAsync(gzip, ct);
        var currentSourceChecksum = await ComputeChecksumAsync(source, ct);
        if (!Sha256Digest.FixedEquals(sourceChecksum, readbackChecksum) ||
            !Sha256Digest.FixedEquals(sourceChecksum, currentSourceChecksum))
            throw new IOException($"Compression readback did not match the unchanged source: {filePath}");
    }

    private static async Task<string> ComputeChecksumAsync(FileStream stream, CancellationToken ct)
    {
        stream.Position = 0;
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
    }

    private async Task WriteChecksumSidecarAsync(string archivePath, CancellationToken ct)
    {
        var checksum = await ComputeChecksumAsync(archivePath, ct);
        await AtomicFileWriter.WriteAsync(archivePath + ".sha256", $"{checksum}  {Path.GetFileName(archivePath)}", ct);
        await AtomicFileWriter.SyncDirectoryAsync(Path.GetDirectoryName(archivePath)!, CancellationToken.None);
    }

    private static async Task VerifyChecksumSidecarAsync(string sidecarPath, FileStream archive, CancellationToken ct)
    {
        var expected = (await File.ReadAllTextAsync(sidecarPath, ct)).Split(' ', 2)[0].Trim();
        if (!string.Equals(expected, await ComputeChecksumAsync(archive, ct), StringComparison.OrdinalIgnoreCase))
            throw new IOException($"Compression checksum does not match its archive: {sidecarPath}");
    }

    private async Task CompleteCompressionCleanupAsync(string filePath)
    {
        // JsonlArchiveFiles makes this recoverable overlap one logical input to all replay
        // readers. Do not move deletion before durable publication: that would risk data loss.
        File.Delete(filePath);
        CompressionCheckpoint?.Invoke(CompressionPublicationStage.SourceDeleted);
        var staleSidecar = filePath + ".sha256";
        if (File.Exists(staleSidecar))
            File.Delete(staleSidecar);
        CompressionCheckpoint?.Invoke(CompressionPublicationStage.SidecarDeleted);
        await AtomicFileWriter.SyncDirectoryAsync(Path.GetDirectoryName(filePath)!, CancellationToken.None);
    }
}

internal enum CompressionPublicationStage
{
    StagedVerified,
    Published,
    PublicationSynced,
    SourceDeleted,
    SidecarDeleted
}

public sealed record FileCompressionOptions(
    bool DryRun = false,
    bool BackupBeforeCompression = true,
    string? BackupPath = null,
    string[]? Paths = null,
    TimeSpan MinimumQuietPeriod = default);

public sealed record FileCompressionResult(
    int FilesProcessed,
    int FilesCompressed,
    IReadOnlyList<string> Errors,
    bool DryRun,
    IReadOnlyList<string>? PlannedActions = null)
{
    public IReadOnlyList<string> RecoveryCopies { get; init; } = [];
    public long BytesBefore { get; init; }
    public long BytesAfter { get; init; }
    public int FilesUncompressed => Math.Max(0, FilesProcessed - FilesCompressed);
    public bool Success => Errors.Count == 0 && (DryRun || FilesCompressed == FilesProcessed);
}

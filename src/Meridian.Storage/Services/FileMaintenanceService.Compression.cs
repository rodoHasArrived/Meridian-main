using System.Collections.Concurrent;
using System.IO.Compression;
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

    private async Task<bool> RecompressAsync(string filePath, CancellationToken ct)
    {
        if (!filePath.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
            return false;
        var outputPath = filePath + ".gz";
        if (File.Exists(outputPath) || File.Exists(outputPath + ".sha256"))
            throw new IOException($"Refusing to replace existing compression destination or checksum: {outputPath}");

        var sourceChecksum = await ComputeChecksumAsync(filePath, ct);
        var stagedPath = outputPath + $".{Guid.NewGuid():N}.recompress.tmp";
        try
        {
            await AtomicFileWriter.WriteStreamAsync(stagedPath, async output =>
            {
                await using var input = File.OpenRead(filePath);
                await using var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true);
                await input.CopyToAsync(gzip, ct);
            }, ct);
            // Verify the actual decompressed bytes before publishing and removing the source.
            await using (var staged = File.OpenRead(stagedPath))
            await using (var gzip = new GZipStream(staged, CompressionMode.Decompress))
            {
                var readbackChecksum = await Sha256Digest.ComputeAsync(gzip, ct);
                var currentSourceChecksum = await ComputeChecksumAsync(filePath, ct);
                if (!Sha256Digest.FixedEquals(sourceChecksum, readbackChecksum) ||
                    !Sha256Digest.FixedEquals(sourceChecksum, currentSourceChecksum))
                    throw new IOException($"Compression readback did not match the unchanged source: {filePath}");
            }
            ct.ThrowIfCancellationRequested();
            File.Move(stagedPath, outputPath, overwrite: false);
            await AtomicFileWriter.SyncDirectoryAsync(Path.GetDirectoryName(outputPath)!, CancellationToken.None);
        }
        finally
        {
            File.Delete(stagedPath);
        }
        File.Delete(filePath);
        var staleSidecar = filePath + ".sha256";
        if (File.Exists(staleSidecar))
            File.Delete(staleSidecar);
        await AtomicFileWriter.SyncDirectoryAsync(Path.GetDirectoryName(filePath)!, CancellationToken.None);
        return true;
    }
}

public sealed record FileCompressionOptions(
    bool DryRun = false,
    bool BackupBeforeCompression = true,
    string? BackupPath = null,
    string[]? Paths = null);

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

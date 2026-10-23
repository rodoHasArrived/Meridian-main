using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
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
        var orphanSidecars = new List<string>();
        var paths = options.Paths is { Length: > 0 } && options.Paths[0] != "*"
            ? options.Paths : [_options.RootPath];
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    if (file.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
                        files.Add(Path.GetFullPath(file));
                    else if (file.EndsWith(".jsonl.sha256", StringComparison.OrdinalIgnoreCase))
                        orphanSidecars.Add(Path.GetFullPath(file));
                }
            }
            else if (File.Exists(path) && path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
                files.Add(Path.GetFullPath(path));
            else
                errors.Add($"Compression target is not a readable directory or plain .jsonl file: {path}");
        }

        // A crash between deleting a compressed source and its sidecar leaves x.jsonl.sha256
        // beside x.jsonl.gz with no x.jsonl. Retire it so it cannot be applied to other bytes.
        var plannedSidecarRemovals = new List<string>();
        foreach (var sidecar in orphanSidecars)
        {
            var source = sidecar[..^".sha256".Length];
            if (File.Exists(source) || !File.Exists(source + ".gz"))
                continue;
            if (options.DryRun)
            {
                plannedSidecarRemovals.Add($"remove orphaned checksum: {sidecar}");
                continue;
            }
            try
            {
                File.Delete(sidecar);
                await AtomicFileWriter.SyncDirectoryAsync(Path.GetDirectoryName(sidecar)!, CancellationToken.None);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"Could not remove orphaned checksum {sidecar}: {ex.Message}");
            }
        }

        // Deferral is the expected outcome for a live period, not a failure: it is reported
        // separately and excluded from the processed count that success is judged against.
        var deferred = new List<string>();
        var healthyFiles = new List<string>();
        foreach (var file in files.Order(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (options.OnlyClosedPeriods && PartitionPeriodEnd(file) is { } periodEnd && periodEnd > DateTimeOffset.UtcNow)
                {
                    deferred.Add($"{file}: partition period is still open for appends");
                    continue;
                }
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < options.MinimumQuietPeriod)
                {
                    deferred.Add($"{file}: written within the quiet period and may still be open for appends");
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
            return new FileCompressionResult(files.Count - deferred.Count, 0, errors, true,
                plannedSidecarRemovals.Concat(healthyFiles.Select(file => $"gzip: {file}")).ToList())
            { Deferred = deferred };

        var backupsBySource = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!await CreateRequiredBackupsAsync(healthyFiles, options.BackupBeforeCompression,
                options.BackupPath, "compression", recoveryCopies, errors, ct, backupsBySource))
            return new FileCompressionResult(files.Count - deferred.Count, 0, errors, false)
            { RecoveryCopies = recoveryCopies, Deferred = deferred };

        var compressed = 0;
        long bytesBefore = 0;
        long bytesAfter = 0;
        CompressionCheckpoint?.Invoke(CompressionPublicationStage.BackupsCompleted);
        foreach (var file in healthyFiles)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var sourceLength = new FileInfo(file).Length;
                if (!await RecompressAsync(file, ct, backupsBySource.GetValueOrDefault(file)))
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
        return new FileCompressionResult(files.Count - deferred.Count, compressed, errors, false)
        { RecoveryCopies = recoveryCopies, BytesBefore = bytesBefore, BytesAfter = bytesAfter, Deferred = deferred };
    }

    private static async Task<bool> CreateRequiredBackupsAsync(
        IEnumerable<string> paths, bool required, string? backupPath, string operation,
        List<string> recoveryCopies, List<string> errors, CancellationToken ct,
        IDictionary<string, string>? backupsBySource = null)
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
                    if (backupsBySource is not null && string.Equals(source, path, StringComparison.Ordinal))
                        backupsBySource[path] = backupFile;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Backup failed for {path}; no source files were changed: {ex.Message}");
            }
        }
        return errors.Count == errorCount;
    }

    // Sinks partition by the event date encoded in the path (JsonlStoragePolicy.FormatDate): an
    // open period is still the live append target, so compressing it would force every later
    // batch through a whole-archive copy. Returns the period's exclusive end, or null when the
    // path carries no recognisable period (the quiet-period rule then applies alone).
    internal DateTimeOffset? PartitionPeriodEnd(string file)
    {
        var relative = Path.GetRelativePath(_options.RootPath, file);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            relative = Path.GetFileName(file);
        relative = relative.Replace('\\', '/');

        static DateTimeOffset? Last(string text, string pattern, Func<Match, DateTimeOffset?> end)
        {
            var matches = Regex.Matches(text, pattern);
            for (var i = matches.Count - 1; i >= 0; i--)
                if (end(matches[i]) is { } value)
                    return value;
            return null;
        }

        static DateTimeOffset? Day(string y, string m, string d) =>
            DateTime.TryParseExact($"{y}-{m}-{d}", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date)
                ? new DateTimeOffset(date, TimeSpan.Zero) : null;

        return Last(relative, @"(?<!\d)(\d{4})-(\d{2})-(\d{2})[_/](\d{2})(?!\d)",
                   m => int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) is var hour && hour < 24
                       ? Day(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value)?.AddHours(hour + 1)
                       : null)
            ?? Last(relative, @"(?<!\d)(\d{4})-(\d{2})-(\d{2})(?!\d)",
                   m => Day(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value)?.AddDays(1))
            ?? Last(relative, @"(?<!\d)(\d{4})(\d{2})(\d{2})(?!\d)",
                   m => Day(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value)?.AddDays(1))
            ?? Last(relative, @"(?<!\d)(\d{4})-(\d{2})(?![-\d])",
                   m => Day(m.Groups[1].Value, m.Groups[2].Value, "01")?.AddMonths(1));
    }

    // Per-instance test seam; never shared across maintenance operations or configured by input.
    internal Action<CompressionPublicationStage>? CompressionCheckpoint { get; init; }

    private async Task<bool> RecompressAsync(string filePath, CancellationToken ct, string? requiredBackup = null)
    {
        if (!filePath.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
            return false;
        var outputPath = filePath + ".gz";
        var outputSidecar = outputPath + ".sha256";
        if (File.Exists(outputSidecar) && !File.Exists(outputPath))
            throw new IOException($"Refusing to replace existing compression checksum without its archive: {outputSidecar}");

        // Fence writers for the whole verify-publish-delete window. JsonlStorageSink holds a shared
        // in-process fence lease and a shared JsonlWriterLock while its stream is open, so an open
        // writer in this process or another refuses compression, and a sink reopening the path
        // waits here, then appends to the published archive once this source is gone. Readers take
        // neither, so a replay keeps its handle. The quiet period and unchanged-source checks below
        // remain as defense in depth.
        if (!JsonlWriteFence.TryEnterExclusive(filePath, out var fence))
            throw new IOException($"Refusing to compress a file that is open for writing: {filePath}");
        using var fenceLease = fence;
        if (!JsonlWriterLock.TryAcquireExclusive(filePath, out var writerLock))
            throw new IOException($"Refusing to compress a file that another process has open for writing: {filePath}");
        await using var writerLease = writerLock!;

        FileStream source;
        try
        {
            // Windows refuses this open while another writer holds the file; readers may share it.
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
            // Backups are taken for every file before any is fenced, so a late write in between
            // would be compressed and deleted while the advertised recovery copy holds older bytes.
            // Under the fence the source cannot change again; refuse unless the backup matches it.
            if (requiredBackup is not null)
            {
                await using var backup = new FileStream(requiredBackup, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (!string.Equals(await ComputeChecksumAsync(backup, ct), sourceChecksum, StringComparison.Ordinal))
                    throw new IOException($"Source changed after its required backup was taken; kept the source and refused compression so the recovery copy matches what would be mutated: {filePath}");
            }
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
                if (!File.Exists(outputSidecar))
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
            // Every archive gets integrity evidence before the source (and any sidecar of its
            // own) is removed; health checks verify a file only against its own sidecar, so an
            // archive without one would pass a later truncation as healthy.
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
        return await Sha256Digest.ComputeAsync(stream, ct);
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
    BackupsCompleted,
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
    TimeSpan MinimumQuietPeriod = default,
    bool OnlyClosedPeriods = true);

public sealed record FileCompressionResult(
    int FilesProcessed,
    int FilesCompressed,
    IReadOnlyList<string> Errors,
    bool DryRun,
    IReadOnlyList<string>? PlannedActions = null)
{
    public IReadOnlyList<string> RecoveryCopies { get; init; } = [];
    /// <summary>Files left plain because they may still receive appends; informational, never a failure.</summary>
    public IReadOnlyList<string> Deferred { get; init; } = [];
    public long BytesBefore { get; init; }
    public long BytesAfter { get; init; }
    public int FilesUncompressed => Math.Max(0, FilesProcessed - FilesCompressed);
    public bool Success => Errors.Count == 0 && (DryRun || FilesCompressed == FilesProcessed);
}

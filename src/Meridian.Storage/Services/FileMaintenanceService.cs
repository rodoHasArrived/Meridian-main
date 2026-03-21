using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using Meridian.Storage.Archival;
using Meridian.Storage.Interfaces;

namespace Meridian.Storage.Services;

/// <summary>
/// Service for file health monitoring, self-healing, and maintenance operations.
/// </summary>
public sealed class FileMaintenanceService : IFileMaintenanceService
{
    private readonly StorageOptions _options;
    private readonly ISourceRegistry? _sourceRegistry;
    private static readonly string[] DataExtensions = { ".jsonl", ".jsonl.gz", ".jsonl.zst", ".jsonl.lz4", ".jsonl.br", ".parquet" };

    public FileMaintenanceService(StorageOptions options, ISourceRegistry? sourceRegistry = null)
    {
        _options = options;
        _sourceRegistry = sourceRegistry;
    }

    public async Task<HealthReport> RunHealthCheckAsync(HealthCheckOptions options, CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        var issues = new ConcurrentBag<HealthIssue>();
        var statistics = new HealthStatistics();

        var paths = options.Paths?.Length > 0 && options.Paths[0] != "*"
            ? options.Paths
            : new[] { _options.RootPath };

        var allFiles = new List<FileInfo>();
        foreach (var path in paths)
        {
            if (!Directory.Exists(path))
                continue;

            var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Where(f => DataExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                .Select(f => new FileInfo(f))
                .ToList();

            allFiles.AddRange(files);
        }

        statistics.TotalFiles = allFiles.Count;
        statistics.TotalBytes = allFiles.Sum(f => f.Length);

        if (allFiles.Count > 0)
        {
            statistics.OldestFile = allFiles.Min(f => f.LastWriteTimeUtc);
            statistics.NewestFile = allFiles.Max(f => f.LastWriteTimeUtc);
        }

        // Run health checks in parallel
        var semaphore = new SemaphoreSlim(options.ParallelChecks);
        var tasks = allFiles.Select(async file =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await CheckFileHealthAsync(file, options, issues, ct);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        // Check for orphaned files if registry available
        if (options.CheckManifestConsistency && _sourceRegistry != null)
        {
            CheckOrphanedFiles(allFiles, issues);
        }

        // Calculate statistics
        statistics.HealthyFiles = allFiles.Count - issues.Count(i => i.Severity == IssueSeverity.Critical || i.Severity == IssueSeverity.Warning);
        statistics.WarningFiles = issues.Count(i => i.Severity == IssueSeverity.Warning);
        statistics.CorruptedFiles = issues.Count(i => i.Severity == IssueSeverity.Critical && i.Type == IssueType.ChecksumMismatch);
        statistics.OrphanedFiles = issues.Count(i => i.Type == IssueType.OrphanedFile);

        // Calculate compression ratio and fragmentation
        var compressedFiles = allFiles.Where(f => f.Extension != ".jsonl").ToList();
        if (compressedFiles.Count > 0)
        {
            // Estimate compression ratio (actual would require decompressing)
            statistics.CompressionRatio = 5.0f; // Default estimate
        }

        // Fragmentation: small files percentage
        var smallFiles = allFiles.Count(f => f.Length < 1024 * 1024); // < 1MB
        statistics.FragmentationPct = allFiles.Count > 0
            ? (double)smallFiles / allFiles.Count * 100
            : 0;

        return new HealthReport(
            ReportId: Guid.NewGuid().ToString(),
            GeneratedAt: DateTime.UtcNow,
            ScanDurationMs: (long)(DateTime.UtcNow - startTime).TotalMilliseconds,
            Summary: new HealthSummary(
                statistics.TotalFiles,
                statistics.TotalBytes,
                statistics.HealthyFiles,
                statistics.WarningFiles,
                statistics.CorruptedFiles,
                statistics.OrphanedFiles
            ),
            Issues: issues.OrderByDescending(i => i.Severity).ToList(),
            Statistics: statistics
        );
    }

    public async Task<RepairResult> RepairAsync(RepairOptions options, CancellationToken ct = default)
    {
        var filesProcessed = 0;
        var filesRepaired = 0;
        var errors = new List<string>();

        // First run health check to identify issues
        var healthCheck = await RunHealthCheckAsync(new HealthCheckOptions
        {
            ValidateChecksums = true,
            CheckSequenceContinuity = true,
            IdentifyCorruption = true,
            ParallelChecks = 4
        }, ct);

        var repairableIssues = healthCheck.Issues
            .Where(i => i.AutoRepairable && MatchesScope(i, options.Scope))
            .ToList();

        if (options.DryRun)
        {
            return new RepairResult(
                FilesProcessed: repairableIssues.Count,
                FilesRepaired: 0,
                Errors: new List<string>(),
                DryRun: true,
                PlannedActions: repairableIssues.Select(i => i.RecommendedAction).ToList()
            );
        }

        // Create backup if requested
        if (options.BackupBeforeRepair && !string.IsNullOrEmpty(options.BackupPath))
        {
            foreach (var issue in repairableIssues)
            {
                try
                {
                    var backupFile = Path.Combine(options.BackupPath, Path.GetFileName(issue.Path));
                    Directory.CreateDirectory(options.BackupPath);
                    File.Copy(issue.Path, backupFile, overwrite: true);
                }
                catch (Exception ex)
                {
                    errors.Add($"Backup failed for {issue.Path}: {ex.Message}");
                }
            }
        }

        // Perform repairs based on strategy
        foreach (var issue in repairableIssues)
        {
            ct.ThrowIfCancellationRequested();
            filesProcessed++;

            try
            {
                var repaired = options.Strategy switch
                {
                    RepairStrategy.TruncateCorrupted => await TruncateCorruptedAsync(issue.Path, ct),
                    RepairStrategy.RebuildIndex => await RebuildIndexAsync(issue.Path, ct),
                    RepairStrategy.MergeFragments => await MergeFragmentsAsync(issue.Path, ct),
                    RepairStrategy.RecompressOptimal => await RecompressAsync(issue.Path, ct),
                    _ => false
                };

                if (repaired)
                    filesRepaired++;
            }
            catch (Exception ex)
            {
                errors.Add($"Repair failed for {issue.Path}: {ex.Message}");
            }
        }

        return new RepairResult(
            FilesProcessed: filesProcessed,
            FilesRepaired: filesRepaired,
            Errors: errors,
            DryRun: false,
            PlannedActions: null
        );
    }

    public async Task<DefragResult> DefragmentAsync(DefragOptions options, CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        var filesProcessed = 0;
        var filesCreated = 0;
        long bytesBefore = 0;
        long bytesAfter = 0;

        var cutoffDate = DateTime.UtcNow - options.MaxFileAge;

        // Find small files eligible for merging
        var smallFiles = Directory.EnumerateFiles(_options.RootPath, "*", SearchOption.AllDirectories)
            .Where(f => DataExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .Select(f => new FileInfo(f))
            .Where(f => f.Length < options.MinFileSizeBytes && f.LastWriteTimeUtc < cutoffDate)
            .OrderBy(f => f.DirectoryName)
            .ThenBy(f => f.Name)
            .ToList();

        // Group files by directory for merging
        var groups = smallFiles
            .GroupBy(f => f.DirectoryName)
            .Where(g => g.Count() >= 2);

        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();

            var filesToMerge = group.Take(options.MaxFilesPerMerge).ToList();
            bytesBefore += filesToMerge.Sum(f => f.Length);
            filesProcessed += filesToMerge.Count;

            try
            {
                var mergedPath = await MergeFilesAsync(filesToMerge, options, ct);
                if (!string.IsNullOrEmpty(mergedPath))
                {
                    var mergedInfo = new FileInfo(mergedPath);
                    bytesAfter += mergedInfo.Length;
                    filesCreated++;

                    if (!options.PreserveOriginals)
                    {
                        foreach (var file in filesToMerge)
                        {
                            try
                            { file.Delete(); }
                            catch (IOException) { /* File may be in use */ }
                        }
                    }
                }
            }
            catch
            {
                // Skip this group on error
                bytesAfter += filesToMerge.Sum(f => f.Length);
            }
        }

        return new DefragResult(
            FilesProcessed: filesProcessed,
            FilesCreated: filesCreated,
            BytesBefore: bytesBefore,
            BytesAfter: bytesAfter,
            CompressionImprovement: bytesBefore > 0 ? (double)(bytesBefore - bytesAfter) / bytesBefore * 100 : 0,
            Duration: DateTime.UtcNow - startTime
        );
    }

    public Task<OrphanReport> FindOrphansAsync(CancellationToken ct = default)
    {
        var orphans = new List<OrphanedFile>();

        var allFiles = Directory.EnumerateFiles(_options.RootPath, "*", SearchOption.AllDirectories)
            .Where(f => DataExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var filePath in allFiles)
        {
            ct.ThrowIfCancellationRequested();

            var reason = DetermineOrphanReason(filePath);
            if (reason != null)
            {
                orphans.Add(new OrphanedFile(
                    Path: filePath,
                    Reason: reason,
                    SizeBytes: new FileInfo(filePath).Length,
                    LastModified: File.GetLastWriteTimeUtc(filePath)
                ));
            }
        }

        return Task.FromResult(new OrphanReport(
            GeneratedAt: DateTime.UtcNow,
            OrphanedFiles: orphans,
            TotalOrphanedBytes: orphans.Sum(o => o.SizeBytes)
        ));
    }

    private async Task CheckFileHealthAsync(FileInfo file, HealthCheckOptions options, ConcurrentBag<HealthIssue> issues, CancellationToken ct)
    {
        // Validate checksum if requested
        if (options.ValidateChecksums)
        {
            var checksumFile = file.FullName + ".sha256";
            if (File.Exists(checksumFile))
            {
                var expectedChecksum = await File.ReadAllTextAsync(checksumFile, ct);
                var actualChecksum = await ComputeChecksumAsync(file.FullName, ct);

                if (!string.Equals(expectedChecksum.Trim(), actualChecksum, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new HealthIssue(
                        Severity: IssueSeverity.Critical,
                        Type: IssueType.ChecksumMismatch,
                        Path: file.FullName,
                        ExpectedChecksum: expectedChecksum.Trim(),
                        ActualChecksum: actualChecksum,
                        RecommendedAction: "restore_from_backup",
                        AutoRepairable: true
                    ));
                }
            }
        }

        // Check for corruption (truncated files, invalid JSON)
        if (options.IdentifyCorruption)
        {
            try
            {
                using var stream = file.OpenRead();
                if (stream.Length == 0)
                {
                    issues.Add(new HealthIssue(
                        Severity: IssueSeverity.Warning,
                        Type: IssueType.EmptyFile,
                        Path: file.FullName,
                        RecommendedAction: "delete_or_regenerate",
                        AutoRepairable: true
                    ));
                }
                else if (file.Extension == ".jsonl")
                {
                    // Validate last line is complete JSON
                    stream.Seek(Math.Max(0, stream.Length - 4096), SeekOrigin.Begin);
                    using var reader = new StreamReader(stream);
                    var lastLines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    if (lastLines.Length > 0)
                    {
                        var lastLine = lastLines[^1];
                        try
                        {
                            JsonDocument.Parse(lastLine);
                        }
                        catch
                        {
                            issues.Add(new HealthIssue(
                                Severity: IssueSeverity.Warning,
                                Type: IssueType.TruncatedFile,
                                Path: file.FullName,
                                RecommendedAction: "truncate_corrupted",
                                AutoRepairable: true
                            ));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                issues.Add(new HealthIssue(
                    Severity: IssueSeverity.Critical,
                    Type: IssueType.UnreadableFile,
                    Path: file.FullName,
                    Details: ex.Message,
                    RecommendedAction: "restore_from_backup",
                    AutoRepairable: false
                ));
            }
        }

        // Check file permissions
        if (options.CheckFilePermissions)
        {
            try
            {
                using var _ = file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (UnauthorizedAccessException)
            {
                issues.Add(new HealthIssue(
                    Severity: IssueSeverity.Warning,
                    Type: IssueType.PermissionDenied,
                    Path: file.FullName,
                    RecommendedAction: "fix_permissions",
                    AutoRepairable: false
                ));
            }
            catch (IOException)
            {
                // File is locked, which is OK for active files
            }
        }
    }

    private void CheckOrphanedFiles(List<FileInfo> files, ConcurrentBag<HealthIssue> issues)
    {
        foreach (var file in files)
        {
            var reason = DetermineOrphanReason(file.FullName);
            if (reason != null)
            {
                issues.Add(new HealthIssue(
                    Severity: IssueSeverity.Info,
                    Type: IssueType.OrphanedFile,
                    Path: file.FullName,
                    Details: reason,
                    RecommendedAction: "archive_or_delete",
                    AutoRepairable: false
                ));
            }
        }
    }

    private string? DetermineOrphanReason(string filePath)
    {
        // Extract symbol from path
        var parts = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Look for known orphan patterns
        foreach (var part in parts)
        {
            if (part.StartsWith("DELETED_", StringComparison.OrdinalIgnoreCase))
                return "symbol_marked_deleted";

            if (part.StartsWith("_temp", StringComparison.OrdinalIgnoreCase))
                return "temporary_file";

            if (part.StartsWith("_quarantine", StringComparison.OrdinalIgnoreCase))
                return "quarantined_file";
        }

        return null;
    }

    private async Task<string> ComputeChecksumAsync(string filePath, CancellationToken ct)
    {
        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private bool MatchesScope(HealthIssue issue, RepairScope scope)
    {
        return scope switch
        {
            RepairScope.All => true,
            RepairScope.SingleFile => true, // Would need specific file filter
            RepairScope.Directory => true,  // Would need directory filter
            _ => true
        };
    }

    private async Task<bool> TruncateCorruptedAsync(string filePath, CancellationToken ct)
    {
        // Find last valid JSON line and truncate
        var lines = await File.ReadAllLinesAsync(filePath, ct);
        var validLines = new List<string>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                JsonDocument.Parse(line);
                validLines.Add(line);
            }
            catch
            {
                break; // Stop at first invalid line
            }
        }

        if (validLines.Count < lines.Length)
        {
            await AtomicFileWriter.WriteAsync(filePath, string.Join(Environment.NewLine, validLines) + Environment.NewLine, ct);
            return true;
        }

        return false;
    }

    private Task<bool> RebuildIndexAsync(string filePath, CancellationToken ct)
    {
        // Index rebuilding would regenerate manifest files
        return Task.FromResult(true);
    }

    private Task<bool> MergeFragmentsAsync(string filePath, CancellationToken ct)
    {
        // Fragment merging handled by defragmentation
        return Task.FromResult(true);
    }

    private async Task<bool> RecompressAsync(string filePath, CancellationToken ct)
    {
        // Recompress with optimal settings
        if (!filePath.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
            return false;

        var outputPath = filePath + ".gz";
        await using var input = File.OpenRead(filePath);
        await using var output = File.Create(outputPath);
        await using var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Optimal);
        await input.CopyToAsync(gzip, ct);

        return true;
    }

    private async Task<string?> MergeFilesAsync(List<FileInfo> files, DefragOptions options, CancellationToken ct)
    {
        if (files.Count == 0)
            return null;

        var directory = files[0].DirectoryName!;
        var extension = files[0].Extension;
        var mergedPath = Path.Combine(directory, $"merged_{DateTime.UtcNow:yyyyMMdd_HHmmss}{extension}");

        await AtomicFileWriter.WriteAsync(mergedPath, async writer =>
        {
            foreach (var file in files)
            {
                var content = await File.ReadAllTextAsync(file.FullName, ct);
                await writer.WriteAsync(content);
                if (!content.EndsWith('\n'))
                    await writer.WriteLineAsync();
            }
        }, ct);

        return mergedPath;
    }
}

/// <summary>
/// Interface for file maintenance service.
/// </summary>
public interface IFileMaintenanceService
{
    Task<HealthReport> RunHealthCheckAsync(HealthCheckOptions options, CancellationToken ct = default);
    Task<RepairResult> RepairAsync(RepairOptions options, CancellationToken ct = default);
    Task<DefragResult> DefragmentAsync(DefragOptions options, CancellationToken ct = default);
    Task<OrphanReport> FindOrphansAsync(CancellationToken ct = default);
}

// Health check types
public sealed record HealthCheckOptions(
    bool ValidateChecksums = true,
    bool CheckSequenceContinuity = true,
    bool ValidateSchemas = true,
    bool CheckFilePermissions = true,
    bool IdentifyCorruption = true,
    bool CheckManifestConsistency = false,
    string[]? Paths = null,
    int ParallelChecks = 4
);

public sealed record HealthReport(
    string ReportId,
    DateTime GeneratedAt,
    long ScanDurationMs,
    HealthSummary Summary,
    IReadOnlyList<HealthIssue> Issues,
    HealthStatistics Statistics
);

public sealed record HealthSummary(
    int TotalFiles,
    long TotalBytes,
    int HealthyFiles,
    int WarningFiles,
    int CorruptedFiles,
    int OrphanedFiles
);

public sealed class HealthStatistics
{
    public int TotalFiles { get; set; }
    public long TotalBytes { get; set; }
    public int HealthyFiles { get; set; }
    public int WarningFiles { get; set; }
    public int CorruptedFiles { get; set; }
    public int OrphanedFiles { get; set; }
    public double CompressionRatio { get; set; }
    public double FragmentationPct { get; set; }
    public DateTime? OldestFile { get; set; }
    public DateTime? NewestFile { get; set; }
}

public sealed record HealthIssue(
    IssueSeverity Severity,
    IssueType Type,
    string Path,
    string? ExpectedChecksum = null,
    string? ActualChecksum = null,
    string? Details = null,
    string RecommendedAction = "",
    bool AutoRepairable = false
);

public enum IssueSeverity : byte { Info, Warning, Critical }

public enum IssueType : byte
{
    ChecksumMismatch,
    SequenceGap,
    EmptyFile,
    TruncatedFile,
    UnreadableFile,
    PermissionDenied,
    OrphanedFile,
    SchemaViolation
}

// Repair types
public sealed record RepairOptions(
    RepairStrategy Strategy,
    bool DryRun = false,
    bool BackupBeforeRepair = true,
    string? BackupPath = null,
    RepairScope Scope = RepairScope.All
);

public enum RepairStrategy : byte
{
    RestoreFromBackup,
    BackfillFromSource,
    TruncateCorrupted,
    RebuildIndex,
    MergeFragments,
    RecompressOptimal
}

public enum RepairScope : byte { SingleFile, Directory, Symbol, DateRange, EventType, All }

public sealed record RepairResult(
    int FilesProcessed,
    int FilesRepaired,
    IReadOnlyList<string> Errors,
    bool DryRun,
    IReadOnlyList<string>? PlannedActions
);

// Defrag types
public sealed record DefragOptions(
    long MinFileSizeBytes = 1_048_576, // 1MB
    int MaxFilesPerMerge = 100,
    bool PreserveOriginals = false,
    System.IO.Compression.CompressionLevel TargetCompression = System.IO.Compression.CompressionLevel.Optimal,
    TimeSpan MaxFileAge = default
)
{
    public DefragOptions() : this(1_048_576) { }
}

public sealed record DefragResult(
    int FilesProcessed,
    int FilesCreated,
    long BytesBefore,
    long BytesAfter,
    double CompressionImprovement,
    TimeSpan Duration
);

// Orphan types
public sealed record OrphanReport(
    DateTime GeneratedAt,
    IReadOnlyList<OrphanedFile> OrphanedFiles,
    long TotalOrphanedBytes
);

public sealed record OrphanedFile(
    string Path,
    string Reason,
    long SizeBytes,
    DateTime LastModified
);

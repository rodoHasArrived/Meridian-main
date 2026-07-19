using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using Meridian.Storage.Archival;
using Meridian.Storage.Interfaces;

namespace Meridian.Storage.Services;

/// <summary>
/// Service for managing data migration between storage tiers.
/// </summary>
public sealed class TierMigrationService : ITierMigrationService
{
    private static readonly string[] DataExtensions =
    [
        ".jsonl",
        ".jsonl.gz",
        ".jsonl.zst",
        ".jsonl.lz4",
        ".parquet"
    ];

    private readonly StorageOptions _options;
    private readonly ISourceRegistry? _sourceRegistry;

    public TierMigrationService(StorageOptions options, ISourceRegistry? sourceRegistry = null)
    {
        _options = options;
        _sourceRegistry = sourceRegistry;
    }

    public async Task<MigrationResult> MigrateAsync(
        string sourcePath,
        StorageTier targetTier,
        MigrationOptions options,
        CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        var filesProcessed = 0;
        var filesFailed = 0;
        long bytesProcessed = 0;
        long bytesSaved = 0;
        var errors = new List<string>();

        if (options.ParallelFiles <= 0)
        {
            return new MigrationResult(
                Success: false,
                FilesProcessed: 0,
                FilesFailed: 0,
                BytesProcessed: 0,
                BytesSaved: 0,
                Duration: DateTime.UtcNow - startTime,
                Errors: new[] { "ParallelFiles must be at least 1 for tier migration." }
            );
        }

        var tierConfig = GetTierConfig(targetTier);
        if (tierConfig == null)
        {
            return new MigrationResult(
                Success: false,
                FilesProcessed: 0,
                FilesFailed: 0,
                BytesProcessed: 0,
                BytesSaved: 0,
                Duration: TimeSpan.Zero,
                Errors: new[] { $"No configuration found for tier: {targetTier}" }
            );
        }

        var files = GetFilesToMigrate(sourcePath);
        var semaphore = new SemaphoreSlim(options.ParallelFiles);

        var tasks = files.Select(async file =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var result = await MigrateFileAsync(file, tierConfig, options, ct);
                Interlocked.Increment(ref filesProcessed);
                Interlocked.Add(ref bytesProcessed, result.OriginalSize);
                Interlocked.Add(ref bytesSaved, result.OriginalSize - result.NewSize);

                options.OnProgress?.Invoke(new MigrationProgress(
                    CurrentFile: file,
                    FilesProcessed: filesProcessed,
                    TotalFiles: files.Count,
                    BytesProcessed: bytesProcessed
                ));
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref filesFailed);
                lock (errors)
                {
                    errors.Add($"{file}: {ex.Message}");
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        return new MigrationResult(
            Success: filesFailed == 0,
            FilesProcessed: filesProcessed,
            FilesFailed: filesFailed,
            BytesProcessed: bytesProcessed,
            BytesSaved: bytesSaved,
            Duration: DateTime.UtcNow - startTime,
            Errors: errors
        );
    }

    public Task<MigrationPlan> PlanMigrationAsync(TimeSpan horizon, CancellationToken ct = default)
    {
        var actions = new List<PlannedMigrationAction>();
        var now = DateTime.UtcNow;

        if (_options.Tiering?.Enabled != true || _options.Tiering.Tiers.Count == 0)
        {
            return Task.FromResult(new MigrationPlan(
                GeneratedAt: DateTimeOffset.UtcNow,
                Horizon: horizon,
                Actions: actions,
                EstimatedBytesToMigrate: 0,
                EstimatedDuration: TimeSpan.Zero
            ));
        }

        // Sort tiers by age threshold
        var sortedTiers = _options.Tiering.Tiers
            .Where(t => t.MaxAgeDays.HasValue)
            .OrderBy(t => t.MaxAgeDays!.Value)
            .ToList();

        for (int i = 0; i < sortedTiers.Count - 1; i++)
        {
            var sourceTier = sortedTiers[i];
            var targetTier = sortedTiers[i + 1];
            var cutoffDate = now.AddDays(-sourceTier.MaxAgeDays!.Value);

            if (!Directory.Exists(sourceTier.Path))
                continue;

            var eligibleFiles = Directory.EnumerateFiles(sourceTier.Path, "*", SearchOption.AllDirectories)
                .Where(IsDataFile)
                .Select(f => new FileInfo(f))
                .Where(f => f.LastWriteTimeUtc < cutoffDate)
                .ToList();

            foreach (var file in eligibleFiles)
            {
                actions.Add(new PlannedMigrationAction(
                    SourcePath: file.FullName,
                    TargetTier: Enum.TryParse<StorageTier>(targetTier.Name, true, out var tier) ? tier : StorageTier.Warm,
                    Reason: $"Age > {sourceTier.MaxAgeDays} days",
                    SizeBytes: file.Length,
                    FileAge: now - file.LastWriteTimeUtc,
                    EstimatedSavings: EstimateSavings(file, sourceTier, targetTier)
                ));
            }
        }

        var totalBytes = actions.Sum(a => a.SizeBytes);
        var estimatedDuration = TimeSpan.FromSeconds(totalBytes / (50 * 1024 * 1024)); // ~50MB/s estimate

        return Task.FromResult(new MigrationPlan(
            GeneratedAt: DateTimeOffset.UtcNow,
            Horizon: horizon,
            Actions: actions,
            EstimatedBytesToMigrate: totalBytes,
            EstimatedDuration: estimatedDuration
        ));
    }

    public StorageTier DetermineTargetTier(string filePath)
    {
        if (_options.Tiering?.Enabled != true)
            return StorageTier.Hot;

        var fileInfo = new FileInfo(filePath);
        var age = DateTime.UtcNow - fileInfo.LastWriteTimeUtc;

        foreach (var tier in _options.Tiering.Tiers.OrderBy(t => t.MaxAgeDays ?? int.MaxValue))
        {
            if (!tier.MaxAgeDays.HasValue || age.TotalDays <= tier.MaxAgeDays.Value)
            {
                return Enum.TryParse<StorageTier>(tier.Name, true, out var result) ? result : StorageTier.Hot;
            }
        }

        return StorageTier.Archive;
    }

    public Task<TierStatistics> GetTierStatisticsAsync(CancellationToken ct = default)
    {
        var tierStats = new Dictionary<StorageTier, TierInfo>();

        if (_options.Tiering?.Tiers != null)
        {
            foreach (var tierConfig in _options.Tiering.Tiers)
            {
                if (!Enum.TryParse<StorageTier>(tierConfig.Name, true, out var tier))
                    continue;

                var info = new TierInfo(
                    FileCount: 0,
                    TotalBytes: 0,
                    OldestFile: null,
                    NewestFile: null
                );

                if (Directory.Exists(tierConfig.Path))
                {
                    var files = Directory.EnumerateFiles(tierConfig.Path, "*", SearchOption.AllDirectories)
                        .Select(f => new FileInfo(f))
                        .ToList();

                    info = new TierInfo(
                        FileCount: files.Count,
                        TotalBytes: files.Sum(f => f.Length),
                        OldestFile: files.Count > 0 ? files.Min(f => f.LastWriteTimeUtc) : null,
                        NewestFile: files.Count > 0 ? files.Max(f => f.LastWriteTimeUtc) : null
                    );
                }

                tierStats[tier] = info;
            }
        }

        return Task.FromResult(new TierStatistics(
            GeneratedAt: DateTimeOffset.UtcNow,
            TierInfo: tierStats
        ));
    }

    private TierConfig? GetTierConfig(StorageTier tier)
    {
        return _options.Tiering?.Tiers.FirstOrDefault(t =>
            t.Name.Equals(tier.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private List<string> GetFilesToMigrate(string sourcePath)
    {
        if (File.Exists(sourcePath))
            return new List<string> { sourcePath };

        if (Directory.Exists(sourcePath))
        {
            return Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories)
                .Where(IsDataFile)
                .ToList();
        }

        return new List<string>();
    }

    private static bool IsDataFile(string path)
        => DataExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    private async Task<FileMigrationResult> MigrateFileAsync(
        string sourcePath,
        TierConfig targetTier,
        MigrationOptions options,
        CancellationToken ct)
    {
        var sourceInfo = new FileInfo(sourcePath);
        var originalSize = sourceInfo.Length;

        // Determine target path. The migrated file must land inside the target tier's root:
        // a source outside the storage root produces ".." segments in the relative path, and
        // writing (or later deleting) through such a path would escape the tier directory.
        var relativePath = Path.GetRelativePath(_options.RootPath, sourcePath);
        var tierRoot = Path.GetFullPath(targetTier.Path);
        var targetPath = Path.GetFullPath(Path.Combine(tierRoot, relativePath));
        if (!targetPath.StartsWith(tierRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(targetPath, tierRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Refusing to migrate '{sourcePath}': the resolved target '{targetPath}' escapes tier root '{tierRoot}'.");
        }

        // Reject conversions this service does not actually implement. Renaming the target
        // extension without converting the payload would ship mislabeled bytes and — with
        // DeleteSource — destroy the only correct copy.
        if (string.Equals(targetTier.Format, "parquet", StringComparison.OrdinalIgnoreCase)
            && !sourcePath.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"Tier '{targetTier.Path}' requests parquet format, but converting '{Path.GetExtension(sourcePath)}' " +
                "sources to parquet is not implemented. Configure the tier with Format 'jsonl' or migrate parquet sources only.");
        }

        if (targetTier.Compression.HasValue && targetTier.Compression != CompressionCodec.None)
        {
            if (targetTier.Compression != CompressionCodec.Gzip)
            {
                throw new NotSupportedException(
                    $"Tier '{targetTier.Path}' requests {targetTier.Compression} compression, but only Gzip is implemented " +
                    "for tier migration. Configure the tier with Gzip or no compression.");
            }

            if (!targetPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                targetPath = ApplyCompressionExtension(targetPath, ".gz");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        // Copy and optionally compress
        if (options.VerifyChecksum)
        {
            await CopyWithVerificationAsync(sourcePath, targetPath, targetTier, ct);
        }
        else
        {
            await CopyFileAsync(sourcePath, targetPath, targetTier, ct);
        }

        var targetInfo = new FileInfo(targetPath);

        // Delete source if requested
        if (options.DeleteSource)
        {
            // The migrated file's data blocks were already fsynced by CopyFileAsync. Persist the
            // target directory metadata (the newly created file entry) so it survives a crash.
            await AtomicFileWriter.SyncDirectoryAsync(DirectoryOfOrCurrent(targetPath), ct);

            // Validate the migrated file actually landed on disk before removing the source of
            // truth. A missing or empty target (for a non-empty source) means the copy did not
            // complete, so deleting the source would lose data irrecoverably.
            targetInfo.Refresh();
            if (!targetInfo.Exists || (originalSize > 0 && targetInfo.Length == 0))
            {
                throw new IOException(
                    $"Refusing to delete source '{sourcePath}': migrated file '{targetPath}' is missing or empty.");
            }

            File.Delete(sourcePath);

            // Make the deletion durable so the source cannot reappear after a crash. This runs
            // post-commit (the source is already gone), so it must not observe caller cancellation
            // and report an already-completed migration as failed.
            await AtomicFileWriter.SyncDirectoryAsync(DirectoryOfOrCurrent(sourcePath), CancellationToken.None);
        }

        return new FileMigrationResult(
            SourcePath: sourcePath,
            TargetPath: targetPath,
            OriginalSize: originalSize,
            NewSize: targetInfo.Length
        );
    }

    // Returns the file's directory, falling back to the current directory for relative paths
    // that have no directory component (e.g. "session.jsonl"), which would otherwise yield an
    // empty string and fault the directory fsync.
    private static string DirectoryOfOrCurrent(string path)
    {
        var directory = Path.GetDirectoryName(path);
        return string.IsNullOrEmpty(directory) ? "." : directory;
    }

    private async Task CopyFileAsync(string source, string target, TierConfig tierConfig, CancellationToken ct)
    {
        // Route through the atomic writer (temp file + fsync + rename + directory sync) so a
        // crash mid-copy can never leave a partial file at the final target path.
        await AtomicFileWriter.WriteStreamAsync(target, async targetStream =>
        {
            await using var sourceStream = File.OpenRead(source);

            if (tierConfig.Compression == CompressionCodec.Gzip)
            {
                // leaveOpen so the GZipStream trailer is flushed on dispose without closing
                // the temp stream the atomic writer still needs to fsync.
                await using var gzip = new GZipStream(targetStream, CompressionLevel.Optimal, leaveOpen: true);
                await sourceStream.CopyToAsync(gzip, ct);
            }
            else
            {
                await sourceStream.CopyToAsync(targetStream, ct);
            }
        }, ct);
    }

    private async Task CopyWithVerificationAsync(string source, string target, TierConfig tierConfig, CancellationToken ct)
    {
        var sourceHash = await ComputeSourceHashAsync(source, ct).ConfigureAwait(false);

        // Copy file
        await CopyFileAsync(source, target, tierConfig, ct).ConfigureAwait(false);

        var targetHash = await ComputeMigratedPayloadHashAsync(target, tierConfig, ct).ConfigureAwait(false);
        if (!sourceHash.AsSpan().SequenceEqual(targetHash))
        {
            File.Delete(target);
            throw new IOException($"Checksum verification failed while migrating '{source}' to '{target}'.");
        }
    }

    private static async Task<byte[]> ComputeSourceHashAsync(string source, CancellationToken ct)
    {
        using var sha256 = SHA256.Create();
        await using var sourceStream = File.OpenRead(source);
        return await sha256.ComputeHashAsync(sourceStream, ct).ConfigureAwait(false);
    }

    private static async Task<byte[]> ComputeMigratedPayloadHashAsync(
        string target,
        TierConfig tierConfig,
        CancellationToken ct)
    {
        using var sha256 = SHA256.Create();
        await using var targetStream = File.OpenRead(target);

        if (tierConfig.Compression == CompressionCodec.Gzip)
        {
            await using var gzip = new GZipStream(targetStream, CompressionMode.Decompress);
            return await sha256.ComputeHashAsync(gzip, ct).ConfigureAwait(false);
        }

        return await sha256.ComputeHashAsync(targetStream, ct).ConfigureAwait(false);
    }

    private static string ApplyCompressionExtension(string targetPath, string extension)
    {
        if (targetPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            return targetPath;

        return targetPath + extension;
    }

    private long EstimateSavings(FileInfo file, TierConfig source, TierConfig target)
    {
        // Estimate compression savings
        if (target.Compression.HasValue && source.Compression != target.Compression)
        {
            return target.Compression switch
            {
                CompressionCodec.Gzip => (long)(file.Length * 0.7), // ~30% savings
                CompressionCodec.Zstd => (long)(file.Length * 0.8), // ~20% additional
                _ => 0
            };
        }
        return 0;
    }
}

/// <summary>
/// Interface for tier migration service.
/// </summary>
public interface ITierMigrationService
{
    Task<MigrationResult> MigrateAsync(string sourcePath, StorageTier targetTier, MigrationOptions options, CancellationToken ct = default);
    Task<MigrationPlan> PlanMigrationAsync(TimeSpan horizon, CancellationToken ct = default);
    StorageTier DetermineTargetTier(string filePath);
    Task<TierStatistics> GetTierStatisticsAsync(CancellationToken ct = default);
}

// Migration types
public sealed record MigrationOptions(
    bool DeleteSource = false,
    bool VerifyChecksum = true,
    bool ConvertFormat = false,
    int ParallelFiles = 4,
    Action<MigrationProgress>? OnProgress = null
);

public sealed record MigrationProgress(
    string CurrentFile,
    int FilesProcessed,
    int TotalFiles,
    long BytesProcessed
);

public sealed record MigrationResult(
    bool Success,
    int FilesProcessed,
    int FilesFailed,
    long BytesProcessed,
    long BytesSaved,
    TimeSpan Duration,
    IReadOnlyList<string> Errors
);

public sealed record MigrationPlan(
    DateTimeOffset GeneratedAt,
    TimeSpan Horizon,
    IReadOnlyList<PlannedMigrationAction> Actions,
    long EstimatedBytesToMigrate,
    TimeSpan EstimatedDuration
);

public sealed record PlannedMigrationAction(
    string SourcePath,
    StorageTier TargetTier,
    string Reason,
    long SizeBytes,
    TimeSpan FileAge,
    long EstimatedSavings
);

public sealed record FileMigrationResult(
    string SourcePath,
    string TargetPath,
    long OriginalSize,
    long NewSize
);

public sealed record TierStatistics(
    DateTimeOffset GeneratedAt,
    Dictionary<StorageTier, TierInfo> TierInfo
);

public sealed record TierInfo(
    int FileCount,
    long TotalBytes,
    DateTime? OldestFile,
    DateTime? NewestFile
);

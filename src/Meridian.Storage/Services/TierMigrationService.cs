using System.IO.Compression;
using System.Threading;
using Meridian.Storage.Interfaces;

namespace Meridian.Storage.Services;

/// <summary>
/// Service for managing data migration between storage tiers.
/// </summary>
public sealed class TierMigrationService : ITierMigrationService
{
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
                .Where(f => f.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) ||
                           f.EndsWith(".jsonl.gz", StringComparison.OrdinalIgnoreCase) ||
                           f.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return new List<string>();
    }

    private async Task<FileMigrationResult> MigrateFileAsync(
        string sourcePath,
        TierConfig targetTier,
        MigrationOptions options,
        CancellationToken ct)
    {
        var sourceInfo = new FileInfo(sourcePath);
        var originalSize = sourceInfo.Length;

        // Determine target path
        var relativePath = Path.GetRelativePath(_options.RootPath, sourcePath);
        var targetPath = Path.Combine(targetTier.Path, relativePath);

        // Change extension if format changes
        if (targetTier.Format == "parquet" && !sourcePath.EndsWith(".parquet"))
        {
            targetPath = Path.ChangeExtension(targetPath, ".parquet");
            // Parquet conversion would happen here
        }
        else if (targetTier.Compression.HasValue && targetTier.Compression != CompressionCodec.None)
        {
            var ext = targetTier.Compression switch
            {
                CompressionCodec.Gzip => ".gz",
                CompressionCodec.Zstd => ".zst",
                CompressionCodec.LZ4 => ".lz4",
                _ => ""
            };

            if (!targetPath.EndsWith(ext))
                targetPath = targetPath.TrimEnd(".jsonl".ToCharArray()) + ".jsonl" + ext;
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
            File.Delete(sourcePath);
        }

        return new FileMigrationResult(
            SourcePath: sourcePath,
            TargetPath: targetPath,
            OriginalSize: originalSize,
            NewSize: targetInfo.Length
        );
    }

    private async Task CopyFileAsync(string source, string target, TierConfig tierConfig, CancellationToken ct)
    {
        await using var sourceStream = File.OpenRead(source);
        await using var targetStream = File.Create(target);

        if (tierConfig.Compression == CompressionCodec.Gzip)
        {
            await using var gzip = new GZipStream(targetStream, CompressionLevel.Optimal);
            await sourceStream.CopyToAsync(gzip, ct);
        }
        else
        {
            await sourceStream.CopyToAsync(targetStream, ct);
        }
    }

    private async Task CopyWithVerificationAsync(string source, string target, TierConfig tierConfig, CancellationToken ct)
    {
        // Compute source checksum
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        await using var sourceStream = File.OpenRead(source);
        var sourceHash = await sha256.ComputeHashAsync(sourceStream, ct);
        sourceStream.Position = 0;

        // Copy file
        await CopyFileAsync(source, target, tierConfig, ct);

        // Verification of compressed files would need decompression.
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

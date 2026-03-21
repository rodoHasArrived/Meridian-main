using System.Collections.Concurrent;
using Meridian.Infrastructure.Contracts;
using Microsoft.Extensions.Logging;

namespace Meridian.Storage.Services;

/// <summary>
/// Engine that enforces tier-based lifecycle policies on stored data.
/// Evaluates files against their configured StoragePolicyConfig and orchestrates
/// tier migrations, compression upgrades, and retention enforcement.
/// </summary>
[ImplementsAdr("ADR-002", "Tiered storage lifecycle enforcement")]
public sealed class LifecyclePolicyEngine : ILifecyclePolicyEngine
{
    private readonly StorageOptions _options;
    private readonly ITierMigrationService _tierMigration;
    private readonly IFileMaintenanceService _maintenanceService;
    private readonly ILogger<LifecyclePolicyEngine> _logger;
    private readonly ConcurrentDictionary<string, LifecycleState> _fileStates = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] DataExtensions = { ".jsonl", ".jsonl.gz", ".jsonl.zst", ".jsonl.lz4", ".parquet" };

    public LifecyclePolicyEngine(
        StorageOptions options,
        ITierMigrationService tierMigration,
        IFileMaintenanceService maintenanceService,
        ILogger<LifecyclePolicyEngine> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tierMigration = tierMigration ?? throw new ArgumentNullException(nameof(tierMigration));
        _maintenanceService = maintenanceService ?? throw new ArgumentNullException(nameof(maintenanceService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<LifecycleEvaluationResult> EvaluateAsync(CancellationToken ct = default)
    {
        var actions = new List<LifecycleAction>();
        var now = DateTime.UtcNow;

        if (!Directory.Exists(_options.RootPath))
        {
            return Task.FromResult(new LifecycleEvaluationResult(
                EvaluatedAtUtc: now,
                FilesEvaluated: 0,
                Actions: actions,
                Errors: new List<string>()));
        }

        var files = Directory.EnumerateFiles(_options.RootPath, "*", SearchOption.AllDirectories)
            .Where(f => DataExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var errors = new List<string>();

        foreach (var filePath in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists)
                    continue;

                var fileAge = now - fileInfo.LastWriteTimeUtc;
                var policy = ResolvePolicy(filePath);
                var currentTier = DetermineTier(filePath);
                var targetTier = DetermineTargetTier(fileAge, policy);

                if (targetTier != currentTier)
                {
                    actions.Add(new LifecycleAction(
                        FilePath: filePath,
                        ActionType: LifecycleActionType.TierMigration,
                        CurrentTier: currentTier,
                        TargetTier: targetTier,
                        Reason: $"File age {fileAge.TotalDays:F0}d exceeds tier threshold",
                        EstimatedSizeBytes: fileInfo.Length,
                        Policy: policy));
                }

                // Check if compression upgrade is needed
                var targetCompression = GetTargetCompression(targetTier, policy);
                if (targetCompression.HasValue && !HasCompression(filePath, targetCompression.Value))
                {
                    actions.Add(new LifecycleAction(
                        FilePath: filePath,
                        ActionType: LifecycleActionType.CompressionUpgrade,
                        CurrentTier: currentTier,
                        TargetTier: targetTier,
                        Reason: $"Compression upgrade to {targetCompression.Value} for {targetTier} tier",
                        EstimatedSizeBytes: fileInfo.Length,
                        Policy: policy));
                }

                // Check retention expiry
                if (policy.Classification != DataClassification.Critical && IsExpired(fileAge, policy))
                {
                    actions.Add(new LifecycleAction(
                        FilePath: filePath,
                        ActionType: LifecycleActionType.Delete,
                        CurrentTier: currentTier,
                        TargetTier: null,
                        Reason: $"Retention expired: file age {fileAge.TotalDays:F0}d",
                        EstimatedSizeBytes: fileInfo.Length,
                        Policy: policy));
                }

                // Track state
                _fileStates[filePath] = new LifecycleState(currentTier, fileAge, policy.Classification);
            }
            catch (Exception ex)
            {
                errors.Add($"Error evaluating {filePath}: {ex.Message}");
            }
        }

        _logger.LogInformation("Lifecycle evaluation complete: {FileCount} files, {ActionCount} actions pending",
            files.Count, actions.Count);

        return Task.FromResult(new LifecycleEvaluationResult(
            EvaluatedAtUtc: now,
            FilesEvaluated: files.Count,
            Actions: actions,
            Errors: errors));
    }

    /// <inheritdoc />
    public async Task<LifecycleExecutionResult> ExecuteAsync(
        IReadOnlyList<LifecycleAction> actions,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        var executed = 0;
        var failed = 0;
        long bytesMigrated = 0;
        long bytesDeleted = 0;
        var errors = new List<string>();

        foreach (var action in actions)
        {
            ct.ThrowIfCancellationRequested();

            if (dryRun)
            {
                _logger.LogInformation("[DryRun] Would {Action} {File}: {Reason}",
                    action.ActionType, action.FilePath, action.Reason);
                executed++;
                continue;
            }

            try
            {
                switch (action.ActionType)
                {
                    case LifecycleActionType.TierMigration:
                        if (action.TargetTier.HasValue)
                        {
                            var result = await _tierMigration.MigrateAsync(
                                action.FilePath,
                                action.TargetTier.Value,
                                new MigrationOptions(
                                    DeleteSource: true,
                                    VerifyChecksum: true,
                                    ConvertFormat: action.TargetTier.Value >= StorageTier.Cold),
                                ct);

                            bytesMigrated += action.EstimatedSizeBytes;
                        }
                        executed++;
                        break;

                    case LifecycleActionType.Delete:
                        if (File.Exists(action.FilePath))
                        {
                            bytesDeleted += new FileInfo(action.FilePath).Length;
                            File.Delete(action.FilePath);
                            _fileStates.TryRemove(action.FilePath, out _);
                        }
                        executed++;
                        break;

                    case LifecycleActionType.CompressionUpgrade:
                        // Handled as part of tier migration
                        executed++;
                        break;

                    case LifecycleActionType.Archive:
                        if (action.TargetTier.HasValue)
                        {
                            await _tierMigration.MigrateAsync(
                                action.FilePath,
                                action.TargetTier.Value,
                                new MigrationOptions(
                                    DeleteSource: true,
                                    VerifyChecksum: true,
                                    ConvertFormat: true),
                                ct);
                            bytesMigrated += action.EstimatedSizeBytes;
                        }
                        executed++;
                        break;
                }
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"Failed to execute {action.ActionType} on {action.FilePath}: {ex.Message}");
                _logger.LogError(ex, "Lifecycle action {ActionType} failed for {FilePath}", action.ActionType, action.FilePath);
            }
        }

        _logger.LogInformation(
            "Lifecycle execution complete: {Executed} executed, {Failed} failed, {BytesMigrated} bytes migrated, {BytesDeleted} bytes deleted",
            executed, failed, bytesMigrated, bytesDeleted);

        return new LifecycleExecutionResult(
            ExecutedAtUtc: DateTime.UtcNow,
            ActionsExecuted: executed,
            ActionsFailed: failed,
            BytesMigrated: bytesMigrated,
            BytesDeleted: bytesDeleted,
            DryRun: dryRun,
            Errors: errors);
    }

    /// <inheritdoc />
    public StoragePolicyConfig ResolvePolicy(string filePath)
    {
        if (_options.Policies == null)
            return DefaultPolicy;

        // Try to match by event type from path
        var pathParts = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var part in pathParts)
        {
            if (_options.Policies.TryGetValue(part, out var policy))
                return policy;
        }

        // Try file name patterns
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        foreach (var kvp in _options.Policies)
        {
            if (fileName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }

        return DefaultPolicy;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<StorageTier, LifecycleTierInfo> GetTierStatistics()
    {
        var stats = new Dictionary<StorageTier, LifecycleTierInfo>();

        foreach (var tier in Enum.GetValues<StorageTier>())
        {
            var filesInTier = _fileStates.Where(kvp => kvp.Value.CurrentTier == tier).ToList();
            if (filesInTier.Count > 0)
            {
                stats[tier] = new LifecycleTierInfo(
                    FileCount: filesInTier.Count,
                    TotalBytes: filesInTier.Sum(f =>
                    {
                        try
                        { return new FileInfo(f.Key).Length; }
                        catch (IOException) { return 0L; }
                        catch (UnauthorizedAccessException) { return 0L; }
                    }),
                    OldestFileAge: filesInTier.Max(f => f.Value.FileAge),
                    NewestFileAge: filesInTier.Min(f => f.Value.FileAge));
            }
        }

        return stats;
    }

    private static StorageTier DetermineTier(string filePath)
    {
        var lowerPath = filePath.ToLowerInvariant();
        if (lowerPath.Contains("/archive/") || lowerPath.Contains("\\archive\\"))
            return StorageTier.Archive;
        if (lowerPath.Contains("/cold/") || lowerPath.Contains("\\cold\\"))
            return StorageTier.Cold;
        if (lowerPath.Contains("/warm/") || lowerPath.Contains("\\warm\\"))
            return StorageTier.Warm;
        if (lowerPath.Contains("/glacier/") || lowerPath.Contains("\\glacier\\"))
            return StorageTier.Glacier;
        return StorageTier.Hot;
    }

    private static StorageTier DetermineTargetTier(TimeSpan fileAge, StoragePolicyConfig policy)
    {
        if (policy.ArchiveTier == "perpetual" && fileAge.TotalDays > policy.ColdTierDays)
            return StorageTier.Archive;
        if (fileAge.TotalDays > policy.ColdTierDays)
            return StorageTier.Cold;
        if (fileAge.TotalDays > policy.WarmTierDays)
            return StorageTier.Warm;
        if (fileAge.TotalDays > policy.HotTierDays)
            return StorageTier.Warm;
        return StorageTier.Hot;
    }

    private static CompressionCodec? GetTargetCompression(StorageTier tier, StoragePolicyConfig policy)
    {
        if (policy.Compression == null)
            return null;

        var tierName = tier.ToString().ToLowerInvariant();
        return policy.Compression.TryGetValue(tierName, out var codec) ? codec : null;
    }

    private static bool HasCompression(string filePath, CompressionCodec codec)
    {
        return codec switch
        {
            CompressionCodec.Gzip => filePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase),
            CompressionCodec.Zstd => filePath.EndsWith(".zst", StringComparison.OrdinalIgnoreCase),
            CompressionCodec.LZ4 => filePath.EndsWith(".lz4", StringComparison.OrdinalIgnoreCase),
            CompressionCodec.Brotli => filePath.EndsWith(".br", StringComparison.OrdinalIgnoreCase),
            CompressionCodec.None => !filePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                                      && !filePath.EndsWith(".zst", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private static bool IsExpired(TimeSpan fileAge, StoragePolicyConfig policy)
    {
        if (policy.Classification == DataClassification.Critical)
            return false;

        if (policy.Archive?.MinRetention.HasValue == true)
            return false; // Perpetual archive

        if (policy.ArchiveTier == "perpetual")
            return false;

        // Total lifecycle: hot + warm + cold
        var totalRetentionDays = policy.HotTierDays + policy.WarmTierDays + policy.ColdTierDays;
        return fileAge.TotalDays > totalRetentionDays;
    }

    private static readonly StoragePolicyConfig DefaultPolicy = new()
    {
        Classification = DataClassification.Standard,
        HotTierDays = 7,
        WarmTierDays = 90,
        ColdTierDays = 365
    };
}

/// <summary>
/// Interface for lifecycle policy engine.
/// </summary>
public interface ILifecyclePolicyEngine
{
    Task<LifecycleEvaluationResult> EvaluateAsync(CancellationToken ct = default);
    Task<LifecycleExecutionResult> ExecuteAsync(IReadOnlyList<LifecycleAction> actions, bool dryRun = false, CancellationToken ct = default);
    StoragePolicyConfig ResolvePolicy(string filePath);
    IReadOnlyDictionary<StorageTier, LifecycleTierInfo> GetTierStatistics();
}

// Lifecycle types
public sealed record LifecycleEvaluationResult(
    DateTime EvaluatedAtUtc,
    int FilesEvaluated,
    IReadOnlyList<LifecycleAction> Actions,
    IReadOnlyList<string> Errors);

public sealed record LifecycleAction(
    string FilePath,
    LifecycleActionType ActionType,
    StorageTier? CurrentTier,
    StorageTier? TargetTier,
    string Reason,
    long EstimatedSizeBytes,
    StoragePolicyConfig? Policy = null);

public enum LifecycleActionType : byte
{
    TierMigration,
    CompressionUpgrade,
    Delete,
    Archive,
    FormatConversion
}

public sealed record LifecycleExecutionResult(
    DateTime ExecutedAtUtc,
    int ActionsExecuted,
    int ActionsFailed,
    long BytesMigrated,
    long BytesDeleted,
    bool DryRun,
    IReadOnlyList<string> Errors);

public sealed record LifecycleState(
    StorageTier CurrentTier,
    TimeSpan FileAge,
    DataClassification Classification);

public sealed record LifecycleTierInfo(
    int FileCount,
    long TotalBytes,
    TimeSpan OldestFileAge,
    TimeSpan NewestFileAge);

namespace Meridian.Storage;

/// <summary>
/// Storage configuration options for market data persistence.
/// </summary>
public sealed class StorageOptions
{
    /// <summary>
    /// Root directory path for all stored data files.
    /// Can be absolute or relative to the application directory.
    /// </summary>
    public string RootPath { get; init; } = "data";

    /// <summary>
    /// Whether to compress output files using gzip.
    /// </summary>
    public bool Compress { get; init; } = false;

    /// <summary>
    /// Compression codec to use when Compress is true.
    /// </summary>
    public CompressionCodec CompressionCodec { get; init; } = CompressionCodec.Gzip;

    /// <summary>
    /// File naming convention for organizing stored data.
    /// </summary>
    public FileNamingConvention NamingConvention { get; init; } = FileNamingConvention.BySymbol;

    /// <summary>
    /// Date partitioning strategy for files.
    /// </summary>
    public DatePartition DatePartition { get; init; } = DatePartition.Daily;

    /// <summary>
    /// Whether to include the data source/provider name in the file path.
    /// </summary>
    public bool IncludeProvider { get; init; } = false;

    /// <summary>
    /// Custom file name prefix (optional).
    /// </summary>
    public string? FilePrefix { get; init; }

    /// <summary>
    /// Optional retention window (in days). Older files are deleted eagerly when new data arrives.
    /// </summary>
    public int? RetentionDays { get; init; }

    /// <summary>
    /// Optional maximum on-disk footprint in bytes. When exceeded, the oldest files are deleted first.
    /// </summary>
    public long? MaxTotalBytes { get; init; }

    /// <summary>
    /// Storage tier configuration for tiered storage architecture.
    /// </summary>
    public TieringOptions? Tiering { get; init; }

    /// <summary>
    /// Quota configuration for capacity management.
    /// </summary>
    public QuotaOptions? Quotas { get; init; }

    /// <summary>
    /// Storage policy configuration by event type.
    /// </summary>
    public Dictionary<string, StoragePolicyConfig>? Policies { get; init; }

    /// <summary>
    /// Whether to generate file manifests for catalog indexing.
    /// </summary>
    public bool GenerateManifests { get; init; } = false;

    /// <summary>
    /// Whether to embed checksum in file names.
    /// </summary>
    public bool EmbedChecksum { get; init; } = false;

    /// <summary>
    /// Whether to verify per-record checksums when reading data files.
    /// When <c>true</c>, each record read from JSONL/WAL storage is validated against
    /// its stored checksum; corrupted records are skipped and logged.
    /// Incurs a small CPU overhead on read paths. Default is <c>false</c>.
    /// </summary>
    public bool VerifyOnRead { get; init; } = false;

    /// <summary>
    /// Whether to enable Parquet storage as an additional sink alongside JSONL.
    /// When enabled, events are written to both JSONL and Parquet via CompositeSink.
    /// </summary>
    /// <remarks>
    /// Superseded by <see cref="ActiveSinks"/> when that list is non-empty.
    /// Retained for backward compatibility.
    /// </remarks>
    public bool EnableParquetSink { get; init; } = false;

    /// <summary>
    /// Explicit list of storage sink plugin IDs to activate at startup.
    /// When non-empty, overrides <see cref="EnableParquetSink"/> and drives
    /// dynamic composition via <c>StorageSinkRegistry</c>.
    /// </summary>
    /// <remarks>
    /// Each entry must match the <c>Id</c> declared on a discovered
    /// <see cref="StorageSinkAttribute"/>-decorated class.
    /// Example: <c>["jsonl", "parquet"]</c>
    /// </remarks>
    public IReadOnlyList<string>? ActiveSinks { get; init; }

    /// <summary>
    /// Partition strategy for multi-dimensional partitioning.
    /// </summary>
    public PartitionStrategy? PartitionStrategy { get; init; }
}

/// <summary>
/// File naming and directory structure conventions.
/// </summary>
public enum FileNamingConvention : byte
{
    /// <summary>
    /// Flat structure: {root}/{symbol}_{type}_{date}.jsonl
    /// All files in root directory, good for small datasets.
    /// Example: data/AAPL_Trade_2024-01-15.jsonl
    /// </summary>
    Flat,

    /// <summary>
    /// Organize by symbol first: {root}/{symbol}/{type}/{date}.jsonl
    /// Best when analyzing individual symbols over time.
    /// Example: data/AAPL/Trade/2024-01-15.jsonl
    /// </summary>
    BySymbol,

    /// <summary>
    /// Organize by date first: {root}/{date}/{symbol}/{type}.jsonl
    /// Best for daily batch processing and archival.
    /// Example: data/2024-01-15/AAPL/Trade.jsonl
    /// </summary>
    ByDate,

    /// <summary>
    /// Organize by event type first: {root}/{type}/{symbol}/{date}.jsonl
    /// Best when analyzing specific event types across symbols.
    /// Example: data/Trade/AAPL/2024-01-15.jsonl
    /// </summary>
    ByType,

    /// <summary>
    /// Organize by data source: {root}/{source}/{symbol}/{type}/{date}.jsonl
    /// Best when comparing data across multiple providers.
    /// Example: data/alpaca/AAPL/Trade/2024-01-15.jsonl
    /// </summary>
    BySource,

    /// <summary>
    /// Organize by asset class: {root}/{asset_class}/{symbol}/{type}/{date}.jsonl
    /// Best when managing multiple asset classes.
    /// Example: data/equity/AAPL/Trade/2024-01-15.jsonl
    /// </summary>
    ByAssetClass,

    /// <summary>
    /// Full hierarchical taxonomy: {root}/{source}/{asset_class}/{symbol}/{type}/{date}.jsonl
    /// Best for enterprise multi-source, multi-asset deployments.
    /// Example: data/alpaca/equity/AAPL/Trade/2024-01-15.jsonl
    /// </summary>
    Hierarchical,

    /// <summary>
    /// Canonical time-based: {root}/{year}/{month}/{day}/{source}/{symbol}/{type}.jsonl
    /// Best for time-series analysis and archival.
    /// Example: data/2024/01/15/alpaca/AAPL/Trade.jsonl
    /// </summary>
    Canonical
}

/// <summary>
/// Date-based file partitioning strategy.
/// </summary>
public enum DatePartition : byte
{
    /// <summary>
    /// No date partitioning - all data in single file per symbol/type.
    /// File name: {symbol}_{type}.jsonl
    /// </summary>
    None,

    /// <summary>
    /// Partition by day: {date:yyyy-MM-dd}
    /// </summary>
    Daily,

    /// <summary>
    /// Partition by hour: {date:yyyy-MM-dd_HH}
    /// Good for high-volume data.
    /// </summary>
    Hourly,

    /// <summary>
    /// Partition by month: {date:yyyy-MM}
    /// Good for long-term storage with less granularity.
    /// </summary>
    Monthly
}

/// <summary>
/// Compression codec options.
/// </summary>
public enum CompressionCodec : byte
{
    /// <summary>No compression.</summary>
    None,
    /// <summary>LZ4 - fast compression, lower ratio.</summary>
    LZ4,
    /// <summary>Gzip - balanced compression.</summary>
    Gzip,
    /// <summary>Zstd - high compression ratio.</summary>
    Zstd,
    /// <summary>Brotli - high compression, slower.</summary>
    Brotli
}

/// <summary>
/// Data classification for storage policies.
/// </summary>
public enum DataClassification : byte
{
    /// <summary>Critical data - never delete (regulatory/compliance).</summary>
    Critical,
    /// <summary>Standard data - normal retention policies apply.</summary>
    Standard,
    /// <summary>Transient data - short-lived, deletable quickly.</summary>
    Transient,
    /// <summary>Derived data - can be regenerated, aggressive cleanup.</summary>
    Derived
}

/// <summary>
/// Storage tier designation.
/// </summary>
public enum StorageTier : byte
{
    /// <summary>Hot tier - real-time access, NVMe SSD.</summary>
    Hot,
    /// <summary>Warm tier - daily batch access, SSD/HDD.</summary>
    Warm,
    /// <summary>Cold tier - weekly/monthly access, HDD/NAS.</summary>
    Cold,
    /// <summary>Archive tier - rare bulk access, object storage.</summary>
    Archive,
    /// <summary>Glacier tier - emergency only, tape/deep archive.</summary>
    Glacier
}

/// <summary>
/// Quota enforcement policy.
/// </summary>
public enum QuotaEnforcementPolicy : byte
{
    /// <summary>Log warning, continue writing.</summary>
    Warn,
    /// <summary>Start cleanup, continue writing.</summary>
    SoftLimit,
    /// <summary>Stop writing until cleanup completes.</summary>
    HardLimit,
    /// <summary>Delete oldest to make room.</summary>
    DropOldest
}

/// <summary>
/// Archive reason for perpetual data.
/// </summary>
public enum ArchiveReason : byte
{
    /// <summary>SEC Rule 17a-4, MiFID II.</summary>
    Regulatory,
    /// <summary>Internal audit requirements.</summary>
    Compliance,
    /// <summary>ML training datasets.</summary>
    Research,
    /// <summary>Litigation hold.</summary>
    Legal,
    /// <summary>Reference data.</summary>
    Historical
}

/// <summary>
/// Conflict resolution strategy for multi-source data.
/// </summary>
public enum ConflictStrategy : byte
{
    /// <summary>Keep first source's data.</summary>
    FirstWins,
    /// <summary>Keep latest source's data.</summary>
    LastWins,
    /// <summary>Use priority order.</summary>
    HighestPriority,
    /// <summary>Prefer lowest-latency source.</summary>
    LowestLatency,
    /// <summary>Source with most fields populated.</summary>
    MostComplete,
    /// <summary>Combine and deduplicate.</summary>
    Merge
}

/// <summary>
/// Partition dimension for multi-dimensional partitioning.
/// </summary>
public enum PartitionDimension : byte
{
    /// <summary>Partition by date.</summary>
    Date,
    /// <summary>Partition by symbol.</summary>
    Symbol,
    /// <summary>Partition by event type.</summary>
    EventType,
    /// <summary>Partition by data source.</summary>
    Source,
    /// <summary>Partition by asset class.</summary>
    AssetClass,
    /// <summary>Partition by exchange.</summary>
    Exchange
}

/// <summary>
/// Multi-dimensional partition strategy.
/// </summary>
public sealed record PartitionStrategy(
    PartitionDimension Primary,
    PartitionDimension? Secondary = null,
    PartitionDimension? Tertiary = null,
    DatePartition DateGranularity = DatePartition.Daily
);

/// <summary>
/// Tiering configuration for multi-tier storage.
/// </summary>
public sealed class TieringOptions
{
    /// <summary>Whether tiering is enabled.</summary>
    public bool Enabled { get; init; }

    /// <summary>Tier configurations.</summary>
    public List<TierConfig> Tiers { get; init; } = new();

    /// <summary>Cron schedule for migration (e.g., "0 2 * * *").</summary>
    public string? MigrationSchedule { get; init; }

    /// <summary>Number of parallel migrations.</summary>
    public int ParallelMigrations { get; init; } = 4;
}

/// <summary>
/// Configuration for a single storage tier.
/// </summary>
public sealed class TierConfig
{
    /// <summary>Tier name (hot, warm, cold, archive).</summary>
    public string Name { get; init; } = "";

    /// <summary>Storage path for this tier.</summary>
    public string Path { get; init; } = "";

    /// <summary>Maximum age in days before migration to next tier.</summary>
    public int? MaxAgeDays { get; init; }

    /// <summary>Maximum size in GB for this tier.</summary>
    public long? MaxSizeGb { get; init; }

    /// <summary>File format for this tier (jsonl, jsonl.gz, parquet).</summary>
    public string Format { get; init; } = "jsonl";

    /// <summary>Compression for this tier.</summary>
    public CompressionCodec? Compression { get; init; }

    /// <summary>S3 storage class for cloud tiers.</summary>
    public string? StorageClass { get; init; }
}

/// <summary>
/// Quota configuration for capacity management.
/// </summary>
public sealed class QuotaOptions
{
    /// <summary>Global storage quota.</summary>
    public StorageQuota? Global { get; init; }

    /// <summary>Per-source quotas.</summary>
    public Dictionary<string, StorageQuota>? PerSource { get; init; }

    /// <summary>Per-symbol quotas.</summary>
    public Dictionary<string, StorageQuota>? PerSymbol { get; init; }

    /// <summary>Per-event-type quotas.</summary>
    public Dictionary<string, StorageQuota>? PerEventType { get; init; }

    /// <summary>Dynamic quota configuration.</summary>
    public DynamicQuotaConfig? Dynamic { get; init; }
}

/// <summary>
/// Storage quota specification.
/// </summary>
public sealed class StorageQuota
{
    /// <summary>Maximum bytes for this quota.</summary>
    public long MaxBytes { get; init; }

    /// <summary>Maximum number of files.</summary>
    public long? MaxFiles { get; init; }

    /// <summary>Maximum events per day.</summary>
    public long? MaxEventsPerDay { get; init; }

    /// <summary>Enforcement policy when quota is exceeded.</summary>
    public QuotaEnforcementPolicy Enforcement { get; init; } = QuotaEnforcementPolicy.SoftLimit;
}

/// <summary>
/// Dynamic quota configuration.
/// </summary>
public sealed class DynamicQuotaConfig
{
    /// <summary>Whether dynamic quotas are enabled.</summary>
    public bool Enabled { get; init; }

    /// <summary>How often to rebalance quotas.</summary>
    public TimeSpan EvaluationPeriod { get; init; } = TimeSpan.FromHours(1);

    /// <summary>Minimum reserve percentage to keep free.</summary>
    public double MinReservePct { get; init; } = 10;

    /// <summary>Allow burst above quota by this factor.</summary>
    public double OverprovisionFactor { get; init; } = 1.1;

    /// <summary>Reallocate from unused quotas.</summary>
    public bool StealFromInactive { get; init; }
}

/// <summary>
/// Storage policy configuration for an event type.
/// </summary>
public sealed class StoragePolicyConfig
{
    /// <summary>Data classification.</summary>
    public DataClassification Classification { get; init; } = DataClassification.Standard;

    /// <summary>Days in hot tier.</summary>
    public int HotTierDays { get; init; } = 7;

    /// <summary>Days in warm tier.</summary>
    public int WarmTierDays { get; init; } = 90;

    /// <summary>Days in cold tier.</summary>
    public int ColdTierDays { get; init; } = 365;

    /// <summary>Archive tier: "perpetual" or null.</summary>
    public string? ArchiveTier { get; init; }

    /// <summary>Compression by tier.</summary>
    public Dictionary<string, CompressionCodec>? Compression { get; init; }

    /// <summary>Archive policy for perpetual data.</summary>
    public ArchivePolicyConfig? Archive { get; init; }
}

/// <summary>
/// Archive policy for perpetual data.
/// </summary>
public sealed class ArchivePolicyConfig
{
    /// <summary>Reason for archival.</summary>
    public ArchiveReason Reason { get; init; }

    /// <summary>Description of archive policy.</summary>
    public string? Description { get; init; }

    /// <summary>Whether data is immutable (write-once).</summary>
    public bool Immutable { get; init; }

    /// <summary>Whether encryption is required.</summary>
    public bool RequiresEncryption { get; init; }

    /// <summary>Minimum retention before deletion allowed.</summary>
    public TimeSpan? MinRetention { get; init; }

    /// <summary>Required approvers for deletion.</summary>
    public string[]? ApproversForDelete { get; init; }
}

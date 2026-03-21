using System.Text.Json.Serialization;

namespace Meridian.Contracts.Catalog;

/// <summary>
/// Root storage catalog manifest stored at _catalog/manifest.json.
/// Provides a comprehensive index of all stored data with integrity metadata.
/// </summary>
public sealed class StorageCatalog
{
    /// <summary>
    /// Catalog format version for compatibility checking.
    /// </summary>
    [JsonPropertyName("catalogVersion")]
    public string CatalogVersion { get; set; } = "1.0.0";

    /// <summary>
    /// Unique identifier for this catalog instance.
    /// </summary>
    [JsonPropertyName("catalogId")]
    public string CatalogId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// When the catalog was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the catalog was last updated.
    /// </summary>
    [JsonPropertyName("lastUpdatedAt")]
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Version of Meridian that created this catalog.
    /// </summary>
    [JsonPropertyName("creatorVersion")]
    public string CreatorVersion { get; set; } = "1.6.1";

    /// <summary>
    /// Global date range across all stored data.
    /// </summary>
    [JsonPropertyName("dateRange")]
    public CatalogDateRange? DateRange { get; set; }

    /// <summary>
    /// Storage statistics summary.
    /// </summary>
    [JsonPropertyName("statistics")]
    public CatalogStatistics Statistics { get; set; } = new();

    /// <summary>
    /// Symbol-level entries with per-symbol statistics.
    /// </summary>
    [JsonPropertyName("symbols")]
    public Dictionary<string, SymbolCatalogEntry> Symbols { get; set; } = new();

    /// <summary>
    /// Data source metadata (references _catalog/sources.json).
    /// </summary>
    [JsonPropertyName("sources")]
    public string[] Sources { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Schema versions in use (references _catalog/schemas/).
    /// </summary>
    [JsonPropertyName("schemas")]
    public Dictionary<string, SchemaReference> Schemas { get; set; } = new();

    /// <summary>
    /// Directory index references.
    /// </summary>
    [JsonPropertyName("directoryIndexes")]
    public string[] DirectoryIndexes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Integrity verification status.
    /// </summary>
    [JsonPropertyName("integrity")]
    public CatalogIntegrity Integrity { get; set; } = new();

    /// <summary>
    /// Configuration snapshot when catalog was created.
    /// </summary>
    [JsonPropertyName("configuration")]
    public CatalogConfiguration? Configuration { get; set; }
}

/// <summary>
/// Date range information for the catalog.
/// </summary>
public sealed class CatalogDateRange
{
    /// <summary>
    /// Earliest data timestamp.
    /// </summary>
    [JsonPropertyName("earliest")]
    public DateTime Earliest { get; set; }

    /// <summary>
    /// Latest data timestamp.
    /// </summary>
    [JsonPropertyName("latest")]
    public DateTime Latest { get; set; }

    /// <summary>
    /// Total calendar days covered.
    /// </summary>
    [JsonPropertyName("calendarDays")]
    public int CalendarDays { get; set; }

    /// <summary>
    /// Estimated trading days covered.
    /// </summary>
    [JsonPropertyName("tradingDays")]
    public int TradingDays { get; set; }
}

/// <summary>
/// Aggregate statistics for the storage catalog.
/// </summary>
public sealed class CatalogStatistics
{
    /// <summary>
    /// Total number of data files.
    /// </summary>
    [JsonPropertyName("totalFiles")]
    public long TotalFiles { get; set; }

    /// <summary>
    /// Total number of events across all files.
    /// </summary>
    [JsonPropertyName("totalEvents")]
    public long TotalEvents { get; set; }

    /// <summary>
    /// Total size in bytes (compressed on disk).
    /// </summary>
    [JsonPropertyName("totalBytesCompressed")]
    public long TotalBytesCompressed { get; set; }

    /// <summary>
    /// Total uncompressed size in bytes.
    /// </summary>
    [JsonPropertyName("totalBytesRaw")]
    public long TotalBytesRaw { get; set; }

    /// <summary>
    /// Compression ratio (raw / compressed).
    /// </summary>
    [JsonPropertyName("compressionRatio")]
    public float CompressionRatio { get; set; }

    /// <summary>
    /// Number of unique symbols.
    /// </summary>
    [JsonPropertyName("uniqueSymbols")]
    public int UniqueSymbols { get; set; }

    /// <summary>
    /// Number of unique data sources.
    /// </summary>
    [JsonPropertyName("uniqueSources")]
    public int UniqueSources { get; set; }

    /// <summary>
    /// Event type breakdown.
    /// </summary>
    [JsonPropertyName("eventTypeCounts")]
    public Dictionary<string, long> EventTypeCounts { get; set; } = new();

    /// <summary>
    /// Storage tier breakdown.
    /// </summary>
    [JsonPropertyName("tierSizes")]
    public Dictionary<string, long> TierSizes { get; set; } = new();
}

/// <summary>
/// Per-symbol catalog entry.
/// </summary>
public sealed class SymbolCatalogEntry
{
    /// <summary>
    /// Canonical symbol name.
    /// </summary>
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Known aliases for this symbol.
    /// </summary>
    [JsonPropertyName("aliases")]
    public string[]? Aliases { get; set; }

    /// <summary>
    /// Asset class (equity, option, future, forex, crypto).
    /// </summary>
    [JsonPropertyName("assetClass")]
    public string? AssetClass { get; set; }

    /// <summary>
    /// Primary exchange.
    /// </summary>
    [JsonPropertyName("exchange")]
    public string? Exchange { get; set; }

    /// <summary>
    /// Date range for this symbol's data.
    /// </summary>
    [JsonPropertyName("dateRange")]
    public CatalogDateRange? DateRange { get; set; }

    /// <summary>
    /// Number of files for this symbol.
    /// </summary>
    [JsonPropertyName("fileCount")]
    public int FileCount { get; set; }

    /// <summary>
    /// Total events for this symbol.
    /// </summary>
    [JsonPropertyName("eventCount")]
    public long EventCount { get; set; }

    /// <summary>
    /// Total bytes for this symbol.
    /// </summary>
    [JsonPropertyName("totalBytes")]
    public long TotalBytes { get; set; }

    /// <summary>
    /// Available event types for this symbol.
    /// </summary>
    [JsonPropertyName("eventTypes")]
    public string[] EventTypes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Data sources for this symbol.
    /// </summary>
    [JsonPropertyName("sources")]
    public string[] Sources { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Last updated timestamp.
    /// </summary>
    [JsonPropertyName("lastUpdated")]
    public DateTime LastUpdated { get; set; }

    /// <summary>
    /// Sequence range tracking.
    /// </summary>
    [JsonPropertyName("sequenceRange")]
    public SequenceRange? SequenceRange { get; set; }
}

/// <summary>
/// Sequence range for data ordering verification.
/// </summary>
public sealed class SequenceRange
{
    /// <summary>
    /// First sequence number observed.
    /// </summary>
    [JsonPropertyName("firstSequence")]
    public long FirstSequence { get; set; }

    /// <summary>
    /// Last sequence number observed.
    /// </summary>
    [JsonPropertyName("lastSequence")]
    public long LastSequence { get; set; }

    /// <summary>
    /// Number of gaps detected in sequence.
    /// </summary>
    [JsonPropertyName("gapsDetected")]
    public int GapsDetected { get; set; }

    /// <summary>
    /// Expected total based on sequence range.
    /// </summary>
    [JsonPropertyName("expectedCount")]
    public long ExpectedCount { get; set; }

    /// <summary>
    /// Actual count recorded.
    /// </summary>
    [JsonPropertyName("actualCount")]
    public long ActualCount { get; set; }
}

/// <summary>
/// Reference to a schema version.
/// </summary>
public sealed class SchemaReference
{
    /// <summary>
    /// Event type name.
    /// </summary>
    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Current version in use.
    /// </summary>
    [JsonPropertyName("currentVersion")]
    public string CurrentVersion { get; set; } = "1.0.0";

    /// <summary>
    /// All versions present in storage.
    /// </summary>
    [JsonPropertyName("versionsInUse")]
    public string[] VersionsInUse { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Path to schema definition file.
    /// </summary>
    [JsonPropertyName("schemaPath")]
    public string? SchemaPath { get; set; }

    /// <summary>
    /// File count by version.
    /// </summary>
    [JsonPropertyName("fileCountByVersion")]
    public Dictionary<string, int> FileCountByVersion { get; set; } = new();
}

/// <summary>
/// Catalog integrity verification information.
/// </summary>
public sealed class CatalogIntegrity
{
    /// <summary>
    /// Overall verification status.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "Pending";

    /// <summary>
    /// When verification was last run.
    /// </summary>
    [JsonPropertyName("lastVerifiedAt")]
    public DateTime? LastVerifiedAt { get; set; }

    /// <summary>
    /// Number of files verified.
    /// </summary>
    [JsonPropertyName("filesVerified")]
    public int FilesVerified { get; set; }

    /// <summary>
    /// Number of files with checksum mismatches.
    /// </summary>
    [JsonPropertyName("checksumFailures")]
    public int ChecksumFailures { get; set; }

    /// <summary>
    /// Number of missing files.
    /// </summary>
    [JsonPropertyName("missingFiles")]
    public int MissingFiles { get; set; }

    /// <summary>
    /// SHA256 checksum of the catalog itself.
    /// </summary>
    [JsonPropertyName("catalogChecksum")]
    public string? CatalogChecksum { get; set; }

    /// <summary>
    /// Integrity issues found.
    /// </summary>
    [JsonPropertyName("issues")]
    public List<CatalogIntegrityIssue> Issues { get; set; } = new();
}

/// <summary>
/// Individual integrity issue.
/// </summary>
public sealed class CatalogIntegrityIssue
{
    /// <summary>
    /// Issue severity (Warning, Error, Critical).
    /// </summary>
    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "Warning";

    /// <summary>
    /// Issue type.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Affected file path.
    /// </summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    /// <summary>
    /// Issue description.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// When the issue was detected.
    /// </summary>
    [JsonPropertyName("detectedAt")]
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Configuration snapshot for reproducibility.
/// </summary>
public sealed class CatalogConfiguration
{
    /// <summary>
    /// File naming convention.
    /// </summary>
    [JsonPropertyName("namingConvention")]
    public string? NamingConvention { get; set; }

    /// <summary>
    /// Date partitioning strategy.
    /// </summary>
    [JsonPropertyName("datePartition")]
    public string? DatePartition { get; set; }

    /// <summary>
    /// Compression codec in use.
    /// </summary>
    [JsonPropertyName("compression")]
    public string? Compression { get; set; }

    /// <summary>
    /// Storage root path.
    /// </summary>
    [JsonPropertyName("rootPath")]
    public string? RootPath { get; set; }
}

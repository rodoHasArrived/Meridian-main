using System.Text.Json.Serialization;

namespace Meridian.Contracts.Catalog;

/// <summary>
/// Per-directory metadata index stored as _index.json in each data directory.
/// Provides file-level metadata with checksums, counts, and sequence ranges.
/// </summary>
public sealed class DirectoryIndex
{
    /// <summary>
    /// Index format version.
    /// </summary>
    [JsonPropertyName("indexVersion")]
    public string IndexVersion { get; set; } = "1.0.0";

    /// <summary>
    /// Relative path from storage root.
    /// </summary>
    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// When this index was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this index was last updated.
    /// </summary>
    [JsonPropertyName("lastUpdatedAt")]
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Directory-level statistics.
    /// </summary>
    [JsonPropertyName("statistics")]
    public DirectoryStatistics Statistics { get; set; } = new();

    /// <summary>
    /// File entries in this directory.
    /// </summary>
    [JsonPropertyName("files")]
    public List<IndexedFileEntry> Files { get; set; } = new();

    /// <summary>
    /// Subdirectory references (relative paths to child _index.json files).
    /// </summary>
    [JsonPropertyName("subdirectories")]
    public string[] Subdirectories { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Symbols present in this directory.
    /// </summary>
    [JsonPropertyName("symbols")]
    public string[] Symbols { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Event types present in this directory.
    /// </summary>
    [JsonPropertyName("eventTypes")]
    public string[] EventTypes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Data sources present in this directory.
    /// </summary>
    [JsonPropertyName("sources")]
    public string[] Sources { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Date range for data in this directory.
    /// </summary>
    [JsonPropertyName("dateRange")]
    public DirectoryDateRange? DateRange { get; set; }

    /// <summary>
    /// Integrity checksum for this index file.
    /// </summary>
    [JsonPropertyName("indexChecksum")]
    public string? IndexChecksum { get; set; }
}

/// <summary>
/// Statistics for a directory.
/// </summary>
public sealed class DirectoryStatistics
{
    /// <summary>
    /// Number of data files.
    /// </summary>
    [JsonPropertyName("fileCount")]
    public int FileCount { get; set; }

    /// <summary>
    /// Total events across all files.
    /// </summary>
    [JsonPropertyName("totalEvents")]
    public long TotalEvents { get; set; }

    /// <summary>
    /// Total compressed size in bytes.
    /// </summary>
    [JsonPropertyName("totalBytesCompressed")]
    public long TotalBytesCompressed { get; set; }

    /// <summary>
    /// Total uncompressed size in bytes.
    /// </summary>
    [JsonPropertyName("totalBytesRaw")]
    public long TotalBytesRaw { get; set; }

    /// <summary>
    /// Number of subdirectories.
    /// </summary>
    [JsonPropertyName("subdirectoryCount")]
    public int SubdirectoryCount { get; set; }

    /// <summary>
    /// Event counts by type.
    /// </summary>
    [JsonPropertyName("eventCountsByType")]
    public Dictionary<string, long> EventCountsByType { get; set; } = new();

    /// <summary>
    /// File counts by symbol.
    /// </summary>
    [JsonPropertyName("fileCountsBySymbol")]
    public Dictionary<string, int> FileCountsBySymbol { get; set; } = new();
}

/// <summary>
/// Individual file entry in the directory index.
/// </summary>
public sealed class IndexedFileEntry
{
    /// <summary>
    /// File name (without path).
    /// </summary>
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Relative path from storage root.
    /// </summary>
    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// File format (jsonl, parquet, csv).
    /// </summary>
    [JsonPropertyName("format")]
    public string Format { get; set; } = "jsonl";

    /// <summary>
    /// Whether the file is compressed.
    /// </summary>
    [JsonPropertyName("isCompressed")]
    public bool IsCompressed { get; set; }

    /// <summary>
    /// Compression type if compressed.
    /// </summary>
    [JsonPropertyName("compressionType")]
    public string? CompressionType { get; set; }

    /// <summary>
    /// Symbol this file contains data for.
    /// </summary>
    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    /// <summary>
    /// Event type in this file.
    /// </summary>
    [JsonPropertyName("eventType")]
    public string? EventType { get; set; }

    /// <summary>
    /// Data source/provider.
    /// </summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    /// <summary>
    /// Date of the data (for date-partitioned files).
    /// </summary>
    [JsonPropertyName("date")]
    public DateTime? Date { get; set; }

    /// <summary>
    /// Schema version used in this file.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public string? SchemaVersion { get; set; }

    /// <summary>
    /// Compressed size on disk in bytes.
    /// </summary>
    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    /// <summary>
    /// Uncompressed size in bytes.
    /// </summary>
    [JsonPropertyName("uncompressedSizeBytes")]
    public long UncompressedSizeBytes { get; set; }

    /// <summary>
    /// Number of events/records in the file.
    /// </summary>
    [JsonPropertyName("eventCount")]
    public long EventCount { get; set; }

    /// <summary>
    /// SHA256 checksum for integrity verification.
    /// </summary>
    [JsonPropertyName("checksumSha256")]
    public string ChecksumSha256 { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp of the first event.
    /// </summary>
    [JsonPropertyName("firstTimestamp")]
    public DateTime? FirstTimestamp { get; set; }

    /// <summary>
    /// Timestamp of the last event.
    /// </summary>
    [JsonPropertyName("lastTimestamp")]
    public DateTime? LastTimestamp { get; set; }

    /// <summary>
    /// Sequence number of the first event.
    /// </summary>
    [JsonPropertyName("firstSequence")]
    public long? FirstSequence { get; set; }

    /// <summary>
    /// Sequence number of the last event.
    /// </summary>
    [JsonPropertyName("lastSequence")]
    public long? LastSequence { get; set; }

    /// <summary>
    /// Number of sequence gaps detected within this file.
    /// </summary>
    [JsonPropertyName("sequenceGaps")]
    public int SequenceGaps { get; set; }

    /// <summary>
    /// File last modified time.
    /// </summary>
    [JsonPropertyName("lastModified")]
    public DateTime LastModified { get; set; }

    /// <summary>
    /// When this entry was indexed.
    /// </summary>
    [JsonPropertyName("indexedAt")]
    public DateTime IndexedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Verification status.
    /// </summary>
    [JsonPropertyName("verificationStatus")]
    public string VerificationStatus { get; set; } = "Pending";

    /// <summary>
    /// Additional metadata.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }
}

/// <summary>
/// Date range for a directory.
/// </summary>
public sealed class DirectoryDateRange
{
    /// <summary>
    /// Earliest timestamp in this directory.
    /// </summary>
    [JsonPropertyName("earliest")]
    public DateTime Earliest { get; set; }

    /// <summary>
    /// Latest timestamp in this directory.
    /// </summary>
    [JsonPropertyName("latest")]
    public DateTime Latest { get; set; }

    /// <summary>
    /// Date granularity (Daily, Hourly, Monthly).
    /// </summary>
    [JsonPropertyName("granularity")]
    public string? Granularity { get; set; }

    /// <summary>
    /// Dates with data in this directory.
    /// </summary>
    [JsonPropertyName("datesWithData")]
    public string[]? DatesWithData { get; set; }
}

/// <summary>
/// Result of a directory scan operation.
/// </summary>
public sealed class DirectoryScanResult
{
    /// <summary>
    /// Whether the scan was successful.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>
    /// Directory path that was scanned.
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// The generated directory index.
    /// </summary>
    [JsonPropertyName("index")]
    public DirectoryIndex? Index { get; set; }

    /// <summary>
    /// Number of files scanned.
    /// </summary>
    [JsonPropertyName("filesScanned")]
    public int FilesScanned { get; set; }

    /// <summary>
    /// Number of files with errors.
    /// </summary>
    [JsonPropertyName("filesWithErrors")]
    public int FilesWithErrors { get; set; }

    /// <summary>
    /// Scan duration.
    /// </summary>
    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    /// <summary>
    /// Error message if scan failed.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>
    /// Warnings encountered during scan.
    /// </summary>
    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();
}

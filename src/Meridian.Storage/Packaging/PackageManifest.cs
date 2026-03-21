using System.Text.Json.Serialization;
using Meridian.Contracts.Manifest;

namespace Meridian.Storage.Packaging;

/// <summary>
/// Comprehensive manifest for a portable data package.
/// Extends DataManifest with packaging-specific metadata.
/// </summary>
public sealed class PackageManifest
{
    /// <summary>
    /// Package manifest version.
    /// </summary>
    [JsonPropertyName("packageVersion")]
    public string PackageVersion { get; set; } = "1.0.0";

    /// <summary>
    /// Unique identifier for this package.
    /// </summary>
    [JsonPropertyName("packageId")]
    public string PackageId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Human-readable package name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of the package contents.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// When the package was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Version of Meridian that created the package.
    /// </summary>
    [JsonPropertyName("creatorVersion")]
    public string CreatorVersion { get; set; } = "1.6.1";

    /// <summary>
    /// Machine/hostname that created the package.
    /// </summary>
    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; set; }

    /// <summary>
    /// Package format (Zip, TarGz, etc.).
    /// </summary>
    [JsonPropertyName("format")]
    public string Format { get; set; } = "Zip";

    /// <summary>
    /// Whether the package is encrypted.
    /// </summary>
    [JsonPropertyName("encrypted")]
    public bool Encrypted { get; set; }

    /// <summary>
    /// Compression algorithm used.
    /// </summary>
    [JsonPropertyName("compression")]
    public string Compression { get; set; } = "Deflate";

    /// <summary>
    /// SHA256 checksum of the entire package file.
    /// </summary>
    [JsonPropertyName("packageChecksum")]
    public string? PackageChecksum { get; set; }

    /// <summary>
    /// Total size of the package file in bytes.
    /// </summary>
    [JsonPropertyName("packageSizeBytes")]
    public long PackageSizeBytes { get; set; }

    /// <summary>
    /// Total uncompressed size of all data files.
    /// </summary>
    [JsonPropertyName("uncompressedSizeBytes")]
    public long UncompressedSizeBytes { get; set; }

    /// <summary>
    /// Data date range information.
    /// </summary>
    [JsonPropertyName("dateRange")]
    public PackageDateRange? DateRange { get; set; }

    /// <summary>
    /// Symbols included in the package.
    /// </summary>
    [JsonPropertyName("symbols")]
    public string[] Symbols { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Event types included in the package.
    /// </summary>
    [JsonPropertyName("eventTypes")]
    public string[] EventTypes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Data sources included in the package.
    /// </summary>
    [JsonPropertyName("sources")]
    public string[] Sources { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Total number of data files in the package.
    /// </summary>
    [JsonPropertyName("totalFiles")]
    public int TotalFiles { get; set; }

    /// <summary>
    /// Total number of events/records in the package.
    /// </summary>
    [JsonPropertyName("totalEvents")]
    public long TotalEvents { get; set; }

    /// <summary>
    /// Detailed file entries.
    /// </summary>
    [JsonPropertyName("files")]
    public PackageFileEntry[] Files { get; set; } = Array.Empty<PackageFileEntry>();

    /// <summary>
    /// Data quality metrics summary.
    /// </summary>
    [JsonPropertyName("quality")]
    public PackageQualityMetrics? Quality { get; set; }

    /// <summary>
    /// Schema definitions for each event type.
    /// </summary>
    [JsonPropertyName("schemas")]
    public Dictionary<string, PackageSchema>? Schemas { get; set; }

    /// <summary>
    /// Internal file layout used in the package.
    /// </summary>
    [JsonPropertyName("layout")]
    public string Layout { get; set; } = "ByDate";

    /// <summary>
    /// Tags for categorization.
    /// </summary>
    [JsonPropertyName("tags")]
    public string[]? Tags { get; set; }

    /// <summary>
    /// Custom metadata provided by the creator.
    /// </summary>
    [JsonPropertyName("customMetadata")]
    public Dictionary<string, string>? CustomMetadata { get; set; }

    /// <summary>
    /// Package signature for authenticity verification.
    /// </summary>
    [JsonPropertyName("signature")]
    public string? Signature { get; set; }

    /// <summary>
    /// Included supplementary files.
    /// </summary>
    [JsonPropertyName("supplementaryFiles")]
    public SupplementaryFileInfo[]? SupplementaryFiles { get; set; }

    /// <summary>
    /// Part number if this is a multi-part package.
    /// </summary>
    [JsonPropertyName("partNumber")]
    public int? PartNumber { get; set; }

    /// <summary>
    /// Total parts if this is a multi-part package.
    /// </summary>
    [JsonPropertyName("totalParts")]
    public int? TotalParts { get; set; }

    /// <summary>
    /// Minimum required version of Meridian to import this package.
    /// </summary>
    [JsonPropertyName("minRequiredVersion")]
    public string MinRequiredVersion { get; set; } = "1.0.0";
}

/// <summary>
/// Date range information for the package.
/// </summary>
public sealed class PackageDateRange
{
    [JsonPropertyName("start")]
    public DateTime Start { get; set; }

    [JsonPropertyName("end")]
    public DateTime End { get; set; }

    [JsonPropertyName("tradingDays")]
    public int TradingDays { get; set; }

    [JsonPropertyName("calendarDays")]
    public int CalendarDays { get; set; }
}

/// <summary>
/// Entry for a data file in the package.
/// </summary>
public sealed class PackageFileEntry
{
    /// <summary>
    /// Relative path within the package.
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Symbol this file contains data for.
    /// </summary>
    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    /// <summary>
    /// Event type stored in this file.
    /// </summary>
    [JsonPropertyName("eventType")]
    public string? EventType { get; set; }

    /// <summary>
    /// Date of data in this file.
    /// </summary>
    [JsonPropertyName("date")]
    public DateTime? Date { get; set; }

    /// <summary>
    /// Data source/provider.
    /// </summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    /// <summary>
    /// File format (jsonl, csv, parquet).
    /// </summary>
    [JsonPropertyName("format")]
    public string Format { get; set; } = "jsonl";

    /// <summary>
    /// Whether the file is compressed within the package.
    /// </summary>
    [JsonPropertyName("compressed")]
    public bool Compressed { get; set; }

    /// <summary>
    /// Compression type if compressed.
    /// </summary>
    [JsonPropertyName("compressionType")]
    public string? CompressionType { get; set; }

    /// <summary>
    /// File size within the package (compressed if applicable).
    /// </summary>
    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    /// <summary>
    /// Original uncompressed size.
    /// </summary>
    [JsonPropertyName("uncompressedSizeBytes")]
    public long UncompressedSizeBytes { get; set; }

    /// <summary>
    /// Number of events/records in the file.
    /// </summary>
    [JsonPropertyName("eventCount")]
    public long EventCount { get; set; }

    /// <summary>
    /// SHA256 checksum of the file.
    /// </summary>
    [JsonPropertyName("checksumSha256")]
    public string ChecksumSha256 { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp of first event.
    /// </summary>
    [JsonPropertyName("firstTimestamp")]
    public DateTime? FirstTimestamp { get; set; }

    /// <summary>
    /// Timestamp of last event.
    /// </summary>
    [JsonPropertyName("lastTimestamp")]
    public DateTime? LastTimestamp { get; set; }

    /// <summary>
    /// Schema version used for this file.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public string? SchemaVersion { get; set; }
}

/// <summary>
/// Quality metrics for the package data.
/// </summary>
public sealed class PackageQualityMetrics
{
    [JsonPropertyName("overallScore")]
    public double OverallScore { get; set; }

    [JsonPropertyName("completenessScore")]
    public double CompletenessScore { get; set; }

    [JsonPropertyName("integrityScore")]
    public double IntegrityScore { get; set; }

    [JsonPropertyName("totalExpectedEvents")]
    public long TotalExpectedEvents { get; set; }

    [JsonPropertyName("totalActualEvents")]
    public long TotalActualEvents { get; set; }

    [JsonPropertyName("gapsDetected")]
    public int GapsDetected { get; set; }

    [JsonPropertyName("sequenceErrors")]
    public int SequenceErrors { get; set; }

    [JsonPropertyName("duplicatesFound")]
    public int DuplicatesFound { get; set; }

    [JsonPropertyName("outliersDetected")]
    public int OutliersDetected { get; set; }

    [JsonPropertyName("grade")]
    public string Grade { get; set; } = "Unknown";

    [JsonPropertyName("issues")]
    public string[]? Issues { get; set; }
}

/// <summary>
/// Schema definition for an event type.
/// </summary>
public sealed class PackageSchema
{
    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("fields")]
    public PackageSchemaField[] Fields { get; set; } = Array.Empty<PackageSchemaField>();

    [JsonPropertyName("jsonSchema")]
    public string? JsonSchema { get; set; }
}

/// <summary>
/// Field definition within a schema.
/// </summary>
public sealed class PackageSchemaField
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("nullable")]
    public bool Nullable { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("example")]
    public string? Example { get; set; }
}

/// <summary>
/// Information about supplementary files in the package.
/// </summary>
public sealed class SupplementaryFileInfo
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }
}

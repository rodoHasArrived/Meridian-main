using System.Text.Json.Serialization;

namespace Meridian.Contracts.Manifest;

/// <summary>
/// Comprehensive manifest for a collection session or archive package.
/// </summary>
public sealed class DataManifest
{
    /// <summary>
    /// Gets or sets the manifest format version.
    /// </summary>
    [JsonPropertyName("manifestVersion")]
    public string ManifestVersion { get; set; } = "1.0";

    /// <summary>
    /// Gets or sets the timestamp when the manifest was generated.
    /// </summary>
    [JsonPropertyName("generatedAt")]
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the unique session identifier.
    /// </summary>
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    /// <summary>
    /// Gets or sets the human-readable session name.
    /// </summary>
    [JsonPropertyName("sessionName")]
    public string? SessionName { get; set; }

    /// <summary>
    /// Gets or sets the date range information for the data.
    /// </summary>
    [JsonPropertyName("dateRange")]
    public DateRangeInfo? DateRange { get; set; }

    /// <summary>
    /// Gets or sets the array of symbols included in this manifest.
    /// </summary>
    [JsonPropertyName("symbols")]
    public string[] Symbols { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the total number of files in the manifest.
    /// </summary>
    [JsonPropertyName("totalFiles")]
    public int TotalFiles { get; set; }

    /// <summary>
    /// Gets or sets the total number of events across all files.
    /// </summary>
    [JsonPropertyName("totalEvents")]
    public long TotalEvents { get; set; }

    /// <summary>
    /// Gets or sets the total uncompressed size in bytes.
    /// </summary>
    [JsonPropertyName("totalBytesRaw")]
    public long TotalBytesRaw { get; set; }

    /// <summary>
    /// Gets or sets the total compressed size in bytes.
    /// </summary>
    [JsonPropertyName("totalBytesCompressed")]
    public long TotalBytesCompressed { get; set; }

    /// <summary>
    /// Gets or sets the array of file entries in the manifest.
    /// </summary>
    [JsonPropertyName("files")]
    public ManifestFileEntry[] Files { get; set; } = Array.Empty<ManifestFileEntry>();

    /// <summary>
    /// Gets or sets the schema definitions keyed by event type.
    /// </summary>
    [JsonPropertyName("schemas")]
    public Dictionary<string, string>? Schemas { get; set; }

    /// <summary>
    /// Gets or sets the data quality metrics for this manifest.
    /// </summary>
    [JsonPropertyName("qualityMetrics")]
    public DataQualityMetrics? QualityMetrics { get; set; }

    /// <summary>
    /// Gets or sets the verification status of the manifest.
    /// </summary>
    [JsonPropertyName("verificationStatus")]
    public string VerificationStatus { get; set; } = VerificationStatusValues.Pending;

    /// <summary>
    /// Gets or sets the timestamp of the last verification.
    /// </summary>
    [JsonPropertyName("lastVerifiedAt")]
    public DateTime? LastVerifiedAt { get; set; }
}

/// <summary>
/// Verification status constants.
/// </summary>
public static class VerificationStatusValues
{
    /// <summary>
    /// Indicates verification has not yet been performed.
    /// </summary>
    public const string Pending = "Pending";

    /// <summary>
    /// Indicates verification completed successfully.
    /// </summary>
    public const string Verified = "Verified";

    /// <summary>
    /// Indicates verification failed.
    /// </summary>
    public const string Failed = "Failed";
}

/// <summary>
/// Date range information.
/// </summary>
public sealed class DateRangeInfo
{
    /// <summary>
    /// Gets or sets the start date of the range.
    /// </summary>
    [JsonPropertyName("start")]
    public DateTime Start { get; set; }

    /// <summary>
    /// Gets or sets the end date of the range.
    /// </summary>
    [JsonPropertyName("end")]
    public DateTime End { get; set; }

    /// <summary>
    /// Gets or sets the number of trading days in the range.
    /// </summary>
    [JsonPropertyName("tradingDays")]
    public int TradingDays { get; set; }
}

/// <summary>
/// Individual file entry in a manifest.
/// </summary>
public sealed class ManifestFileEntry
{
    /// <summary>
    /// Gets or sets the absolute file path.
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the relative file path within the archive.
    /// </summary>
    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ticker symbol for this file.
    /// </summary>
    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    /// <summary>
    /// Gets or sets the event type contained in this file.
    /// </summary>
    [JsonPropertyName("eventType")]
    public string? EventType { get; set; }

    /// <summary>
    /// Gets or sets the date of the data in this file.
    /// </summary>
    [JsonPropertyName("date")]
    public DateTime? Date { get; set; }

    /// <summary>
    /// Gets or sets the SHA-256 checksum for file integrity.
    /// </summary>
    [JsonPropertyName("checksumSha256")]
    public string ChecksumSha256 { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the uncompressed file size in bytes.
    /// </summary>
    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the compressed file size in bytes.
    /// </summary>
    [JsonPropertyName("compressedSizeBytes")]
    public long? CompressedSizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the number of events in the file.
    /// </summary>
    [JsonPropertyName("eventCount")]
    public long EventCount { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the first event in the file.
    /// </summary>
    [JsonPropertyName("firstTimestamp")]
    public DateTime? FirstTimestamp { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the last event in the file.
    /// </summary>
    [JsonPropertyName("lastTimestamp")]
    public DateTime? LastTimestamp { get; set; }

    /// <summary>
    /// Gets or sets the schema version used for the data.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public string? SchemaVersion { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the file is compressed.
    /// </summary>
    [JsonPropertyName("isCompressed")]
    public bool IsCompressed { get; set; }

    /// <summary>
    /// Gets or sets the compression algorithm used (e.g., "gzip", "lz4").
    /// </summary>
    [JsonPropertyName("compressionType")]
    public string? CompressionType { get; set; }

    /// <summary>
    /// Gets or sets the verification status of the file.
    /// </summary>
    [JsonPropertyName("verificationStatus")]
    public string VerificationStatus { get; set; } = VerificationStatusValues.Pending;

    /// <summary>
    /// Gets or sets the timestamp of the last verification.
    /// </summary>
    [JsonPropertyName("lastVerifiedAt")]
    public DateTime? LastVerifiedAt { get; set; }
}

/// <summary>
/// Data quality metrics for manifests and sessions.
/// </summary>
public sealed class DataQualityMetrics
{
    /// <summary>
    /// Gets or sets the completeness score (0.0 to 1.0).
    /// </summary>
    [JsonPropertyName("completenessScore")]
    public float CompletenessScore { get; set; }

    /// <summary>
    /// Gets or sets the data integrity score (0.0 to 1.0).
    /// </summary>
    [JsonPropertyName("integrityScore")]
    public float IntegrityScore { get; set; }

    /// <summary>
    /// Gets or sets the overall quality score (0.0 to 1.0).
    /// </summary>
    [JsonPropertyName("overallScore")]
    public float OverallScore { get; set; }

    /// <summary>
    /// Gets or sets the number of data gaps detected.
    /// </summary>
    [JsonPropertyName("gapsDetected")]
    public int GapsDetected { get; set; }

    /// <summary>
    /// Gets or sets the number of sequence errors found.
    /// </summary>
    [JsonPropertyName("sequenceErrors")]
    public int SequenceErrors { get; set; }

    /// <summary>
    /// Gets or sets the number of duplicate records found.
    /// </summary>
    [JsonPropertyName("duplicatesFound")]
    public int DuplicatesFound { get; set; }

    /// <summary>
    /// Gets or sets the expected number of events.
    /// </summary>
    [JsonPropertyName("expectedEvents")]
    public long ExpectedEvents { get; set; }

    /// <summary>
    /// Gets or sets the actual number of events recorded.
    /// </summary>
    [JsonPropertyName("actualEvents")]
    public long ActualEvents { get; set; }

    /// <summary>
    /// Gets or sets the array of missing trading day dates.
    /// </summary>
    [JsonPropertyName("missingTradingDays")]
    public string[]? MissingTradingDays { get; set; }

    /// <summary>
    /// Gets or sets the number of outliers detected.
    /// </summary>
    [JsonPropertyName("outliersDetected")]
    public int OutliersDetected { get; set; }
}

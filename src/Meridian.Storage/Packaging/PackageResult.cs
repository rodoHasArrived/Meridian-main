using System.Text.Json.Serialization;

namespace Meridian.Storage.Packaging;

/// <summary>
/// Result of a packaging operation.
/// </summary>
public sealed class PackageResult
{
    /// <summary>
    /// Unique job ID for this packaging operation.
    /// </summary>
    [JsonPropertyName("jobId")]
    public string JobId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Whether the packaging completed successfully.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>
    /// Package ID if successful.
    /// </summary>
    [JsonPropertyName("packageId")]
    public string? PackageId { get; set; }

    /// <summary>
    /// Full path to the created package file.
    /// </summary>
    [JsonPropertyName("packagePath")]
    public string? PackagePath { get; set; }

    /// <summary>
    /// Package file name.
    /// </summary>
    [JsonPropertyName("packageFileName")]
    public string? PackageFileName { get; set; }

    /// <summary>
    /// Size of the package file in bytes.
    /// </summary>
    [JsonPropertyName("packageSizeBytes")]
    public long PackageSizeBytes { get; set; }

    /// <summary>
    /// Total uncompressed size of all data.
    /// </summary>
    [JsonPropertyName("uncompressedSizeBytes")]
    public long UncompressedSizeBytes { get; set; }

    /// <summary>
    /// Compression ratio achieved.
    /// </summary>
    [JsonPropertyName("compressionRatio")]
    public double CompressionRatio => UncompressedSizeBytes > 0
        ? (double)UncompressedSizeBytes / PackageSizeBytes
        : 1.0;

    /// <summary>
    /// When the operation started.
    /// </summary>
    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the operation completed.
    /// </summary>
    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Duration in seconds.
    /// </summary>
    [JsonPropertyName("durationSeconds")]
    public double DurationSeconds => CompletedAt.HasValue
        ? (CompletedAt.Value - StartedAt).TotalSeconds
        : (DateTime.UtcNow - StartedAt).TotalSeconds;

    /// <summary>
    /// Number of data files included.
    /// </summary>
    [JsonPropertyName("filesIncluded")]
    public int FilesIncluded { get; set; }

    /// <summary>
    /// Total events/records packaged.
    /// </summary>
    [JsonPropertyName("totalEvents")]
    public long TotalEvents { get; set; }

    /// <summary>
    /// Symbols included in the package.
    /// </summary>
    [JsonPropertyName("symbols")]
    public string[] Symbols { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Event types included.
    /// </summary>
    [JsonPropertyName("eventTypes")]
    public string[] EventTypes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Date range of data in the package.
    /// </summary>
    [JsonPropertyName("dateRange")]
    public PackageDateRange? DateRange { get; set; }

    /// <summary>
    /// SHA256 checksum of the package file.
    /// </summary>
    [JsonPropertyName("packageChecksum")]
    public string? PackageChecksum { get; set; }

    /// <summary>
    /// The generated manifest.
    /// </summary>
    [JsonPropertyName("manifest")]
    public PackageManifest? Manifest { get; set; }

    /// <summary>
    /// Warnings encountered during packaging.
    /// </summary>
    [JsonPropertyName("warnings")]
    public string[] Warnings { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Error message if failed.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>
    /// Paths to additional package parts if split.
    /// </summary>
    [JsonPropertyName("additionalParts")]
    public string[]? AdditionalParts { get; set; }

    /// <summary>
    /// Create a successful result.
    /// </summary>
    public static PackageResult CreateSuccess(string packagePath, PackageManifest manifest) => new()
    {
        Success = true,
        PackagePath = packagePath,
        PackageFileName = Path.GetFileName(packagePath),
        PackageId = manifest.PackageId,
        Manifest = manifest,
        CompletedAt = DateTime.UtcNow
    };

    /// <summary>
    /// Create a failure result.
    /// </summary>
    public static PackageResult CreateFailure(string error) => new()
    {
        Success = false,
        Error = error,
        CompletedAt = DateTime.UtcNow
    };
}

/// <summary>
/// Result of a package import/extraction operation.
/// </summary>
public sealed class ImportResult
{
    /// <summary>
    /// Unique job ID for this import operation.
    /// </summary>
    [JsonPropertyName("jobId")]
    public string JobId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Whether the import completed successfully.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>
    /// Package ID that was imported.
    /// </summary>
    [JsonPropertyName("packageId")]
    public string? PackageId { get; set; }

    /// <summary>
    /// Source package path.
    /// </summary>
    [JsonPropertyName("sourcePath")]
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Destination directory where data was extracted.
    /// </summary>
    [JsonPropertyName("destinationPath")]
    public string DestinationPath { get; set; } = string.Empty;

    /// <summary>
    /// When the operation started.
    /// </summary>
    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the operation completed.
    /// </summary>
    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Duration in seconds.
    /// </summary>
    [JsonPropertyName("durationSeconds")]
    public double DurationSeconds => CompletedAt.HasValue
        ? (CompletedAt.Value - StartedAt).TotalSeconds
        : (DateTime.UtcNow - StartedAt).TotalSeconds;

    /// <summary>
    /// Number of files extracted.
    /// </summary>
    [JsonPropertyName("filesExtracted")]
    public int FilesExtracted { get; set; }

    /// <summary>
    /// Total bytes extracted.
    /// </summary>
    [JsonPropertyName("bytesExtracted")]
    public long BytesExtracted { get; set; }

    /// <summary>
    /// Number of files that passed checksum validation.
    /// </summary>
    [JsonPropertyName("filesValidated")]
    public int FilesValidated { get; set; }

    /// <summary>
    /// Number of files that failed validation.
    /// </summary>
    [JsonPropertyName("validationFailures")]
    public int ValidationFailures { get; set; }

    /// <summary>
    /// Symbols imported.
    /// </summary>
    [JsonPropertyName("symbols")]
    public string[] Symbols { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Event types imported.
    /// </summary>
    [JsonPropertyName("eventTypes")]
    public string[] EventTypes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Date range of imported data.
    /// </summary>
    [JsonPropertyName("dateRange")]
    public PackageDateRange? DateRange { get; set; }

    /// <summary>
    /// The manifest from the imported package.
    /// </summary>
    [JsonPropertyName("manifest")]
    public PackageManifest? Manifest { get; set; }

    /// <summary>
    /// Warnings encountered during import.
    /// </summary>
    [JsonPropertyName("warnings")]
    public string[] Warnings { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Error message if failed.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>
    /// Details of validation failures.
    /// </summary>
    [JsonPropertyName("validationErrors")]
    public ValidationError[]? ValidationErrors { get; set; }

    /// <summary>
    /// Create a successful result.
    /// </summary>
    public static ImportResult CreateSuccess(string sourcePath, string destinationPath, PackageManifest manifest) => new()
    {
        Success = true,
        SourcePath = sourcePath,
        DestinationPath = destinationPath,
        PackageId = manifest.PackageId,
        Manifest = manifest,
        CompletedAt = DateTime.UtcNow
    };

    /// <summary>
    /// Create a failure result.
    /// </summary>
    public static ImportResult CreateFailure(string sourcePath, string error) => new()
    {
        Success = false,
        SourcePath = sourcePath,
        Error = error,
        CompletedAt = DateTime.UtcNow
    };
}

/// <summary>
/// Details of a validation error during import.
/// </summary>
public sealed class ValidationError
{
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("errorType")]
    public string ErrorType { get; set; } = string.Empty;

    [JsonPropertyName("expectedValue")]
    public string? ExpectedValue { get; set; }

    [JsonPropertyName("actualValue")]
    public string? ActualValue { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Progress information for packaging/import operations.
/// </summary>
public sealed class PackageProgress
{
    [JsonPropertyName("jobId")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("stage")]
    public PackageStage Stage { get; set; }

    [JsonPropertyName("currentFile")]
    public string? CurrentFile { get; set; }

    [JsonPropertyName("filesProcessed")]
    public int FilesProcessed { get; set; }

    [JsonPropertyName("totalFiles")]
    public int TotalFiles { get; set; }

    [JsonPropertyName("bytesProcessed")]
    public long BytesProcessed { get; set; }

    [JsonPropertyName("totalBytes")]
    public long TotalBytes { get; set; }

    [JsonPropertyName("percentComplete")]
    public double PercentComplete => TotalFiles > 0
        ? (double)FilesProcessed / TotalFiles * 100
        : 0;

    [JsonPropertyName("estimatedSecondsRemaining")]
    public double? EstimatedSecondsRemaining { get; set; }

    [JsonPropertyName("throughputBytesPerSecond")]
    public double ThroughputBytesPerSecond { get; set; }
}

/// <summary>
/// Stages of packaging/import operations.
/// </summary>
public enum PackageStage : byte
{
    /// <summary>Initializing operation.</summary>
    Initializing,

    /// <summary>Scanning source files.</summary>
    Scanning,

    /// <summary>Validating source data.</summary>
    Validating,

    /// <summary>Compressing/decompressing data.</summary>
    Processing,

    /// <summary>Writing package file.</summary>
    Writing,

    /// <summary>Generating manifest.</summary>
    GeneratingManifest,

    /// <summary>Computing checksums.</summary>
    ComputingChecksums,

    /// <summary>Finalizing package.</summary>
    Finalizing,

    /// <summary>Operation complete.</summary>
    Complete,

    /// <summary>Operation failed.</summary>
    Failed
}

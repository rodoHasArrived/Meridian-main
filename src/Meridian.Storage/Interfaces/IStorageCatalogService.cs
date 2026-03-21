using Meridian.Contracts.Catalog;

namespace Meridian.Storage.Interfaces;

/// <summary>
/// Service for managing the storage catalog and manifest system.
/// Provides comprehensive indexing, integrity verification, and metadata management.
/// </summary>
public interface IStorageCatalogService
{
    /// <summary>
    /// Gets the current storage catalog.
    /// </summary>
    StorageCatalog GetCatalog();

    /// <summary>
    /// Initializes or loads the catalog from the storage root.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Rebuilds the catalog by scanning all storage directories.
    /// </summary>
    Task<CatalogRebuildResult> RebuildCatalogAsync(
        CatalogRebuildOptions? options = null,
        IProgress<CatalogRebuildProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Updates the catalog with a new or modified file entry.
    /// </summary>
    Task UpdateFileEntryAsync(IndexedFileEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Removes a file entry from the catalog.
    /// </summary>
    Task RemoveFileEntryAsync(string relativePath, CancellationToken ct = default);

    /// <summary>
    /// Gets the directory index for a specific path.
    /// </summary>
    Task<DirectoryIndex?> GetDirectoryIndexAsync(string relativePath, CancellationToken ct = default);

    /// <summary>
    /// Updates or creates a directory index.
    /// </summary>
    Task UpdateDirectoryIndexAsync(DirectoryIndex index, CancellationToken ct = default);

    /// <summary>
    /// Scans a directory and creates/updates its index.
    /// </summary>
    Task<DirectoryScanResult> ScanDirectoryAsync(
        string path,
        bool recursive = false,
        CancellationToken ct = default);

    /// <summary>
    /// Gets catalog statistics.
    /// </summary>
    CatalogStatistics GetStatistics();

    /// <summary>
    /// Verifies catalog integrity by checking all file checksums.
    /// </summary>
    Task<CatalogVerificationResult> VerifyIntegrityAsync(
        CatalogVerificationOptions? options = null,
        IProgress<CatalogVerificationProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets files for a specific symbol.
    /// </summary>
    IEnumerable<IndexedFileEntry> GetFilesForSymbol(string symbol);

    /// <summary>
    /// Gets files for a specific date range.
    /// </summary>
    IEnumerable<IndexedFileEntry> GetFilesForDateRange(DateTime start, DateTime end);

    /// <summary>
    /// Gets files by event type.
    /// </summary>
    IEnumerable<IndexedFileEntry> GetFilesForEventType(string eventType);

    /// <summary>
    /// Searches for files matching criteria.
    /// </summary>
    IEnumerable<IndexedFileEntry> SearchFiles(CatalogSearchCriteria criteria);

    /// <summary>
    /// Saves the catalog to disk.
    /// </summary>
    Task SaveCatalogAsync(CancellationToken ct = default);

    /// <summary>
    /// Exports the catalog to a portable format.
    /// </summary>
    Task ExportCatalogAsync(string outputPath, CatalogExportFormat format = CatalogExportFormat.Json, CancellationToken ct = default);
}

// ISymbolRegistryService has been extracted to its own file: ISymbolRegistryService.cs

/// <summary>
/// Options for catalog rebuild operation.
/// </summary>
public sealed class CatalogRebuildOptions
{
    /// <summary>
    /// Whether to compute checksums for all files.
    /// </summary>
    public bool ComputeChecksums { get; init; } = true;

    /// <summary>
    /// Whether to count events in each file.
    /// </summary>
    public bool CountEvents { get; init; } = true;

    /// <summary>
    /// Whether to extract sequence information.
    /// </summary>
    public bool ExtractSequenceInfo { get; init; } = true;

    /// <summary>
    /// Maximum parallelism for file processing.
    /// </summary>
    public int MaxParallelism { get; init; } = Environment.ProcessorCount;

    /// <summary>
    /// Whether to include subdirectories recursively.
    /// </summary>
    public bool Recursive { get; init; } = true;

    /// <summary>
    /// File patterns to include (e.g., "*.jsonl", "*.parquet").
    /// </summary>
    public string[] IncludePatterns { get; init; } = new[] { "*.jsonl", "*.jsonl.gz", "*.parquet" };

    /// <summary>
    /// Paths to exclude from scanning.
    /// </summary>
    public string[] ExcludePaths { get; init; } = new[] { "_catalog", "_wal", "_temp" };
}

/// <summary>
/// Progress information for catalog rebuild.
/// </summary>
public sealed class CatalogRebuildProgress
{
    /// <summary>
    /// Current phase of the rebuild.
    /// </summary>
    public string Phase { get; init; } = string.Empty;

    /// <summary>
    /// Number of files processed.
    /// </summary>
    public int FilesProcessed { get; init; }

    /// <summary>
    /// Total files to process.
    /// </summary>
    public int TotalFiles { get; init; }

    /// <summary>
    /// Current file being processed.
    /// </summary>
    public string? CurrentFile { get; init; }

    /// <summary>
    /// Percentage complete (0-100).
    /// </summary>
    public double PercentComplete => TotalFiles > 0 ? (double)FilesProcessed / TotalFiles * 100 : 0;
}

/// <summary>
/// Result of a catalog rebuild operation.
/// </summary>
public sealed class CatalogRebuildResult
{
    /// <summary>
    /// Whether the rebuild was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Number of files indexed.
    /// </summary>
    public int FilesIndexed { get; init; }

    /// <summary>
    /// Number of directories indexed.
    /// </summary>
    public int DirectoriesIndexed { get; init; }

    /// <summary>
    /// Total events counted.
    /// </summary>
    public long TotalEvents { get; init; }

    /// <summary>
    /// Total bytes processed.
    /// </summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// Duration of the rebuild.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Errors encountered.
    /// </summary>
    public List<string> Errors { get; init; } = new();

    /// <summary>
    /// Warnings generated.
    /// </summary>
    public List<string> Warnings { get; init; } = new();
}

/// <summary>
/// Options for catalog verification.
/// </summary>
public sealed class CatalogVerificationOptions
{
    /// <summary>
    /// Whether to verify file checksums.
    /// </summary>
    public bool VerifyChecksums { get; init; } = true;

    /// <summary>
    /// Whether to verify file exists.
    /// </summary>
    public bool VerifyFileExists { get; init; } = true;

    /// <summary>
    /// Whether to verify event counts.
    /// </summary>
    public bool VerifyEventCounts { get; init; } = false;

    /// <summary>
    /// Maximum parallelism.
    /// </summary>
    public int MaxParallelism { get; init; } = Environment.ProcessorCount;

    /// <summary>
    /// Whether to stop on first error.
    /// </summary>
    public bool StopOnFirstError { get; init; } = false;
}

/// <summary>
/// Progress for catalog verification.
/// </summary>
public sealed class CatalogVerificationProgress
{
    /// <summary>
    /// Files verified so far.
    /// </summary>
    public int FilesVerified { get; init; }

    /// <summary>
    /// Total files to verify.
    /// </summary>
    public int TotalFiles { get; init; }

    /// <summary>
    /// Current file being verified.
    /// </summary>
    public string? CurrentFile { get; init; }

    /// <summary>
    /// Errors found so far.
    /// </summary>
    public int ErrorsFound { get; init; }
}

/// <summary>
/// Result of catalog verification.
/// </summary>
public sealed class CatalogVerificationResult
{
    /// <summary>
    /// Overall verification success.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Files that passed verification.
    /// </summary>
    public int FilesPassed { get; init; }

    /// <summary>
    /// Files with checksum mismatches.
    /// </summary>
    public int ChecksumMismatches { get; init; }

    /// <summary>
    /// Files that are missing.
    /// </summary>
    public int MissingFiles { get; init; }

    /// <summary>
    /// Files with count mismatches.
    /// </summary>
    public int CountMismatches { get; init; }

    /// <summary>
    /// Verification duration.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Detailed issues found.
    /// </summary>
    public List<CatalogIntegrityIssue> Issues { get; init; } = new();
}

/// <summary>
/// Search criteria for catalog files.
/// </summary>
public sealed class CatalogSearchCriteria
{
    /// <summary>
    /// Symbols to include.
    /// </summary>
    public string[]? Symbols { get; init; }

    /// <summary>
    /// Event types to include.
    /// </summary>
    public string[]? EventTypes { get; init; }

    /// <summary>
    /// Data sources to include.
    /// </summary>
    public string[]? Sources { get; init; }

    /// <summary>
    /// Start date filter.
    /// </summary>
    public DateTime? StartDate { get; init; }

    /// <summary>
    /// End date filter.
    /// </summary>
    public DateTime? EndDate { get; init; }

    /// <summary>
    /// Minimum file size.
    /// </summary>
    public long? MinSizeBytes { get; init; }

    /// <summary>
    /// Maximum file size.
    /// </summary>
    public long? MaxSizeBytes { get; init; }

    /// <summary>
    /// File path pattern (supports wildcards).
    /// </summary>
    public string? PathPattern { get; init; }

    /// <summary>
    /// Schema version filter.
    /// </summary>
    public string? SchemaVersion { get; init; }
}

/// <summary>
/// Export format for catalog.
/// </summary>
public enum CatalogExportFormat : byte
{
    /// <summary>
    /// JSON format.
    /// </summary>
    Json,

    /// <summary>
    /// CSV format (file listing).
    /// </summary>
    Csv,

    /// <summary>
    /// Parquet format (for analytics).
    /// </summary>
    Parquet
}

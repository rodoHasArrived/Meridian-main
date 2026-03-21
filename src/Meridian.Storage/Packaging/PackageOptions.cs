using System.Text.Json.Serialization;

namespace Meridian.Storage.Packaging;

/// <summary>
/// Options for creating portable data packages.
/// </summary>
public sealed class PackageOptions
{
    /// <summary>
    /// Name for the package (used in file name and manifest).
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = $"market-data-{DateTime.UtcNow:yyyyMMdd}";

    /// <summary>
    /// Description of the package contents.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Package format (Zip or TarGz).
    /// </summary>
    [JsonPropertyName("format")]
    public PackageFormat Format { get; set; } = PackageFormat.Zip;

    /// <summary>
    /// Compression level for the package archive.
    /// </summary>
    [JsonPropertyName("compressionLevel")]
    public PackageCompressionLevel CompressionLevel { get; set; } = PackageCompressionLevel.Balanced;

    /// <summary>
    /// Symbols to include (null = all symbols).
    /// </summary>
    [JsonPropertyName("symbols")]
    public string[]? Symbols { get; set; }

    /// <summary>
    /// Event types to include (Trade, BboQuote, L2Snapshot, etc.).
    /// </summary>
    [JsonPropertyName("eventTypes")]
    public string[]? EventTypes { get; set; }

    /// <summary>
    /// Start date for data selection.
    /// </summary>
    [JsonPropertyName("startDate")]
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// End date for data selection.
    /// </summary>
    [JsonPropertyName("endDate")]
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// Data sources to include (null = all sources).
    /// </summary>
    [JsonPropertyName("sources")]
    public string[]? Sources { get; set; }

    /// <summary>
    /// Output directory for the package file.
    /// </summary>
    [JsonPropertyName("outputDirectory")]
    public string OutputDirectory { get; set; } = "packages";

    /// <summary>
    /// Whether to include data quality report in the package.
    /// </summary>
    [JsonPropertyName("includeQualityReport")]
    public bool IncludeQualityReport { get; set; } = true;

    /// <summary>
    /// Whether to include data dictionary documentation.
    /// </summary>
    [JsonPropertyName("includeDataDictionary")]
    public bool IncludeDataDictionary { get; set; } = true;

    /// <summary>
    /// Whether to include sample loader scripts.
    /// </summary>
    [JsonPropertyName("includeLoaderScripts")]
    public bool IncludeLoaderScripts { get; set; } = true;

    /// <summary>
    /// Whether to verify checksums during packaging.
    /// </summary>
    [JsonPropertyName("verifyChecksums")]
    public bool VerifyChecksums { get; set; } = true;

    /// <summary>
    /// Whether to decompress files before packaging (for universal compatibility).
    /// </summary>
    [JsonPropertyName("decompressFiles")]
    public bool DecompressFiles { get; set; } = false;

    /// <summary>
    /// Convert files to a specific format before packaging.
    /// </summary>
    [JsonPropertyName("convertToFormat")]
    public PackageDataFormat ConvertToFormat { get; set; } = PackageDataFormat.Original;

    /// <summary>
    /// Maximum package size in bytes (split into multiple parts if exceeded).
    /// </summary>
    [JsonPropertyName("maxPackageBytes")]
    public long? MaxPackageBytes { get; set; }

    /// <summary>
    /// Password for encrypted packages (null = no encryption).
    /// </summary>
    [JsonPropertyName("password")]
    public string? Password { get; set; }

    /// <summary>
    /// Tags for categorizing the package.
    /// </summary>
    [JsonPropertyName("tags")]
    public string[]? Tags { get; set; }

    /// <summary>
    /// Custom metadata to include in the manifest.
    /// </summary>
    [JsonPropertyName("customMetadata")]
    public Dictionary<string, string>? CustomMetadata { get; set; }

    /// <summary>
    /// Internal file organization within the package.
    /// </summary>
    [JsonPropertyName("internalLayout")]
    public PackageLayout InternalLayout { get; set; } = PackageLayout.ByDate;

    /// <summary>
    /// Whether to include schema definitions for data validation.
    /// </summary>
    [JsonPropertyName("includeSchemas")]
    public bool IncludeSchemas { get; set; } = true;

    /// <summary>
    /// Whether to generate import scripts for various databases.
    /// </summary>
    [JsonPropertyName("generateImportScripts")]
    public bool GenerateImportScripts { get; set; } = false;

    /// <summary>
    /// Target platforms for import scripts.
    /// </summary>
    [JsonPropertyName("importScriptTargets")]
    public ImportScriptTarget[]? ImportScriptTargets { get; set; }
}

/// <summary>
/// Package archive format.
/// </summary>
public enum PackageFormat : byte
{
    /// <summary>ZIP archive - widely compatible.</summary>
    Zip,

    /// <summary>Gzipped TAR archive - better for Unix/Linux.</summary>
    TarGz,

    /// <summary>7-Zip archive - higher compression ratio.</summary>
    SevenZip
}

/// <summary>
/// Compression level for package archives.
/// </summary>
public enum PackageCompressionLevel : byte
{
    /// <summary>No compression - fastest, largest files.</summary>
    None,

    /// <summary>Fast compression - good speed, moderate size.</summary>
    Fast,

    /// <summary>Balanced compression - good balance of speed and size.</summary>
    Balanced,

    /// <summary>Maximum compression - slowest, smallest files.</summary>
    Maximum
}

/// <summary>
/// Data format conversion options.
/// </summary>
public enum PackageDataFormat : byte
{
    /// <summary>Keep original format (JSONL, Parquet, etc.).</summary>
    Original,

    /// <summary>Convert to JSONL for universal compatibility.</summary>
    Jsonl,

    /// <summary>Convert to CSV for spreadsheet compatibility.</summary>
    Csv,

    /// <summary>Convert to Parquet for analytics.</summary>
    Parquet
}

/// <summary>
/// Internal file layout within the package.
/// </summary>
public enum PackageLayout : byte
{
    /// <summary>Organize by date: data/{date}/{symbol}/{type}.jsonl</summary>
    ByDate,

    /// <summary>Organize by symbol: data/{symbol}/{type}/{date}.jsonl</summary>
    BySymbol,

    /// <summary>Organize by event type: data/{type}/{symbol}/{date}.jsonl</summary>
    ByType,

    /// <summary>Flat structure: data/{symbol}_{type}_{date}.jsonl</summary>
    Flat
}

/// <summary>
/// Target platforms for import script generation.
/// </summary>
public enum ImportScriptTarget : byte
{
    /// <summary>Python with pandas.</summary>
    Python,

    /// <summary>R with tidyverse.</summary>
    R,

    /// <summary>PostgreSQL/TimescaleDB.</summary>
    PostgreSql,

    /// <summary>ClickHouse.</summary>
    ClickHouse,

    /// <summary>Apache Spark.</summary>
    Spark,

    /// <summary>DuckDB.</summary>
    DuckDb
}

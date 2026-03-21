using System.Text.Json.Serialization;

namespace Meridian.Storage.Export;

/// <summary>
/// Defines export profiles for different external analysis tools.
/// Each profile specifies format, settings, and post-processing options.
/// </summary>
public sealed class ExportProfile
{
    /// <summary>
    /// Unique identifier for this profile.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Human-readable name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of the profile and target tool.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Target analysis tool (Python, R, Lean, Excel, PostgreSQL, etc.)
    /// </summary>
    [JsonPropertyName("targetTool")]
    public string TargetTool { get; set; } = "Python";

    /// <summary>
    /// Output format (Parquet, CSV, JSONL, Lean, XLSX, SQL).
    /// </summary>
    [JsonPropertyName("format")]
    public ExportFormat Format { get; set; } = ExportFormat.Parquet;

    /// <summary>
    /// Compression settings.
    /// </summary>
    [JsonPropertyName("compression")]
    public CompressionSettings Compression { get; set; } = new();

    /// <summary>
    /// Timestamp handling settings.
    /// </summary>
    [JsonPropertyName("timestampSettings")]
    public TimestampSettings TimestampSettings { get; set; } = new();

    /// <summary>
    /// Fields to include in export (null = all fields).
    /// </summary>
    [JsonPropertyName("includeFields")]
    public string[]? IncludeFields { get; set; }

    /// <summary>
    /// Fields to exclude from export.
    /// </summary>
    [JsonPropertyName("excludeFields")]
    public string[]? ExcludeFields { get; set; }

    /// <summary>
    /// Whether to generate sample loader code.
    /// </summary>
    [JsonPropertyName("includeLoaderScript")]
    public bool IncludeLoaderScript { get; set; } = true;

    /// <summary>
    /// Whether to generate data dictionary.
    /// </summary>
    [JsonPropertyName("includeDataDictionary")]
    public bool IncludeDataDictionary { get; set; } = true;

    /// <summary>
    /// File naming pattern.
    /// Variables: {symbol}, {date}, {type}, {format}
    /// </summary>
    [JsonPropertyName("fileNamePattern")]
    public string FileNamePattern { get; set; } = "{symbol}_{date}.{format}";

    /// <summary>
    /// Whether to split files by symbol.
    /// </summary>
    [JsonPropertyName("splitBySymbol")]
    public bool SplitBySymbol { get; set; } = true;

    /// <summary>
    /// Whether to split files by date.
    /// </summary>
    [JsonPropertyName("splitByDate")]
    public bool SplitByDate { get; set; }

    /// <summary>
    /// Maximum records per file (null = unlimited).
    /// </summary>
    [JsonPropertyName("maxRecordsPerFile")]
    public long? MaxRecordsPerFile { get; set; }

    /// <summary>
    /// Pre-built profile for Python/Pandas.
    /// </summary>
    public static ExportProfile PythonPandas => new()
    {
        Id = "python-pandas",
        Name = "Python/Pandas",
        Description = "Parquet with datetime64[ns], optimized for pandas.read_parquet()",
        TargetTool = "Python",
        Format = ExportFormat.Parquet,
        Compression = new() { Type = CompressionType.Snappy },
        TimestampSettings = new() { Format = TimestampFormat.UnixNanoseconds, Timezone = "UTC" },
        IncludeLoaderScript = true,
        IncludeDataDictionary = true
    };

    /// <summary>
    /// Pre-built profile for R.
    /// </summary>
    public static ExportProfile RStats => new()
    {
        Id = "r-stats",
        Name = "R Statistics",
        Description = "CSV with proper NA handling, ISO date formats",
        TargetTool = "R",
        Format = ExportFormat.Csv,
        Compression = new() { Type = CompressionType.None },
        TimestampSettings = new() { Format = TimestampFormat.Iso8601, Timezone = "UTC" },
        IncludeLoaderScript = true,
        IncludeDataDictionary = true
    };

    /// <summary>
    /// Pre-built profile for QuantConnect Lean.
    /// </summary>
    public static ExportProfile QuantConnectLean => new()
    {
        Id = "quantconnect-lean",
        Name = "QuantConnect Lean",
        Description = "Native Lean data format with zip packaging",
        TargetTool = "Lean",
        Format = ExportFormat.Lean,
        Compression = new() { Type = CompressionType.Zip },
        TimestampSettings = new() { Format = TimestampFormat.UnixMilliseconds, Timezone = "America/New_York" },
        IncludeLoaderScript = false,
        IncludeDataDictionary = false,
        SplitBySymbol = true,
        SplitByDate = true
    };

    /// <summary>
    /// Pre-built profile for Excel.
    /// </summary>
    public static ExportProfile Excel => new()
    {
        Id = "excel",
        Name = "Microsoft Excel",
        Description = "XLSX with multiple sheets by symbol",
        TargetTool = "Excel",
        Format = ExportFormat.Xlsx,
        Compression = new() { Type = CompressionType.None },
        TimestampSettings = new() { Format = TimestampFormat.Iso8601, Timezone = "UTC" },
        IncludeLoaderScript = false,
        IncludeDataDictionary = true,
        MaxRecordsPerFile = 1000000 // Excel row limit
    };

    /// <summary>
    /// Pre-built profile for PostgreSQL/TimescaleDB.
    /// </summary>
    public static ExportProfile PostgreSql => new()
    {
        Id = "postgresql",
        Name = "PostgreSQL",
        Description = "CSV with COPY command, includes DDL scripts",
        TargetTool = "PostgreSQL",
        Format = ExportFormat.Csv,
        Compression = new() { Type = CompressionType.None },
        TimestampSettings = new() { Format = TimestampFormat.Iso8601, Timezone = "UTC" },
        IncludeLoaderScript = true,
        IncludeDataDictionary = true,
        FileNamePattern = "{symbol}_{type}_{date}.csv"
    };

    /// <summary>
    /// Pre-built profile for Apache Arrow/Feather.
    /// </summary>
    public static ExportProfile ArrowFeather => new()
    {
        Id = "arrow-feather",
        Name = "Apache Arrow (Feather)",
        Description = "Arrow IPC format for zero-copy interop with Python (PyArrow), R, Julia, and Spark",
        TargetTool = "PyArrow",
        Format = ExportFormat.Arrow,
        Compression = new() { Type = CompressionType.None },
        TimestampSettings = new() { Format = TimestampFormat.UnixNanoseconds, Timezone = "UTC" },
        IncludeLoaderScript = true,
        IncludeDataDictionary = true
    };

    /// <summary>
    /// Creates a copy of this profile with only the format overridden.
    /// All other settings (compression, timestamps, loader script, data dictionary, file naming, etc.) are preserved.
    /// </summary>
    public ExportProfile WithFormat(ExportFormat newFormat) => new()
    {
        Id = Id,
        Name = Name,
        Description = Description,
        TargetTool = TargetTool,
        Format = newFormat,
        Compression = Compression,
        TimestampSettings = TimestampSettings,
        IncludeFields = IncludeFields,
        ExcludeFields = ExcludeFields,
        IncludeLoaderScript = IncludeLoaderScript,
        IncludeDataDictionary = IncludeDataDictionary,
        FileNamePattern = FileNamePattern,
        SplitBySymbol = SplitBySymbol,
        SplitByDate = SplitByDate,
        MaxRecordsPerFile = MaxRecordsPerFile
    };

    /// <summary>
    /// Get all pre-built profiles.
    /// </summary>
    public static IReadOnlyList<ExportProfile> GetBuiltInProfiles() => new[]
    {
        PythonPandas,
        RStats,
        QuantConnectLean,
        Excel,
        PostgreSql,
        ArrowFeather
    };
}

/// <summary>
/// Supported export formats.
/// </summary>
public enum ExportFormat : byte
{
    /// <summary>Apache Parquet - columnar format for analytics.</summary>
    Parquet,
    /// <summary>Comma-separated values.</summary>
    Csv,
    /// <summary>JSON Lines - one JSON object per line.</summary>
    Jsonl,
    /// <summary>QuantConnect Lean native format.</summary>
    Lean,
    /// <summary>Microsoft Excel XLSX format.</summary>
    Xlsx,
    /// <summary>SQL statements (INSERT or COPY).</summary>
    Sql,
    /// <summary>Apache Arrow IPC format (Feather v2).</summary>
    Arrow
}

/// <summary>
/// Compression settings for exports.
/// </summary>
public sealed class CompressionSettings
{
    [JsonPropertyName("type")]
    public CompressionType Type { get; set; } = CompressionType.None;

    [JsonPropertyName("level")]
    public int Level { get; set; } = 6;
}

/// <summary>
/// Supported compression types.
/// </summary>
public enum CompressionType : byte
{
    None,
    Gzip,
    Snappy,
    Zstd,
    Lz4,
    Zip
}

/// <summary>
/// Timestamp handling settings.
/// </summary>
public sealed class TimestampSettings
{
    [JsonPropertyName("format")]
    public TimestampFormat Format { get; set; } = TimestampFormat.Iso8601;

    [JsonPropertyName("timezone")]
    public string Timezone { get; set; } = "UTC";

    [JsonPropertyName("includeNanoseconds")]
    public bool IncludeNanoseconds { get; set; } = true;
}

/// <summary>
/// Supported timestamp formats.
/// </summary>
public enum TimestampFormat : byte
{
    /// <summary>ISO 8601 string format.</summary>
    Iso8601,
    /// <summary>Unix timestamp in seconds.</summary>
    UnixSeconds,
    /// <summary>Unix timestamp in milliseconds.</summary>
    UnixMilliseconds,
    /// <summary>Unix timestamp in nanoseconds (for pandas datetime64[ns]).</summary>
    UnixNanoseconds,
    /// <summary>Excel serial date format.</summary>
    ExcelSerial
}

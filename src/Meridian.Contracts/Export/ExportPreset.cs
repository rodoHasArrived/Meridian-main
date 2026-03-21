using System.Text.Json.Serialization;

namespace Meridian.Contracts.Export;

/// <summary>
/// Represents a saveable export preset configuration.
/// Implements Feature Refinement #69 - Archive Export Presets.
/// </summary>
public sealed class ExportPreset
{
    /// <summary>
    /// Unique identifier for this preset.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Human-readable name for the preset.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of what this preset does.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Export format (Parquet, CSV, JSONL, Lean, XLSX, SQL).
    /// </summary>
    [JsonPropertyName("format")]
    public ExportPresetFormat Format { get; set; } = ExportPresetFormat.Parquet;

    /// <summary>
    /// Compression type.
    /// </summary>
    [JsonPropertyName("compression")]
    public ExportPresetCompression Compression { get; set; } = ExportPresetCompression.Snappy;

    /// <summary>
    /// Destination path template with variable support.
    /// Variables: {year}, {month}, {day}, {symbol}, {date}, {format}
    /// </summary>
    [JsonPropertyName("destination")]
    public string Destination { get; set; } = string.Empty;

    /// <summary>
    /// Filename pattern template.
    /// Variables: {symbol}, {date}, {type}, {format}
    /// </summary>
    [JsonPropertyName("filenamePattern")]
    public string FilenamePattern { get; set; } = "{symbol}_{date}.{format}";

    /// <summary>
    /// Filter settings for the export.
    /// </summary>
    [JsonPropertyName("filters")]
    public ExportPresetFilters Filters { get; set; } = new();

    /// <summary>
    /// Optional cron schedule for automated exports.
    /// Format: "0 6 * * *" (minute hour day month weekday)
    /// </summary>
    [JsonPropertyName("schedule")]
    public string? Schedule { get; set; }

    /// <summary>
    /// Whether the scheduled export is enabled.
    /// </summary>
    [JsonPropertyName("scheduleEnabled")]
    public bool ScheduleEnabled { get; set; }

    /// <summary>
    /// Post-export hook command to run after export completes.
    /// Variables: {date}, {destination}, {symbol}, {files}
    /// </summary>
    [JsonPropertyName("postExportHook")]
    public string? PostExportHook { get; set; }

    /// <summary>
    /// Whether to send a notification when export completes.
    /// </summary>
    [JsonPropertyName("notifyOnComplete")]
    public bool NotifyOnComplete { get; set; }

    /// <summary>
    /// Whether to include a data dictionary with the export.
    /// </summary>
    [JsonPropertyName("includeDataDictionary")]
    public bool IncludeDataDictionary { get; set; } = true;

    /// <summary>
    /// Whether to include loader scripts for the target format.
    /// </summary>
    [JsonPropertyName("includeLoaderScript")]
    public bool IncludeLoaderScript { get; set; } = true;

    /// <summary>
    /// Whether to overwrite existing files.
    /// </summary>
    [JsonPropertyName("overwriteExisting")]
    public bool OverwriteExisting { get; set; }

    /// <summary>
    /// When this preset was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this preset was last modified.
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this preset was last used for an export.
    /// </summary>
    [JsonPropertyName("lastUsedAt")]
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// Number of times this preset has been used.
    /// </summary>
    [JsonPropertyName("useCount")]
    public int UseCount { get; set; }

    /// <summary>
    /// Whether this is a built-in preset (read-only).
    /// </summary>
    [JsonPropertyName("isBuiltIn")]
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// Specific columns to include in the export.
    /// Empty array means all available columns.
    /// </summary>
    [JsonPropertyName("columns")]
    public string[] Columns { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Whether to include a manifest file with the export.
    /// The manifest documents what data was exported, quality metrics, and file checksums.
    /// </summary>
    [JsonPropertyName("includeManifest")]
    public bool IncludeManifest { get; set; } = true;

    /// <summary>
    /// Pre-export validation rules. When set, the export validates these conditions
    /// before writing any files and aborts if any error-level issues are found.
    /// </summary>
    [JsonPropertyName("validation")]
    public ExportValidationRules? Validation { get; set; }
}

/// <summary>
/// Filter settings for export presets.
/// </summary>
public sealed class ExportPresetFilters
{
    /// <summary>
    /// Event types to include (Trade, BboQuote, LOBSnapshot, HistoricalBar).
    /// Empty array means all event types.
    /// </summary>
    [JsonPropertyName("eventTypes")]
    public string[] EventTypes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Symbols to include.
    /// Empty array means all symbols.
    /// </summary>
    [JsonPropertyName("symbols")]
    public string[] Symbols { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Date range type for export.
    /// </summary>
    [JsonPropertyName("dateRangeType")]
    public DateRangeType DateRangeType { get; set; } = DateRangeType.LastWeek;

    /// <summary>
    /// Custom start date (used when DateRangeType is Custom).
    /// </summary>
    [JsonPropertyName("customStartDate")]
    public DateTime? CustomStartDate { get; set; }

    /// <summary>
    /// Custom end date (used when DateRangeType is Custom).
    /// </summary>
    [JsonPropertyName("customEndDate")]
    public DateTime? CustomEndDate { get; set; }

    /// <summary>
    /// Session filter (All, RegularHours, PreMarket, AfterHours, ExtendedHours).
    /// </summary>
    [JsonPropertyName("sessionFilter")]
    public string SessionFilter { get; set; } = "All";

    /// <summary>
    /// Minimum quality score (0-1). Exclude data below this threshold.
    /// </summary>
    [JsonPropertyName("minQualityScore")]
    public double? MinQualityScore { get; set; }
}

/// <summary>
/// Date range types for export presets.
/// </summary>
public enum DateRangeType : byte
{
    /// <summary>Export today's data.</summary>
    Today,
    /// <summary>Export yesterday's data.</summary>
    Yesterday,
    /// <summary>Export last 7 days.</summary>
    LastWeek,
    /// <summary>Export last 30 days.</summary>
    LastMonth,
    /// <summary>Export last quarter (90 days).</summary>
    LastQuarter,
    /// <summary>Export last year.</summary>
    LastYear,
    /// <summary>Export all available data.</summary>
    All,
    /// <summary>Use custom date range.</summary>
    Custom
}

/// <summary>
/// Supported export formats for presets.
/// </summary>
public enum ExportPresetFormat : byte
{
    /// <summary>Apache Parquet columnar format.</summary>
    Parquet,
    /// <summary>Comma-separated values.</summary>
    Csv,
    /// <summary>JSON Lines format.</summary>
    Jsonl,
    /// <summary>QuantConnect Lean native format.</summary>
    Lean,
    /// <summary>Microsoft Excel XLSX format.</summary>
    Xlsx,
    /// <summary>SQL statements for database import.</summary>
    Sql,
    /// <summary>Raw format (copy without conversion).</summary>
    Raw
}

/// <summary>
/// Compression types for export presets.
/// </summary>
public enum ExportPresetCompression : byte
{
    /// <summary>No compression.</summary>
    None,
    /// <summary>Gzip compression.</summary>
    Gzip,
    /// <summary>Snappy compression (fast, moderate ratio).</summary>
    Snappy,
    /// <summary>ZSTD compression (balanced).</summary>
    Zstd,
    /// <summary>LZ4 compression (fastest).</summary>
    Lz4,
    /// <summary>ZIP archive.</summary>
    Zip
}

/// <summary>
/// Validation rules applied before an export starts.
/// </summary>
public sealed class ExportValidationRules
{
    /// <summary>
    /// Minimum available disk space multiplier relative to the estimated output size.
    /// Default 1.2 means 20% headroom required beyond the estimated export size.
    /// </summary>
    [JsonPropertyName("diskSpaceMultiplier")]
    public double DiskSpaceMultiplier { get; set; } = 1.2;

    /// <summary>
    /// Whether to abort the export when no data is found for the requested filters.
    /// When false, an empty export is allowed with a warning.
    /// </summary>
    [JsonPropertyName("requireData")]
    public bool RequireData { get; set; }

    /// <summary>
    /// Emit a warning when exporting nested data structures to CSV format,
    /// because nested fields are flattened and may lose precision.
    /// </summary>
    [JsonPropertyName("warnCsvComplexTypes")]
    public bool WarnCsvComplexTypes { get; set; } = true;
}

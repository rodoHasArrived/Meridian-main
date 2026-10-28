using System.Text.Json.Serialization;

namespace Meridian.Contracts.Export;


/// <summary>
/// Reports export progress, the active symbol, processed rows, and elapsed time.
/// </summary>
public sealed class ExportProgressEventArgs : EventArgs
{
    public float Progress { get; set; }
    public string? CurrentSymbol { get; set; }
    public int RowsProcessed { get; set; }
    public TimeSpan Elapsed { get; set; }
}



/// <summary>
/// Selects the market data, fields, aggregation, encoding, and destination for an analysis export.
/// </summary>
public sealed class AnalysisExportOptions
{
    public List<string>? Symbols { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public AnalysisExportFormat Format { get; set; } = AnalysisExportFormat.Parquet;
    public DataAggregation? Aggregation { get; set; }
    public string[]? IncludeFields { get; set; }
    public string[]? ExcludeFields { get; set; }
    public Dictionary<string, string>? Filters { get; set; }
    public string? OutputPath { get; set; }
    public string? FileName { get; set; }
    public CompressionType? Compression { get; set; }
    public bool IncludeMetadata { get; set; } = true;
    public bool SplitBySymbol { get; set; }
    public string? Timezone { get; set; }
}

/// <summary>
/// Selects the symbols, date range, chart inclusion, and format for a data quality report.
/// </summary>
public sealed class QualityReportOptions
{
    public List<string>? Symbols { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public bool IncludeCharts { get; set; }
    public string Format { get; set; } = "CSV";
}

/// <summary>
/// Selects order flow metrics, aggregation, date range, and output destination for export.
/// </summary>
public sealed class OrderFlowExportOptions
{
    public List<string>? Symbols { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public string[]? Metrics { get; set; }
    public string Aggregation { get; set; } = "Raw";
    public string Format { get; set; } = "Parquet";
    public string? OutputPath { get; set; }
}

/// <summary>
/// Selects symbols, dates, and event types for a data integrity export.
/// </summary>
public sealed class IntegrityExportOptions
{
    public List<string>? Symbols { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public string[]? EventTypes { get; set; }
    public string Format { get; set; } = "CSV";
    public string? OutputPath { get; set; }
}

/// <summary>
/// Configures the data, metadata, and quality report included in a named research package.
/// </summary>
public sealed class ResearchPackageOptions
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string>? Symbols { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public DataTypeInclusion IncludeData { get; set; } = new();
    public bool IncludeMetadata { get; set; } = true;
    public bool IncludeQualityReport { get; set; }
    public string Format { get; set; } = "Parquet";
    public string? OutputPath { get; set; }
}

/// <summary>
/// Selects which market data categories a research package includes.
/// </summary>
public sealed class DataTypeInclusion
{
    public bool Trades { get; set; } = true;
    public bool Quotes { get; set; } = true;
    public bool Bars { get; set; } = true;
    public bool OrderBook { get; set; }
    public bool OrderFlow { get; set; }
}



/// <summary>
/// Reports analysis export success, created files, output volume, duration, and warnings.
/// </summary>
public sealed class AnalysisExportResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Format { get; set; }
    public string? OutputPath { get; set; }
    public List<string> FilesCreated { get; set; } = new();
    public long RowsExported { get; set; }
    public long BytesWritten { get; set; }
    public TimeSpan Duration { get; set; }
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Reports available export formats or the failure encountered while discovering them.
/// </summary>
public sealed class ExportFormatsResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<ExportFormatInfo> Formats { get; set; } = new();
}

/// <summary>
/// Describes an export format's file extension, purpose, and compression support.
/// </summary>
public sealed class ExportFormatInfo
{
    public string Name { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool SupportsCompression { get; set; }
}

/// <summary>
/// Describes a selectable aggregation value and its display text.
/// </summary>
public sealed class AggregationOption
{
    public string Value { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Reports quality report creation status, output path, and summary metrics.
/// </summary>
public sealed class QualityReportResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Format { get; set; }
    public string? ReportPath { get; set; }
    public QualityReportSummary? Summary { get; set; }
}

/// <summary>
/// Summarizes symbol and day coverage, quality score, gaps, and anomalies for an exported report.
/// </summary>
public sealed class QualityReportSummary
{
    public int TotalSymbols { get; set; }
    public int TotalDays { get; set; }
    public float OverallScore { get; set; }
    public int GapsFound { get; set; }
    public int AnomaliesFound { get; set; }
}

/// <summary>
/// Reports research package creation status, artifact paths, and package size.
/// </summary>
public sealed class ResearchPackageResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Format { get; set; }
    public string? PackagePath { get; set; }
    public string? ManifestPath { get; set; }
    public long SizeBytes { get; set; }
}

/// <summary>
/// Groups reusable export format, aggregation, field, and metadata settings.
/// </summary>
public sealed class ExportTemplate
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AnalysisExportFormat Format { get; set; }
    public DataAggregation Aggregation { get; set; }
    public string[]? IncludeFields { get; set; }
    public bool IncludeMetadata { get; set; }
}



/// <summary>
/// Carries the API response for an analysis export, including generated files and output statistics.
/// </summary>
public sealed class AnalysisExportResponse
{
    public bool Success { get; set; }
    public string? OutputPath { get; set; }
    public string[]? FilesCreated { get; set; }
    public long RowsExported { get; set; }
    public long BytesWritten { get; set; }
    public float DurationSeconds { get; set; }
    public string[]? Warnings { get; set; }
}

/// <summary>
/// Browser and desktop request contract for the shared analysis export endpoint.
/// </summary>
public sealed record ExportAnalysisApiRequest(
    string? ProfileId,
    string[]? Symbols,
    string? Format,
    DateTime? StartDate,
    DateTime? EndDate);

/// <summary>
/// File-level evidence returned from an analysis export run.
/// </summary>
public sealed record ExportAnalysisApiFile(
    string Path,
    string? Symbol,
    string? Format,
    long SizeBytes,
    long RecordCount);

/// <summary>
/// Shared response contract for the analysis export endpoint.
/// </summary>
public sealed record ExportAnalysisApiResponse(
    string? JobId,
    bool Success,
    string Status,
    string ProfileId,
    string[]? Symbols,
    int FilesGenerated,
    long TotalRecords,
    long TotalBytes,
    string? OutputDirectory,
    double DurationSeconds,
    string? Error,
    string[] Warnings,
    IReadOnlyList<ExportAnalysisApiFile> Files,
    DateTimeOffset Timestamp);

/// <summary>
/// Shared response contract for the specialized export compatibility routes.
/// These routes use the canonical analysis exporter and therefore return the
/// same artifact-level evidence instead of implying a separate renderer.
/// </summary>
public sealed record SpecializedExportApiResponse(
    string? JobId,
    bool Success,
    string Status,
    string Format,
    string[]? Symbols,
    int FilesGenerated,
    long TotalRecords,
    long TotalBytes,
    string? OutputDirectory,
    string? Error,
    string[] Warnings,
    IReadOnlyList<ExportAnalysisApiFile> Files,
    string? DataDictionaryPath,
    string? LoaderScriptPath,
    string? LineageManifestPath,
    QualityReportSummary? QualitySummary,
    DateTimeOffset Timestamp);

/// <summary>
/// Carries the export format choices returned by the API.
/// </summary>
public sealed class ExportFormatsResponse
{
    public List<ExportFormatInfo>? Formats { get; set; }
}

/// <summary>
/// Carries the generated quality report path and summary returned by the API.
/// </summary>
public sealed class QualityReportResponse
{
    public string? ReportPath { get; set; }
    public QualityReportSummary? Summary { get; set; }
}

/// <summary>
/// Carries research package and manifest paths with the generated package size.
/// </summary>
public sealed class ResearchPackageResponse
{
    public string? PackagePath { get; set; }
    public string? ManifestPath { get; set; }
    public long SizeBytes { get; set; }
}



public enum AnalysisExportFormat : byte
{
    CSV,
    Parquet,
    JSON,
    JSONL,
    Excel,
    HDF5,
    Feather
}

public enum DataAggregation : byte
{
    Tick,
    Second,
    Minute,
    FiveMinute,
    FifteenMinute,
    ThirtyMinute,
    Hour,
    Daily,
    Weekly,
    Monthly
}

public enum CompressionType : byte
{
    None,
    Gzip,
    LZ4,
    Snappy,
    ZSTD
}

using System.Text.Json.Serialization;

namespace Meridian.Contracts.Export;


public sealed class ExportProgressEventArgs : EventArgs
{
    public float Progress { get; set; }
    public string? CurrentSymbol { get; set; }
    public int RowsProcessed { get; set; }
    public TimeSpan Elapsed { get; set; }
}



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

public sealed class QualityReportOptions
{
    public List<string>? Symbols { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public bool IncludeCharts { get; set; } = true;
    public string Format { get; set; } = "HTML";
}

public sealed class OrderFlowExportOptions
{
    public List<string>? Symbols { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public string[]? Metrics { get; set; }
    public string Aggregation { get; set; } = "Minute";
    public string Format { get; set; } = "Parquet";
    public string? OutputPath { get; set; }
}

public sealed class IntegrityExportOptions
{
    public List<string>? Symbols { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public string[]? EventTypes { get; set; }
    public string Format { get; set; } = "CSV";
    public string? OutputPath { get; set; }
}

public sealed class ResearchPackageOptions
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string>? Symbols { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public DataTypeInclusion IncludeData { get; set; } = new();
    public bool IncludeMetadata { get; set; } = true;
    public bool IncludeQualityReport { get; set; } = true;
    public string Format { get; set; } = "Parquet";
    public string? OutputPath { get; set; }
}

public sealed class DataTypeInclusion
{
    public bool Trades { get; set; } = true;
    public bool Quotes { get; set; } = true;
    public bool Bars { get; set; } = true;
    public bool OrderBook { get; set; }
    public bool OrderFlow { get; set; }
}



public sealed class AnalysisExportResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? OutputPath { get; set; }
    public List<string> FilesCreated { get; set; } = new();
    public long RowsExported { get; set; }
    public long BytesWritten { get; set; }
    public TimeSpan Duration { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public sealed class ExportFormatsResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<ExportFormatInfo> Formats { get; set; } = new();
}

public sealed class ExportFormatInfo
{
    public string Name { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool SupportsCompression { get; set; }
}

public sealed class AggregationOption
{
    public string Value { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class QualityReportResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? ReportPath { get; set; }
    public QualityReportSummary? Summary { get; set; }
}

public sealed class QualityReportSummary
{
    public int TotalSymbols { get; set; }
    public int TotalDays { get; set; }
    public float OverallScore { get; set; }
    public int GapsFound { get; set; }
    public int AnomaliesFound { get; set; }
}

public sealed class ResearchPackageResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? PackagePath { get; set; }
    public string? ManifestPath { get; set; }
    public long SizeBytes { get; set; }
}

public sealed class ExportTemplate
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AnalysisExportFormat Format { get; set; }
    public DataAggregation Aggregation { get; set; }
    public string[]? IncludeFields { get; set; }
    public bool IncludeMetadata { get; set; }
}



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

public sealed class ExportFormatsResponse
{
    public List<ExportFormatInfo>? Formats { get; set; }
}

public sealed class QualityReportResponse
{
    public string? ReportPath { get; set; }
    public QualityReportSummary? Summary { get; set; }
}

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


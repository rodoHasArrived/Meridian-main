using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Export;

namespace Meridian.Ui.Services.Services;

/// <summary>
/// Analysis export service that provides export operations, format queries,
/// aggregation options, and templates. Uses <see cref="ApiClientService"/>
/// directly for all API communication.
/// </summary>
public sealed class AnalysisExportService
{
    private static readonly Lazy<AnalysisExportService> _instance = new(() => new AnalysisExportService());
    public static AnalysisExportService Instance => _instance.Value;

    public event EventHandler<ExportProgressEventArgs>? ProgressChanged;

    private AnalysisExportService() { }

    private void OnProgressChanged(ExportProgressEventArgs e)
        => ProgressChanged?.Invoke(this, e);

    private async Task<(bool Success, string? ErrorMessage, T? Data)> PostApiAsync<T>(string endpoint, object body, CancellationToken ct) where T : class
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<T>(endpoint, body, ct);
        return (response.Success, response.ErrorMessage, response.Data);
    }

    private async Task<(bool Success, string? ErrorMessage, T? Data)> GetApiAsync<T>(string endpoint, CancellationToken ct) where T : class
    {
        var response = await ApiClientService.Instance.GetWithResponseAsync<T>(endpoint, ct);
        return (response.Success, response.ErrorMessage, response.Data);
    }

    public async Task<AnalysisExportResult> ExportAsync(AnalysisExportOptions options, CancellationToken ct = default)
    {
        var (success, errorMessage, data) = await PostApiAsync<AnalysisExportResponse>(
            "/api/export/analysis",
            new
            {
                symbols = options.Symbols,
                fromDate = options.FromDate?.ToString("yyyy-MM-dd"),
                toDate = options.ToDate?.ToString("yyyy-MM-dd"),
                format = options.Format.ToString(),
                aggregation = options.Aggregation?.ToString(),
                includeFields = options.IncludeFields,
                excludeFields = options.ExcludeFields,
                filters = options.Filters,
                outputPath = options.OutputPath,
                fileName = options.FileName,
                compression = options.Compression?.ToString(),
                includeMetadata = options.IncludeMetadata,
                splitBySymbol = options.SplitBySymbol,
                timezone = options.Timezone
            },
            ct);

        if (success && data != null)
        {
            return new AnalysisExportResult
            {
                Success = data.Success,
                OutputPath = data.OutputPath,
                FilesCreated = data.FilesCreated != null ? new List<string>(data.FilesCreated) : new List<string>(),
                RowsExported = data.RowsExported,
                BytesWritten = data.BytesWritten,
                Duration = TimeSpan.FromSeconds(data.DurationSeconds),
                Warnings = data.Warnings != null ? new List<string>(data.Warnings) : new List<string>()
            };
        }

        return new AnalysisExportResult { Success = false, Error = errorMessage ?? "Export failed" };
    }

    public async Task<ExportFormatsResult> GetAvailableFormatsAsync(CancellationToken ct = default)
    {
        var (success, _, data) = await GetApiAsync<ExportFormatsResponse>("/api/export/formats", ct);

        if (success && data != null)
        {
            return new ExportFormatsResult
            {
                Success = true,
                Formats = data.Formats ?? new List<ExportFormatInfo>()
            };
        }

        return new ExportFormatsResult
        {
            Success = true,
            Formats = GetDefaultFormats()
        };
    }

    public Task<List<AggregationOption>> GetAggregationOptionsAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new List<AggregationOption>
        {
            new() { Value = "Tick", DisplayName = "Tick (No Aggregation)", Description = "Raw tick data" },
            new() { Value = "Second", DisplayName = "1 Second", Description = "Aggregate to 1-second bars" },
            new() { Value = "Minute", DisplayName = "1 Minute", Description = "Aggregate to 1-minute bars" },
            new() { Value = "FiveMinute", DisplayName = "5 Minutes", Description = "Aggregate to 5-minute bars" },
            new() { Value = "FifteenMinute", DisplayName = "15 Minutes", Description = "Aggregate to 15-minute bars" },
            new() { Value = "ThirtyMinute", DisplayName = "30 Minutes", Description = "Aggregate to 30-minute bars" },
            new() { Value = "Hour", DisplayName = "1 Hour", Description = "Aggregate to hourly bars" },
            new() { Value = "Daily", DisplayName = "Daily", Description = "Aggregate to daily bars" },
            new() { Value = "Weekly", DisplayName = "Weekly", Description = "Aggregate to weekly bars" },
            new() { Value = "Monthly", DisplayName = "Monthly", Description = "Aggregate to monthly bars" }
        });
    }

    public async Task<QualityReportResult> GenerateQualityReportAsync(QualityReportOptions options, CancellationToken ct = default)
    {
        var (success, errorMessage, data) = await PostApiAsync<QualityReportResponse>(
            "/api/export/quality-report",
            new
            {
                symbols = options.Symbols,
                fromDate = options.FromDate?.ToString("yyyy-MM-dd"),
                toDate = options.ToDate?.ToString("yyyy-MM-dd"),
                includeCharts = options.IncludeCharts,
                format = options.Format
            },
            ct);

        if (success && data != null)
        {
            return new QualityReportResult
            {
                Success = true,
                ReportPath = data.ReportPath,
                Summary = data.Summary
            };
        }

        return new QualityReportResult { Success = false, Error = errorMessage ?? "Failed to generate report" };
    }

    public async Task<AnalysisExportResult> ExportOrderFlowAsync(OrderFlowExportOptions options, CancellationToken ct = default)
    {
        var (success, errorMessage, data) = await PostApiAsync<AnalysisExportResponse>(
            "/api/export/orderflow",
            new
            {
                symbols = options.Symbols,
                fromDate = options.FromDate?.ToString("yyyy-MM-dd"),
                toDate = options.ToDate?.ToString("yyyy-MM-dd"),
                metrics = options.Metrics,
                aggregation = options.Aggregation,
                format = options.Format,
                outputPath = options.OutputPath
            },
            ct);

        if (success && data != null)
        {
            return new AnalysisExportResult
            {
                Success = data.Success,
                OutputPath = data.OutputPath,
                FilesCreated = data.FilesCreated != null ? new List<string>(data.FilesCreated) : new List<string>(),
                RowsExported = data.RowsExported,
                BytesWritten = data.BytesWritten
            };
        }

        return new AnalysisExportResult { Success = false, Error = errorMessage ?? "Export failed" };
    }

    public async Task<AnalysisExportResult> ExportIntegrityEventsAsync(IntegrityExportOptions options, CancellationToken ct = default)
    {
        var (success, errorMessage, data) = await PostApiAsync<AnalysisExportResponse>(
            "/api/export/integrity",
            new
            {
                symbols = options.Symbols,
                fromDate = options.FromDate?.ToString("yyyy-MM-dd"),
                toDate = options.ToDate?.ToString("yyyy-MM-dd"),
                eventTypes = options.EventTypes,
                format = options.Format,
                outputPath = options.OutputPath
            },
            ct);

        if (success && data != null)
        {
            return new AnalysisExportResult
            {
                Success = data.Success,
                OutputPath = data.OutputPath,
                FilesCreated = data.FilesCreated != null ? new List<string>(data.FilesCreated) : new List<string>(),
                RowsExported = data.RowsExported
            };
        }

        return new AnalysisExportResult { Success = false, Error = errorMessage ?? "Export failed" };
    }

    public async Task<ResearchPackageResult> CreateResearchPackageAsync(ResearchPackageOptions options, CancellationToken ct = default)
    {
        var (success, errorMessage, data) = await PostApiAsync<ResearchPackageResponse>(
            "/api/export/research-package",
            new
            {
                name = options.Name,
                description = options.Description,
                symbols = options.Symbols,
                fromDate = options.FromDate?.ToString("yyyy-MM-dd"),
                toDate = options.ToDate?.ToString("yyyy-MM-dd"),
                includeData = options.IncludeData,
                includeMetadata = options.IncludeMetadata,
                includeQualityReport = options.IncludeQualityReport,
                format = options.Format,
                outputPath = options.OutputPath
            },
            ct);

        if (success && data != null)
        {
            return new ResearchPackageResult
            {
                Success = true,
                PackagePath = data.PackagePath,
                ManifestPath = data.ManifestPath,
                SizeBytes = data.SizeBytes
            };
        }

        return new ResearchPackageResult { Success = false, Error = errorMessage ?? "Failed to create package" };
    }

    public Task<List<ExportTemplate>> GetExportTemplatesAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new List<ExportTemplate>
        {
            new()
            {
                Name = "Academic Research",
                Description = "Clean dataset suitable for academic research papers",
                Format = AnalysisExportFormat.Parquet,
                Aggregation = DataAggregation.Daily,
                IncludeFields = new[] { "Symbol", "Date", "Open", "High", "Low", "Close", "Volume", "AdjClose" },
                IncludeMetadata = true
            },
            new()
            {
                Name = "Machine Learning",
                Description = "Format optimized for ML pipelines",
                Format = AnalysisExportFormat.Parquet,
                Aggregation = DataAggregation.Minute,
                IncludeFields = new[] { "Timestamp", "Symbol", "Price", "Volume", "BidPrice", "AskPrice", "Spread" },
                IncludeMetadata = false
            },
            new()
            {
                Name = "Backtesting",
                Description = "Data format compatible with backtesting engines",
                Format = AnalysisExportFormat.CSV,
                Aggregation = DataAggregation.Minute,
                IncludeFields = new[] { "DateTime", "Symbol", "Open", "High", "Low", "Close", "Volume" },
                IncludeMetadata = false
            },
            new()
            {
                Name = "Order Flow Analysis",
                Description = "Tick data with trade direction and size",
                Format = AnalysisExportFormat.Parquet,
                Aggregation = DataAggregation.Tick,
                IncludeFields = new[] { "Timestamp", "Symbol", "Price", "Size", "Side", "Exchange", "Sequence" },
                IncludeMetadata = true
            },
            new()
            {
                Name = "Market Microstructure",
                Description = "Full LOB snapshots and trades for microstructure research",
                Format = AnalysisExportFormat.HDF5,
                Aggregation = DataAggregation.Tick,
                IncludeFields = new[] { "Timestamp", "Symbol", "EventType", "Price", "Size", "BidPrices", "AskPrices", "BidSizes", "AskSizes" },
                IncludeMetadata = true
            }
        });
    }

    private static List<ExportFormatInfo> GetDefaultFormats() => new()
    {
        new() { Name = "CSV", Extension = ".csv", Description = "Comma-separated values", SupportsCompression = true },
        new() { Name = "Parquet", Extension = ".parquet", Description = "Apache Parquet columnar format", SupportsCompression = true },
        new() { Name = "JSON", Extension = ".json", Description = "JSON format", SupportsCompression = true },
        new() { Name = "JSONL", Extension = ".jsonl", Description = "JSON Lines format (one JSON per line)", SupportsCompression = true },
        new() { Name = "Excel", Extension = ".xlsx", Description = "Microsoft Excel format", SupportsCompression = false },
        new() { Name = "HDF5", Extension = ".h5", Description = "HDF5 hierarchical data format", SupportsCompression = true },
        new() { Name = "Feather", Extension = ".feather", Description = "Apache Arrow Feather format", SupportsCompression = true }
    };
}

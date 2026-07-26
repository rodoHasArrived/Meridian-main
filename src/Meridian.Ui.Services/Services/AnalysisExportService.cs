using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Api;
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
        // Observe cancellation deterministically before dispatching: a pre-cancelled token
        // must always surface as OperationCanceledException rather than racing on whether the
        // underlying HTTP client happens to throw or return a failed response.
        ct.ThrowIfCancellationRequested();
        var response = await ApiClientService.Instance.PostWithResponseAsync<T>(endpoint, body, ct);
        return (response.Success, response.ErrorMessage, response.Data);
    }

    private async Task<(bool Success, string? ErrorMessage, T? Data)> GetApiAsync<T>(string endpoint, CancellationToken ct) where T : class
    {
        ct.ThrowIfCancellationRequested();
        var response = await ApiClientService.Instance.GetWithResponseAsync<T>(endpoint, ct);
        return (response.Success, response.ErrorMessage, response.Data);
    }

    public async Task<AnalysisExportResult> ExportAsync(AnalysisExportOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var request = CreateCanonicalExportRequest(options);
        var (success, errorMessage, data) = await PostApiAsync<ExportAnalysisApiResponse>(
            "/api/export/analysis",
            request,
            ct);

        if (success && data != null)
            return MapCanonicalExportResponse(data);

        return new AnalysisExportResult { Success = false, Error = errorMessage ?? "Export failed" };
    }

    public async Task<ExportFormatsResult> GetAvailableFormatsAsync(CancellationToken ct = default)
    {
        var (success, errorMessage, data) = await GetApiAsync<ExportFormatsResponse>("/api/export/formats", ct);

        if (success && data != null)
        {
            var compatible = (data.Formats ?? [])
                .Where(format => GetCanonicalFormats().Any(candidate =>
                    string.Equals(candidate.Extension, format.Extension, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (compatible.Count == 0)
            {
                return new ExportFormatsResult
                {
                    Success = false,
                    Error = "The export service did not advertise any format supported by the canonical analysis export request.",
                    Formats = GetCanonicalFormats()
                };
            }

            return new ExportFormatsResult
            {
                Success = true,
                Formats = compatible
            };
        }

        return new ExportFormatsResult
        {
            Success = false,
            Error = errorMessage ?? "The export format capability endpoint is unavailable.",
            Formats = GetCanonicalFormats()
        };
    }

    public Task<List<AggregationOption>> GetAggregationOptionsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // ExportAnalysisApiRequest has no aggregation field. An empty capability list prevents a
        // client from selecting a value that the shared endpoint would silently ignore.
        return Task.FromResult(new List<AggregationOption>());
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
            UiApiRoutes.ExportStrategyPackage,
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
        ct.ThrowIfCancellationRequested();
        // The historical presets included aggregation and field-selection semantics absent from
        // ExportAnalysisApiRequest. Do not advertise them until the shared contract can execute
        // every retained option.
        return Task.FromResult(new List<ExportTemplate>());
    }

    internal static ExportAnalysisApiRequest CreateCanonicalExportRequest(AnalysisExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        EnsureCanonicalRequestCanRepresent(options);
        var (profileId, format) = options.Format switch
        {
            AnalysisExportFormat.CSV => ("r-stats", "csv"),
            AnalysisExportFormat.Parquet => ("python-pandas", "parquet"),
            AnalysisExportFormat.Excel => ("excel", "xlsx"),
            AnalysisExportFormat.Feather => ("arrow-feather", "arrow"),
            _ => throw new NotSupportedException(
                $"Analysis export format '{options.Format}' is not supported by a registered export profile.")
        };

        return new ExportAnalysisApiRequest(
            profileId,
            options.Symbols?.ToArray(),
            format,
            ToUtcDateTime(options.FromDate),
            ToUtcDateTime(options.ToDate));
    }

    private static void EnsureCanonicalRequestCanRepresent(AnalysisExportOptions options)
    {
        var unsupported = new List<string>();
        if (options.Aggregation.HasValue)
            unsupported.Add(nameof(options.Aggregation));
        if (options.IncludeFields is { Length: > 0 })
            unsupported.Add(nameof(options.IncludeFields));
        if (options.ExcludeFields is { Length: > 0 })
            unsupported.Add(nameof(options.ExcludeFields));
        if (options.Filters is { Count: > 0 })
            unsupported.Add(nameof(options.Filters));
        if (!string.IsNullOrWhiteSpace(options.OutputPath))
            unsupported.Add(nameof(options.OutputPath));
        if (!string.IsNullOrWhiteSpace(options.FileName))
            unsupported.Add(nameof(options.FileName));
        if (options.Compression.HasValue)
            unsupported.Add(nameof(options.Compression));
        if (!options.IncludeMetadata)
            unsupported.Add($"{nameof(options.IncludeMetadata)}=false");
        if (options.SplitBySymbol)
            unsupported.Add($"{nameof(options.SplitBySymbol)}=true");
        if (!string.IsNullOrWhiteSpace(options.Timezone))
            unsupported.Add(nameof(options.Timezone));

        if (unsupported.Count > 0)
        {
            throw new NotSupportedException(
                "The shared analysis export API cannot represent these requested options: " +
                string.Join(", ", unsupported) +
                ". No export was requested.");
        }
    }

    internal static AnalysisExportResult MapCanonicalExportResponse(ExportAnalysisApiResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var files = response.Files
            .Select(file =>
                string.IsNullOrWhiteSpace(response.OutputDirectory) || Path.IsPathRooted(file.Path)
                    ? file.Path
                    : Path.Combine(response.OutputDirectory, file.Path))
            .ToList();

        return new AnalysisExportResult
        {
            Success = response.Success,
            Error = response.Error,
            OutputPath = response.OutputDirectory,
            FilesCreated = files,
            RowsExported = response.TotalRecords,
            BytesWritten = response.TotalBytes,
            Duration = TimeSpan.FromSeconds(response.DurationSeconds),
            Warnings = response.Warnings.ToList()
        };
    }

    private static DateTime? ToUtcDateTime(DateOnly? date) =>
        date.HasValue
            ? date.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            : null;

    internal static List<ExportFormatInfo> GetCanonicalFormats() => new()
    {
        new() { Name = "CSV", Extension = ".csv", Description = "Comma-separated values", SupportsCompression = false },
        new() { Name = "Parquet", Extension = ".parquet", Description = "Apache Parquet columnar format", SupportsCompression = false },
        new() { Name = "Excel", Extension = ".xlsx", Description = "Microsoft Excel format", SupportsCompression = false },
        new() { Name = "Apache Arrow IPC", Extension = ".arrow", Description = "Apache Arrow IPC format", SupportsCompression = false }
    };
}

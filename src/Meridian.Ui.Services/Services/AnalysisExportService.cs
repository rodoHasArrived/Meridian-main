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
        ArgumentNullException.ThrowIfNull(options);
        ct.ThrowIfCancellationRequested();
        EnsureQualityReportRequestCanRepresent(options);

        var (success, errorMessage, data) = await PostApiAsync<SpecializedExportApiResponse>(
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
            return MapQualityReportResponse(data);

        return new QualityReportResult { Success = false, Error = errorMessage ?? "Failed to generate report" };
    }

    public async Task<AnalysisExportResult> ExportOrderFlowAsync(OrderFlowExportOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ct.ThrowIfCancellationRequested();
        EnsureOrderFlowRequestCanRepresent(options);

        var (success, errorMessage, data) = await PostApiAsync<SpecializedExportApiResponse>(
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
            return MapSpecializedExportResponse(data);

        return new AnalysisExportResult { Success = false, Error = errorMessage ?? "Export failed" };
    }

    public async Task<AnalysisExportResult> ExportIntegrityEventsAsync(IntegrityExportOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ct.ThrowIfCancellationRequested();
        EnsureIntegrityRequestCanRepresent(options);

        var (success, errorMessage, data) = await PostApiAsync<SpecializedExportApiResponse>(
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
            return MapSpecializedExportResponse(data);

        return new AnalysisExportResult { Success = false, Error = errorMessage ?? "Export failed" };
    }

    public async Task<ResearchPackageResult> CreateResearchPackageAsync(ResearchPackageOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ct.ThrowIfCancellationRequested();
        EnsureResearchPackageRequestCanRepresent(options);

        var (success, errorMessage, data) = await PostApiAsync<SpecializedExportApiResponse>(
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
            return MapResearchPackageResponse(data);

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
        var payloadError = ValidateCanonicalPayload(response);

        return new AnalysisExportResult
        {
            Success = response.Success && payloadError is null,
            Error = payloadError ?? response.Error,
            Format = ResolveResponseFormat(response.Files),
            OutputPath = response.OutputDirectory,
            FilesCreated = files,
            RowsExported = response.TotalRecords,
            BytesWritten = response.TotalBytes,
            Duration = TimeSpan.FromSeconds(response.DurationSeconds),
            Warnings = response.Warnings.ToList()
        };
    }

    internal static AnalysisExportResult MapSpecializedExportResponse(SpecializedExportApiResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var files = ResolveResponseFiles(response.OutputDirectory, response.Files);
        var payloadError = ValidateSpecializedPayload(response);
        return new AnalysisExportResult
        {
            Success = response.Success && payloadError is null,
            Error = payloadError ?? response.Error,
            Format = response.Format,
            OutputPath = response.OutputDirectory,
            FilesCreated = files,
            RowsExported = response.TotalRecords,
            BytesWritten = response.TotalBytes,
            Warnings = response.Warnings.ToList()
        };
    }

    internal static QualityReportResult MapQualityReportResponse(SpecializedExportApiResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var payloadError = ValidateSpecializedPayload(response);
        return new QualityReportResult
        {
            Success = response.Success && payloadError is null,
            Error = payloadError ?? response.Error,
            Format = response.Format,
            ReportPath = response.OutputDirectory,
            Summary = response.QualitySummary
        };
    }

    internal static ResearchPackageResult MapResearchPackageResponse(SpecializedExportApiResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var payloadError = ValidateSpecializedPayload(response);
        return new ResearchPackageResult
        {
            Success = response.Success && payloadError is null,
            Error = payloadError ?? response.Error,
            Format = response.Format,
            PackagePath = response.OutputDirectory,
            ManifestPath = response.LineageManifestPath,
            SizeBytes = response.TotalBytes
        };
    }

    internal static void EnsureQualityReportRequestCanRepresent(QualityReportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var unsupported = new List<string>();
        if (options.IncludeCharts)
            unsupported.Add($"{nameof(options.IncludeCharts)}=true");

        EnsureSpecializedRequestCanRepresent(
            "quality report",
            options.Format,
            options.FromDate,
            options.ToDate,
            unsupported);
    }

    internal static void EnsureOrderFlowRequestCanRepresent(OrderFlowExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var unsupported = new List<string>();
        if (options.Metrics is { Length: > 0 })
            unsupported.Add(nameof(options.Metrics));
        if (!string.IsNullOrWhiteSpace(options.Aggregation) &&
            !string.Equals(options.Aggregation, "raw", StringComparison.OrdinalIgnoreCase))
        {
            unsupported.Add(nameof(options.Aggregation));
        }
        if (!string.IsNullOrWhiteSpace(options.OutputPath))
            unsupported.Add(nameof(options.OutputPath));

        EnsureSpecializedRequestCanRepresent(
            "order-flow export",
            options.Format,
            options.FromDate,
            options.ToDate,
            unsupported);
    }

    internal static void EnsureIntegrityRequestCanRepresent(IntegrityExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var unsupported = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.OutputPath))
            unsupported.Add(nameof(options.OutputPath));

        EnsureSpecializedRequestCanRepresent(
            "integrity export",
            options.Format,
            options.FromDate,
            options.ToDate,
            unsupported);
    }

    internal static void EnsureResearchPackageRequestCanRepresent(ResearchPackageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var unsupported = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.Name))
            unsupported.Add(nameof(options.Name));
        if (!string.IsNullOrWhiteSpace(options.Description))
            unsupported.Add(nameof(options.Description));
        if (options.IncludeQualityReport)
            unsupported.Add($"{nameof(options.IncludeQualityReport)}=true");
        if (!options.IncludeMetadata)
            unsupported.Add($"{nameof(options.IncludeMetadata)}=false");
        if (!string.IsNullOrWhiteSpace(options.OutputPath))
            unsupported.Add(nameof(options.OutputPath));

        var includeData = options.IncludeData;
        if (!includeData.Trades &&
            !includeData.Quotes &&
            !includeData.Bars &&
            !includeData.OrderBook &&
            !includeData.OrderFlow)
        {
            unsupported.Add("IncludeData has no selected data type");
        }

        EnsureSpecializedRequestCanRepresent(
            "strategy package",
            options.Format,
            options.FromDate,
            options.ToDate,
            unsupported);
    }

    private static void EnsureSpecializedRequestCanRepresent(
        string operation,
        string format,
        DateOnly? fromDate,
        DateOnly? toDate,
        List<string> unsupported)
    {
        if (!IsCanonicalSpecializedFormat(format))
            unsupported.Add($"Format={format}");
        if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
            unsupported.Add("FromDate is after ToDate");

        if (unsupported.Count == 0)
            return;

        throw new NotSupportedException(
            $"The canonical {operation} API cannot represent these requested options: " +
            string.Join(", ", unsupported) +
            ". No export was requested.");
    }

    private static bool IsCanonicalSpecializedFormat(string? format)
        => format?.Trim().ToLowerInvariant() is
            "csv" or "parquet" or "xlsx" or "excel" or "arrow" or "feather";

    private static string? ValidateCanonicalPayload(ExportAnalysisApiResponse response)
    {
        if (!response.Success)
            return response.Error ?? "The export operation failed.";
        if (!string.Equals(response.Status, "completed", StringComparison.OrdinalIgnoreCase))
            return $"The export response claimed success with non-completed status '{response.Status}'.";
        if (response.FilesGenerated <= 0 || response.Files.Count == 0)
            return "The export response claimed success without any generated artifact.";
        if (response.FilesGenerated != response.Files.Count)
            return "The export response file count does not match its artifact evidence.";
        if (response.TotalRecords <= 0)
            return "The export response claimed success without any exported record.";

        return null;
    }

    private static string? ValidateSpecializedPayload(SpecializedExportApiResponse response)
    {
        if (!response.Success)
            return response.Error ?? "The export operation failed.";
        if (!string.Equals(response.Status, "completed", StringComparison.OrdinalIgnoreCase))
            return $"The export response claimed success with non-completed status '{response.Status}'.";
        if (response.FilesGenerated <= 0 || response.Files.Count == 0)
            return "The export response claimed success without any generated artifact.";
        if (response.FilesGenerated != response.Files.Count)
            return "The export response file count does not match its artifact evidence.";
        if (response.TotalRecords <= 0)
            return "The export response claimed success without any exported record.";
        if (string.IsNullOrWhiteSpace(response.Format))
            return "The export response did not identify the generated format.";

        var mismatchedFormat = response.Files.FirstOrDefault(file =>
            !string.Equals(file.Format, response.Format, StringComparison.OrdinalIgnoreCase));
        if (mismatchedFormat is not null)
        {
            return $"The export response format '{response.Format}' does not match artifact " +
                   $"'{mismatchedFormat.Path}' format '{mismatchedFormat.Format}'.";
        }

        return null;
    }

    private static List<string> ResolveResponseFiles(
        string? outputDirectory,
        IReadOnlyList<ExportAnalysisApiFile> files)
        => files.Select(file =>
                string.IsNullOrWhiteSpace(outputDirectory) || Path.IsPathRooted(file.Path)
                    ? file.Path
                    : Path.Combine(outputDirectory, file.Path))
            .ToList();

    private static string? ResolveResponseFormat(IReadOnlyList<ExportAnalysisApiFile> files)
    {
        var formats = files
            .Select(static file => file.Format)
            .Where(static format => !string.IsNullOrWhiteSpace(format))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return formats.Length switch
        {
            0 => null,
            1 => formats[0],
            _ => "mixed"
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

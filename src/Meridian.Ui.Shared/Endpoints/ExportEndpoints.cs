using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Api;
using Meridian.Contracts.Export;
using Meridian.Identity.Auth;
using Meridian.Storage;
using Meridian.Storage.Export;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering data export API endpoints.
/// Wired to the real AnalysisExportService for actual data export.
/// </summary>
public static class ExportEndpoints
{
    private static readonly string ExportBaseDir = Path.Combine(Path.GetTempPath(), "meridian-exports");
    private static readonly TimeSpan ExportMaxAge = TimeSpan.FromHours(24);

    public static void MapExportEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Export");

        // Export preview - read-only scope check for browser and desktop clients.
        group.MapGet(UiApiRoutes.ExportPreview, (
            string? profile,
            string? profileId,
            string? symbols,
            string? eventTypes,
            DateTime? startDate,
            DateTime? endDate,
            int? sampleSize,
            [FromServices] AnalysisExportService? exportService) =>
        {
            var resolvedProfileId = string.IsNullOrWhiteSpace(profileId)
                ? profile
                : profileId;
            resolvedProfileId = string.IsNullOrWhiteSpace(resolvedProfileId)
                ? "python-pandas"
                : resolvedProfileId.Trim();

            var requestedSymbols = SplitCsv(symbols);
            var requestedEventTypes = SplitCsv(eventTypes);
            if (requestedEventTypes.Length == 0)
            {
                requestedEventTypes = new[] { "Trade", "BboQuote" };
            }

            var profiles = exportService?.GetProfiles();
            var matchedProfile = profiles?.FirstOrDefault(p =>
                string.Equals(p.Id, resolvedProfileId, StringComparison.OrdinalIgnoreCase));

            var previewSampleSize = Math.Clamp(sampleSize ?? 25, 1, 500);
            var from = startDate ?? DateTime.UtcNow.AddDays(-7);
            var to = endDate ?? DateTime.UtcNow;

            return Results.Json(new
            {
                previewOnly = true,
                profileId = matchedProfile?.Id ?? resolvedProfileId,
                profileName = matchedProfile?.Name ?? resolvedProfileId,
                format = matchedProfile?.Format.ToString().ToLowerInvariant(),
                compression = matchedProfile?.Compression.Type.ToString().ToLowerInvariant(),
                symbols = requestedSymbols,
                eventTypes = requestedEventTypes,
                startDate = from,
                endDate = to,
                sampleSize = previewSampleSize,
                serviceAvailable = exportService is not null,
                canRunExport = exportService is not null,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("PreviewExport")
        .Produces(200);

        // Analysis export — wired to real AnalysisExportService
        group.MapPost(UiApiRoutes.ExportAnalysis, async (
            ExportAnalysisApiRequest? req,
            [FromServices] AnalysisExportService? exportService,
            CancellationToken ct) =>
        {
            CleanupOldExportDirectories();
            var normalizedRequest = NormalizeExportAnalysisRequest(req);

            if (exportService is null)
            {
                return Results.Json(
                    CreateUnavailableExportResponse(normalizedRequest.ProfileId),
                    jsonOptions,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var profile = FindProfile(exportService, normalizedRequest.ProfileId);
            if (profile is null)
            {
                return Results.Json(
                    CreateInvalidExportResponse(
                        normalizedRequest.ProfileId,
                        $"Unknown export profile: {normalizedRequest.ProfileId}"),
                    jsonOptions,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!string.IsNullOrWhiteSpace(normalizedRequest.Format))
            {
                if (!TryParseExportFormat(normalizedRequest.Format, out var requestedFormat))
                {
                    return Results.Json(
                        CreateInvalidExportResponse(
                            profile.Id,
                            $"Unsupported export format: {normalizedRequest.Format}"),
                        jsonOptions,
                        statusCode: StatusCodes.Status400BadRequest);
                }

                if (profile.Format != requestedFormat)
                {
                    return Results.Json(
                        CreateInvalidExportResponse(
                            profile.Id,
                            $"Export profile '{profile.Id}' produces '{ToCanonicalFormat(profile.Format)}', not '{ToCanonicalFormat(requestedFormat)}'."),
                        jsonOptions,
                        statusCode: StatusCodes.Status400BadRequest);
                }
            }

            var outputDir = Path.Combine(
                Path.GetTempPath(),
                "meridian-exports",
                $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}");

            var exportRequest = new ExportRequest
            {
                ProfileId = profile.Id,
                Symbols = normalizedRequest.Symbols,
                StartDate = normalizedRequest.StartDate ?? DateTime.UtcNow.AddDays(-7),
                EndDate = normalizedRequest.EndDate ?? DateTime.UtcNow,
                OutputDirectory = outputDir,
                EventTypes = new[] { "Trade", "BboQuote" }
            };

            var result = await exportService.ExportAsync(exportRequest, ct);

            return Results.Json(CreateExportResponse(result, profile), jsonOptions);
        })
        .WithName("ExportAnalysis")
        .AddEndpointFilter(EndpointAuthorization.Require(UserPermission.ExportData))
        .Produces(200)
        .Produces(400)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Available export formats — wired to real profiles from AnalysisExportService
        group.MapGet(UiApiRoutes.ExportFormats, ([FromServices] AnalysisExportService? exportService) =>
        {
            if (exportService is null)
            {
                return Results.Problem(
                    "Export format capabilities are unavailable because the export service is not registered.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var availableFormats = exportService.GetProfiles()
                .Select(static profile => profile.Format)
                .Distinct()
                .Select(static format => format switch
                {
                    ExportFormat.Csv => new ExportFormatInfo
                    {
                        Name = "CSV",
                        Extension = ".csv",
                        Description = "Comma-separated values",
                        SupportsCompression = false
                    },
                    ExportFormat.Parquet => new ExportFormatInfo
                    {
                        Name = "Parquet",
                        Extension = ".parquet",
                        Description = "Apache Parquet columnar format",
                        SupportsCompression = false
                    },
                    ExportFormat.Xlsx => new ExportFormatInfo
                    {
                        Name = "Excel",
                        Extension = ".xlsx",
                        Description = "Microsoft Excel workbook",
                        SupportsCompression = false
                    },
                    ExportFormat.Arrow => new ExportFormatInfo
                    {
                        Name = "Apache Arrow IPC",
                        Extension = ".arrow",
                        Description = "Apache Arrow IPC format",
                        SupportsCompression = false
                    },
                    _ => null
                })
                .Where(static format => format is not null)
                .Select(static format => format!)
                .OrderBy(static format => format.Extension, StringComparer.Ordinal)
                .ToList();

            return Results.Json(new ExportFormatsResponse
            {
                Formats = availableFormats
            }, jsonOptions);
        })
        .WithName("GetExportFormats")
        .Produces<ExportFormatsResponse>(200)
        .Produces(503);

        // Quality report compatibility route — fail closed until an existing quality-report
        // generator is composed with canonical artifact evidence. A raw analysis extract is not
        // promoted as a completed quality report.
        group.MapPost(UiApiRoutes.ExportQualityReport, (
            QualityReportExportRequest? req) =>
        {
            return Results.Json(
                CreateUnavailableSpecializedExportResponse(
                    req?.Format,
                    "Quality-report generation is not connected to a canonical report artifact workflow. " +
                    "No raw analysis extract was produced."),
                jsonOptions,
                statusCode: StatusCodes.Status501NotImplemented);
        })
        .WithName("ExportQualityReport")
        .AddEndpointFilter(EndpointAuthorization.Require(UserPermission.ExportData))
        .Produces<SpecializedExportApiResponse>(StatusCodes.Status501NotImplemented)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Orderflow export — wired to real backend with Trade event type
        group.MapPost(UiApiRoutes.ExportOrderflow, async (
            OrderflowExportRequest? req,
            [FromServices] AnalysisExportService? exportService,
            CancellationToken ct) =>
        {
            CleanupOldExportDirectories();

            if (exportService is null)
            {
                return Results.Json(new { error = "Export service not available" }, jsonOptions, statusCode: 503);
            }

            if (!TryResolveProfile(
                    exportService,
                    req?.Format,
                    defaultProfileId: "python-pandas",
                    out var profile,
                    out var formatError))
            {
                return Results.Json(
                    new
                    {
                        success = false,
                        status = "invalid",
                        error = formatError,
                        timestamp = DateTimeOffset.UtcNow
                    },
                    jsonOptions,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var optionError = ValidateOrderflowRequest(req);
            if (optionError is not null)
            {
                return Results.Json(
                    CreateInvalidSpecializedExportResponse(req?.Format, optionError),
                    jsonOptions,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var outputDir = CreateExportOutputDirectory("orderflow");

            var exportRequest = new ExportRequest
            {
                ProfileId = profile!.Id,
                Symbols = req?.Symbols,
                StartDate = req?.FromDate ?? DateTime.UtcNow.AddDays(-7),
                EndDate = req?.ToDate ?? DateTime.UtcNow,
                OutputDirectory = outputDir,
                IncludeManifest = req?.IncludeMetadata ?? true,
                EventTypes = new[] { "Trade" }
            };

            var result = await exportService.ExportAsync(exportRequest, ct);

            return Results.Json(CreateSpecializedExportResponse(result, profile!), jsonOptions);
        })
        .WithName("ExportOrderflow")
        .AddEndpointFilter(EndpointAuthorization.Require(UserPermission.ExportData))
        .Produces(200)
        .Produces(400)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Integrity export — wired to real backend
        group.MapPost(UiApiRoutes.ExportIntegrity, async (
            IntegrityExportRequest? req,
            [FromServices] AnalysisExportService? exportService,
            CancellationToken ct) =>
        {
            CleanupOldExportDirectories();

            if (exportService is null)
            {
                return Results.Json(new { error = "Export service not available" }, jsonOptions, statusCode: 503);
            }

            if (!TryResolveProfile(
                    exportService,
                    req?.Format,
                    defaultProfileId: "r-stats",
                    out var profile,
                    out var formatError))
            {
                return Results.Json(
                    CreateInvalidSpecializedExportResponse(req?.Format, formatError!),
                    jsonOptions,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var optionError = ValidateIntegrityRequest(req);
            if (optionError is not null)
            {
                return Results.Json(
                    CreateInvalidSpecializedExportResponse(req?.Format, optionError),
                    jsonOptions,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var outputDir = CreateExportOutputDirectory("integrity");

            var exportRequest = new ExportRequest
            {
                ProfileId = profile!.Id,
                Symbols = req?.Symbols,
                StartDate = req?.FromDate ?? DateTime.UtcNow.AddDays(-7),
                EndDate = req?.ToDate ?? DateTime.UtcNow,
                OutputDirectory = outputDir,
                ValidateBeforeExport = true,
                IncludeManifest = req?.IncludeMetadata ?? true,
                EventTypes = req?.EventTypes is { Length: > 0 }
                    ? req.EventTypes
                    : new[] { "Integrity" }
            };

            var result = await exportService.ExportAsync(exportRequest, ct);

            return Results.Json(CreateSpecializedExportResponse(result, profile!), jsonOptions);
        })
        .WithName("ExportIntegrity")
        .AddEndpointFilter(EndpointAuthorization.Require(UserPermission.ExportData))
        .Produces(200)
        .Produces(400)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        async Task<IResult> ExportStrategyPackageAsync(
            StrategyPackageRequest? req,
            [FromServices] AnalysisExportService? exportService,
            CancellationToken ct)
        {
            CleanupOldExportDirectories();

            if (exportService is null)
            {
                return Results.Json(new { error = "Export service not available" }, jsonOptions, statusCode: 503);
            }

            if (!TryResolveProfile(
                    exportService,
                    req?.Format,
                    defaultProfileId: "python-pandas",
                    out var profile,
                    out var formatError))
            {
                return Results.Json(
                    new
                    {
                        success = false,
                        status = "invalid",
                        error = formatError,
                        timestamp = DateTimeOffset.UtcNow
                    },
                    jsonOptions,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var optionError = ValidateStrategyPackageRequest(req);
            if (optionError is not null)
            {
                return Results.Json(
                    CreateInvalidSpecializedExportResponse(req?.Format, optionError),
                    jsonOptions,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var outputDir = CreateExportOutputDirectory("strategy");

            var exportRequest = new ExportRequest
            {
                ProfileId = profile!.Id,
                Symbols = req?.Symbols,
                StartDate = req?.FromDate ?? DateTime.UtcNow.AddDays(-7),
                EndDate = req?.ToDate ?? DateTime.UtcNow,
                OutputDirectory = outputDir,
                EventTypes = ResolveStrategyEventTypes(req?.IncludeData),
                ValidateBeforeExport = true,
                IncludeManifest = req?.IncludeMetadata ?? true
            };

            var result = await exportService.ExportAsync(exportRequest, ct);

            return Results.Json(CreateSpecializedExportResponse(result, profile!), jsonOptions);
        }

        // Strategy package export is canonical; the research route is retained for clients still on the old API name.
        group.MapPost(UiApiRoutes.ExportStrategyPackage, ExportStrategyPackageAsync)
        .WithName("ExportStrategyPackage")
        .AddEndpointFilter(EndpointAuthorization.Require(UserPermission.ExportData))
        .Produces(200)
        .Produces(400)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(UiApiRoutes.ExportResearchPackage, ExportStrategyPackageAsync)
        .WithName("ExportResearchPackage")
        .AddEndpointFilter(EndpointAuthorization.Require(UserPermission.ExportData))
        .Produces(200)
        .Produces(400)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private sealed record ExportPreviewRequest(string? ProfileId, string[]? Symbols, string[]? EventTypes, DateTime? StartDate, DateTime? EndDate, int? SampleSize);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record QualityReportExportRequest(
        string? Format,
        string[]? Symbols,
        DateTime? FromDate,
        DateTime? ToDate,
        bool? IncludeCharts,
        bool? IncludeMetadata);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record OrderflowExportRequest(
        string[]? Symbols,
        DateTime? FromDate,
        DateTime? ToDate,
        string[]? Metrics,
        string? Aggregation,
        string? Format,
        string? OutputPath,
        bool? IncludeMetadata);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record IntegrityExportRequest(
        string[]? Symbols,
        DateTime? FromDate,
        DateTime? ToDate,
        string[]? EventTypes,
        string? Format,
        string? OutputPath,
        bool? IncludeMetadata);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record StrategyPackageRequest(
        string? Name,
        string? Description,
        string[]? Symbols,
        DateTime? FromDate,
        DateTime? ToDate,
        DataTypeInclusion? IncludeData,
        bool? IncludeMetadata,
        bool? IncludeQualityReport,
        string? Format,
        string? OutputPath);

    private static string[] SplitCsv(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();

        return value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static ExportAnalysisApiRequest NormalizeExportAnalysisRequest(ExportAnalysisApiRequest? request)
    {
        var profileId = string.IsNullOrWhiteSpace(request?.ProfileId)
            ? "python-pandas"
            : request.ProfileId.Trim();

        var symbols = request?.Symbols?
            .Select(static symbol => symbol?.Trim())
            .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ExportAnalysisApiRequest(
            profileId,
            symbols is { Length: > 0 } ? symbols! : null,
            request?.Format?.Trim(),
            request?.StartDate,
            request?.EndDate);
    }

    private static ExportAnalysisApiResponse CreateUnavailableExportResponse(string profileId)
        => new(
            JobId: null,
            Success: false,
            Status: "unavailable",
            ProfileId: profileId,
            Symbols: null,
            FilesGenerated: 0,
            TotalRecords: 0,
            TotalBytes: 0,
            OutputDirectory: null,
            DurationSeconds: 0,
            Error: "Export service not available",
            Warnings: Array.Empty<string>(),
            Files: Array.Empty<ExportAnalysisApiFile>(),
            Timestamp: DateTimeOffset.UtcNow);

    private static ExportAnalysisApiResponse CreateInvalidExportResponse(string profileId, string error)
        => new(
            JobId: null,
            Success: false,
            Status: "invalid",
            ProfileId: profileId,
            Symbols: null,
            FilesGenerated: 0,
            TotalRecords: 0,
            TotalBytes: 0,
            OutputDirectory: null,
            DurationSeconds: 0,
            Error: error,
            Warnings: Array.Empty<string>(),
            Files: Array.Empty<ExportAnalysisApiFile>(),
            Timestamp: DateTimeOffset.UtcNow);

    internal static ExportAnalysisApiResponse CreateExportResponse(
        ExportResult result,
        ExportProfile profile)
    {
        var formatEvidenceError = ValidateArtifactFormatEvidence(result, profile);
        var success = result.Success && formatEvidenceError is null;

        return new ExportAnalysisApiResponse(
            JobId: result.JobId,
            Success: success,
            Status: success ? "completed" : "failed",
            ProfileId: result.ProfileId,
            Symbols: result.Symbols,
            FilesGenerated: result.FilesGenerated,
            TotalRecords: result.TotalRecords,
            TotalBytes: result.TotalBytes,
            OutputDirectory: result.OutputDirectory,
            DurationSeconds: result.DurationSeconds,
            Error: formatEvidenceError ?? result.Error,
            Warnings: result.Warnings?.ToArray() ?? Array.Empty<string>(),
            Files: result.Files.Select(static f => new ExportAnalysisApiFile(
                f.RelativePath,
                f.Symbol,
                f.Format,
                f.SizeBytes,
                f.RecordCount)).ToArray(),
            Timestamp: DateTimeOffset.UtcNow);
    }

    private static SpecializedExportApiResponse CreateSpecializedExportResponse(
        ExportResult result,
        ExportProfile profile)
    {
        var formatEvidenceError = ValidateArtifactFormatEvidence(result, profile);
        var success = result.Success && formatEvidenceError is null;

        return new SpecializedExportApiResponse(
            JobId: result.JobId,
            Success: success,
            Status: success ? "completed" : "failed",
            Format: ResolveActualFormat(result, profile),
            Symbols: result.Symbols,
            FilesGenerated: result.FilesGenerated,
            TotalRecords: result.TotalRecords,
            TotalBytes: result.TotalBytes,
            OutputDirectory: result.OutputDirectory,
            Error: formatEvidenceError ?? result.Error,
            Warnings: result.Warnings?.ToArray() ?? Array.Empty<string>(),
            Files: result.Files.Select(static file => new ExportAnalysisApiFile(
                file.RelativePath,
                file.Symbol,
                file.Format,
                file.SizeBytes,
                file.RecordCount)).ToArray(),
            DataDictionaryPath: result.DataDictionaryPath,
            LoaderScriptPath: result.LoaderScriptPath,
            LineageManifestPath: result.LineageManifestPath,
            QualitySummary: MapQualitySummary(result),
            Timestamp: DateTimeOffset.UtcNow);
    }

    private static SpecializedExportApiResponse CreateInvalidSpecializedExportResponse(
        string? requestedFormat,
        string error)
    {
        var format = !string.IsNullOrWhiteSpace(requestedFormat) &&
                     TryParseExportFormat(requestedFormat, out var parsedFormat)
            ? ToCanonicalFormat(parsedFormat)
            : requestedFormat?.Trim().ToLowerInvariant() ?? string.Empty;

        return new SpecializedExportApiResponse(
            JobId: null,
            Success: false,
            Status: "invalid",
            Format: format,
            Symbols: null,
            FilesGenerated: 0,
            TotalRecords: 0,
            TotalBytes: 0,
            OutputDirectory: null,
            Error: error,
            Warnings: Array.Empty<string>(),
            Files: Array.Empty<ExportAnalysisApiFile>(),
            DataDictionaryPath: null,
            LoaderScriptPath: null,
            LineageManifestPath: null,
            QualitySummary: null,
            Timestamp: DateTimeOffset.UtcNow);
    }

    private static SpecializedExportApiResponse CreateUnavailableSpecializedExportResponse(
        string? requestedFormat,
        string error)
    {
        var format = !string.IsNullOrWhiteSpace(requestedFormat) &&
                     TryParseExportFormat(requestedFormat, out var parsedFormat)
            ? ToCanonicalFormat(parsedFormat)
            : requestedFormat?.Trim().ToLowerInvariant() ?? string.Empty;

        return new SpecializedExportApiResponse(
            JobId: null,
            Success: false,
            Status: "unavailable",
            Format: format,
            Symbols: null,
            FilesGenerated: 0,
            TotalRecords: 0,
            TotalBytes: 0,
            OutputDirectory: null,
            Error: error,
            Warnings: Array.Empty<string>(),
            Files: Array.Empty<ExportAnalysisApiFile>(),
            DataDictionaryPath: null,
            LoaderScriptPath: null,
            LineageManifestPath: null,
            QualitySummary: null,
            Timestamp: DateTimeOffset.UtcNow);
    }

    private static QualityReportSummary? MapQualitySummary(ExportResult result)
    {
        if (result.QualitySummary is not { } summary)
            return null;

        return new QualityReportSummary
        {
            TotalSymbols = result.Symbols.Length,
            TotalDays = result.DateRange?.TradingDays ?? 0,
            OverallScore = (float)summary.OverallScore,
            GapsFound = summary.GapsDetected,
            AnomaliesFound = summary.OutliersDetected
        };
    }

    private static string CreateExportOutputDirectory(string prefix)
        => Path.Combine(
            ExportBaseDir,
            $"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}");

    private static string? ValidateOrderflowRequest(OrderflowExportRequest? request)
    {
        if (request?.Metrics is { Length: > 0 })
            return "Order-flow metric selection is not represented by the canonical export workflow.";

        if (!string.IsNullOrWhiteSpace(request?.Aggregation) &&
            !string.Equals(request.Aggregation, "raw", StringComparison.OrdinalIgnoreCase))
        {
            return "Order-flow aggregation is unsupported by the canonical export workflow; request raw data instead.";
        }

        if (!string.IsNullOrWhiteSpace(request?.OutputPath))
            return "Caller-selected outputPath is unsupported; exports are written to the managed export directory.";

        if (request?.IncludeMetadata is false)
            return "The order-flow compatibility route cannot suppress every profile metadata artifact. IncludeMetadata=false is unsupported.";

        return ValidateDateRange(request?.FromDate, request?.ToDate);
    }

    private static string? ValidateIntegrityRequest(IntegrityExportRequest? request)
    {
        if (!string.IsNullOrWhiteSpace(request?.OutputPath))
            return "Caller-selected outputPath is unsupported; exports are written to the managed export directory.";

        if (request?.IncludeMetadata is false)
            return "The integrity compatibility route cannot suppress every profile metadata artifact. IncludeMetadata=false is unsupported.";

        return ValidateDateRange(request?.FromDate, request?.ToDate);
    }

    private static string? ValidateStrategyPackageRequest(StrategyPackageRequest? request)
    {
        var unsupported = new List<string>();
        if (!string.IsNullOrWhiteSpace(request?.Name))
            unsupported.Add("name");
        if (!string.IsNullOrWhiteSpace(request?.Description))
            unsupported.Add("description");
        if (request?.IncludeQualityReport is true)
            unsupported.Add("includeQualityReport=true");
        if (request?.IncludeMetadata is false)
            unsupported.Add("includeMetadata=false");
        if (!string.IsNullOrWhiteSpace(request?.OutputPath))
            unsupported.Add("outputPath");

        if (unsupported.Count > 0)
        {
            return "The canonical export workflow cannot represent these strategy-package options: " +
                   string.Join(", ", unsupported) +
                   ". No export was requested.";
        }

        if (request?.IncludeData is { } includeData &&
            !includeData.Trades &&
            !includeData.Quotes &&
            !includeData.Bars &&
            !includeData.OrderBook &&
            !includeData.OrderFlow)
        {
            return "At least one strategy-package data type must be selected.";
        }

        return ValidateDateRange(request?.FromDate, request?.ToDate);
    }

    private static string? ValidateDateRange(DateTime? start, DateTime? end)
        => start.HasValue && end.HasValue && start.Value.Date > end.Value.Date
            ? "The export start date must be on or before the end date."
            : null;

    private static string[] ResolveStrategyEventTypes(DataTypeInclusion? requested)
    {
        var includeData = requested ?? new DataTypeInclusion();
        var eventTypes = new List<string>();
        if (includeData.Trades)
            eventTypes.Add("Trade");
        if (includeData.Quotes)
            eventTypes.Add("BboQuote");
        if (includeData.Bars)
            eventTypes.Add("Bar");
        if (includeData.OrderBook)
            eventTypes.Add("LOBSnapshot");
        if (includeData.OrderFlow)
            eventTypes.Add("OrderFlow");

        return eventTypes.ToArray();
    }

    private static ExportProfile? FindProfile(AnalysisExportService exportService, string profileId)
        => exportService.GetProfiles().FirstOrDefault(profile =>
            string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase));

    private static bool TryResolveProfile(
        AnalysisExportService exportService,
        string? requestedFormat,
        string defaultProfileId,
        out ExportProfile? profile,
        out string? error)
    {
        if (string.IsNullOrWhiteSpace(requestedFormat))
        {
            profile = FindProfile(exportService, defaultProfileId);
            error = profile is null
                ? $"Required export profile '{defaultProfileId}' is not registered."
                : null;
            return profile is not null;
        }

        if (!TryParseExportFormat(requestedFormat, out var format))
        {
            profile = null;
            error = $"Unsupported export format: {requestedFormat.Trim()}";
            return false;
        }

        profile = exportService.GetProfiles().FirstOrDefault(candidate => candidate.Format == format);
        error = profile is null
            ? $"No registered export profile produces '{ToCanonicalFormat(format)}'."
            : null;
        return profile is not null;
    }

    private static bool TryParseExportFormat(string value, out ExportFormat format)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "parquet":
                format = ExportFormat.Parquet;
                return true;
            case "csv":
                format = ExportFormat.Csv;
                return true;
            case "jsonl":
            case "json-lines":
            case "jsonlines":
                format = ExportFormat.Jsonl;
                return true;
            case "lean":
                format = ExportFormat.Lean;
                return true;
            case "xlsx":
            case "excel":
                format = ExportFormat.Xlsx;
                return true;
            case "sql":
                format = ExportFormat.Sql;
                return true;
            case "arrow":
            case "feather":
                format = ExportFormat.Arrow;
                return true;
            default:
                format = default;
                return false;
        }
    }

    private static string ResolveActualFormat(ExportResult result, ExportProfile profile)
    {
        var formats = result.Files
            .Select(file => file.Format)
            .Where(format => !string.IsNullOrWhiteSpace(format))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return formats.Length switch
        {
            0 => ToCanonicalFormat(profile.Format),
            1 => formats[0],
            _ => "mixed"
        };
    }

    private static string? ValidateArtifactFormatEvidence(
        ExportResult result,
        ExportProfile profile)
    {
        if (!result.Success)
            return null;

        var expected = ToCanonicalFormat(profile.Format);
        if (result.Files.Length == 0)
            return $"Export profile '{profile.Id}' reported success without any artifact format evidence.";
        if (result.FilesGenerated != result.Files.Length)
        {
            return $"Export profile '{profile.Id}' reported {result.FilesGenerated} generated files but " +
                   $"provided {result.Files.Length} artifact records.";
        }

        var missingFormat = result.Files.FirstOrDefault(
            static file => string.IsNullOrWhiteSpace(file.Format));
        if (missingFormat is not null)
            return $"Export artifact '{missingFormat.RelativePath}' did not identify its generated format.";

        var formats = result.Files
            .Select(static file => file.Format)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (formats.Length > 1)
        {
            return $"Export profile '{profile.Id}' expected '{expected}' but the artifact evidence " +
                   $"reported mixed formats: {string.Join(", ", formats)}.";
        }

        var mismatch = result.Files.FirstOrDefault(
            file => !string.Equals(file.Format, expected, StringComparison.OrdinalIgnoreCase));
        return mismatch is null
            ? null
            : $"Export profile '{profile.Id}' expected '{expected}' but artifact " +
              $"'{mismatch.RelativePath}' reported '{mismatch.Format}'.";
    }

    private static string ToCanonicalFormat(ExportFormat format) =>
        format switch
        {
            ExportFormat.Parquet => "parquet",
            ExportFormat.Csv => "csv",
            ExportFormat.Jsonl => "jsonl",
            ExportFormat.Lean => "lean",
            ExportFormat.Xlsx => "xlsx",
            ExportFormat.Sql => "sql",
            ExportFormat.Arrow => "arrow",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown export format.")
        };

    /// <summary>
    /// Removes export directories older than <see cref="ExportMaxAge"/> to prevent unbounded disk usage.
    /// </summary>
    private static void CleanupOldExportDirectories()
    {
        try
        {
            if (!Directory.Exists(ExportBaseDir))
                return;

            foreach (var dir in Directory.EnumerateDirectories(ExportBaseDir))
            {
                try
                {
                    var created = Directory.GetCreationTimeUtc(dir);
                    if (DateTime.UtcNow - created > ExportMaxAge)
                    {
                        Directory.Delete(dir, recursive: true);
                    }
                }
                catch (IOException)
                {
                    // Directory may be in use or already deleted
                }
                catch (UnauthorizedAccessException)
                {
                    // Insufficient permissions to delete
                }
            }
        }
        catch (IOException)
        {
            // Base directory inaccessible, skip cleanup
        }
    }
}

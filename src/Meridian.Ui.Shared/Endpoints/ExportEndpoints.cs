using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Export;
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

            return Results.Json(CreateExportResponse(result), jsonOptions);
        })
        .WithName("ExportAnalysis")
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

        // Quality report export — wired to real backend
        group.MapPost(UiApiRoutes.ExportQualityReport, async (
            QualityReportExportRequest? req,
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

            var outputDir = Path.Combine(Path.GetTempPath(), "meridian-exports", "quality-" + DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture));

            var exportRequest = new ExportRequest
            {
                ProfileId = profile!.Id,
                Symbols = req?.Symbols,
                OutputDirectory = outputDir,
                ValidateBeforeExport = true,
                EventTypes = new[] { "Trade", "BboQuote" }
            };

            var result = await exportService.ExportAsync(exportRequest, ct);

            return Results.Json(new
            {
                jobId = result.JobId,
                success = result.Success,
                status = result.Success ? "completed" : "failed",
                format = ResolveActualFormat(result, profile!),
                filesGenerated = result.FilesGenerated,
                totalRecords = result.TotalRecords,
                outputDirectory = result.OutputDirectory,
                qualitySummary = result.QualitySummary,
                error = result.Error,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("ExportQualityReport")
        .Produces(200)
        .Produces(400)
        .Produces(503)
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

            var outputDir = Path.Combine(Path.GetTempPath(), "meridian-exports", "orderflow-" + DateTime.UtcNow.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture));

            var exportRequest = new ExportRequest
            {
                ProfileId = profile!.Id,
                Symbols = req?.Symbols,
                OutputDirectory = outputDir,
                EventTypes = new[] { "Trade" }
            };

            var result = await exportService.ExportAsync(exportRequest, ct);

            return Results.Json(new
            {
                jobId = result.JobId,
                success = result.Success,
                status = result.Success ? "completed" : "failed",
                symbols = result.Symbols,
                format = ResolveActualFormat(result, profile!),
                filesGenerated = result.FilesGenerated,
                totalRecords = result.TotalRecords,
                totalBytes = result.TotalBytes,
                outputDirectory = result.OutputDirectory,
                error = result.Error,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("ExportOrderflow")
        .Produces(200)
        .Produces(400)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Integrity export — wired to real backend
        group.MapPost(UiApiRoutes.ExportIntegrity, async (
            [FromServices] AnalysisExportService? exportService,
            CancellationToken ct) =>
        {
            CleanupOldExportDirectories();

            if (exportService is null)
            {
                return Results.Json(new { error = "Export service not available" }, jsonOptions, statusCode: 503);
            }

            var outputDir = Path.Combine(Path.GetTempPath(), "meridian-exports", "integrity-" + DateTime.UtcNow.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture));

            var exportRequest = new ExportRequest
            {
                ProfileId = "r-stats",
                ValidateBeforeExport = true
            };

            var result = await exportService.ExportAsync(exportRequest, ct);

            return Results.Json(new
            {
                jobId = result.JobId,
                success = result.Success,
                status = result.Success ? "completed" : "failed",
                format = "csv",
                filesGenerated = result.FilesGenerated,
                totalRecords = result.TotalRecords,
                outputDirectory = result.OutputDirectory,
                error = result.Error,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("ExportIntegrity")
        .Produces(200)
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

            var outputDir = Path.Combine(Path.GetTempPath(), "meridian-exports", "strategy-" + DateTime.UtcNow.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture));

            var exportRequest = new ExportRequest
            {
                ProfileId = profile!.Id,
                Symbols = req?.Symbols,
                OutputDirectory = outputDir,
                EventTypes = new[] { "Trade", "BboQuote", "LOBSnapshot" },
                ValidateBeforeExport = req?.IncludeMetadata ?? true
            };

            var result = await exportService.ExportAsync(exportRequest, ct);

            return Results.Json(new
            {
                jobId = result.JobId,
                success = result.Success,
                status = result.Success ? "completed" : "failed",
                symbols = result.Symbols,
                format = ResolveActualFormat(result, profile!),
                filesGenerated = result.FilesGenerated,
                totalRecords = result.TotalRecords,
                totalBytes = result.TotalBytes,
                outputDirectory = result.OutputDirectory,
                dataDictionaryPath = result.DataDictionaryPath,
                loaderScriptPath = result.LoaderScriptPath,
                error = result.Error,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        }

        // Strategy package export is canonical; the research route is retained for clients still on the old API name.
        group.MapPost(UiApiRoutes.ExportStrategyPackage, ExportStrategyPackageAsync)
        .WithName("ExportStrategyPackage")
        .Produces(200)
        .Produces(400)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(UiApiRoutes.ExportResearchPackage, ExportStrategyPackageAsync)
        .WithName("ExportResearchPackage")
        .Produces(200)
        .Produces(400)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private sealed record ExportPreviewRequest(string? ProfileId, string[]? Symbols, string[]? EventTypes, DateTime? StartDate, DateTime? EndDate, int? SampleSize);
    private sealed record QualityReportExportRequest(string? Format, string[]? Symbols);
    private sealed record OrderflowExportRequest(string[]? Symbols, string? Format);
    private sealed record StrategyPackageRequest(string[]? Symbols, bool? IncludeMetadata, string? Format);

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

    private static ExportAnalysisApiResponse CreateExportResponse(ExportResult result)
        => new(
            JobId: result.JobId,
            Success: result.Success,
            Status: result.Success ? "completed" : "failed",
            ProfileId: result.ProfileId,
            Symbols: result.Symbols,
            FilesGenerated: result.FilesGenerated,
            TotalRecords: result.TotalRecords,
            TotalBytes: result.TotalBytes,
            OutputDirectory: result.OutputDirectory,
            DurationSeconds: result.DurationSeconds,
            Error: result.Error,
            Warnings: result.Warnings?.ToArray() ?? Array.Empty<string>(),
            Files: result.Files.Select(static f => new ExportAnalysisApiFile(
                f.RelativePath,
                f.Symbol,
                f.Format,
                f.SizeBytes,
                f.RecordCount)).ToArray(),
            Timestamp: DateTimeOffset.UtcNow);

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

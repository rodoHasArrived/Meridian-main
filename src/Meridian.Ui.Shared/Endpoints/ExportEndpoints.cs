using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Api;
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

        // Analysis export — wired to real AnalysisExportService
        group.MapPost(UiApiRoutes.ExportAnalysis, async (
            ExportAnalysisRequest req,
            [FromServices] AnalysisExportService? exportService,
            CancellationToken ct) =>
        {
            if (exportService is null)
            {
                return Results.Json(new { error = "Export service not available" }, jsonOptions, statusCode: 503);
            }

            var outputDir = Path.Combine(
                Path.GetTempPath(),
                "meridian-exports",
                $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}");

            var exportRequest = new ExportRequest
            {
                ProfileId = req.ProfileId ?? "python-pandas",
                Symbols = req.Symbols,
                StartDate = req.StartDate ?? DateTime.UtcNow.AddDays(-7),
                EndDate = req.EndDate ?? DateTime.UtcNow,
                OutputDirectory = outputDir,
                EventTypes = new[] { "Trade", "BboQuote" }
            };

            var result = await exportService.ExportAsync(exportRequest, ct);

            return Results.Json(new
            {
                jobId = result.JobId,
                success = result.Success,
                status = result.Success ? "completed" : "failed",
                profileId = result.ProfileId,
                symbols = result.Symbols,
                filesGenerated = result.FilesGenerated,
                totalRecords = result.TotalRecords,
                totalBytes = result.TotalBytes,
                outputDirectory = result.OutputDirectory,
                durationSeconds = result.DurationSeconds,
                error = result.Error,
                warnings = result.Warnings,
                files = result.Files.Select(f => new
                {
                    path = f.RelativePath,
                    symbol = f.Symbol,
                    format = f.Format,
                    sizeBytes = f.SizeBytes,
                    recordCount = f.RecordCount
                }),
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("ExportAnalysis")
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Available export formats — wired to real profiles from AnalysisExportService
        group.MapGet(UiApiRoutes.ExportFormats, ([FromServices] AnalysisExportService? exportService) =>
        {
            var formats = new[]
            {
                new { id = "parquet", name = "Apache Parquet", description = "Columnar format for analytics (Python/pandas, Spark)", extensions = new[] { ".parquet" } },
                new { id = "csv", name = "CSV", description = "Comma-separated values (Excel, R, SQL)", extensions = new[] { ".csv", ".csv.gz" } },
                new { id = "jsonl", name = "JSON Lines", description = "One JSON object per line (streaming, interchange)", extensions = new[] { ".jsonl", ".jsonl.gz" } },
                new { id = "lean", name = "QuantConnect Lean", description = "Native Lean Engine format for backtesting", extensions = new[] { ".zip" } },
                new { id = "xlsx", name = "Microsoft Excel", description = "Excel workbook with formatted sheets", extensions = new[] { ".xlsx" } },
                new { id = "sql", name = "SQL", description = "SQL INSERT/COPY statements for databases", extensions = new[] { ".sql" } },
                new { id = "arrow", name = "Apache Arrow IPC", description = "In-memory columnar format for zero-copy interchange", extensions = new[] { ".arrow" } }
            };

            // Pull real profiles from the service if available
            var profiles = exportService?.GetProfiles()
                .Select(p => new { id = p.Id, name = p.Name, format = p.Format.ToString().ToLowerInvariant(), compression = p.Compression.Type.ToString().ToLowerInvariant() })
                .ToArray()
                ?? new[]
                {
                    new { id = "python-pandas", name = "Python / Pandas", format = "parquet", compression = "snappy" },
                    new { id = "r-stats", name = "R / data.frame", format = "csv", compression = "none" },
                    new { id = "quantconnect-lean", name = "QuantConnect Lean", format = "lean", compression = "zip" },
                    new { id = "excel", name = "Microsoft Excel", format = "xlsx", compression = "none" },
                    new { id = "postgresql", name = "PostgreSQL / TimescaleDB", format = "csv", compression = "none" }
                };

            return Results.Json(new
            {
                formats,
                profiles,
                serviceAvailable = exportService is not null,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetExportFormats")
        .Produces(200);

        // Quality report export — wired to real backend
        group.MapPost(UiApiRoutes.ExportQualityReport, async (
            QualityReportExportRequest? req,
            [FromServices] AnalysisExportService? exportService,
            CancellationToken ct) =>
        {
            if (exportService is null)
            {
                return Results.Json(new { error = "Export service not available" }, jsonOptions, statusCode: 503);
            }

            var outputDir = Path.Combine(Path.GetTempPath(), "meridian-exports", "quality-" + DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture));

            var exportRequest = new ExportRequest
            {
                ProfileId = req?.Format == "csv" ? "r-stats" : "python-pandas",
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
                format = req?.Format ?? "csv",
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
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Orderflow export — wired to real backend with Trade event type
        group.MapPost(UiApiRoutes.ExportOrderflow, async (
            OrderflowExportRequest? req,
            [FromServices] AnalysisExportService? exportService,
            CancellationToken ct) =>
        {
            if (exportService is null)
            {
                return Results.Json(new { error = "Export service not available" }, jsonOptions, statusCode: 503);
            }

            var outputDir = Path.Combine(Path.GetTempPath(), "meridian-exports", "orderflow-" + DateTime.UtcNow.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture));

            var formatProfile = (req?.Format ?? "parquet") switch
            {
                "csv" => "r-stats",
                "jsonl" => "python-pandas",
                _ => "python-pandas"
            };

            var exportRequest = new ExportRequest
            {
                ProfileId = formatProfile,
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
                format = req?.Format ?? "parquet",
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
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Integrity export — wired to real backend
        group.MapPost(UiApiRoutes.ExportIntegrity, async (
            [FromServices] AnalysisExportService? exportService,
            CancellationToken ct) =>
        {
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

        // Research package export — wired to real backend
        group.MapPost(UiApiRoutes.ExportResearchPackage, async (
            ResearchPackageRequest? req,
            [FromServices] AnalysisExportService? exportService,
            CancellationToken ct) =>
        {
            if (exportService is null)
            {
                return Results.Json(new { error = "Export service not available" }, jsonOptions, statusCode: 503);
            }

            var outputDir = Path.Combine(Path.GetTempPath(), "meridian-exports", "research-" + DateTime.UtcNow.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture));

            var exportRequest = new ExportRequest
            {
                ProfileId = "python-pandas",
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
                filesGenerated = result.FilesGenerated,
                totalRecords = result.TotalRecords,
                totalBytes = result.TotalBytes,
                outputDirectory = result.OutputDirectory,
                dataDictionaryPath = result.DataDictionaryPath,
                loaderScriptPath = result.LoaderScriptPath,
                error = result.Error,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("ExportResearchPackage")
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private sealed record ExportAnalysisRequest(string? ProfileId, string[]? Symbols, string? Format, DateTime? StartDate, DateTime? EndDate);
    private sealed record ExportPreviewRequest(string? ProfileId, string[]? Symbols, string[]? EventTypes, DateTime? StartDate, DateTime? EndDate, int? SampleSize);
    private sealed record QualityReportExportRequest(string? Format, string[]? Symbols);
    private sealed record OrderflowExportRequest(string[]? Symbols, string? Format);
    private sealed record ResearchPackageRequest(string[]? Symbols, bool? IncludeMetadata);

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

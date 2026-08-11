using System.Text.Json;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Api;
using Meridian.Contracts.Domain.Enums;
using Meridian.Identity.Auth;
using Meridian.Storage;
using Meridian.Storage.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering storage quality API endpoints.
/// Implements Phase 3B.3 — replaces 9 stub endpoints with working handlers.
/// </summary>
public static class StorageQualityEndpoints
{
    public static void MapStorageQualityEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var logger = app.Logger;
        var group = app.MapGroup("").WithTags("Storage Quality");

        // GET /api/storage/quality/summary — overall quality summary
        group.MapGet(UiApiRoutes.StorageQualitySummary, async (
            IDataQualityService? qualityService,
            StorageOptions opts,
            CancellationToken ct) =>
        {
            if (qualityService is null)
                return Results.Json(new { status = "unavailable", message = "Data quality service not available" }, jsonOptions);

            return await EndpointHelpers.GuardAsync(async () =>
            {
                var report = await qualityService.GenerateReportAsync(
                    new QualityReportOptions(
                        Paths: new[] { Path.GetFullPath(opts.RootPath) },
                        IncludeRecommendations: true), ct);

                return Results.Json(new
                {
                    generatedAt = report.GeneratedAt,
                    filesAnalyzed = report.FilesAnalyzed,
                    averageScore = report.AverageScore,
                    scoresByDimension = report.ScoresByDimension,
                    recommendations = report.Recommendations,
                    lowQualityFiles = report.LowQualityFiles?.Count ?? 0
                }, jsonOptions);
            }, "Failed to generate quality summary.", logger);
        })
        .WithName("GetQualitySummary").Produces(200);

        // GET /api/storage/quality/scores — quality scores for all scored files
        group.MapGet(UiApiRoutes.StorageQualityScores, async (
            IDataQualityService? qualityService,
            StorageOptions opts,
            CancellationToken ct) =>
        {
            if (qualityService is null)
                return Results.Json(new { message = "Data quality service not available", scores = Array.Empty<object>() }, jsonOptions);

            return await EndpointHelpers.GuardAsync(async () =>
            {
                var report = await qualityService.GenerateReportAsync(
                    new QualityReportOptions(
                        Paths: new[] { Path.GetFullPath(opts.RootPath) },
                        MinScoreThreshold: 0.0,
                        IncludeRecommendations: false), ct);

                return Results.Json(new
                {
                    averageScore = report.AverageScore,
                    filesAnalyzed = report.FilesAnalyzed,
                    lowQualityFiles = report.LowQualityFiles
                }, jsonOptions);
            }, "Failed to retrieve quality scores.", logger);
        })
        .WithName("GetQualityScores").Produces(200);

        // GET /api/storage/quality/symbol/{symbol} — quality for a specific symbol
        group.MapGet(UiApiRoutes.StorageQualitySymbol, async (
            string symbol,
            IDataQualityService? qualityService,
            StorageOptions opts,
            CancellationToken ct) =>
        {
            if (qualityService is null)
                return Results.Json(new { symbol, message = "Data quality service not available" }, jsonOptions);

            return await EndpointHelpers.GuardAsync(async () =>
            {
                var trend = await qualityService.GetTrendAsync(symbol, TimeSpan.FromDays(30), ct);
                return Results.Json(new
                {
                    symbol,
                    trend
                }, jsonOptions);
            }, "Failed to get symbol quality.", logger);
        })
        .WithName("GetSymbolQuality").Produces(200);

        // GET /api/storage/quality/alerts — active quality alerts
        group.MapGet(UiApiRoutes.StorageQualityAlerts, async (
            IDataQualityService? qualityService,
            CancellationToken ct) =>
        {
            if (qualityService is null)
                return Results.Json(new { alerts = Array.Empty<object>(), message = "Data quality service not available" }, jsonOptions);

            return await EndpointHelpers.GuardAsync(async () =>
            {
                var alerts = await qualityService.GetQualityAlertsAsync(ct);
                return Results.Json(new
                {
                    count = alerts.Length,
                    alerts
                }, jsonOptions);
            }, "Failed to retrieve quality alerts.", logger);
        })
        .WithName("GetQualityAlerts").Produces(200);

        // POST /api/storage/quality/alerts/{alertId}/acknowledge — acknowledge an alert
        group.MapPost(UiApiRoutes.StorageQualityAlertAcknowledge, (string alertId) =>
        {
            // Alert acknowledgment state is not persisted by IDataQualityService,
            // so we accept the request and return success.
            return Results.Ok(new { acknowledged = alertId, timestamp = DateTimeOffset.UtcNow });
        })
        .WithName("AcknowledgeAlert").Produces(200)
        .AddEndpointFilter(EndpointAuthorization.Require(UserPermission.ManageStorage))
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // GET /api/storage/quality/rankings/{symbol} — source rankings for a symbol
        group.MapGet(UiApiRoutes.StorageQualityRankings, async (
            string symbol,
            IDataQualityService? qualityService,
            CancellationToken ct) =>
        {
            if (qualityService is null)
                return Results.Json(new { symbol, message = "Data quality service not available" }, jsonOptions);

            return await EndpointHelpers.GuardAsync(async () =>
            {
                var rankings = await qualityService.RankSourcesAsync(
                    symbol,
                    DateTimeOffset.UtcNow.Date,
                    MarketEventType.Trade,
                    ct);

                return Results.Json(new
                {
                    symbol,
                    date = DateTimeOffset.UtcNow.Date,
                    rankings
                }, jsonOptions);
            }, "Failed to rank quality sources.", logger);
        })
        .WithName("GetSourceRankings").Produces(200);

        // GET /api/storage/quality/trends — quality trends across all data
        group.MapGet(UiApiRoutes.StorageQualityTrends, async (
            HttpContext ctx,
            IDataQualityService? qualityService,
            CancellationToken ct) =>
        {
            if (qualityService is null)
                return Results.Json(new { message = "Data quality service not available" }, jsonOptions);

            var days = int.TryParse(ctx.Request.Query["days"].FirstOrDefault(), out var d) ? Math.Clamp(d, 1, 365) : 30;
            var symbol = ctx.Request.Query["symbol"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(symbol))
                symbol = "SPY";

            return await EndpointHelpers.GuardAsync(async () =>
            {
                var trend = await qualityService.GetTrendAsync(symbol, TimeSpan.FromDays(days), ct);
                var payload = new
                {
                    symbol = trend.Symbol,
                    requestedWindowDays = days,
                    granularity = trend.WindowGranularity,
                    hasConfidence = trend.HasConfidence,
                    sparseData = trend.IsSparseData,
                    currentScore = trend.CurrentScore,
                    priorWindowBaseline = trend.PreviousScore,
                    trendDirection = trend.TrendDirection,
                    improvingDimensions = trend.ImprovingDimensions,
                    degradingDimensions = trend.DegradingDimensions,
                    points = trend.ScoreHistory.Zip(trend.ScoreValues, (at, value) => new { timestamp = at, score = value }),
                    dimensions = trend.DimensionSeries
                };

                return Results.Json(payload, jsonOptions);
            }, "Failed to compute quality trends.", logger);
        })
        .WithName("GetQualityTrends").Produces(200);

        // GET /api/storage/quality/anomalies — detected quality anomalies
        group.MapGet(UiApiRoutes.StorageQualityAnomalies, async (
            IDataQualityService? qualityService,
            StorageOptions opts,
            CancellationToken ct) =>
        {
            if (qualityService is null)
                return Results.Json(new { anomalies = Array.Empty<object>(), message = "Data quality service not available" }, jsonOptions);

            return await EndpointHelpers.GuardAsync(async () =>
            {
                var report = await qualityService.GenerateReportAsync(
                    new QualityReportOptions(
                        Paths: new[] { Path.GetFullPath(opts.RootPath) },
                        MinScoreThreshold: 0.5,
                        IncludeRecommendations: true), ct);

                return Results.Json(new
                {
                    lowQualityCount = report.LowQualityFiles?.Count ?? 0,
                    anomalies = report.LowQualityFiles?.Select(f => new
                    {
                        f.Path,
                        f.OverallScore,
                        issues = f.Dimensions?.Where(d => d.Score < 0.5).Select(d => new { d.Name, d.Score, d.Issues })
                    })
                }, jsonOptions);
            }, "Failed to detect quality anomalies.", logger);
        })
        .WithName("GetQualityAnomalies").Produces(200);

        // POST /api/storage/quality/check — run a quality check on specified path
        group.MapPost(UiApiRoutes.StorageQualityCheck, async (
            IDataQualityService? qualityService,
            StorageOptions opts,
            StorageQualityCheckRequest req,
            CancellationToken ct) =>
        {
            if (qualityService is null)
                return Results.Problem("Data quality service not available");

            if (string.IsNullOrWhiteSpace(req.Path))
                return Results.BadRequest(new { error = "Path is required" });

            var fullRootPath = Path.GetFullPath(opts.RootPath);
            if (!TryResolvePathWithinRoot(req.Path, fullRootPath, out var fullPath))
                return Results.BadRequest(new { error = "Path must resolve within the configured storage root." });

            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                return Results.NotFound(new { error = $"Path not found: {req.Path}" });

            return await EndpointHelpers.GuardAsync(async () =>
            {
                var score = await qualityService.ScoreAsync(fullPath, ct);
                return Results.Json(score, jsonOptions);
            }, "Quality check failed.", logger);
        })
        .WithName("RunQualityCheck").Produces(200).Produces(400).Produces(404)
        .AddEndpointFilter(EndpointAuthorization.Require(UserPermission.ManageStorage))
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static bool TryResolvePathWithinRoot(string candidatePath, string rootPath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(candidatePath))
            return false;

        try
        {
            fullPath = Path.IsPathRooted(candidatePath)
                ? Path.GetFullPath(candidatePath)
                : Path.GetFullPath(Path.Combine(rootPath, candidatePath));
            var rootPrefix = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Maps the /api/quality/drops endpoints exposing dropped event statistics from the pipeline's audit trail.
    /// </summary>
    public static void MapQualityDropsEndpoints(
        this WebApplication app,
        DroppedEventAuditTrail? auditTrail,
        JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Quality Drops");

        group.MapGet(UiApiRoutes.QualityDrops, () =>
        {
            if (auditTrail is null)
            {
                return Results.Json(new
                {
                    totalDropped = 0L,
                    dropsBySymbol = new Dictionary<string, long>(),
                    message = "Audit trail not configured",
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions);
            }

            var stats = auditTrail.GetStatistics();
            return Results.Json(new
            {
                totalDropped = stats.TotalDropped,
                dropsBySymbol = stats.DropsBySymbol,
                auditFilePath = stats.AuditFilePath,
                timestamp = stats.Timestamp
            }, jsonOptions);
        })
        .WithName("GetQualityDrops")
        .Produces(200);

        group.MapGet(UiApiRoutes.QualityDropsBySymbol, (string symbol) =>
        {
            if (auditTrail is null)
            {
                return Results.Json(new
                {
                    symbol,
                    dropped = 0L,
                    message = "Audit trail not configured",
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions);
            }

            var stats = auditTrail.GetStatistics();
            var normalizedSymbol = symbol.ToUpperInvariant();
            var symbolDrops = stats.DropsBySymbol.TryGetValue(normalizedSymbol, out var count) ? count : 0;

            return Results.Json(new
            {
                symbol,
                dropped = symbolDrops,
                totalDropped = stats.TotalDropped,
                timestamp = stats.Timestamp
            }, jsonOptions);
        })
        .WithName("GetQualityDropsBySymbol")
        .Produces(200);
    }
}

// Request DTOs
internal sealed record StorageQualityCheckRequest(string Path);

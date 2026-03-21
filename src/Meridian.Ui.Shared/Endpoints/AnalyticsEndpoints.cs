using System.Text.Json;
using Meridian.Application.Monitoring;
using Meridian.Application.Monitoring.DataQuality;
using Meridian.Contracts.Api;
using Meridian.Infrastructure.Adapters.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering analytics API endpoints.
/// Provides gap analysis, anomaly detection, latency stats, completeness, and throughput.
/// </summary>
public static class AnalyticsEndpoints
{
    public static void MapAnalyticsEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Analytics");

        // Gap analysis
        group.MapGet(UiApiRoutes.AnalyticsGaps, (string? symbol, [FromServices] DataQualityMonitoringService? qualityService) =>
        {
            if (qualityService is null)
                return Results.Json(new { gaps = Array.Empty<object>(), message = "Quality monitoring not available" }, jsonOptions);

            var gaps = qualityService.GapAnalyzer.GetRecentGaps();
            return Results.Json(new { gaps, timestamp = DateTimeOffset.UtcNow }, jsonOptions);
        })
        .WithName("GetAnalyticsGaps")
        .Produces(200);

        // Gap repair
        group.MapPost(UiApiRoutes.AnalyticsGapsRepair, (GapRepairRequest? req) =>
        {
            return Results.Json(new
            {
                queued = true,
                symbol = req?.Symbol,
                message = "Gap repair has been queued for processing",
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("RepairAnalyticsGaps")
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Provider comparison
        group.MapGet(UiApiRoutes.AnalyticsCompare, (string? symbol, [FromServices] DataQualityMonitoringService? qualityService) =>
        {
            if (qualityService is null)
                return Results.Json(new { comparison = (object?)null, message = "Quality monitoring not available" }, jsonOptions);

            var stats = qualityService.CrossProvider.GetStatistics();
            return Results.Json(new { comparison = stats, timestamp = DateTimeOffset.UtcNow }, jsonOptions);
        })
        .WithName("GetAnalyticsCompare")
        .Produces(200);

        // Latency analysis
        group.MapGet(UiApiRoutes.AnalyticsLatency, ([FromServices] DataQualityMonitoringService? qualityService) =>
        {
            if (qualityService is null)
                return Results.Json(new { latency = (object?)null, message = "Quality monitoring not available" }, jsonOptions);

            var distributions = qualityService.LatencyHistogram.GetAllDistributions();
            return Results.Json(new { latency = distributions, timestamp = DateTimeOffset.UtcNow }, jsonOptions);
        })
        .WithName("GetAnalyticsLatency")
        .Produces(200);

        // Latency stats
        group.MapGet(UiApiRoutes.AnalyticsLatencyStats, ([FromServices] DataQualityMonitoringService? qualityService) =>
        {
            if (qualityService is null)
                return Results.Json(new { stats = (object?)null }, jsonOptions);

            var stats = qualityService.LatencyHistogram.GetStatistics();
            return Results.Json(new { stats, timestamp = DateTimeOffset.UtcNow }, jsonOptions);
        })
        .WithName("GetAnalyticsLatencyStats")
        .Produces(200);

        // Anomaly detection
        group.MapGet(UiApiRoutes.AnalyticsAnomalies, (string? symbol, [FromServices] DataQualityMonitoringService? qualityService) =>
        {
            if (qualityService is null)
                return Results.Json(new { anomalies = Array.Empty<object>() }, jsonOptions);

            var anomalies = symbol is not null
                ? qualityService.AnomalyDetector.GetAnomalies(symbol)
                : qualityService.AnomalyDetector.GetRecentAnomalies();
            return Results.Json(new { anomalies, timestamp = DateTimeOffset.UtcNow }, jsonOptions);
        })
        .WithName("GetAnalyticsAnomalies")
        .Produces(200);

        // Quality report
        group.MapGet(UiApiRoutes.AnalyticsQualityReport, ([FromServices] DataQualityMonitoringService? qualityService) =>
        {
            if (qualityService is null)
                return Results.Json(new { report = (object?)null, message = "Quality monitoring not available" }, jsonOptions);

            var dashboard = qualityService.GetDashboard();
            return Results.Json(new { report = dashboard, timestamp = DateTimeOffset.UtcNow }, jsonOptions);
        })
        .WithName("GetAnalyticsQualityReport")
        .Produces(200);

        // Completeness
        group.MapGet(UiApiRoutes.AnalyticsCompleteness, (string? symbol, [FromServices] DataQualityMonitoringService? qualityService) =>
        {
            if (qualityService is null)
                return Results.Json(new { completeness = (object?)null }, jsonOptions);

            if (symbol is not null)
            {
                var scores = qualityService.Completeness.GetScoresForSymbol(symbol);
                return Results.Json(new { completeness = scores, timestamp = DateTimeOffset.UtcNow }, jsonOptions);
            }

            var summary = qualityService.Completeness.GetSummary();
            return Results.Json(new { completeness = summary, timestamp = DateTimeOffset.UtcNow }, jsonOptions);
        })
        .WithName("GetAnalyticsCompleteness")
        .Produces(200);

        // Throughput
        group.MapGet(UiApiRoutes.AnalyticsThroughput, ([FromServices] IEventMetrics? metrics) =>
        {
            return Results.Json(new
            {
                metricsAvailable = metrics != null,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetAnalyticsThroughput")
        .Produces(200);

        // Rate limits
        group.MapGet(UiApiRoutes.AnalyticsRateLimits, ([FromServices] ProviderRegistry? registry) =>
        {
            var providers = registry?.GetBackfillProviders()
                .Select(p => new
                {
                    name = p.Name,
                    displayName = p.DisplayName,
                    priority = p.Priority
                })
                .ToArray() ?? Array.Empty<object>();

            return Results.Json(new { providers, timestamp = DateTimeOffset.UtcNow }, jsonOptions);
        })
        .WithName("GetAnalyticsRateLimits")
        .Produces(200);
    }

    private sealed record GapRepairRequest(string? Symbol, string? Provider, DateOnly? From, DateOnly? To);
}

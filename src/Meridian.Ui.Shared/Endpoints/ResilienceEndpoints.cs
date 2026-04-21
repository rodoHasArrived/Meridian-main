using System.Text.Json;
using Meridian.Application.Backfill;
using Meridian.Application.Monitoring;
using Meridian.Contracts.Api;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering resilience, cost estimation, and compliance endpoints.
/// </summary>
public static class ResilienceEndpoints
{
    public static void MapResilienceEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Resilience");

        // Circuit breaker dashboard
        group.MapGet(UiApiRoutes.ResilienceCircuitBreakers, (
            [FromServices] CircuitBreakerStatusService? cbService) =>
        {
            if (cbService is null)
            {
                return Results.Json(new
                {
                    overallHealth = "Unknown",
                    totalBreakers = 0,
                    breakers = Array.Empty<object>(),
                    message = "CircuitBreakerStatusService not registered.",
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions);
            }

            var dashboard = cbService.GetDashboard();
            return Results.Json(dashboard, jsonOptions);
        })
        .WithName("GetCircuitBreakerDashboard")
        .WithDescription("Returns the current state of all circuit breakers in the system.")
        .Produces(200);

        // Backfill cost estimation
        group.MapPost(UiApiRoutes.BackfillCostEstimate, (
            [FromBody] BackfillCostEstimateRequest req,
            [FromServices] BackfillCostEstimator? estimator) =>
        {
            if (estimator is null)
            {
                return Results.Json(new
                {
                    error = "BackfillCostEstimator not available. No historical providers registered.",
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions, statusCode: 503);
            }

            if (!DataGranularityExtensions.TryParseValue(req.Granularity, out var granularity) &&
                !string.IsNullOrWhiteSpace(req.Granularity))
            {
                return Results.Json(new
                {
                    error = $"Unsupported granularity '{req.Granularity}'.",
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions, statusCode: 400);
            }

            var costRequest = new BackfillCostRequest(
                Symbols: req.Symbols,
                Provider: req.Provider,
                From: req.From,
                To: req.To,
                Granularity: granularity);

            var estimate = estimator.Estimate(costRequest);
            return Results.Json(estimate, jsonOptions);
        })
        .WithName("EstimateBackfillCost")
        .WithDescription("Estimates API calls, wall-clock time, and quota impact for a backfill request.")
        .Produces(200)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Retention compliance report
        group.MapGet(UiApiRoutes.RetentionComplianceReport, async (
            [FromServices] RetentionComplianceReporter? reporter,
            CancellationToken ct) =>
        {
            if (reporter is null)
            {
                return Results.Json(new
                {
                    error = "RetentionComplianceReporter not available.",
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions, statusCode: 503);
            }

            var report = await reporter.GenerateReportAsync(ct);
            return Results.Json(report, jsonOptions);
        })
        .WithName("GetRetentionComplianceReport")
        .WithDescription("Generates a retention compliance report by scanning stored data against configured policies.")
        .Produces(200)
        .Produces(503);
    }

    private sealed record BackfillCostEstimateRequest(
        string[]? Symbols,
        string? Provider = null,
        DateOnly? From = null,
        DateOnly? To = null,
        string? Granularity = null);
}

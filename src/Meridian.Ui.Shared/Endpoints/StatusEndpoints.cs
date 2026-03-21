using System.Text.Json;
using Meridian.Application.Monitoring;
using Meridian.Application.UI;
using Meridian.Contracts.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering status and health API endpoints.
/// Shared between web dashboard and desktop application hosts.
/// Uses StatusEndpointHandlers for the actual response generation.
/// </summary>
public static class StatusEndpoints
{
    /// <summary>
    /// Maps all status and health API endpoints.
    /// </summary>
    public static void MapStatusEndpoints(this WebApplication app, StatusEndpointHandlers handlers, JsonSerializerOptions jsonOptions)
    {
        // Health check endpoint - comprehensive health status (D7: OpenAPI typed annotations)
        app.MapGet(UiApiRoutes.Health, () =>
        {
            var response = handlers.GetHealthCheck();
            var statusCode = handlers.GetHealthStatusCode(response);
            return Results.Json(response, jsonOptions, statusCode: statusCode);
        })
        .WithName("GetHealth")
        .WithTags("Health")
        .WithDescription("Returns comprehensive health status including provider connectivity and storage health.")
        .Produces<HealthCheckResponse>(200)
        .Produces(503);

        // Alias: /api/health → /health (for backward compatibility with tests)
        app.MapGet("/api/health", () =>
        {
            var response = handlers.GetHealthCheck();
            var statusCode = handlers.GetHealthStatusCode(response);
            return Results.Json(response, jsonOptions, statusCode: statusCode);
        })
        .WithName("GetHealthApi")
        .WithTags("Health")
        .WithDescription("Alias for /health endpoint for backward compatibility.")
        .Produces<HealthCheckResponse>(200)
        .Produces(503);

        // Kubernetes-style health endpoints
        app.MapGet("/healthz", () => Results.Ok("healthy"))
            .WithName("GetHealthz")
            .WithTags("Health")
            .WithDescription("Kubernetes-style liveness probe returning 200 if the process is running.")
            .Produces(200);

        // Readiness probe
        app.MapGet(UiApiRoutes.Ready, () =>
        {
            var (isReady, message) = handlers.CheckReadiness();
            return isReady ? Results.Ok(message) : Results.StatusCode(503);
        })
        .WithName("GetReady")
        .WithTags("Health")
        .WithDescription("Readiness probe returning 200 when the service is ready to accept requests, or 503 if not.")
        .Produces(200)
        .Produces(503);

        app.MapGet("/readyz", () =>
        {
            var (isReady, message) = handlers.CheckReadiness();
            return isReady ? Results.Ok(message) : Results.StatusCode(503);
        })
        .WithName("GetReadyz")
        .WithTags("Health")
        .Produces(200)
        .Produces(503);

        // Liveness probe
        app.MapGet(UiApiRoutes.Live, () => Results.Ok("alive"))
            .WithName("GetLive").WithTags("Health").Produces(200);
        app.MapGet("/livez", () => Results.Ok("alive"))
            .WithName("GetLivez").WithTags("Health").Produces(200);

        // Prometheus metrics
        app.MapGet(UiApiRoutes.Metrics, () =>
        {
            var content = handlers.GetPrometheusMetrics();
            return Results.Content(content, "text/plain; version=0.0.4");
        })
        .WithName("GetMetrics")
        .WithTags("Monitoring")
        .WithDescription("Returns Prometheus-format metrics for scraping by monitoring systems.")
        .Produces(200);

        // Full status endpoint (D7: OpenAPI typed annotations)
        app.MapGet(UiApiRoutes.Status, () =>
        {
            var response = handlers.GetStatus();
            return Results.Json(response, jsonOptions);
        })
        .WithName("GetStatus")
        .WithTags("Status")
        .WithDescription("Returns full system status including connection state, metrics, and symbol information.")
        .Produces<StatusResponse>(200);

        // Errors endpoint with optional filtering
        app.MapGet(UiApiRoutes.Errors, (int? count, string? level, string? symbol) =>
        {
            var response = handlers.GetErrors(count ?? 10, level, symbol);
            return Results.Json(response, jsonOptions);
        })
        .WithName("GetErrors")
        .WithTags("Status")
        .WithDescription("Returns recent error entries with optional filtering by count, severity level, and symbol.")
        .Produces<ErrorsResponseDto>(200);

        // Backpressure status
        app.MapGet(UiApiRoutes.Backpressure, () =>
        {
            var response = handlers.GetBackpressure();
            return Results.Json(response, jsonOptions);
        })
        .WithName("GetBackpressure")
        .WithTags("Status")
        .WithDescription("Returns current backpressure status including queue utilization and drop rates.")
        .Produces<BackpressureStatusDto>(200);

        // Provider latency
        app.MapGet(UiApiRoutes.ProvidersLatency, () =>
        {
            var (summary, error) = handlers.GetProviderLatency();
            if (error != null)
            {
                return Results.Json(new { error, providers = Array.Empty<object>() }, jsonOptions);
            }
            return Results.Json(summary, jsonOptions);
        })
        .WithName("GetProviderLatency")
        .WithTags("Monitoring")
        .WithDescription("Returns latency statistics for all providers including average, min, max, and percentiles.")
        .Produces<ProviderLatencySummaryDto>(200);

        // Connection health
        app.MapGet(UiApiRoutes.Connections, () =>
        {
            var (snapshot, error) = handlers.GetConnectionHealth();
            if (error != null)
            {
                return Results.Json(new { error, connections = Array.Empty<object>() }, jsonOptions);
            }
            return Results.Json(snapshot, jsonOptions);
        })
        .WithName("GetConnections")
        .WithTags("Monitoring")
        .WithDescription("Returns connection health snapshot for all active provider connections.")
        .Produces<ConnectionHealthSnapshotDto>(200);

        // Detailed health (async)
        app.MapGet(UiApiRoutes.HealthDetailed, async () =>
        {
            var (report, error) = await handlers.GetDetailedHealthAsync();
            if (error != null || report is null)
            {
                return Results.Json(new { error = error ?? "Health report unavailable" }, jsonOptions, statusCode: 501);
            }

            var statusCode = report.Status switch
            {
                DetailedHealthStatus.Healthy => 200,
                DetailedHealthStatus.Degraded => 200,
                DetailedHealthStatus.Unhealthy => 503,
                _ => 200
            };
            return Results.Json(report, jsonOptions, statusCode: statusCode);
        })
        .WithName("GetDetailedHealth")
        .WithTags("Health")
        .Produces(200)
        .Produces(503);

        // Alias: /api/health/detailed → /health/detailed (for backward compatibility with tests)
        app.MapGet("/api/health/detailed", async () =>
        {
            var (report, error) = await handlers.GetDetailedHealthAsync();
            if (error != null || report is null)
            {
                return Results.Json(new { error = error ?? "Health report unavailable" }, jsonOptions, statusCode: 501);
            }

            var statusCode = report.Status switch
            {
                DetailedHealthStatus.Healthy => 200,
                DetailedHealthStatus.Degraded => 200,
                DetailedHealthStatus.Unhealthy => 503,
                _ => 200
            };
            return Results.Json(report, jsonOptions, statusCode: statusCode);
        })
        .WithName("GetDetailedHealthApi")
        .WithTags("Health")
        .WithDescription("Alias for /health/detailed endpoint for backward compatibility.")
        .Produces(200)
        .Produces(503);

        // Server-Sent Events endpoint for real-time dashboard updates
        app.MapGet("/api/events/stream", async (HttpContext ctx, CancellationToken ct) =>
        {
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            var sseJsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var status = handlers.GetStatus();
                    var backpressure = handlers.GetBackpressure();
                    var (latency, _) = handlers.GetProviderLatency();
                    var errors = handlers.GetErrors(5, null, null);

                    var ssePayload = new
                    {
                        timestamp = DateTimeOffset.UtcNow,
                        status,
                        backpressure,
                        providerLatency = latency,
                        recentErrors = errors
                    };

                    var json = JsonSerializer.Serialize(ssePayload, sseJsonOptions);
                    await ctx.Response.WriteAsync($"data: {json}\n\n", ct).ConfigureAwait(false);
                    await ctx.Response.Body.FlushAsync(ct).ConfigureAwait(false);
                    await Task.Delay(2000, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Client disconnected
            }
        });
    }
}

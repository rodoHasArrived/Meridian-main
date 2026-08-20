using System.Text.Json;
using Meridian.Application.Composition.Startup;
using Meridian.Application.Monitoring;
using Meridian.Application.UI;
using Meridian.Contracts.Api;
using Meridian.Contracts.Lifecycle;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        .WithName("GetHealth").DeclareOpenRead("Liveness health check returning 503 when unhealthy; status, uptime and coarse check names only, so it needs no permission beyond the session a configured deployment already requires. The unauthenticated probe surface is /healthz and its siblings, which the session middleware exempts outright.")
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
        .WithName("GetHealthApi").DeclareOpenRead("Documented alias of /health kept for backward compatibility; same coarse liveness payload, and the same reasoning applies.")
        .WithTags("Health")
        .WithDescription("Alias for /health endpoint for backward compatibility.")
        .Produces<HealthCheckResponse>(200)
        .Produces(503);

        // Kubernetes-style health endpoints
        app.MapGet("/healthz", () => Results.Ok("healthy"))
            .WithName("GetHealthz").DeclareOpenRead("Kubernetes liveness probe; exempt from session authentication, so a permission would refuse the orchestrator that must call it.")
            .WithTags("Health")
            .WithDescription("Kubernetes-style liveness probe returning 200 if the process is running.")
            .Produces(200);

        // Readiness probe
        app.MapGet(UiApiRoutes.Ready, (CancellationToken ct) =>
            GetReadinessResultAsync(app, handlers, jsonOptions, ct))
        .WithName("GetReady").DeclareOpenRead("Kubernetes readiness probe; exempt from session authentication, so a permission would refuse the orchestrator that must call it.")
        .WithTags("Health")
        .WithDescription("Readiness probe returning 200 when the service is ready to accept requests, or 503 if not.")
        .Produces(200)
        .Produces(503);

        app.MapGet("/readyz", (CancellationToken ct) =>
            GetReadinessResultAsync(app, handlers, jsonOptions, ct))
        .WithName("GetReadyz").DeclareOpenRead("Kubernetes readiness probe alias; exempt from session authentication, so a permission would refuse the orchestrator that must call it.")
        .WithTags("Health")
        .Produces(200)
        .Produces(503);

        app.MapGet("/startupz", (CancellationToken ct) =>
            GetStartupResultAsync(app, handlers, jsonOptions, ct))
        .WithName("GetStartupz").DeclareOpenRead("Sanitized pre-login startup progress; reached before any session exists, so a permission would refuse every caller it is for.")
        .WithTags("Health")
        .WithDescription("Sanitized pre-login startup progress for the local workstation.")
        .Produces<RuntimeLifecycleSnapshotDto>(200)
        .Produces<RuntimeLifecycleSnapshotDto>(202)
        .Produces<RuntimeLifecycleSnapshotDto>(503);

        app.MapGet("/startup", () => Results.Content(
                HtmlTemplateGenerator.Startup(),
                "text/html; charset=utf-8"))
            .WithName("GetStartupCenter").DeclareOpenRead("Pre-login lifecycle progress page; reached before any session exists, so a permission would refuse every caller it is for.")
            .WithTags("Health")
            .WithDescription("Pre-login lifecycle progress and readiness checks.")
            .Produces(StatusCodes.Status200OK, contentType: "text/html");

        // Liveness probe
        app.MapGet(UiApiRoutes.Live, () => Results.Ok("alive"))
            .WithName("GetLive").WithTags("Health")
            .DeclareOpenRead("Kubernetes liveness probe; exempt from session authentication, so a permission would refuse the orchestrator that must call it.")
            .Produces(200);
        app.MapGet("/livez", () => Results.Ok("alive"))
            .WithName("GetLivez").WithTags("Health")
            .DeclareOpenRead("Kubernetes liveness probe alias; exempt from session authentication, so a permission would refuse the orchestrator that must call it.")
            .Produces(200);

        // Prometheus metrics
        app.MapGet(UiApiRoutes.Metrics, async (CancellationToken cancellationToken) =>
        {
            var content = await handlers.GetPrometheusMetricsAsync(cancellationToken).ConfigureAwait(false);
            return Results.Content(content, "text/plain; version=0.0.4");
        })
        .WithName("GetMetrics").DeclareOpenRead("Prometheus scrape contract in text exposition format; operational counters only, so no permission is warranted beyond the session or API key a configured deployment already requires of any out-of-band client.")
        .WithTags("Monitoring")
        .WithDescription("Returns Prometheus-format metrics for scraping by monitoring systems.")
        .Produces(200);

        // Full status endpoint (D7: OpenAPI typed annotations)
        app.MapGet(UiApiRoutes.Status, () =>
        {
            var response = handlers.GetStatus();
            return Results.Json(response, jsonOptions);
        })
        .WithName("GetStatus").RequireAnyPermission(UserPermission.ViewConfig, UserPermission.ViewDiagnostics, UserPermission.ManageProviders, UserPermission.AdminMaintenance)
        .WithTags("Status")
        .WithDescription("Returns full system status including connection state, metrics, and symbol information.")
        .Produces<StatusResponse>(200);

        // Errors endpoint with optional filtering
        app.MapGet(UiApiRoutes.Errors, (int? count, string? level, string? symbol) =>
        {
            var response = handlers.GetErrors(count ?? 10, level, symbol);
            return Results.Json(response, jsonOptions);
        })
        .WithName("GetErrors").RequireAnyPermission(UserPermission.ViewConfig, UserPermission.ViewDiagnostics, UserPermission.ManageProviders, UserPermission.AdminMaintenance)
        .WithTags("Status")
        .WithDescription("Returns recent error entries with optional filtering by count, severity level, and symbol.")
        .Produces<ErrorsResponseDto>(200);

        // Backpressure status
        app.MapGet(UiApiRoutes.Backpressure, () =>
        {
            var response = handlers.GetBackpressure();
            return Results.Json(response, jsonOptions);
        })
        .WithName("GetBackpressure").RequireAnyPermission(UserPermission.ViewConfig, UserPermission.ViewDiagnostics, UserPermission.ManageProviders, UserPermission.AdminMaintenance)
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
        .WithName("GetProviderLatency").RequireAnyPermission(UserPermission.ViewConfig, UserPermission.ViewDiagnostics, UserPermission.ManageProviders, UserPermission.AdminMaintenance)
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
        .WithName("GetConnections").RequireAnyPermission(UserPermission.ViewConfig, UserPermission.ViewDiagnostics, UserPermission.ManageProviders, UserPermission.AdminMaintenance)
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
        .WithName("GetDetailedHealth").RequirePermission(UserPermission.ViewDiagnostics)
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
        .WithName("GetDetailedHealthApi").RequirePermission(UserPermission.ViewDiagnostics)
        .WithTags("Health")
        .WithDescription("Alias for /health/detailed endpoint for backward compatibility.")
        .Produces(200)
        .Produces(503);

        // Server-Sent Events endpoint for real-time dashboard updates.
        // Publish cadence is operator-tunable via configuration ("Status:SsePublishIntervalMs");
        // non-positive values would spin or throw in the publish loop, so they fall back to the default.
        var configuredSsePublishIntervalMs = app.Configuration.GetValue<int?>("Status:SsePublishIntervalMs");
        var ssePublishIntervalMs = configuredSsePublishIntervalMs is > 0 ? configuredSsePublishIntervalMs.Value : 2000;
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
                    await Task.Delay(ssePublishIntervalMs, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Client disconnected
            }
        })
        // The stream publishes the same runtime status and back-pressure the sibling reads serve, so it
        // answers the same operational permissions rather than being open because it is a stream.
        .RequireAnyPermission(
            UserPermission.ViewConfig,
            UserPermission.ViewDiagnostics,
            UserPermission.ManageProviders,
            UserPermission.AdminMaintenance);
    }

    private static async Task<IResult> GetReadinessResultAsync(
        WebApplication app,
        StatusEndpointHandlers handlers,
        JsonSerializerOptions jsonOptions,
        CancellationToken ct)
    {
        var readinessService = app.Services.GetService<IRuntimeReadinessService>();
        if (readinessService is null)
        {
            var (legacyIsReady, message) = handlers.CheckReadiness();
            return legacyIsReady ? Results.Ok(message) : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var snapshot = await readinessService.EvaluateAsync(ct).ConfigureAwait(false);
        var isReady = snapshot.AcceptingWork &&
                      snapshot.Readiness is RuntimeReadinessStatus.Ready or RuntimeReadinessStatus.Degraded;
        return Results.Json(
            snapshot,
            jsonOptions,
            statusCode: isReady
                ? StatusCodes.Status200OK
                : StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> GetStartupResultAsync(
        WebApplication app,
        StatusEndpointHandlers handlers,
        JsonSerializerOptions jsonOptions,
        CancellationToken ct)
    {
        var readinessService = app.Services.GetService<IRuntimeReadinessService>();
        if (readinessService is null)
        {
            var (isReady, _) = handlers.CheckReadiness();
            return Results.Json(
                new { state = isReady ? "ready" : "starting" },
                jsonOptions,
                statusCode: isReady
                    ? StatusCodes.Status200OK
                    : StatusCodes.Status202Accepted);
        }

        var snapshot = await readinessService.EvaluateAsync(ct).ConfigureAwait(false);
        var statusCode = snapshot.State switch
        {
            RuntimeLifecycleState.Ready or RuntimeLifecycleState.Degraded => StatusCodes.Status200OK,
            RuntimeLifecycleState.Failed or
            RuntimeLifecycleState.ShutdownRequested or
            RuntimeLifecycleState.Draining or
            RuntimeLifecycleState.Flushing or
            RuntimeLifecycleState.StoppingHost or
            RuntimeLifecycleState.Stopped => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status202Accepted
        };
        return Results.Json(snapshot, jsonOptions, statusCode: statusCode);
    }
}

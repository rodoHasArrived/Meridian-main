using System.Text.Json;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Api;
using Meridian.Contracts.Pipeline;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Identity.Auth;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Maps REST API endpoints for the unified ingestion job contract.
/// Exposes job creation and queries; state transitions go through the governed workstation action route.
/// </summary>
/// <remarks>
/// Addresses P0: "No unified job contract across realtime/backfill flows"
/// and P0: "Limited checkpoint semantics exposed to users".
/// </remarks>
public static class IngestionJobEndpoints
{
    /// <summary>
    /// Maps all ingestion job API endpoints.
    /// </summary>
    public static void MapIngestionJobEndpoints(
        this WebApplication app,
        JsonSerializerOptions jsonOptions)
    {
        // List all jobs with optional filtering
        app.MapGet(UiApiRoutes.IngestionJobs, (string? state, string? workload, HttpContext ctx) =>
        {
            if (ResolveService(ctx) is not { } jobService)
                return ServiceUnavailable();

            IngestionJobState? stateFilter = null;
            IngestionWorkloadType? workloadFilter = null;

            if (!string.IsNullOrEmpty(state) && Enum.TryParse<IngestionJobState>(state, true, out var parsedState))
                stateFilter = parsedState;

            if (!string.IsNullOrEmpty(workload) && Enum.TryParse<IngestionWorkloadType>(workload, true, out var parsedWorkload))
                workloadFilter = parsedWorkload;

            var jobs = jobService.GetJobs(stateFilter, workloadFilter);
            var summary = jobService.GetSummary();

            return Results.Json(new { summary, jobs }, jsonOptions);
        })
        .WithName("ListIngestionJobs")
        .RequireAnyPermission(UserPermission.ViewHistoricalData, UserPermission.TriggerBackfill)
        .WithTags("Ingestion")
        .WithDescription("Lists all ingestion jobs with optional state and workload type filtering.")
        .Produces(200);

        // Get a specific job by ID
        app.MapGet(UiApiRoutes.IngestionJobById, (string jobId, HttpContext ctx) =>
        {
            if (ResolveService(ctx) is not { } jobService)
                return ServiceUnavailable();

            var job = jobService.GetJob(jobId);
            if (job == null)
                return Results.NotFound(new { error = $"Job '{jobId}' not found" });

            return Results.Json(job, jsonOptions);
        })
        .WithName("GetIngestionJob")
        .RequireAnyPermission(UserPermission.ViewHistoricalData, UserPermission.TriggerBackfill)
        .WithTags("Ingestion")
        .WithDescription("Gets details of a specific ingestion job including progress and checkpoint.")
        .Produces<IngestionJob>(200)
        .Produces(404);

        // State transitions and deletion are deliberately not exposed here. Operator state changes
        // (pause, resume, retry, cancel) go through the governed workstation action route
        // (ApplyWorkstationIngestionOperationAction), which validates the transition against the
        // job's checkpoint and retry envelope, requires a rationale and idempotency key, enforces
        // tenant/company scope and retains action evidence. A raw transition/delete route here would
        // bypass every one of those safeguards.

        // Create a new ingestion job
        app.MapPost(UiApiRoutes.IngestionJobs, async (HttpContext ctx, CancellationToken ct) =>
        {
            if (ResolveService(ctx) is not { } jobService)
                return ServiceUnavailable();

            CreateIngestionJobRequest? request;
            try
            {
                request = await ctx.Request.ReadFromJsonAsync<CreateIngestionJobRequest>(jsonOptions, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // As above: an aborted body read is not a malformed request.
                throw;
            }
            catch
            {
                return Results.BadRequest(new { error = "Invalid request body" });
            }

            if (request == null || request.Symbols == null || request.Symbols.Length == 0)
                return Results.BadRequest(new { error = "symbols array is required" });

            if (string.IsNullOrEmpty(request.Provider))
                return Results.BadRequest(new { error = "provider is required" });

            if (!Enum.TryParse<IngestionWorkloadType>(request.WorkloadType ?? "Historical", true, out var workloadType))
                return Results.BadRequest(new { error = $"Invalid workloadType: '{request.WorkloadType}'" });

            var job = await jobService.CreateJobAsync(
                workloadType,
                request.Symbols,
                request.Provider,
                request.FromDate,
                request.ToDate,
                request.Sla,
                ct);

            return Results.Created($"/api/ingestion/jobs/{job.JobId}", job);
        })
        .WithName("CreateIngestionJob")
        .RequirePermission(UserPermission.TriggerBackfill)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .WithTags("Ingestion")
        .WithDescription("Creates a new ingestion job in Draft state.")
        .Produces<IngestionJob>(201)
        .Produces(400);

        // Get resumable jobs
        app.MapGet("/api/ingestion/jobs/resumable", (HttpContext ctx) =>
        {
            if (ResolveService(ctx) is not { } jobService)
                return ServiceUnavailable();

            var jobs = jobService.GetResumableJobs();
            return Results.Json(new { count = jobs.Count, jobs }, jsonOptions);
        })
        .WithName("GetResumableJobs")
        .RequireAnyPermission(UserPermission.ViewHistoricalData, UserPermission.TriggerBackfill)
        .WithTags("Ingestion")
        .WithDescription("Returns all jobs that can be resumed (failed or paused with a checkpoint).")
        .Produces(200);

        // Get job summary
        app.MapGet("/api/ingestion/summary", (HttpContext ctx) =>
        {
            if (ResolveService(ctx) is not { } jobService)
                return ServiceUnavailable();

            var summary = jobService.GetSummary();
            return Results.Json(summary, jsonOptions);
        })
        .WithName("GetIngestionSummary")
        .RequireAnyPermission(UserPermission.ViewHistoricalData, UserPermission.TriggerBackfill)
        .WithTags("Ingestion")
        .WithDescription("Returns a summary of all ingestion jobs by state and workload type.")
        .Produces<IngestionJobSummary>(200);

    }

    private static IngestionJobService? ResolveService(HttpContext context) =>
        context.RequestServices.GetService<IngestionJobService>();

    private static IResult ServiceUnavailable() =>
        Results.Problem("Ingestion job service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
}

/// <summary>
/// Request body for creating a new ingestion job.
/// </summary>
internal sealed class CreateIngestionJobRequest
{
    public string? WorkloadType { get; set; }
    public string[] Symbols { get; set; } = Array.Empty<string>();
    public string Provider { get; set; } = string.Empty;
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public IngestionSla? Sla { get; set; }
}

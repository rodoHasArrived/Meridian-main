using System.Text.Json;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Api;
using Meridian.Contracts.Pipeline;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Maps REST API endpoints for the unified ingestion job contract.
/// Exposes job creation, state transitions, checkpoint management, and queries.
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
        IngestionJobService jobService,
        JsonSerializerOptions jsonOptions)
    {
        // List all jobs with optional filtering
        app.MapGet(UiApiRoutes.IngestionJobs, (string? state, string? workload) =>
        {
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
        .WithTags("Ingestion")
        .WithDescription("Lists all ingestion jobs with optional state and workload type filtering.")
        .Produces(200);

        // Get a specific job by ID
        app.MapGet(UiApiRoutes.IngestionJobById, (string jobId) =>
        {
            var job = jobService.GetJob(jobId);
            if (job == null)
                return Results.NotFound(new { error = $"Job '{jobId}' not found" });

            return Results.Json(job, jsonOptions);
        })
        .WithName("GetIngestionJob")
        .WithTags("Ingestion")
        .WithDescription("Gets details of a specific ingestion job including progress and checkpoint.")
        .Produces<IngestionJob>(200)
        .Produces(404);

        // Transition a job to a new state
        app.MapPost(UiApiRoutes.IngestionJobTransition, async (string jobId, HttpContext ctx, CancellationToken ct) =>
        {
            IngestionJobTransitionRequest? request;
            try
            {
                request = await ctx.Request.ReadFromJsonAsync<IngestionJobTransitionRequest>(jsonOptions, ct);
            }
            catch
            {
                return Results.BadRequest(new { error = "Invalid request body" });
            }

            if (request == null || string.IsNullOrEmpty(request.TargetState))
                return Results.BadRequest(new { error = "targetState is required" });

            if (!Enum.TryParse<IngestionJobState>(request.TargetState, true, out var targetState))
                return Results.BadRequest(new { error = $"Invalid state: '{request.TargetState}'" });

            var success = await jobService.TransitionAsync(jobId, targetState, request.ErrorMessage, ct);
            if (!success)
            {
                var job = jobService.GetJob(jobId);
                if (job == null)
                    return Results.NotFound(new { error = $"Job '{jobId}' not found" });

                return Results.Conflict(new
                {
                    error = $"Cannot transition from '{job.State}' to '{targetState}'",
                    currentState = job.State.ToString(),
                    requestedState = targetState.ToString()
                });
            }

            return Results.Json(jobService.GetJob(jobId), jsonOptions);
        })
        .WithName("TransitionIngestionJob")
        .WithTags("Ingestion")
        .WithDescription("Transitions an ingestion job to a new state (e.g., Queued, Running, Paused, Completed, Failed, Cancelled).")
        .Produces<IngestionJob>(200)
        .Produces(400)
        .Produces(404)
        .Produces(409);

        // Create a new ingestion job
        app.MapPost(UiApiRoutes.IngestionJobs, async (HttpContext ctx, CancellationToken ct) =>
        {
            CreateIngestionJobRequest? request;
            try
            {
                request = await ctx.Request.ReadFromJsonAsync<CreateIngestionJobRequest>(jsonOptions, ct);
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
        .WithTags("Ingestion")
        .WithDescription("Creates a new ingestion job in Draft state.")
        .Produces<IngestionJob>(201)
        .Produces(400);

        // Get resumable jobs
        app.MapGet("/api/ingestion/jobs/resumable", () =>
        {
            var jobs = jobService.GetResumableJobs();
            return Results.Json(new { count = jobs.Count, jobs }, jsonOptions);
        })
        .WithName("GetResumableJobs")
        .WithTags("Ingestion")
        .WithDescription("Returns all jobs that can be resumed (failed or paused with a checkpoint).")
        .Produces(200);

        // Get job summary
        app.MapGet("/api/ingestion/summary", () =>
        {
            var summary = jobService.GetSummary();
            return Results.Json(summary, jsonOptions);
        })
        .WithName("GetIngestionSummary")
        .WithTags("Ingestion")
        .WithDescription("Returns a summary of all ingestion jobs by state and workload type.")
        .Produces<IngestionJobSummary>(200);

        // Delete a terminal job
        app.MapDelete(UiApiRoutes.IngestionJobById, async (string jobId, CancellationToken ct) =>
        {
            var success = await jobService.DeleteJobAsync(jobId, ct);
            if (!success)
            {
                var job = jobService.GetJob(jobId);
                if (job == null)
                    return Results.NotFound(new { error = $"Job '{jobId}' not found" });

                return Results.Conflict(new { error = "Can only delete jobs in a terminal state (Completed, Failed, Cancelled)" });
            }

            return Results.Ok(new { deleted = true, jobId });
        })
        .WithName("DeleteIngestionJob")
        .WithTags("Ingestion")
        .WithDescription("Deletes a terminal ingestion job (completed, failed, or cancelled).")
        .Produces(200)
        .Produces(404)
        .Produces(409);
    }
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

/// <summary>
/// Request body for transitioning a job state.
/// </summary>
internal sealed class IngestionJobTransitionRequest
{
    public string TargetState { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

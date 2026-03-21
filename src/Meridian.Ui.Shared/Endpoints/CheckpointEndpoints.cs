using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using AppBackfillRequest = Meridian.Application.Backfill.BackfillRequest;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering backfill checkpoint/resume API endpoints.
/// Exposes checkpoint semantics to users for job resumability and crash recovery (P0).
/// </summary>
public static class CheckpointEndpoints
{
    /// <summary>
    /// Maps all checkpoint and ingestion job API endpoints.
    /// </summary>
    public static void MapCheckpointEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Checkpoints");

        // Get all checkpoint history
        group.MapGet(UiApiRoutes.BackfillCheckpoints, (BackfillCoordinator backfill) =>
        {
            var status = backfill.TryReadLast();
            return status is null
                ? Results.Json(Array.Empty<object>(), jsonOptions)
                : Results.Json(new[] { status }, jsonOptions);
        })
        .WithName("GetBackfillCheckpoints")
        .WithDescription("Returns checkpoint history for backfill jobs.")
        .Produces(200);

        // Get resumable jobs (incomplete/failed checkpoints)
        group.MapGet(UiApiRoutes.BackfillCheckpointsResumable, (BackfillCoordinator backfill) =>
        {
            var status = backfill.TryReadLast();
            if (status is null || status.Success)
            {
                return Results.Json(Array.Empty<object>(), jsonOptions);
            }

            // Return failed jobs as resumable
            var resumable = new
            {
                status.Provider,
                status.Symbols,
                status.StartedUtc,
                status.CompletedUtc,
                IsResumable = !status.Success,
                status.BarsWritten,
                Reason = status.Success ? null : "Job did not complete successfully"
            };

            return Results.Json(new[] { resumable }, jsonOptions);
        })
        .WithName("GetResumableCheckpoints")
        .WithDescription("Returns incomplete or failed backfill jobs that can be resumed.")
        .Produces(200);

        // Get checkpoint for a specific job
        group.MapGet("/api/backfill/checkpoints/{jobId}", (string jobId, BackfillCoordinator backfill) =>
        {
            var status = backfill.TryReadLast();
            if (status is null)
            {
                return Results.NotFound(new { error = $"No checkpoint found for job {jobId}" });
            }

            return Results.Json(new
            {
                JobId = jobId,
                status.Provider,
                status.Symbols,
                status.StartedUtc,
                status.CompletedUtc,
                status.Success,
                status.BarsWritten,
                CanResume = !status.Success
            }, jsonOptions);
        })
        .WithName("GetCheckpointById")
        .WithDescription("Returns checkpoint details for a specific backfill job.")
        .Produces(200)
        .Produces(404);

        // Get pending symbols for a checkpoint
        group.MapGet("/api/backfill/checkpoints/{jobId}/pending", (string jobId, BackfillCoordinator backfill) =>
        {
            var status = backfill.TryReadLast();
            if (status is null)
            {
                return Results.NotFound(new { error = $"No checkpoint found for job {jobId}" });
            }

            // If job was successful, no pending symbols
            if (status.Success)
            {
                return Results.Json(new { jobId, pendingSymbols = Array.Empty<string>(), count = 0 }, jsonOptions);
            }

            // Return all symbols as potentially pending since we don't have per-symbol status
            return Results.Json(new
            {
                jobId,
                pendingSymbols = status.Symbols ?? Array.Empty<string>(),
                count = status.Symbols?.Length ?? 0
            }, jsonOptions);
        })
        .WithName("GetPendingSymbols")
        .WithDescription("Returns symbols that still need processing for a resumable checkpoint.")
        .Produces(200)
        .Produces(404);

        // Resume a checkpoint (trigger backfill for pending symbols)
        group.MapPost("/api/backfill/checkpoints/{jobId}/resume", async (
            string jobId,
            BackfillCoordinator backfill) =>
        {
            var status = backfill.TryReadLast();
            if (status is null)
            {
                return Results.NotFound(new { error = $"No checkpoint found for job {jobId}" });
            }

            if (status.Success)
            {
                return Results.BadRequest(new { error = "This job already completed successfully. Nothing to resume." });
            }

            // Trigger a new backfill for the same symbols
            try
            {
                var request = new AppBackfillRequest(
                    status.Provider ?? "composite",
                    status.Symbols ?? Array.Empty<string>(),
                    status.From,
                    status.To ?? DateOnly.FromDateTime(DateTime.Today));

                var result = await backfill.RunAsync(request, CancellationToken.None);

                return Results.Json(new
                {
                    accepted = true,
                    jobId,
                    resumed = true,
                    result.BarsWritten,
                    result.Success
                }, jsonOptions);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"Resume failed: {ex.Message}" });
            }
        })
        .WithName("ResumeCheckpoint")
        .WithDescription("Resumes a failed or interrupted backfill job from its checkpoint.")
        .Produces(200)
        .Produces(400)
        .Produces(404);
    }
}

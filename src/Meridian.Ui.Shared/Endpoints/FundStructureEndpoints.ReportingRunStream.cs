using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;
using Meridian.Ui.Shared.Streaming;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

// Report-run status stream surface, split out of FundStructureEndpoints.cs to keep that file under
// its size cap. Mapped by its own extension (below) rather than from the at-cap
// MapFundStructureEndpoints, but on the same /api/fund-structure group.
public static partial class FundStructureEndpoints
{
    /// <summary>
    /// Register the report-run status Server-Sent Events endpoint. Authorization is enforced here,
    /// where the <see cref="HttpContext"/> is available; the background broadcaster then rebuilds each
    /// run's payload by run id alone (a run's status is a property of the run, not the viewer).
    /// </summary>
    public static void MapReportingRunStreamEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/fund-structure").WithTags("Fund Structure");

        // GET /api/fund-structure/reporting/runs/{runId}/stream — SSE channel emitting
        // `event: report-run` frames with the run's status/audit payload whenever it changes (notably
        // approval transitions), plus `event: heartbeat` frames while idle.
        group.MapGet("/reporting/runs/{runId}/stream", async (
            string runId,
            HttpContext context,
            CancellationToken ct) =>
        {
            if (!HasReportingReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var orchestration = context.RequestServices.GetService<IReportingOrchestrationService>();
            if (orchestration is null)
            {
                return WorkspaceServiceUnavailable();
            }

            var trimmedRunId = (runId ?? string.Empty).Trim();
            var manifest = orchestration.GetManifest(trimmedRunId);
            if (manifest is null)
            {
                return Results.Problem($"Reporting run '{runId}' was not found.", statusCode: StatusCodes.Status404NotFound);
            }

            // Access is a property of the run's template governance; enforce it at subscribe time.
            var governedCatalog = context.RequestServices.GetService<GovernedReportingTemplateCatalog>();
            if (governedCatalog is not null)
            {
                var evaluation = governedCatalog.EvaluateAccess(manifest.TemplateId, BuildReportAccessQueryContext(context));
                if (!evaluation.IsAccessible)
                {
                    return Results.Problem(evaluation.Reason, statusCode: StatusCodes.Status403Forbidden);
                }
            }

            var broadcaster = context.RequestServices.GetService<ReportRunStreamBroadcaster>();
            if (broadcaster is null)
            {
                // Mirror the quote stream endpoint: a missing broadcaster is an unavailable
                // streaming surface (503), distinct from the 501 used when the core workspace
                // orchestration service itself is not registered.
                return Results.Problem("Report-run stream is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var subscription = broadcaster.TrySubscribe(
                StreamTopic.ReportRun(trimmedRunId),
                StreamEndpointHelpers.ResolveStreamSessionId(context));
            if (subscription is null)
            {
                context.Response.Headers["Retry-After"] = "5";
                return Results.Problem("Too many concurrent streams for this session.", statusCode: StatusCodes.Status429TooManyRequests);
            }

            await using (subscription)
            {
                await StreamEndpointHelpers.WriteEventStreamAsync(
                    context,
                    subscription.Reader,
                    "report-run",
                    jsonOptions,
                    StreamEndpointHelpers.DefaultHeartbeatIntervalMs,
                    ct);
            }

            return Results.Empty;
        })
        .WithName("GetReportingRunStream")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status429TooManyRequests)
        .Produces(StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>
    /// Build a report-run status/audit payload by run id alone, for the report-run stream fan-out
    /// (<see cref="Meridian.Ui.Shared.Streaming.ReportRunStreamBroadcaster"/>). Authorization is
    /// enforced at subscribe time by the SSE endpoint, so this pure builder needs no access context —
    /// a run's status is a property of the run, not the viewer. Returns null when the orchestration
    /// service or the run is unavailable.
    /// </summary>
    internal static ReportingRunAuditTrailDto? TryBuildReportRunAuditTrail(IServiceProvider services, string runId)
    {
        var orchestration = services.GetService<IReportingOrchestrationService>();
        if (orchestration is null)
        {
            return null;
        }

        var manifest = orchestration.GetManifest((runId ?? string.Empty).Trim());
        return manifest is null
            ? null
            : ProjectReportingRunAuditTrail(manifest, orchestration.GetAudit(manifest.RunId));
    }
}

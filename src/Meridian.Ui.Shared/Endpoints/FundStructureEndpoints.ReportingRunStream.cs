using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
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
    /// run's payload by its immutable tenant, company, and run identity.
    /// </summary>
    public static void MapReportingRunStreamEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/fund-structure")
            .WithTags("Fund Structure")
            .RequireWorkstationTenantScope();

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
            var accessContext = BuildReportAccessQueryContext(context);
            if (accessContext.RequireBoundScope != true
                || string.IsNullOrWhiteSpace(accessContext.TenantId)
                || string.IsNullOrWhiteSpace(accessContext.CompanyId))
            {
                return Results.Problem(
                    "Reporting run streams require a bound tenant and company scope.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var manifest = orchestration.GetManifest(
                    accessContext.TenantId,
                    trimmedRunId)
                ?? orchestration.GetManifest(trimmedRunId);
            if (manifest is null)
            {
                return Results.Problem($"Reporting run '{runId}' was not found.", statusCode: StatusCodes.Status404NotFound);
            }

            // Historical run access is governed by the immutable tenant and access snapshot retained
            // on the manifest, not by the template's current policy. Legacy unscoped manifests fail
            // closed on this tenant-bound endpoint.
            var evaluation = ReportAccessPolicyEvaluator.Evaluate(manifest, accessContext);
            if (!evaluation.IsAccessible)
            {
                return Results.Problem(evaluation.Reason, statusCode: StatusCodes.Status403Forbidden);
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
                StreamTopic.ReportRun(
                    accessContext.TenantId,
                    accessContext.CompanyId,
                    manifest.RunId),
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
        .WithName("GetReportingRunStream").RequireAnyPermission(UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.ApproveReporting, UserPermission.DeliverReporting, UserPermission.AdminMaintenance)
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status429TooManyRequests)
        .Produces(StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>
    /// Build a report-run status/audit payload by scoped identity for report-run stream fan-out
    /// (<see cref="Meridian.Ui.Shared.Streaming.ReportRunStreamBroadcaster"/>). Authorization is
    /// enforced at subscribe time by the SSE endpoint. Returns null when the orchestration service,
    /// scoped run, or retained company binding is unavailable. Legacy unscoped topic arguments remain
    /// readable only when the orchestration boundary can resolve one unambiguous run.
    /// </summary>
    internal static ReportingRunAuditTrailDto? TryBuildReportRunAuditTrail(IServiceProvider services, string runId)
    {
        var orchestration = services.GetService<IReportingOrchestrationService>();
        if (orchestration is null)
        {
            return null;
        }

        var argument = (runId ?? string.Empty).Trim();
        ReportingOutputManifest? manifest;
        IReadOnlyList<ReportingRunAuditEntry> audit;
        if (StreamTopic.TryParseScopedReportRun(
                argument,
                out var tenantId,
                out var companyId,
                out var scopedRunId))
        {
            manifest = orchestration.GetManifest(tenantId, scopedRunId);
            if (!string.Equals(
                    manifest?.OperationalScope?.CompanyId,
                    companyId,
                    StringComparison.Ordinal))
            {
                return null;
            }

            audit = orchestration.GetAudit(tenantId, scopedRunId);
        }
        else
        {
            manifest = orchestration.GetManifest(argument);
            audit = manifest is null
                ? []
                : orchestration.GetAudit(manifest.RunId);
        }

        return manifest is null
            ? null
            : ProjectReportingRunAuditTrail(manifest, audit);
    }
}

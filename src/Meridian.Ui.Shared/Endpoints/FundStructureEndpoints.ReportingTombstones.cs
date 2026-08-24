using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class FundStructureEndpoints
{
    /// <summary>
    /// Why the 410 Gone tombstones for the retired reporting lifecycle stay reachable without a
    /// permission: they perform no action and only answer with the canonical replacement route, so
    /// guarding them would swap that pointer for a 403 while protecting nothing. Remove the
    /// declaration together with the tombstone when the route is unmapped.
    /// </summary>
    private const string LegacyReportingTombstoneReason =
        "410 Gone tombstone for the retired reporting lifecycle; it performs no action and only " +
        "points the caller at the canonical replacement route.";

    /// <summary>
    /// Maps the 410 Gone tombstones for the retired report-pack lifecycle: every route answers
    /// with the canonical replacement and performs no action.
    /// </summary>
    private static void MapLegacyReportingPackTombstones(WebApplication app, RouteGroupBuilder reportingGroup)
    {
        reportingGroup.MapPost("/packs/create", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/runs",
                "Legacy report-pack creation bypassed the canonical run contract and certified snapshot."))
        .DeclarePermissionlessMutation(LegacyReportingTombstoneReason);

        reportingGroup.MapPost("/packs", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/runs",
                "Legacy report-pack creation bypassed the canonical run contract and certified snapshot."))
        .WithName("CreateReportingPackWorkflow")
        .DeclarePermissionlessMutation(LegacyReportingTombstoneReason)
        .ProducesProblem(StatusCodes.Status410Gone);

        reportingGroup.MapPost("/packs/{reportId:guid}/validate", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/runs/{runId}/validate",
                "The legacy pack lifecycle was mutable and is not authoritative."))
        .DeclarePermissionlessMutation(LegacyReportingTombstoneReason);
        reportingGroup.MapPost("/packs/{reportId:guid}/submit", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/runs/{runId}/submit",
                "The legacy pack lifecycle was mutable and is not authoritative."))
        .DeclarePermissionlessMutation(LegacyReportingTombstoneReason);
        reportingGroup.MapPost("/packs/{reportId:guid}/approve", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/runs/{runId}/approve",
                "The legacy pack lifecycle did not enforce the canonical maker-checker state machine."))
        .DeclarePermissionlessMutation(LegacyReportingTombstoneReason);
        reportingGroup.MapPost("/packs/{reportId:guid}/reject", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/runs",
                "Legacy rejection mutated the old pack record; remediation must create or advance a governed run."))
        .WithName("RejectReportingPackWorkflow")
        .DeclarePermissionlessMutation(LegacyReportingTombstoneReason)
        .ProducesProblem(StatusCodes.Status410Gone);
        reportingGroup.MapPost("/packs/{reportId:guid}/publish", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/runs/{runId}/release",
                "Legacy publication accepted caller-supplied signers, hashes, manifest ids, and retention paths instead of verified retained artifacts."))
        .DeclarePermissionlessMutation(LegacyReportingTombstoneReason);

        reportingGroup.MapPost("/packs/{reportId:guid}/restatements", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/runs/{runId}/restatement-requests",
                "In-place legacy restatement could rewrite a released pack; governed restatement creates a new revision after independent approval."))
        .WithName("RestateReportingPackWorkflow")
        .DeclarePermissionlessMutation(LegacyReportingTombstoneReason)
        .ProducesProblem(StatusCodes.Status410Gone);

        reportingGroup.MapPost("/packs/{reportId:guid}/archive", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/runs/{runId}",
                "Client-driven archive mutation is not part of the immutable governed reporting lifecycle."))
        .DeclarePermissionlessMutation(LegacyReportingTombstoneReason);

        reportingGroup.MapGet("/packs/{reportId:guid}/deliveries", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/distribution/packages/{runId}/deliveries",
                "Legacy delivery history embedded deterministic query-token routes and synthetic delivery state."))
        .WithName("GetReportingPackDeliveryHistory").RequireAnyPermission(UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.ApproveReporting, UserPermission.DeliverReporting, UserPermission.AdminMaintenance)
        .ProducesProblem(StatusCodes.Status410Gone);

        reportingGroup.MapGet("/packs/{reportId:guid}/deliveries/{attemptId:guid}/package", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/portal/reporting/access-grants/{grantId}/exchange",
                "Query-string package tokens are retired; exchange an opaque, scoped grant in a POST body."))
        .WithName("GetReportingPackDeliveryPackage").RequireAnyPermission(UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.ApproveReporting, UserPermission.DeliverReporting, UserPermission.AdminMaintenance)
        .ProducesProblem(StatusCodes.Status410Gone);

        reportingGroup.MapGet("/packs/{reportId:guid}/deliveries/{attemptId:guid}/artifacts/{artifactName}", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/distribution/packages/{runId}/artifacts/{artifactId}",
                "Query-string artifact tokens are retired; authenticated downloads now use the immutable artifact vault."))
        .WithName("GetReportingPackDeliveryArtifact").RequireAnyPermission(UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.ApproveReporting, UserPermission.DeliverReporting, UserPermission.AdminMaintenance)
        .ProducesProblem(StatusCodes.Status410Gone);

        app.MapGet(UiApiRoutes.ReportingPackDeliveryPortalPackage, (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/portal/reporting/access-grants/{grantId}/exchange",
                "Query-string portal tokens are retired; the opaque grant must be exchanged through a no-store POST body."))
        .WithName("GetReportingPortalDeliveryPackage").DeclareOpenRead("Retired route that only answers 410 Gone with a pointer to the grant-exchange replacement; it reads nothing.")
        .ProducesProblem(StatusCodes.Status410Gone);

        reportingGroup.MapPost("/packs/{reportId:guid}/deliveries", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/distribution/deliveries",
                "Legacy delivery could bypass canonical Released verification and durable transport receipts."))
        .WithName("CreateReportingPackDeliveryAttempt")
        .DeclarePermissionlessMutation(LegacyReportingTombstoneReason)
        .ProducesProblem(StatusCodes.Status410Gone);

        reportingGroup.MapPost("/packs/{reportId:guid}/deliveries/failures", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/distribution/deliveries/{jobId}",
                "Client-recorded synthetic failures are retired; provider receipts are server-owned and reflected on the durable delivery job."))
        .WithName("CreateReportingPackDeliveryFailure")
        .DeclarePermissionlessMutation(LegacyReportingTombstoneReason)
        .ProducesProblem(StatusCodes.Status410Gone);

        reportingGroup.MapPost("/packs/{reportId:guid}/restate", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/runs/{runId}/restatement-requests",
                "In-place legacy restatement is retired; approval creates a new immutable governed revision."))
        .DeclarePermissionlessMutation(LegacyReportingTombstoneReason);
    }

    private static IResult LegacyReportingRouteGone(
        HttpContext context,
        string canonicalRoute,
        string reason)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        return Results.Problem(
            detail: $"{reason} Use {canonicalRoute}.",
            statusCode: StatusCodes.Status410Gone,
            title: "Legacy reporting route retired");
    }
}

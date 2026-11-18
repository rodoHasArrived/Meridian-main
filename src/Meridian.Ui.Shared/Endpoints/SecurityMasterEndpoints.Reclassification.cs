using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Meridian.Ui.Shared.Endpoints;

// Report-only Security Master reclassification endpoints: the ADR-022 structured-credit
// reclassification report and the taxonomy-revision classification drift report. Neither writes.
public static partial class SecurityMasterEndpoints
{
    // ── Structured-credit reclassification report (ADR-022) ─────────────────

    private static void MapStructuredCreditReclassificationEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        // Report only: the scan proposes StructuredCredit terms for legacy securitised Bond and
        // CustomAsset rows and never writes. Applying a proposal is a governed amendment.
        group.MapPost(UiApiRoutes.SecurityMasterStructuredCreditReclassificationReport, async (
            HttpContext context,
            [FromServices] ISecurityMasterStructuredCreditReclassificationService reclassificationService,
            CancellationToken ct) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance))
                return EndpointHelpers.Forbidden();

            var report = await reclassificationService.BuildReportAsync(ct).ConfigureAwait(false);
            return Results.Json(report, jsonOptions);
        })
        .WithName("BuildSecurityMasterStructuredCreditReclassificationReport")
        .Produces<StructuredCreditReclassificationReportDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden);
    }

    // ── Classification drift report (taxonomy revisions) ────────────────────

    private static void MapClassificationDriftEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        // Report only: lists active securities whose retained classification differs from the
        // current taxonomy. Classification is derived on read, and retained events are append-only.
        group.MapPost(UiApiRoutes.SecurityMasterClassificationDriftReport, async (
            HttpContext context,
            [FromServices] ISecurityMasterClassificationDriftReportService driftReportService,
            CancellationToken ct) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance))
                return EndpointHelpers.Forbidden();

            var report = await driftReportService.BuildReportAsync(ct).ConfigureAwait(false);
            return Results.Json(report, jsonOptions);
        })
        .WithName("BuildSecurityMasterClassificationDriftReport")
        .Produces<SecurityClassificationDriftReportDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden);
    }
}

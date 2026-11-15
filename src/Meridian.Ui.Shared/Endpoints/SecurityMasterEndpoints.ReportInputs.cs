using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class SecurityMasterEndpoints
{
    private static void MapSecurityMasterReportInputs(RouteGroupBuilder group, JsonSerializerOptions options)
    {
        group.MapGet(UiApiRoutes.SecurityMasterReportInputs, async (Guid securityId, string? runId, HttpContext context) =>
        {
            if (!HasReportInputPermission(context))
                return EndpointHelpers.Forbidden();
            var service = context.RequestServices.GetService<ReleasedReportSecurityInputReadService>();
            if (service is null)
                return Results.Problem("Retained report inputs are unavailable in this host.", statusCode: 503);
            try
            { return Results.Json(await service.GetComparisonsAsync(securityId, runId, context.RequestAborted).ConfigureAwait(false), options); }
            catch (UnauthorizedAccessException) { return EndpointHelpers.Forbidden(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (Exception exception) when (exception is InvalidDataException or ReportingGovernanceException)
            { return Results.Problem("Exact retained report inputs could not be verified.", statusCode: 503); }
        }).WithName("CompareSecurityMasterReportInputs").RequireAnyPermission(UserPermission.ViewReporting,
            UserPermission.ManageReporting, UserPermission.ApproveReporting, UserPermission.DeliverReporting, UserPermission.AdminMaintenance);

        group.MapGet(UiApiRoutes.SecurityMasterReportInputLinks, async (string runId, HttpContext context) =>
        {
            if (!HasReportInputPermission(context))
                return EndpointHelpers.Forbidden();
            var service = context.RequestServices.GetService<ReleasedReportSecurityInputReadService>();
            if (service is null)
                return Results.Problem("Retained report inputs are unavailable in this host.", statusCode: 503);
            try
            { return Results.Json(await service.GetRunLinksAsync(runId, context.RequestAborted).ConfigureAwait(false), options); }
            catch (UnauthorizedAccessException) { return EndpointHelpers.Forbidden(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (Exception exception) when (exception is InvalidDataException or ReportingGovernanceException)
            { return Results.Problem("Exact retained report inputs could not be verified.", statusCode: 503); }
        }).WithName("ListReportSecurityInputLinks").RequireAnyPermission(UserPermission.ViewReporting,
            UserPermission.ManageReporting, UserPermission.ApproveReporting, UserPermission.DeliverReporting, UserPermission.AdminMaintenance);
    }

    private static bool HasReportInputPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(context, UserPermission.ViewReporting, UserPermission.ManageReporting,
            UserPermission.ApproveReporting, UserPermission.DeliverReporting, UserPermission.AdminMaintenance);
}

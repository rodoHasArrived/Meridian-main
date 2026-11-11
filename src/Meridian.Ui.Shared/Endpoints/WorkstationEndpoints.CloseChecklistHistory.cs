using System.Text.Json;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static void MapCloseChecklistHistoryEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet("/operations/continuity/{workflowId:guid}/checklist/history", async (Guid workflowId, HttpContext context) =>
        {
            if (!HasOperationsContinuityReadPermission(context)) return EndpointHelpers.Forbidden();
            try
            {
                // The same exact account/book/fund ownership seam used by approval collaboration.
                var authority = await ResolveApprovalCollaborationAuthorityAsync(workflowId, context).ConfigureAwait(false);
                if (authority is null) return EndpointHelpers.Forbidden();
                var timeline = await authority.Value.Service.GetTimelineAsync(workflowId, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(OperationsCloseChecklistHistory.Build(workflowId, timeline, DateTimeOffset.UtcNow), jsonOptions);
            }
            catch (UnauthorizedAccessException) { return EndpointHelpers.Forbidden(); }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or System.Data.Common.DbException)
            {
                return Results.Problem("Retained close checklist history is unavailable or fails its audit integrity check.", statusCode: 503);
            }
        }).WithName("GetOperationsCloseChecklistHistory").RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope()
            .RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster,
                UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance);
    }
}

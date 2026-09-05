using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static void MapCloseReadinessEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.FinancialOperationsCommandCenter), async (
            string? fundProfileId, Guid? ledgerBookId, Guid? fundAccountId, string? periodId,
            string? entityId, HttpContext context) =>
        {
            if (!HasOperationsContinuityReadPermission(context))
                return EndpointHelpers.Forbidden();
            var service = context.RequestServices.GetService<IFinancialOperationsCommandCenterReadService>();
            if (service is null)
                return Results.Problem("Close readiness is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var payload = await service.GetCommandCenterAsync(fundProfileId, ledgerBookId, fundAccountId,
                periodId, entityId, context.RequestAborted, tenant.TenantId, tenant.CompanyId).ConfigureAwait(false);
            return Results.Json(payload, jsonOptions);
        })
        .WithName("GetFinancialOperationsCommandCenter")
        .RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster,
            UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .RequireFundProfileTenantScope(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster,
            UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<FinancialOperationsCommandCenterDto>(200)
        .Produces(403)
        .Produces(503);
    }
}

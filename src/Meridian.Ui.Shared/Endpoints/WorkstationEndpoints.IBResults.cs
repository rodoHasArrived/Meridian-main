using Meridian.Contracts.Api;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static void MapIBResultEndpoints(RouteGroupBuilder group, System.Text.Json.JsonSerializerOptions jsonOptions)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.IBResults), (
            string? family, string? accountId, string? modelAccountId,
            IWorkstationTenantContextAccessor tenantContext, IBResultQueryService results) =>
        {
            var tenant = tenantContext.GetRequired();
            var items = results.Get(tenant.TenantId!, family, accountId, modelAccountId)
                .Select(x => new
                {
                    request = x.Request,
                    lineage = x.Lineage,
                    availability = x.Lineage?.Availability.ToString(),
                    delayed = x.Lineage?.IsDelayed ?? false,
                    subscriptionEvidence = x.Lineage?.Subscription,
                    terminalStatus = x.Request.Status.ToString(),
                    requestErrors = new { x.Request.ErrorCode, x.Request.ErrorMessage }
                });
            return Results.Json(new { provider = "interactive-brokers", results = items }, jsonOptions);
        }).WithName("GetWorkstationIBResults").Produces(200).Produces(403);
    }
}

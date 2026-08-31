using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>Exposes one provider-neutral projection for every workstation client.</summary>
public static class ProviderDataProjectionEndpoints
{
    public static void MapProviderDataProjectionEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapGet(UiApiRoutes.ProviderDataProjection, (
            [FromServices] ProviderDataReadModelService service,
            [FromServices] IWorkstationTenantContextAccessor tenantContext) =>
        {
            var tenant = tenantContext.GetRequired();
            return Results.Json(
                CreateProjection(service, tenant.TenantId!, tenant.CompanyId!),
                jsonOptions);
        })
            .WithName("GetProviderDataProjection")
            .WithTags("Providers")
            .Produces<ProviderDataProjectionSnapshot>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireWorkstationTenantScope()
            .RequireWorkstationTenantCompanyScope()
            .RequirePermission(UserPermission.ViewTrades);
    }

    public static ProviderDataProjectionSnapshot CreateProjection(ProviderDataReadModelService service) =>
        (service ?? throw new ArgumentNullException(nameof(service))).GetProjection();

    public static ProviderDataProjectionSnapshot CreateProjection(
        ProviderDataReadModelService service,
        string tenantId,
        string companyId) =>
        (service ?? throw new ArgumentNullException(nameof(service))).GetProjection(tenantId, companyId);
}

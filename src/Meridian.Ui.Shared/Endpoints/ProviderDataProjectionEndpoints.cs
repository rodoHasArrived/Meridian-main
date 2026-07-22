using System.Text.Json;
using Meridian.Contracts.Api;
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
        app.MapGet(UiApiRoutes.ProviderDataProjection, ([FromServices] ProviderDataReadModelService service) =>
            Results.Json(CreateProjection(service), jsonOptions))
            .WithName("GetProviderDataProjection")
            .WithTags("Providers")
            .Produces<ProviderDataProjectionSnapshot>(StatusCodes.Status200OK);
    }

    public static ProviderDataProjectionSnapshot CreateProjection(ProviderDataReadModelService service) =>
        (service ?? throw new ArgumentNullException(nameof(service))).GetProjection();
}

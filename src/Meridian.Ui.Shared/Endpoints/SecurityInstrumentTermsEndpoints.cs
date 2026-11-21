using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Meridian.ReferenceData.SecurityMaster;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>Read-only, exact-value instrument terms for browser and desktop clients.</summary>
public static class SecurityInstrumentTermsEndpoints
{
    public static void MapSecurityInstrumentTermsEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        if (app.Services.GetService<ISecurityMasterQueryService>() is null) return;

        app.MapGet(UiApiRoutes.SecurityMasterInstrumentTerms, async (
            Guid securityId,
            HttpContext context,
            [FromServices] ISecurityMasterQueryService query,
            CancellationToken ct) =>
        {
            var profiles = context.RequestServices.GetService<ISecurityAssetProfileCatalog>();
            var result = await new SecurityInstrumentTermsReadService(query, profiles)
                .ReadAsync(securityId, ct).ConfigureAwait(false);
            context.Response.Headers.CacheControl = "no-store";
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("GetSecurityMasterInstrumentTerms")
        .WithTags("SecurityMaster")
        .RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<SecurityInstrumentTermsDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);
    }
}

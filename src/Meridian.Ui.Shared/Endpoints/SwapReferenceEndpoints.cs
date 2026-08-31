using System.Text.Json;
using Meridian.Instruments.Derivatives;
using Meridian.Contracts.Api;
using Meridian.Contracts.Derivatives;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

public static class SwapReferenceEndpoints
{
    public static void MapSwapReferenceEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(string.Empty).WithTags("SwapReference");

        group.MapGet(UiApiRoutes.ReferenceDataSwapReference, async (
            Guid securityId,
            [FromServices] ISwapReferenceService service,
            CancellationToken ct) =>
        {
            var reference = await service.GetReferenceAsync(securityId, ct).ConfigureAwait(false);
            return reference is null ? Results.NotFound() : Results.Json(reference, jsonOptions);
        })
        .WithName("GetSwapReference").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<SwapReferenceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(UiApiRoutes.ReferenceDataSwapsByType, async (
            [FromQuery] string swapType,
            [FromServices] ISwapReferenceService service,
            CancellationToken ct) =>
        {
            var results = await service.GetBySwapTypeAsync(swapType, ct).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetSwapsByType").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<IReadOnlyList<SwapReferenceDto>>(StatusCodes.Status200OK);

        group.MapGet(UiApiRoutes.ReferenceDataSwapsMaturingBefore, async (
            [FromQuery] DateOnly beforeDate,
            [FromServices] ISwapReferenceService service,
            CancellationToken ct) =>
        {
            var results = await service.GetMaturingBeforeAsync(beforeDate, ct).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetSwapsMaturingBefore").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<IReadOnlyList<SwapReferenceDto>>(StatusCodes.Status200OK);
    }
}

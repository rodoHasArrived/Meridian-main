using System.Text.Json;
using Meridian.Instruments.Futures;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Contracts.Futures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

public static class FutureReferenceEndpoints
{
    public static void MapFutureReferenceEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(string.Empty).WithTags("FutureReference");
        group.AddEndpointFilter(EndpointAuthorization.RequireAny(
            UserPermission.ViewSecurityMaster,
            UserPermission.ModifySecurityMaster));

        group.MapGet(UiApiRoutes.ReferenceDataFutureReference, async (
            Guid securityId,
            [FromServices] IFutureReferenceService service,
            CancellationToken ct) =>
        {
            var reference = await service.GetReferenceAsync(securityId, ct).ConfigureAwait(false);
            return reference is null ? Results.NotFound() : Results.Json(reference, jsonOptions);
        })
        .WithName("GetFutureReference").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<FutureReferenceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(UiApiRoutes.ReferenceDataFuturesByRoot, async (
            [FromQuery] string rootSymbol,
            [FromServices] IFutureReferenceService service,
            CancellationToken ct) =>
        {
            var results = await service.GetByRootSymbolAsync(rootSymbol, ct).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetFuturesByRootSymbol").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<IReadOnlyList<FutureReferenceDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapGet(UiApiRoutes.ReferenceDataFutureExpiryLadder, async (
            [FromQuery] string rootSymbol,
            [FromServices] IFutureReferenceService service,
            CancellationToken ct) =>
        {
            var ladder = await service.GetExpiryLadderAsync(rootSymbol, ct).ConfigureAwait(false);
            return Results.Json(ladder, jsonOptions);
        })
        .WithName("GetFutureExpiryLadder").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<IReadOnlyList<FutureReferenceDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapGet(UiApiRoutes.ReferenceDataFutureFrontMonth, async (
            [FromQuery] string rootSymbol,
            [FromServices] IFutureReferenceService service,
            CancellationToken ct) =>
        {
            var frontMonth = await service.GetFrontMonthAsync(rootSymbol, ct).ConfigureAwait(false);
            return frontMonth is null ? Results.NotFound() : Results.Json(frontMonth, jsonOptions);
        })
        .WithName("GetFutureFrontMonth").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<FutureReferenceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);
    }
}

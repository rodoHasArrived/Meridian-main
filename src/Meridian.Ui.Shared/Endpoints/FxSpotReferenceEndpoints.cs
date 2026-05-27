using System.Text.Json;
using Meridian.Application.FxSpot;
using Meridian.Contracts.Api;
using Meridian.Contracts.FxSpot;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

public static class FxSpotReferenceEndpoints
{
    public static void MapFxSpotReferenceEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(string.Empty).WithTags("FxSpotReference");

        group.MapGet(UiApiRoutes.ReferenceDataFxSpotReference, async (
            Guid securityId,
            [FromServices] IFxSpotReferenceService service,
            CancellationToken ct) =>
        {
            var reference = await service.GetReferenceAsync(securityId, ct).ConfigureAwait(false);
            return reference is null ? Results.NotFound() : Results.Json(reference, jsonOptions);
        })
        .WithName("GetFxSpotReference")
        .Produces<FxSpotReferenceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(UiApiRoutes.ReferenceDataFxSpotByPairCode, async (
            [FromRoute] string pairCode,
            [FromServices] IFxSpotReferenceService service,
            CancellationToken ct) =>
        {
            var reference = await service.GetByPairCodeAsync(pairCode, ct).ConfigureAwait(false);
            return reference is null ? Results.NotFound() : Results.Json(reference, jsonOptions);
        })
        .WithName("GetFxSpotByPairCode")
        .Produces<FxSpotReferenceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(UiApiRoutes.ReferenceDataFxSpotByCurrency, async (
            [FromQuery] string currency,
            [FromServices] IFxSpotReferenceService service,
            CancellationToken ct) =>
        {
            var pairs = await service.GetByCurrencyAsync(currency, ct).ConfigureAwait(false);
            return Results.Json(pairs, jsonOptions);
        })
        .WithName("GetFxSpotByCurrency")
        .Produces<IReadOnlyList<FxSpotReferenceDto>>(StatusCodes.Status200OK);
    }
}

using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Commodities;
using Meridian.Instruments.Commodities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

public static class CommodityReferenceEndpoints
{
    public static void MapCommodityReferenceEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(string.Empty).WithTags("CommodityReference");

        group.MapGet(UiApiRoutes.ReferenceDataCommodityReference, async (
            Guid securityId,
            [FromServices] ICommodityReferenceService service,
            CancellationToken ct) =>
        {
            var reference = await service.GetReferenceAsync(securityId, ct).ConfigureAwait(false);
            return reference is null ? Results.NotFound() : Results.Json(reference, jsonOptions);
        })
        .WithName("GetCommodityReference")
        .Produces<CommodityReferenceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(UiApiRoutes.ReferenceDataCommoditiesByType, async (
            [FromQuery] string commodityType,
            [FromServices] ICommodityReferenceService service,
            CancellationToken ct) =>
        {
            var results = await service.GetByCommodityTypeAsync(commodityType, ct).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetCommoditiesByType")
        .Produces<IReadOnlyList<CommodityReferenceDto>>(StatusCodes.Status200OK);

        group.MapGet(UiApiRoutes.ReferenceDataCommoditiesByExchange, async (
            [FromQuery] string exchangeCode,
            [FromServices] ICommodityReferenceService service,
            CancellationToken ct) =>
        {
            var results = await service.GetByExchangeAsync(exchangeCode, ct).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetCommoditiesByExchange")
        .Produces<IReadOnlyList<CommodityReferenceDto>>(StatusCodes.Status200OK);
    }
}

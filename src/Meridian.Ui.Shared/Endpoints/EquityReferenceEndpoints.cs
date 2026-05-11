using System.Text.Json;
using Meridian.Application.Equity;
using Meridian.Contracts.Api;
using Meridian.Contracts.Equity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

public static class EquityReferenceEndpoints
{
    public static void MapEquityReferenceEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(string.Empty).WithTags("EquityReference");

        group.MapGet(UiApiRoutes.ReferenceDataEquityReference, async (
            Guid securityId,
            [FromServices] IEquityReferenceService service,
            CancellationToken ct) =>
        {
            var reference = await service.GetReferenceAsync(securityId, ct).ConfigureAwait(false);
            return reference is null ? Results.NotFound() : Results.Json(reference, jsonOptions);
        })
        .WithName("GetEquityReference")
        .Produces<EquityReferenceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(UiApiRoutes.ReferenceDataEquityByExchange, async (
            [FromQuery] string exchangeCode,
            [FromServices] IEquityReferenceService service,
            CancellationToken ct) =>
        {
            var results = await service.GetByExchangeAsync(exchangeCode, ct).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetEquityByExchange")
        .Produces<IReadOnlyList<EquityReferenceDto>>(StatusCodes.Status200OK);

        group.MapGet(UiApiRoutes.ReferenceDataEquityByIssuer, async (
            [FromQuery] string issuerName,
            [FromServices] IEquityReferenceService service,
            CancellationToken ct) =>
        {
            var results = await service.GetByIssuerAsync(issuerName, ct).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetEquityByIssuer")
        .Produces<IReadOnlyList<EquityReferenceDto>>(StatusCodes.Status200OK);
    }
}

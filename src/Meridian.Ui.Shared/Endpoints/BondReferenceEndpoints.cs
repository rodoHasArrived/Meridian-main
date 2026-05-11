using System.Text.Json;
using Meridian.Application.FixedIncome;
using Meridian.Contracts.Api;
using Meridian.Contracts.FixedIncome;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

public static class BondReferenceEndpoints
{
    public static void MapBondReferenceEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(string.Empty).WithTags("BondReference");

        group.MapGet(UiApiRoutes.ReferenceDataBondReference, async (
            Guid securityId,
            [FromServices] IBondReferenceService service,
            CancellationToken ct) =>
        {
            var reference = await service.GetReferenceAsync(securityId, ct).ConfigureAwait(false);
            return reference is null ? Results.NotFound() : Results.Json(reference, jsonOptions);
        })
        .WithName("GetBondReference")
        .Produces<BondReferenceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(UiApiRoutes.ReferenceDataBondLifecycle, async (
            Guid securityId,
            [FromServices] IBondReferenceService service,
            CancellationToken ct) =>
        {
            var lifecycle = await service.GetLifecycleAsync(securityId, ct).ConfigureAwait(false);
            return lifecycle is null ? Results.NotFound() : Results.Json(lifecycle, jsonOptions);
        })
        .WithName("GetBondLifecycle")
        .Produces<BondLifecycleDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(UiApiRoutes.ReferenceDataBondAccrualConvention, async (
            Guid securityId,
            [FromServices] IBondReferenceService service,
            CancellationToken ct) =>
        {
            var accrual = await service.GetAccrualConventionAsync(securityId, ct).ConfigureAwait(false);
            return accrual is null ? Results.NotFound() : Results.Json(accrual, jsonOptions);
        })
        .WithName("GetBondAccrualConvention")
        .Produces<BondAccrualConventionDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(UiApiRoutes.ReferenceDataBondIssuerLadder, async (
            [FromQuery] string issuerName,
            [FromServices] IBondReferenceService service,
            CancellationToken ct) =>
        {
            var ladder = await service.GetIssuerLadderAsync(issuerName, ct).ConfigureAwait(false);
            return Results.Json(ladder, jsonOptions);
        })
        .WithName("GetBondIssuerLadder")
        .Produces<IReadOnlyList<BondReferenceDto>>(StatusCodes.Status200OK);

        group.MapGet(UiApiRoutes.ReferenceDataBondMaturityLadder, async (
            [FromQuery] DateOnly from,
            [FromQuery] DateOnly to,
            [FromServices] IBondReferenceService service,
            CancellationToken ct) =>
        {
            var ladder = await service.GetMaturityLadderAsync(from, to, ct).ConfigureAwait(false);
            return Results.Json(ladder, jsonOptions);
        })
        .WithName("GetBondMaturityLadder")
        .Produces<IReadOnlyList<BondReferenceDto>>(StatusCodes.Status200OK);
    }
}

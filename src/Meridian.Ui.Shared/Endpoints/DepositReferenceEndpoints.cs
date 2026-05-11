using System.Text.Json;
using Meridian.Application.Deposits;
using Meridian.Contracts.Api;
using Meridian.Contracts.Deposits;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

public static class DepositReferenceEndpoints
{
    public static void MapDepositReferenceEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(string.Empty).WithTags("DepositReference");

        group.MapGet(UiApiRoutes.ReferenceDataDepositReference, async (
            Guid securityId,
            [FromServices] IDepositReferenceService service,
            CancellationToken ct) =>
        {
            var reference = await service.GetReferenceAsync(securityId, ct).ConfigureAwait(false);
            return reference is null ? Results.NotFound() : Results.Json(reference, jsonOptions);
        })
        .WithName("GetDepositReference")
        .Produces<DepositReferenceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(UiApiRoutes.ReferenceDataDepositsByInstitution, async (
            [FromQuery] string institutionName,
            [FromServices] IDepositReferenceService service,
            CancellationToken ct) =>
        {
            var results = await service.GetByInstitutionAsync(institutionName, ct).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetDepositsByInstitution")
        .Produces<IReadOnlyList<DepositReferenceDto>>(StatusCodes.Status200OK);

        group.MapGet(UiApiRoutes.ReferenceDataDepositsMaturingBefore, async (
            [FromQuery] DateOnly beforeDate,
            [FromServices] IDepositReferenceService service,
            CancellationToken ct) =>
        {
            var results = await service.GetMaturingBeforeAsync(beforeDate, ct).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetDepositsMaturingBefore")
        .Produces<IReadOnlyList<DepositReferenceDto>>(StatusCodes.Status200OK);
    }
}

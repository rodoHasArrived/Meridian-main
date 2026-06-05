using System.Text.Json;
using Meridian.Instruments.Options;
using Meridian.Contracts.Api;
using Meridian.Contracts.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

public static class OptionReferenceEndpoints
{
    public static void MapOptionReferenceEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(string.Empty).WithTags("OptionReference");

        group.MapGet(UiApiRoutes.ReferenceDataOptionContract, async (
            [FromRoute] string contractSymbol,
            [FromServices] IOptionReferenceService service,
            CancellationToken ct) =>
        {
            var contract = await service.GetContractAsync(contractSymbol, ct).ConfigureAwait(false);
            return contract is null ? Results.NotFound() : Results.Json(contract, jsonOptions);
        })
        .WithName("GetOptionContractReference")
        .Produces<OptionContractReferenceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(UiApiRoutes.ReferenceDataOptionSeries, async (
            [FromRoute] string optionChainId,
            [FromQuery] DateOnly expiryDate,
            [FromServices] IOptionReferenceService service,
            CancellationToken ct) =>
        {
            var series = await service.GetSeriesAsync(optionChainId, expiryDate, ct).ConfigureAwait(false);
            return series is null ? Results.NotFound() : Results.Json(series, jsonOptions);
        })
        .WithName("GetOptionSeriesReference")
        .Produces<OptionSeriesDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(UiApiRoutes.ReferenceDataOptionUnderlyingLinkage, async (
            [FromRoute] string contractSymbol,
            [FromServices] IOptionReferenceService service,
            CancellationToken ct) =>
        {
            var linkage = await service.GetUnderlyingLinkageAsync(contractSymbol, ct).ConfigureAwait(false);
            return linkage is null ? Results.NotFound() : Results.Json(linkage, jsonOptions);
        })
        .WithName("GetOptionUnderlyingLinkage")
        .Produces<OptionContractReferenceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(UiApiRoutes.ReferenceDataOptionExpiryLadder, async (
            [FromQuery] string underlyingSymbol,
            [FromServices] IOptionReferenceService service,
            CancellationToken ct) =>
        {
            var ladder = await service.GetExpiryLadderAsync(underlyingSymbol, ct).ConfigureAwait(false);
            return Results.Json(ladder, jsonOptions);
        })
        .WithName("GetOptionExpiryLadder")
        .Produces<IReadOnlyList<DateOnly>>(StatusCodes.Status200OK);
    }
}

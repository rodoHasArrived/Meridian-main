using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.MoneyMarketFunds;
using Meridian.Instruments.MoneyMarketFunds;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

public static class MoneyMarketFundReferenceEndpoints
{
    public static void MapMoneyMarketFundReferenceEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(string.Empty).WithTags("MoneyMarketFundReference");

        group.MapGet(UiApiRoutes.ReferenceDataMoneyMarketFundReference, async (
            Guid securityId,
            [FromServices] IMoneyMarketFundReferenceService service,
            CancellationToken ct) =>
        {
            var reference = await service.GetReferenceAsync(securityId, ct).ConfigureAwait(false);
            return reference is null ? Results.NotFound() : Results.Json(reference, jsonOptions);
        })
        .WithName("GetMoneyMarketFundReference")
        .Produces<MoneyMarketFundReferenceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(UiApiRoutes.ReferenceDataMoneyMarketFundsByFamily, async (
            [FromQuery] string fundFamily,
            [FromServices] IMoneyMarketFundReferenceService service,
            CancellationToken ct) =>
        {
            var results = await service.GetByFundFamilyAsync(fundFamily, ct).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetMoneyMarketFundsByFamily")
        .Produces<IReadOnlyList<MoneyMarketFundReferenceDto>>(StatusCodes.Status200OK);

        group.MapGet(UiApiRoutes.ReferenceDataMoneyMarketFundsBySweepEligibility, async (
            [FromQuery] bool sweepEligible,
            [FromServices] IMoneyMarketFundReferenceService service,
            CancellationToken ct) =>
        {
            var results = await service.GetBySweepEligibilityAsync(sweepEligible, ct).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetMoneyMarketFundsBySweepEligibility")
        .Produces<IReadOnlyList<MoneyMarketFundReferenceDto>>(StatusCodes.Status200OK);
    }
}

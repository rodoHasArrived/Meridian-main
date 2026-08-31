using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Instruments.AssetOperations;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Identity.Auth;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Minimal API endpoint group for the portfolio-wide cash ladder: aggregated
/// projected inflows/outflows by source over a forward horizon, with a
/// cumulative cash line, minimum-balance breaches, and stress scenarios.
/// </summary>
public static class PortfolioCashLadderEndpoints
{
    private const string RoutePrefix = "/api/portfolio/cash-ladder";
    private const int MaxHorizonDays = 366;

    public static void MapPortfolioCashLadderEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(RoutePrefix).WithTags("PortfolioCashLadder");

        /// <summary>
        /// Returns the portfolio cash ladder for the requested horizon and scenario.
        /// Every instrument contribution is traceable to its projection run and
        /// Security Master terms version; scenario output states what is modeled
        /// versus assumed.
        /// </summary>
        group.MapGet("/", async (
            HttpContext context,
            CancellationToken ct) =>
        {
            var service = context.RequestServices.GetService<IPortfolioCashLadderQueryService>()
                ?? new PortfolioCashLadderReadService();
            var query = new PortfolioCashLadderQuery(
                ScenarioId: context.Request.Query["scenario"].FirstOrDefault(),
                HorizonDays: ParseBoundedInt(context.Request.Query["horizonDays"].FirstOrDefault(), 90, 1, MaxHorizonDays),
                MinimumCashThreshold: ParseOptionalDecimal(context.Request.Query["minimumCash"].FirstOrDefault()),
                BucketDays: ParseBoundedInt(context.Request.Query["bucketDays"].FirstOrDefault(), 7, 1, 31),
                FundAccountId: context.Request.Query["fundAccountId"].FirstOrDefault());
            var ladder = await service.GetCashLadderAsync(query, ct).ConfigureAwait(false);
            return Results.Json(ladder, jsonOptions);
        })
        .WithName("GetPortfolioCashLadder").RequirePermission(UserPermission.ViewTrades)
        .Produces<PortfolioCashLadderDto>(StatusCodes.Status200OK);

        /// <summary>
        /// Returns the stress-scenario catalog, including per-scenario modeled
        /// effects and assumptions, without computing a ladder.
        /// </summary>
        group.MapGet("/scenarios", (HttpContext _) =>
            Results.Json(PortfolioCashLadderEngine.ScenarioCatalog, jsonOptions))
        .WithName("GetPortfolioCashLadderScenarios").RequirePermission(UserPermission.ViewTrades)
        .Produces<IReadOnlyList<PortfolioCashScenarioDto>>(StatusCodes.Status200OK);
    }

    private static int ParseBoundedInt(string? raw, int fallback, int min, int max)
        => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, min, max)
            : fallback;

    private static decimal? ParseOptionalDecimal(string? raw)
        => decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}

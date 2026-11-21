using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Workstation;
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
                FundAccountId: context.Request.Query["fundAccountId"].FirstOrDefault(),
                EngineVersion: context.Request.Query["engineVersion"].FirstOrDefault());
            var ladder = await service.GetCashLadderAsync(query, ct).ConfigureAwait(false);
            return Results.Json(ladder, jsonOptions);
        })
        .WithName("GetPortfolioCashLadder").RequirePermission(UserPermission.ViewTrades)
        .Produces<PortfolioCashLadderDto>(StatusCodes.Status200OK);

        /// <summary>
        /// Compares today's ladder with the ladder rebuilt as of a prior date (default: the end of the
        /// previous month). Same query parameters as the ladder, plus <c>baselineAsOf</c> (yyyy-MM-dd).
        /// </summary>
        group.MapGet("/comparison", async (
            HttpContext context,
            CancellationToken ct) =>
        {
            var service = context.RequestServices.GetService<IPortfolioCashLadderQueryService>() ?? new PortfolioCashLadderReadService();
            if (service is not PortfolioCashLadderReadService readService)
            {
                return Results.Problem("The registered cash ladder service does not support prior-period comparison.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var rawBaseline = context.Request.Query["baselineAsOf"].FirstOrDefault();
            DateOnly? baselineAsOf = null;
            if (!string.IsNullOrWhiteSpace(rawBaseline))
            {
                if (!DateOnly.TryParseExact(rawBaseline, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                {
                    return Results.Problem("baselineAsOf must be a date in yyyy-MM-dd form.", statusCode: StatusCodes.Status400BadRequest);
                }

                baselineAsOf = parsed;
            }

            var query = new PortfolioCashLadderQuery(
                ScenarioId: context.Request.Query["scenario"].FirstOrDefault(),
                HorizonDays: ParseBoundedInt(context.Request.Query["horizonDays"].FirstOrDefault(), 90, 1, MaxHorizonDays),
                MinimumCashThreshold: ParseOptionalDecimal(context.Request.Query["minimumCash"].FirstOrDefault()),
                BucketDays: ParseBoundedInt(context.Request.Query["bucketDays"].FirstOrDefault(), 7, 1, 31),
                FundAccountId: context.Request.Query["fundAccountId"].FirstOrDefault(),
                EngineVersion: context.Request.Query["engineVersion"].FirstOrDefault());
            var comparison = await readService.GetCashLadderComparisonAsync(query, baselineAsOf, ct).ConfigureAwait(false);
            return Results.Json(comparison, jsonOptions);
        })
        .WithName("GetPortfolioCashLadderComparison").RequirePermission(UserPermission.ViewTrades)
        .Produces<PeriodComparisonDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status501NotImplemented);

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

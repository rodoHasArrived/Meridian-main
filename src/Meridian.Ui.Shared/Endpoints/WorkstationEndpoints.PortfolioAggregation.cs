using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Strategies.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    /// <summary>
    /// Cross-strategy aggregate portfolio routes: netted positions, gross/net exposure, and
    /// per-symbol exposure across all active runs — the same aggregation surface that feeds
    /// the portfolio-aware pre-trade risk rules.
    /// </summary>
    private static void MapCrossStrategyPortfolioRoutes(RouteGroupBuilder portfolioGroup, JsonSerializerOptions jsonOptions)
    {
        portfolioGroup.MapGet(PortfolioSubroute(UiApiRoutes.PortfolioAggregate), (HttpContext context) =>
        {
            var aggregator = context.RequestServices.GetService<IAggregatePortfolioService>();
            if (aggregator is null)
                return Results.Problem("Aggregate portfolio service is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var positions = aggregator.GetAggregatedPositions();
            return Results.Json(positions, jsonOptions);
        })
        .WithName("GetPortfolioAggregate")
        .Produces<IReadOnlyList<AggregatedPosition>>(200)
        .Produces(503);

        portfolioGroup.MapGet(PortfolioSubroute(UiApiRoutes.PortfolioExposure), (HttpContext context) =>
        {
            var aggregator = context.RequestServices.GetService<IAggregatePortfolioService>();
            if (aggregator is null)
                return Results.Problem("Aggregate portfolio service is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var report = aggregator.GetCrossStrategyExposure();
            return Results.Json(report, jsonOptions);
        })
        .WithName("GetPortfolioExposure")
        .Produces<CrossStrategyExposureReport>(200)
        .Produces(503);

        portfolioGroup.MapGet(PortfolioSubroute(UiApiRoutes.PortfolioSymbolExposure), (string symbol, HttpContext context) =>
        {
            var aggregator = context.RequestServices.GetService<IAggregatePortfolioService>();
            if (aggregator is null)
                return Results.Problem("Aggregate portfolio service is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var net = aggregator.GetNetPositionForSymbol(symbol);
            return Results.Json(net, jsonOptions);
        })
        .WithName("GetPortfolioSymbolExposure")
        .Produces<NetSymbolPosition>(200)
        .Produces(503);
    }
}

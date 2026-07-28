using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
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
    /// the portfolio-aware pre-trade risk rules. All routes require trade-read permission,
    /// and the per-run position breakdown additionally filters fund-account contributions
    /// to the caller's scoped authority.
    /// </summary>
    private static void MapCrossStrategyPortfolioRoutes(RouteGroupBuilder portfolioGroup, JsonSerializerOptions jsonOptions)
    {
        portfolioGroup.MapGet(PortfolioSubroute(UiApiRoutes.PortfolioAggregate), async (HttpContext context) =>
        {
            var aggregator = context.RequestServices.GetService<IAggregatePortfolioService>();
            if (aggregator is null)
                return Results.Problem("Aggregate portfolio service is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var positions = aggregator.GetAggregatedPositions();
            var scoped = await FilterToAuthorizedAccountsAsync(context, positions).ConfigureAwait(false);
            return Results.Json(scoped, jsonOptions);
        })
        .WithName("GetPortfolioAggregate")
        .Produces<IReadOnlyList<AggregatedPosition>>(200)
        .Produces(403)
        .Produces(503)
        .RequirePermission(UserPermission.ViewTrades);

        portfolioGroup.MapGet(PortfolioSubroute(UiApiRoutes.PortfolioExposure), async (HttpContext context) =>
        {
            var aggregator = context.RequestServices.GetService<IAggregatePortfolioService>();
            if (aggregator is null)
                return Results.Problem("Aggregate portfolio service is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);

            // Built from the caller's authorized contributions rather than
            // GetCrossStrategyExposure(): the summary must not span funds the caller
            // cannot see, and per-contribution valuation is the only correct basis when
            // runs hold opposing quantities of the same symbol (the netted signed
            // weighted-average cost can go negative and produce nonsensical gross).
            var scoped = await FilterToAuthorizedAccountsAsync(
                context, aggregator.GetAggregatedPositions()).ConfigureAwait(false);
            return Results.Json(BuildExposureReport(scoped), jsonOptions);
        })
        .WithName("GetPortfolioExposure")
        .Produces<CrossStrategyExposureReport>(200)
        .Produces(403)
        .Produces(503)
        .RequirePermission(UserPermission.ViewTrades);

        portfolioGroup.MapGet(PortfolioSubroute(UiApiRoutes.PortfolioSymbolExposure), async (string symbol, HttpContext context) =>
        {
            var aggregator = context.RequestServices.GetService<IAggregatePortfolioService>();
            if (aggregator is null)
                return Results.Problem("Aggregate portfolio service is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);

            // Same scoping as the aggregate route: a per-symbol net/gross across every
            // registered account would otherwise leak other funds' holdings exactly.
            var scoped = await FilterToAuthorizedAccountsAsync(
                context, aggregator.GetAggregatedPositions()).ConfigureAwait(false);
            var contributions = scoped
                .Where(position => string.Equals(position.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                .SelectMany(static position => position.Contributions)
                .ToArray();

            return Results.Json(
                new NetSymbolPosition(
                    Symbol: symbol,
                    NetQuantity: contributions.Sum(static c => c.Quantity),
                    GrossQuantity: contributions.Sum(static c => Math.Abs(c.Quantity))),
                jsonOptions);
        })
        .WithName("GetPortfolioSymbolExposure")
        .Produces<NetSymbolPosition>(200)
        .Produces(403)
        .Produces(503)
        .RequirePermission(UserPermission.ViewTrades);
    }

    /// <summary>
    /// Computes gross/net exposure from individual run contributions valued at their own
    /// positive cost basis. Netting first (as the aggregation service's weighted-average
    /// cost does) is wrong whenever runs hold opposing quantities of a symbol: long 100 at
    /// $100 against short 90 at $200 nets to a negative weighted cost and would report
    /// negative gross exposure.
    /// </summary>
    internal static CrossStrategyExposureReport BuildExposureReport(IReadOnlyList<AggregatedPosition> positions)
    {
        var longExposure = 0m;
        var shortExposure = 0m;
        var symbolGross = new List<(string Symbol, decimal Gross)>(positions.Count);

        foreach (var position in positions)
        {
            var gross = 0m;
            foreach (var contribution in position.Contributions)
            {
                var value = Math.Abs(contribution.Quantity) * Math.Abs(contribution.CostBasis);
                gross += value;
                if (contribution.Quantity >= 0m)
                {
                    longExposure += value;
                }
                else
                {
                    shortExposure += value;
                }
            }

            symbolGross.Add((position.Symbol, gross));
        }

        return new CrossStrategyExposureReport(
            GrossExposure: longExposure + shortExposure,
            NetExposure: longExposure - shortExposure,
            Top5Concentrations: symbolGross
                .OrderByDescending(static entry => entry.Gross)
                .Take(5)
                .Select(static entry => entry.Symbol)
                .ToArray(),
            AsOf: DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Filters per-run contributions to the caller's scoped account authority: fund-account
    /// contributions (Guid account ids) outside the caller's scoped trade-read grants are
    /// removed and the position aggregates recomputed from what survives, so one fund's
    /// operator cannot read another fund's holdings or trade intentions. Run-local
    /// (non-Guid) account ids belong to paper strategy runs, not funds, and stay visible
    /// to any trade-read holder. <see cref="UserPermission.AdminMaintenance"/> sees the
    /// full aggregation. One scope check per distinct account, not per contribution.
    /// </summary>
    internal static async Task<IReadOnlyList<AggregatedPosition>> FilterToAuthorizedAccountsAsync(
        HttpContext context,
        IReadOnlyList<AggregatedPosition> positions)
    {
        if (EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance))
        {
            return positions;
        }

        var scopeCache = new Dictionary<Guid, bool>();
        var filtered = new List<AggregatedPosition>(positions.Count);
        foreach (var position in positions)
        {
            List<RunPositionContribution>? surviving = null;
            var removedAny = false;
            foreach (var contribution in position.Contributions)
            {
                var authorized = true;
                if (Guid.TryParse(contribution.AccountId, out var fundAccountId))
                {
                    if (!scopeCache.TryGetValue(fundAccountId, out authorized))
                    {
                        authorized = await EndpointAuthorization.HasScopedPermissionAsync(
                            context,
                            UserPermission.ViewTrades,
                            AccessScopeKindDto.Account,
                            fundAccountId,
                            context.RequestAborted).ConfigureAwait(false);
                        scopeCache[fundAccountId] = authorized;
                    }
                }

                if (authorized)
                {
                    (surviving ??= []).Add(contribution);
                }
                else
                {
                    removedAny = true;
                }
            }

            if (!removedAny)
            {
                filtered.Add(position);
                continue;
            }

            if (surviving is not { Count: > 0 })
            {
                continue;
            }

            // Rebuild the aggregates from the surviving contributions with the same math
            // the aggregation service uses, so totals never leak filtered funds' sizes.
            var totalQuantity = surviving.Sum(static c => c.Quantity);
            filtered.Add(new AggregatedPosition(
                Symbol: position.Symbol,
                TotalQuantity: totalQuantity,
                LongQuantity: surviving.Where(static c => c.Quantity > 0).Sum(static c => c.Quantity),
                ShortQuantity: surviving.Where(static c => c.Quantity < 0).Sum(static c => Math.Abs(c.Quantity)),
                WeightedAverageCost: totalQuantity != 0m
                    ? surviving.Sum(static c => c.Quantity * c.CostBasis) / totalQuantity
                    : 0m,
                TotalUnrealisedPnl: surviving.Sum(static c => c.UnrealisedPnl),
                Contributions: surviving));
        }

        return filtered;
    }
}

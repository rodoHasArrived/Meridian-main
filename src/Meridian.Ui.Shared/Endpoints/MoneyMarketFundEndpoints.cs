using System.Text.Json;
using Meridian.Application.Treasury;
using Meridian.Contracts.Treasury;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Minimal API endpoint group for money market fund reference data,
/// liquidity projections, sweep profiles, fund-family views, and rebuild orchestration.
/// </summary>
public static class MoneyMarketFundEndpoints
{
    private const string RoutePrefix = "/api/security-master/money-market-funds";

    public static void MapMoneyMarketFundEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(RoutePrefix).WithTags("MoneyMarketFunds");

        // ── Reference ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the full MMF reference record for the given security ID.
        /// Includes canonical identity, sweep eligibility, WAM, and liquidity-fee flag.
        /// </summary>
        group.MapGet("/{securityId:guid}", async (
            Guid securityId,
            IMoneyMarketFundService service,
            CancellationToken ct) =>
        {
            var detail = await service.GetByIdAsync(securityId, ct).ConfigureAwait(false);
            return detail is null
                ? Results.NotFound()
                : Results.Json(detail, jsonOptions);
        })
        .WithName("GetMoneyMarketFundById")
        .Produces<MmfDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        /// <summary>
        /// Searches for MMFs matching the supplied filter criteria.
        /// Supports filtering by fund family, sweep eligibility, liquidity fee, WAM cap, and active status.
        /// </summary>
        group.MapPost("/search", async (
            MmfSearchQuery query,
            IMoneyMarketFundService service,
            CancellationToken ct) =>
        {
            var results = await service.SearchAsync(query, ct).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("SearchMoneyMarketFunds")
        .Produces<IReadOnlyList<MmfDetailDto>>(StatusCodes.Status200OK);

        // ── Liquidity ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the current liquidity projection (state and WAM) for the given MMF.
        /// State is derived from WAM bands: WAM ≤ 60 days → Liquid; > 60 → Restricted.
        /// A manual override takes precedence when set.
        /// </summary>
        group.MapGet("/{securityId:guid}/liquidity", async (
            Guid securityId,
            IMoneyMarketFundService service,
            CancellationToken ct) =>
        {
            var liquidity = await service.GetLiquidityAsync(securityId, ct).ConfigureAwait(false);
            return liquidity is null
                ? Results.NotFound()
                : Results.Json(liquidity, jsonOptions);
        })
        .WithName("GetMoneyMarketFundLiquidity")
        .Produces<MmfLiquidityDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        /// <summary>
        /// Returns all active MMFs currently in the Liquid liquidity state.
        /// Used by treasury and cash-management dashboards for portfolio-level liquidity views.
        /// </summary>
        group.MapGet("/liquidity/liquid", async (
            IMmfLiquidityService liquidityService,
            CancellationToken ct) =>
        {
            var funds = await liquidityService.GetAllLiquidFundsAsync(ct).ConfigureAwait(false);
            return Results.Json(funds, jsonOptions);
        })
        .WithName("GetAllLiquidMoneyMarketFunds")
        .Produces<IReadOnlyList<MmfLiquidityDto>>(StatusCodes.Status200OK);

        // ── Sweep ────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the sweep-eligibility and liquidity-fee profile for the given MMF.
        /// Used by cash-management systems to determine program routing eligibility.
        /// </summary>
        group.MapGet("/{securityId:guid}/sweep-profile", async (
            Guid securityId,
            IMoneyMarketFundService service,
            CancellationToken ct) =>
        {
            var sweep = await service.GetSweepProfileAsync(securityId, ct).ConfigureAwait(false);
            return sweep is null
                ? Results.NotFound()
                : Results.Json(sweep, jsonOptions);
        })
        .WithName("GetMoneyMarketFundSweepProfile")
        .Produces<MmfSweepProfileDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // ── Fund-family ──────────────────────────────────────────────────────

        /// <summary>
        /// Returns the fund-family grouping for the given family name.
        /// The family name is normalised to upper-case for consistent lookup.
        /// Returns 404 when no funds are registered under that family.
        /// </summary>
        group.MapGet("/family/{familyName}", async (
            string familyName,
            IMmfLiquidityService liquidityService,
            CancellationToken ct) =>
        {
            var family = await liquidityService.GetFamilyProjectionAsync(familyName, ct).ConfigureAwait(false);
            return family is null
                ? Results.NotFound()
                : Results.Json(family, jsonOptions);
        })
        .WithName("GetMoneyMarketFundFamily")
        .Produces<MmfFundFamilyDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // ── Rebuild orchestration ────────────────────────────────────────────

        /// <summary>
        /// Triggers a deterministic projection rebuild for the given MMF.
        /// Records a checkpoint so governance consumers can verify rebuild freshness.
        /// </summary>
        group.MapPost("/{securityId:guid}/rebuild", async (
            Guid securityId,
            IMoneyMarketFundService service,
            CancellationToken ct) =>
        {
            await service.RebuildProjectionsAsync(securityId, ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithName("RebuildMoneyMarketFundProjections")
        .Produces(StatusCodes.Status204NoContent);

        /// <summary>
        /// Returns all projection rebuild checkpoints.
        /// Used by operations and governance to verify that all MMF projections
        /// have been rebuilt from the canonical event stream.
        /// </summary>
        group.MapGet("/rebuild/checkpoints", async (
            IMoneyMarketFundService service,
            CancellationToken ct) =>
        {
            var checkpoints = await service.GetRebuildCheckpointsAsync(ct).ConfigureAwait(false);
            return Results.Json(checkpoints, jsonOptions);
        })
        .WithName("GetMoneyMarketFundRebuildCheckpoints")
        .Produces<IReadOnlyList<MmfRebuildCheckpointDto>>(StatusCodes.Status200OK);
    }
}

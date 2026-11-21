using System.Text.Json;
using Meridian.Instruments.MoneyMarketFunds;
using Meridian.Contracts.Treasury;
using Meridian.Identity.Auth;
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
            HttpContext context) =>
        {
            if (ResolveService<IMoneyMarketFundService>(context) is not { } service)
                return ServiceUnavailable();
            var ct = context.RequestAborted;
            var detail = await service.GetByIdAsync(securityId, ct).ConfigureAwait(false);
            return detail is null
                ? Results.NotFound()
                : Results.Json(detail, jsonOptions);
        })
        .WithName("GetMoneyMarketFundById")
        .RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<MmfDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        /// <summary>
        /// Searches for MMFs matching the supplied filter criteria.
        /// Supports filtering by fund family, sweep eligibility, liquidity fee, WAM cap, and active status.
        /// </summary>
        group.MapPost("/search", async (
            JsonElement body,
            HttpContext context) =>
        {
            if (ResolveService<IMoneyMarketFundService>(context) is not { } service)
                return ServiceUnavailable();
            var ct = context.RequestAborted;
            MmfSearchQuery? query;
            try
            {
                query = JsonSerializer.Deserialize<MmfSearchQuery>(body.GetRawText(), jsonOptions);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
            {
                return Results.Problem("Search query body is not valid.", statusCode: StatusCodes.Status400BadRequest);
            }

            if (query is null)
                return Results.Problem("Search query body is required.", statusCode: StatusCodes.Status400BadRequest);

            var results = await service.SearchAsync(query, ct).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("SearchMoneyMarketFunds")
        .DeclareNonMutating("Searches money market funds by filter criteria; the body carries the filter because it has several optional fields, and the handler only reads through IMoneyMarketFundService.SearchAsync.")
        .RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<IReadOnlyList<MmfDetailDto>>(StatusCodes.Status200OK);

        // ── Liquidity ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the current liquidity projection (state and WAM) for the given MMF.
        /// State is derived from WAM bands: WAM ≤ 60 days → Liquid; > 60 → Restricted.
        /// A manual override takes precedence when set.
        /// </summary>
        group.MapGet("/{securityId:guid}/liquidity", async (
            Guid securityId,
            HttpContext context) =>
        {
            if (ResolveService<IMoneyMarketFundService>(context) is not { } service)
                return ServiceUnavailable();
            var ct = context.RequestAborted;
            var liquidity = await service.GetLiquidityAsync(securityId, ct).ConfigureAwait(false);
            return liquidity is null
                ? Results.NotFound()
                : Results.Json(liquidity, jsonOptions);
        })
        .WithName("GetMoneyMarketFundLiquidity")
        .RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<MmfLiquidityDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        /// <summary>
        /// Returns all active MMFs currently in the Liquid liquidity state.
        /// Used by treasury and cash-management dashboards for portfolio-level liquidity views.
        /// </summary>
        group.MapGet("/liquidity/liquid", async (
            HttpContext context) =>
        {
            if (ResolveService<IMmfLiquidityService>(context) is not { } liquidityService)
                return ServiceUnavailable();
            var ct = context.RequestAborted;
            var funds = await liquidityService.GetAllLiquidFundsAsync(ct).ConfigureAwait(false);
            return Results.Json(funds, jsonOptions);
        })
        .WithName("GetAllLiquidMoneyMarketFunds")
        .RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<IReadOnlyList<MmfLiquidityDto>>(StatusCodes.Status200OK);

        // ── Sweep ────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the sweep-eligibility and liquidity-fee profile for the given MMF.
        /// Used by cash-management systems to determine program routing eligibility.
        /// </summary>
        group.MapGet("/{securityId:guid}/sweep-profile", async (
            Guid securityId,
            HttpContext context) =>
        {
            if (ResolveService<IMoneyMarketFundService>(context) is not { } service)
                return ServiceUnavailable();
            var ct = context.RequestAborted;
            var sweep = await service.GetSweepProfileAsync(securityId, ct).ConfigureAwait(false);
            return sweep is null
                ? Results.NotFound()
                : Results.Json(sweep, jsonOptions);
        })
        .WithName("GetMoneyMarketFundSweepProfile")
        .RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
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
            HttpContext context) =>
        {
            if (ResolveService<IMmfLiquidityService>(context) is not { } liquidityService)
                return ServiceUnavailable();
            var ct = context.RequestAborted;
            var family = await liquidityService.GetFamilyProjectionAsync(familyName, ct).ConfigureAwait(false);
            return family is null
                ? Results.NotFound()
                : Results.Json(family, jsonOptions);
        })
        .WithName("GetMoneyMarketFundFamily")
        .RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<MmfFundFamilyDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // ── Rebuild orchestration ────────────────────────────────────────────

        /// <summary>
        /// Triggers a deterministic projection rebuild for the given MMF.
        /// Records a checkpoint so governance consumers can verify rebuild freshness.
        /// </summary>
        group.MapPost("/{securityId:guid}/rebuild", async (
            Guid securityId,
            HttpContext context) =>
        {
            if (ResolveService<IMoneyMarketFundService>(context) is not { } service)
                return ServiceUnavailable();
            var ct = context.RequestAborted;
            await service.RebuildProjectionsAsync(securityId, ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithName("RebuildMoneyMarketFundProjections")
        .RequirePermission(UserPermission.ModifySecurityMaster)
        .Produces(StatusCodes.Status204NoContent);

        /// <summary>
        /// Returns all projection rebuild checkpoints.
        /// Used by operations and governance to verify that all MMF projections
        /// have been rebuilt from the canonical event stream.
        /// </summary>
        group.MapGet("/rebuild/checkpoints", async (
            HttpContext context) =>
        {
            if (ResolveService<IMoneyMarketFundService>(context) is not { } service)
                return ServiceUnavailable();
            var ct = context.RequestAborted;
            var checkpoints = await service.GetRebuildCheckpointsAsync(ct).ConfigureAwait(false);
            return Results.Json(checkpoints, jsonOptions);
        })
        .WithName("GetMoneyMarketFundRebuildCheckpoints")
        .RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<IReadOnlyList<MmfRebuildCheckpointDto>>(StatusCodes.Status200OK);
    }

    private static TService? ResolveService<TService>(HttpContext context)
        where TService : class =>
        context.RequestServices.GetService<TService>();

    private static IResult ServiceUnavailable() =>
        Results.Problem("Money market fund service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
}

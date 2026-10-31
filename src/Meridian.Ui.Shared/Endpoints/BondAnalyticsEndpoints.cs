using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.FixedIncome;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>Read-only, explicitly dated bond analysis. No valuation result grants posting authority.</summary>
public static class BondAnalyticsEndpoints
{
    public static void MapBondAnalyticsEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapPost(UiApiRoutes.ReferenceDataBondSettlementConsideration, async (
            BondSettlementConsiderationRequest request,
            [FromServices] IBondSettlementConsiderationService service, CancellationToken ct) =>
            Results.Json(await service.PrepareAsync(request, ct).ConfigureAwait(false), jsonOptions))
            .WithTags("BondAnalytics")
            .WithName("PrepareBondSettlementConsideration")
            .RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
            .Produces<BondSettlementPreparationResult>();

        app.MapGet(UiApiRoutes.ReferenceDataBondAnalytics, async (
            Guid securityId,
            [FromQuery] DateTimeOffset asOf,
            [FromQuery] DateOnly? settlementDate,
            [FromQuery] DateTimeOffset? knownAt,
            [FromQuery] decimal? cleanPrice,
            [FromQuery(Name = "yield")] decimal? annualYield,
            [FromQuery] string? yieldConvention,
            [FromServices] IBondAnalyticsService service,
            CancellationToken ct) =>
        {
            BondYieldConvention? convention = null;
            if (yieldConvention is not null)
            {
                if (!Enum.TryParse<BondYieldConvention>(yieldConvention, ignoreCase: true, out var parsed)
                    || !Enum.IsDefined(parsed)
                    || !Enum.GetNames<BondYieldConvention>().Contains(yieldConvention, StringComparer.OrdinalIgnoreCase))
                {
                    return Results.BadRequest(new { error = "yieldConvention must be NominalPeriodic, AnnualEffective, or Simple." });
                }
                convention = parsed;
            }

            if (asOf == default || asOf.Offset != TimeSpan.Zero || knownAt?.Offset is { } offset && offset != TimeSpan.Zero)
            {
                return Results.BadRequest(new { error = "asOf and knownAt must be explicit UTC timestamps." });
            }
            if (cleanPrice.HasValue == annualYield.HasValue)
            {
                return Results.BadRequest(new { error = "Supply exactly one of cleanPrice per 100 face or annual fractional yield." });
            }

            var result = await service.CalculateAsync(new BondAnalyticsRequest(
                securityId, new BondCalculationContext(asOf, settlementDate, knownAt),
                cleanPrice, annualYield, convention), ct).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithTags("BondAnalytics")
        .WithName("GetBondAnalytics")
        .RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<BondAnalyticsResult>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);
    }
}

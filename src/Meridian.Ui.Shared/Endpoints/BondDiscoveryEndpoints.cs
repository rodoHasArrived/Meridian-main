using System.Text.Json;
using Meridian.Application.Subscriptions.Services;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Subscriptions.Models;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>Provider lookup supplies candidate identity evidence, never approved Security Master terms.</summary>
public static class BondDiscoveryEndpoints
{
    public static void MapBondDiscoveryEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(string.Empty).RequireWorkstationTenantScope();
        group.MapGet(UiApiRoutes.ReferenceDataBondDiscovery, async (HttpContext context, string? query) =>
        {
            var search = context.RequestServices.GetService<SymbolSearchService>();
            if (search is null)
                return Results.Problem("Provider-backed bond discovery is not configured on this host.", statusCode: 503);

            var normalized = query?.Trim() ?? string.Empty;
            if (normalized.Length > 200)
                return Results.BadRequest(new { error = "Bond lookup accepts at most 200 characters." });

            // Alpaca is the retained implementation with a fixed_income filter. Other symbol
            // providers must not silently broaden this bond-only surface to equities.
            var provider = (await search.GetProvidersAsync(context.RequestAborted).ConfigureAwait(false))
                .FirstOrDefault(candidate => string.Equals(candidate.Name, "alpaca", StringComparison.OrdinalIgnoreCase));
            var available = provider?.Available == true;
            SymbolSearchResponse? results = null;
            if (available && normalized.Length > 0)
            {
                try
                {
                    results = await search.SearchAuthoritativeAsync(new SymbolSearchRequest(normalized, Limit: 25,
                        AssetType: "fixed_income", Provider: "alpaca", IncludeFigi: false), context.RequestAborted).ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                    return Results.Problem("Alpaca bond lookup could not be completed. The provider may be unavailable or throttled; retry lookup before reviewing candidates.", statusCode: 503);
                }
            }
            var candidates = (results?.Results ?? []).Where(candidate =>
                string.Equals(candidate.AssetType, "FixedIncome", StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.AssetType, "fixed_income", StringComparison.OrdinalIgnoreCase)
                || (SecurityAssetClassCatalog.TryGet(candidate.AssetType, out var descriptor)
                    && descriptor.CarriesBondCouponStructure)).ToArray();

            EndpointAuthorization.TryGetPermissions(context, out var permissions);
            return Results.Json(new
            {
                provider = "alpaca",
                providerLabel = "Alpaca",
                available,
                unavailableReason = available ? null : "Alpaca bond lookup is unavailable or its credentials are not configured.",
                canStage = (permissions & UserPermission.ModifySecurityMaster) == UserPermission.ModifySecurityMaster,
                query = normalized,
                candidates,
                observedAtUtc = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("DiscoverProviderBonds")
        .RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status503ServiceUnavailable);
    }
}

using System.Text.Json;
using Meridian.Application.Canonicalization;
using Meridian.Contracts.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering canonicalization parity dashboard endpoints.
/// Provides Phase 2 validation metrics: match rates, unresolved counts, and per-provider breakdowns.
/// </summary>
public static class CanonicalizationEndpoints
{
    public static void MapCanonicalizationEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Canonicalization");

        // Full canonicalization status with metrics snapshot
        group.MapGet(UiApiRoutes.CanonicalizationStatus, () =>
        {
            var snapshot = CanonicalizationMetrics.GetSnapshot();
            var total = snapshot.SuccessTotal + snapshot.SoftFailTotal + snapshot.HardFailTotal;

            return Results.Json(new
            {
                enabled = snapshot.ActiveVersion > 0,
                version = snapshot.ActiveVersion,
                eventsTotal = total,
                successTotal = snapshot.SuccessTotal,
                softFailTotal = snapshot.SoftFailTotal,
                hardFailTotal = snapshot.HardFailTotal,
                dualWriteTotal = snapshot.DualWriteTotal,
                matchRatePercent = total > 0
                    ? Math.Round(100.0 * snapshot.SuccessTotal / total, 2)
                    : 0,
                unresolvedRate = total > 0
                    ? Math.Round(100.0 * snapshot.SoftFailTotal / total, 4)
                    : 0,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetCanonicalizationStatus")
        .Produces(200);

        // Per-provider parity breakdown
        group.MapGet(UiApiRoutes.CanonicalizationParity, () =>
        {
            var snapshot = CanonicalizationMetrics.GetSnapshot();

            var providers = snapshot.ProviderParity.Select(kv => new
            {
                provider = kv.Key,
                total = kv.Value.Total,
                success = kv.Value.Success,
                softFail = kv.Value.SoftFail,
                hardFail = kv.Value.HardFail,
                unresolvedSymbol = kv.Value.UnresolvedSymbol,
                unresolvedVenue = kv.Value.UnresolvedVenue,
                unresolvedCondition = kv.Value.UnresolvedCondition,
                matchRatePercent = kv.Value.MatchRatePercent
            }).ToArray();

            return Results.Json(new
            {
                providers,
                version = snapshot.ActiveVersion,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetCanonicalizationParity")
        .Produces(200);

        // Single provider detail
        group.MapGet(UiApiRoutes.CanonicalizationParityByProvider, (string provider) =>
        {
            var snapshot = CanonicalizationMetrics.GetSnapshot();

            if (!snapshot.ProviderParity.TryGetValue(provider, out var stats))
            {
                return Results.Json(new
                {
                    provider,
                    found = false,
                    message = $"No canonicalization data for provider '{provider}'",
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions);
            }

            // Also pull unresolved field breakdown from the global counts
            var unresolvedFields = snapshot.UnresolvedCounts
                .Where(kv => string.Equals(kv.Key.Provider, provider, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(kv => kv.Key.Field, kv => kv.Value);

            return Results.Json(new
            {
                provider,
                found = true,
                total = stats.Total,
                success = stats.Success,
                softFail = stats.SoftFail,
                hardFail = stats.HardFail,
                matchRatePercent = stats.MatchRatePercent,
                unresolvedBreakdown = unresolvedFields,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetCanonicalizationParityByProvider")
        .Produces(200);

        // Current canonicalization config (read-only view)
        group.MapGet(UiApiRoutes.CanonicalizationConfig, () =>
        {
            var snapshot = CanonicalizationMetrics.GetSnapshot();

            return Results.Json(new
            {
                activeVersion = snapshot.ActiveVersion,
                enabled = snapshot.ActiveVersion > 0,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetCanonicalizationConfig")
        .Produces(200);
    }
}

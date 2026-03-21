using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering subscription management API endpoints.
/// </summary>
public static class SubscriptionEndpoints
{
    public static void MapSubscriptionEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Subscriptions");

        // Active subscriptions
        group.MapGet(UiApiRoutes.SubscriptionsActive, ([FromServices] ConfigStore store) =>
        {
            var config = store.Load();
            var symbols = config.Symbols?.Select(s => new
            {
                symbol = s.Symbol,
                trades = s.SubscribeTrades,
                depth = s.SubscribeDepth,
                depthLevels = s.DepthLevels
            }).ToArray() ?? Array.Empty<object>();

            return Results.Json(new
            {
                subscriptions = symbols,
                total = symbols.Length,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetActiveSubscriptions")
        .Produces(200);

        // Subscribe to symbol
        group.MapPost(UiApiRoutes.SubscriptionsSubscribe, async ([FromServices] ConfigStore store, SubscribeRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req.Symbol))
                return Results.BadRequest(new { error = "Symbol is required" });

            var config = store.Load();
            var symbols = config.Symbols?.ToList() ?? new List<SymbolConfig>();

            if (symbols.Any(s => string.Equals(s.Symbol, req.Symbol, StringComparison.OrdinalIgnoreCase)))
                return Results.Json(new { message = "Symbol already subscribed", symbol = req.Symbol }, jsonOptions);

            symbols.Add(new SymbolConfig(
                Symbol: req.Symbol.ToUpperInvariant(),
                SubscribeTrades: req.Trades ?? true,
                SubscribeDepth: req.Depth ?? false,
                DepthLevels: req.DepthLevels ?? 5
            ));

            var next = config with { Symbols = symbols.ToArray() };
            await store.SaveAsync(next);

            return Results.Json(new { success = true, symbol = req.Symbol, timestamp = DateTimeOffset.UtcNow }, jsonOptions);
        })
        .WithName("Subscribe")
        .Produces(200)
        .Produces(400)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Unsubscribe from symbol
        group.MapPost(UiApiRoutes.SubscriptionsUnsubscribe, async (string symbol, [FromServices] ConfigStore store) =>
        {
            var config = store.Load();
            var symbols = config.Symbols?.ToList() ?? new List<SymbolConfig>();

            var removed = symbols.RemoveAll(s => string.Equals(s.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
                return Results.NotFound(new { error = $"Symbol '{symbol}' not found in subscriptions" });

            var next = config with { Symbols = symbols.ToArray() };
            await store.SaveAsync(next);

            return Results.Json(new { success = true, symbol, timestamp = DateTimeOffset.UtcNow }, jsonOptions);
        })
        .WithName("Unsubscribe")
        .Produces(200)
        .Produces(404)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private sealed record SubscribeRequest(string? Symbol, bool? Trades, bool? Depth, int? DepthLevels);
}

using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Operations;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Identity.Auth;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering demo mode API endpoints.
/// Provides seeded market data for new operators without external API keys.
/// </summary>
public static class DemoModeEndpoints
{
    /// <summary>Demo output is always seeded; every response renders this persistent badge.</summary>
    private const DataProvenance DemoProvenance = DataProvenance.Seeded;

    private static readonly DataProvenanceBadge DemoProvenanceBadge =
        DataProvenanceBadge.TryCreate(DemoProvenance)!;

    private static readonly Dictionary<string, DemoMarketSnapshot[]> DemoMarketData = new()
    {
        ["AAPL"] = new[]
        {
            new DemoMarketSnapshot("AAPL", 180.50m, 180.30m, 180.70m, 1000, 1200, 5000000, DateTime.UtcNow, 178.00m, 182.00m, 177.50m, 180.50m),
            new DemoMarketSnapshot("AAPL", 180.75m, 180.50m, 181.00m, 1100, 1300, 5500000, DateTime.UtcNow.AddMinutes(-1), 178.00m, 182.00m, 177.50m, 180.75m),
            new DemoMarketSnapshot("AAPL", 180.25m, 180.00m, 180.50m, 950, 1050, 4800000, DateTime.UtcNow.AddMinutes(-2), 178.00m, 182.00m, 177.50m, 180.25m),
        },
        ["MSFT"] = new[]
        {
            new DemoMarketSnapshot("MSFT", 420.30m, 420.00m, 420.60m, 800, 900, 3500000, DateTime.UtcNow, 415.00m, 425.00m, 414.50m, 420.30m),
            new DemoMarketSnapshot("MSFT", 420.60m, 420.30m, 420.90m, 850, 950, 3600000, DateTime.UtcNow.AddMinutes(-1), 415.00m, 425.00m, 414.50m, 420.60m),
            new DemoMarketSnapshot("MSFT", 420.00m, 419.70m, 420.30m, 750, 850, 3400000, DateTime.UtcNow.AddMinutes(-2), 415.00m, 425.00m, 414.50m, 420.00m),
        },
        ["GOOG"] = new[]
        {
            new DemoMarketSnapshot("GOOG", 155.80m, 155.60m, 156.00m, 600, 700, 2800000, DateTime.UtcNow, 150.00m, 160.00m, 149.50m, 155.80m),
            new DemoMarketSnapshot("GOOG", 156.10m, 155.80m, 156.40m, 650, 750, 2900000, DateTime.UtcNow.AddMinutes(-1), 150.00m, 160.00m, 149.50m, 156.10m),
            new DemoMarketSnapshot("GOOG", 155.50m, 155.20m, 155.80m, 550, 650, 2700000, DateTime.UtcNow.AddMinutes(-2), 150.00m, 160.00m, 149.50m, 155.50m),
        },
        ["SPY"] = new[]
        {
            new DemoMarketSnapshot("SPY", 465.20m, 465.00m, 465.40m, 2000, 2200, 80000000, DateTime.UtcNow, 460.00m, 470.00m, 459.50m, 465.20m),
            new DemoMarketSnapshot("SPY", 465.50m, 465.20m, 465.70m, 2100, 2300, 82000000, DateTime.UtcNow.AddMinutes(-1), 460.00m, 470.00m, 459.50m, 465.50m),
            new DemoMarketSnapshot("SPY", 464.80m, 464.50m, 465.20m, 1900, 2100, 78000000, DateTime.UtcNow.AddMinutes(-2), 460.00m, 470.00m, 459.50m, 464.80m),
        },
        ["TSLA"] = new[]
        {
            new DemoMarketSnapshot("TSLA", 245.30m, 245.00m, 245.60m, 1200, 1400, 40000000, DateTime.UtcNow, 240.00m, 250.00m, 239.50m, 245.30m),
            new DemoMarketSnapshot("TSLA", 245.70m, 245.30m, 246.00m, 1300, 1500, 41000000, DateTime.UtcNow.AddMinutes(-1), 240.00m, 250.00m, 239.50m, 245.70m),
            new DemoMarketSnapshot("TSLA", 244.80m, 244.50m, 245.30m, 1100, 1300, 39000000, DateTime.UtcNow.AddMinutes(-2), 240.00m, 250.00m, 239.50m, 244.80m),
        },
        ["AMZN"] = new[]
        {
            new DemoMarketSnapshot("AMZN", 178.50m, 178.20m, 178.80m, 900, 1000, 35000000, DateTime.UtcNow, 173.00m, 180.00m, 172.50m, 178.50m),
            new DemoMarketSnapshot("AMZN", 178.90m, 178.50m, 179.20m, 950, 1050, 36000000, DateTime.UtcNow.AddMinutes(-1), 173.00m, 180.00m, 172.50m, 178.90m),
            new DemoMarketSnapshot("AMZN", 178.00m, 177.70m, 178.50m, 850, 950, 34000000, DateTime.UtcNow.AddMinutes(-2), 173.00m, 180.00m, 172.50m, 178.00m),
        },
    };

    private static readonly Dictionary<string, DemoHistoricalBar[]> DemoHistoricalData = new()
    {
        ["AAPL"] = GenerateHistoricalBars("AAPL", 178.00m, 182.00m),
        ["MSFT"] = GenerateHistoricalBars("MSFT", 415.00m, 425.00m),
        ["GOOG"] = GenerateHistoricalBars("GOOG", 150.00m, 160.00m),
        ["SPY"] = GenerateHistoricalBars("SPY", 460.00m, 470.00m),
        ["TSLA"] = GenerateHistoricalBars("TSLA", 240.00m, 250.00m),
        ["AMZN"] = GenerateHistoricalBars("AMZN", 173.00m, 180.00m),
    };

    public static void MapDemoModeEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Demo Mode");

        // Get demo mode status
        group.MapGet(UiApiRoutes.DemoMode, (ConfigStore store, IServiceProvider serviceProvider, HttpContext context) =>
        {
            var cfg = store.Load();
            var isDemo = cfg.DataSources?.Sources?.Length == 0 ||
                        (cfg.DataSources?.Sources?.All(s => string.IsNullOrEmpty(s.Alpaca?.KeyId) &&
                                                           string.IsNullOrEmpty(s.Polygon?.ApiKey)) ?? true);
            // W9-TRUTH-001: report the provenance pinned into the composed graph (seeded demo,
            // forced simulated label for in-memory local durables) so connected workstations keep
            // labeling this host's records; without a pinned declaration fall back to the demo
            // heuristic so legacy behavior is unchanged.
            var pinned = serviceProvider.GetService<Meridian.Application.Composition.MeridianDataProvenanceDeclaration>();

            // The seeded symbol list is market data and stays behind a market-data permission. The
            // posture flags are not: the browser shell treats provenance as the source of a persistent,
            // non-dismissable banner on every workspace, so an operator who cannot read it is left
            // labelling this host UNKNOWN -- which is the one answer the banner exists to prevent.
            var canReadSeededSymbols = EndpointAuthorization.HasAnyPermission(
                context,
                UserPermission.ViewMarketData,
                UserPermission.ViewHistoricalData);

            return Results.Json(new DemoModeConfig(
                Enabled: isDemo,
                DemoSymbols: canReadSeededSymbols ? DemoMarketData.Keys.ToArray() : [],
                LastUpdated: DateTime.UtcNow,
                Provenance: pinned?.Provenance ?? (isDemo ? DataProvenance.Seeded : DataProvenance.Real)
            ), jsonOptions);
        })
        .WithName("GetDemoMode").RequireEstablishedPrincipalRead()
        .WithDescription("Returns current demo mode status and available seeded symbols.")
        .Produces<DemoModeConfig>(200);

        // Get all demo symbols
        group.MapGet(UiApiRoutes.DemoSymbols, () =>
        {
            return Results.Json(new
            {
                symbols = DemoMarketData.Keys.ToArray(),
                count = DemoMarketData.Count,
                message = "Demo mode provides seeded market data without external API keys",
                provenance = DemoProvenance.Token(),
                provenanceBadge = DemoProvenanceBadge
            }, jsonOptions);
        })
        .WithName("GetDemoSymbols").RequireAnyPermission(UserPermission.ViewMarketData, UserPermission.ViewHistoricalData)
        .WithDescription("Returns list of symbols available in demo mode.")
        .Produces(200);

        // Get live market data snapshot for demo symbol
        group.MapGet(UiApiRoutes.DemoMarketData, (string symbol) =>
        {
            if (!DemoMarketData.TryGetValue(symbol.ToUpperInvariant(), out var snapshots))
                return Results.NotFound(new { error = $"Symbol {symbol} not available in demo mode" });

            var latest = snapshots.FirstOrDefault();
            return latest is null
                ? Results.NotFound()
                : Results.Json(new
                {
                    snapshot = latest,
                    provenance = DemoProvenance.Token(),
                    provenanceBadge = DemoProvenanceBadge
                }, jsonOptions);
        })
        .WithName("GetDemoMarketData").RequireAnyPermission(UserPermission.ViewMarketData, UserPermission.ViewHistoricalData)
        .WithDescription("Returns latest market data snapshot for a demo symbol.")
        .Produces(200)
        .Produces(404);

        // Get historical data for demo symbol
        group.MapGet(UiApiRoutes.DemoHistoricalData, (string symbol, [FromQuery] int days = 60) =>
        {
            if (!DemoHistoricalData.TryGetValue(symbol.ToUpperInvariant(), out var bars))
                return Results.NotFound(new { error = $"Symbol {symbol} not available in demo mode" });

            var result = bars.Take(Math.Min(days, bars.Length)).ToArray();
            return Results.Json(new
            {
                symbol = symbol,
                bars = result,
                count = result.Length,
                range = new { start = result.LastOrDefault()?.Date, end = result.FirstOrDefault()?.Date },
                provenance = DemoProvenance.Token(),
                provenanceBadge = DemoProvenanceBadge
            }, jsonOptions);
        })
        .WithName("GetDemoHistoricalData").RequireAnyPermission(UserPermission.ViewMarketData, UserPermission.ViewHistoricalData)
        .WithDescription("Returns historical price data for a demo symbol.")
        .Produces(200)
        .Produces(404);
    }

    private static DemoHistoricalBar[] GenerateHistoricalBars(string symbol, decimal minPrice, decimal maxPrice)
    {
        var bars = new List<DemoHistoricalBar>();
        var random = new Random(symbol.GetHashCode());
        var currentDate = DateTime.UtcNow.Date;

        for (int i = 60; i >= 0; i--)
        {
            var date = currentDate.AddDays(-i);
            if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                continue;

            var open = minPrice + (maxPrice - minPrice) * (decimal)random.NextDouble();
            var close = minPrice + (maxPrice - minPrice) * (decimal)random.NextDouble();
            var high = Math.Max(open, close) * (decimal)(1.0 + random.NextDouble() * 0.02);
            var low = Math.Min(open, close) * (decimal)(1.0 - random.NextDouble() * 0.02);
            var volume = (long)(random.Next(1000000, 100000000));

            bars.Add(new DemoHistoricalBar(date, open, high, low, close, volume));
        }

        return bars.ToArray();
    }
}

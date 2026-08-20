using Meridian.Identity.Auth;
using System.Text.Json;
using System.Text.RegularExpressions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Domain.Enums;
using Meridian.Domain.Collectors;
using Meridian.Storage;
using Meridian.Storage.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering symbol management API endpoints.
/// Implements Phase 3B.1 — replaces 15 stub endpoints with working handlers.
/// </summary>
public static class SymbolEndpoints
{
    private static readonly Regex s_symbolPattern = new(@"^[A-Z0-9./-]{1,20}$", RegexOptions.Compiled);

    public static void MapSymbolEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Symbols");
        var logger = app.Logger;

        // GET /api/symbols — all configured symbols as SymbolRecord[]
        group.MapGet(UiApiRoutes.Symbols, (ConfigStore store, QuoteCollector? quoteCollector) =>
        {
            var cfg = store.Load();
            var symbols = cfg.Symbols ?? Array.Empty<SymbolConfig>();
            var records = symbols.Select(s =>
            {
                Meridian.Contracts.Domain.Models.BboQuotePayload? bbo = null;
                if (quoteCollector is not null)
                    quoteCollector.TryGet(s.Symbol, out bbo);
                return new
                {
                    symbol = s.Symbol,
                    status = bbo is not null ? "Active" : "Monitored",
                    provider = bbo?.Venue ?? bbo?.StreamId,
                    lastEventAt = bbo?.Timestamp.ToString("O"),
                    eventCount = 0,
                    hasHistoricalData = false
                };
            });
            return Results.Json(records, jsonOptions);
        })
        .WithName("GetSymbols").RequirePermission(UserPermission.ViewMarketData)
        .Produces(200);

        // GET /api/symbols/monitored — symbols configured for monitoring
        group.MapGet(UiApiRoutes.SymbolsMonitored, (ConfigStore store) =>
        {
            var cfg = store.Load();
            var symbols = cfg.Symbols ?? Array.Empty<SymbolConfig>();
            return Results.Json(new
            {
                count = symbols.Length,
                symbols = symbols.Select(s => new
                {
                    s.Symbol,
                    s.SubscribeTrades,
                    s.SubscribeDepth,
                    s.DepthLevels,
                    s.Exchange,
                    s.Currency,
                    s.InstrumentType
                })
            }, jsonOptions);
        })
        .WithName("GetMonitoredSymbols").RequirePermission(UserPermission.ViewConfig)
        .Produces(200);

        // GET /api/symbols/archived — symbols that have stored data files
        group.MapGet(UiApiRoutes.SymbolsArchived, async (
            IStorageSearchService? searchService,
            StorageOptions storageOptions,
            CancellationToken ct) =>
        {
            if (searchService is null)
                return Results.Json(new { count = 0, symbols = Array.Empty<string>(), message = "Storage search service not available" }, jsonOptions);

            return await EndpointHelpers.GuardAsync(async () =>
            {
                var catalog = await searchService.DiscoverAsync(new DiscoveryQuery(), ct);
                var symbols = catalog.Symbols?.Select(s => s.Symbol).ToArray() ?? Array.Empty<string>();
                return Results.Json(new { count = symbols.Length, symbols }, jsonOptions);
            }, "Failed to discover archived symbols.", logger);
        })
        .WithName("GetArchivedSymbols").RequirePermission(UserPermission.ViewHistoricalData)
        .Produces(200);

        // GET /api/symbols/{symbol}/status — detailed status for one symbol
        group.MapGet(UiApiRoutes.SymbolStatus, async (
            string symbol,
            ConfigStore store,
            IStorageSearchService? searchService,
            StorageOptions storageOptions,
            HttpContext context,
            CancellationToken ct) =>
        {
            // Composite payload, projected by what the caller could fetch head-on. The subscription
            // configuration is what GetMonitoredSymbols serves under ViewConfig, and the storage block
            // is what the storage-backed symbol reads serve under ViewHistoricalData -- so a
            // ViewMarketData-only caller got both through this one route, and a historical-only caller
            // got the configuration. The admitted set stays wide; the payload narrows.
            var canReadConfiguration = EndpointAuthorization.HasAnyPermission(
                context, UserPermission.ViewConfig, UserPermission.ModifyConfig);
            var canReadStorage = EndpointAuthorization.HasPermission(context, UserPermission.ViewHistoricalData);

            var cfg = store.Load();
            var symbolCfg = (cfg.Symbols ?? Array.Empty<SymbolConfig>())
                .FirstOrDefault(s => string.Equals(s.Symbol, symbol, StringComparison.OrdinalIgnoreCase));

            object? storageInfo = null;
            if (canReadStorage && searchService is not null)
            {
                try
                {
                    var result = await searchService.SearchFilesAsync(
                        new FileSearchQuery(Symbols: new[] { symbol }, Take: 50), ct);
                    storageInfo = new
                    {
                        totalFiles = result.TotalMatches,
                        files = result.Results?.Take(10).Select(f => new { f.Path, f.SizeBytes, f.EventCount })
                    };
                }
                catch (IOException) { /* storage search not critical - file access issue */ }
                catch (InvalidOperationException) { /* storage search not critical - service issue */ }
            }

            return Results.Json(new
            {
                symbol,
                configured = symbolCfg is not null,
                config = canReadConfiguration ? symbolCfg : null,
                storage = storageInfo
            }, jsonOptions);
        })
        .WithName("GetSymbolStatus").RequireAnyPermission(UserPermission.ViewMarketData, UserPermission.ViewHistoricalData)
        .Produces(200);

        // POST /api/symbols/add — add a single symbol; returns {success, symbol}
        group.MapPost(UiApiRoutes.SymbolsAdd, async (ConfigStore store, SymbolSingleAddRequest req) =>
        {
            var upper = req.Symbol?.Trim().ToUpperInvariant() ?? "";
            if (string.IsNullOrWhiteSpace(upper) || !s_symbolPattern.IsMatch(upper))
                return Results.BadRequest(new { success = false, symbol = upper, error = "Invalid symbol format" });

            var cfg = store.Load();
            var list = (cfg.Symbols ?? Array.Empty<SymbolConfig>()).ToList();

            if (list.Any(s => string.Equals(s.Symbol, upper, StringComparison.OrdinalIgnoreCase)))
                return Results.Json(new { success = false, symbol = upper, error = $"Symbol '{upper}' is already in the watchlist" }, jsonOptions);

            list.Add(new SymbolConfig(Symbol: upper, SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10));
            var next = cfg with { Symbols = list.ToArray() };
            await store.SaveAsync(next);

            return Results.Json(new { success = true, symbol = upper }, jsonOptions);
        })
        .WithName("AddSymbols").RequirePermission(UserPermission.ModifyConfig)
        .Produces(200)
        .Produces(400)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // POST /api/symbols/{symbol}/remove — remove a symbol; returns {success, symbol}
        group.MapPost(UiApiRoutes.SymbolRemove, async (string symbol, ConfigStore store) =>
        {
            var upper = symbol.Trim().ToUpperInvariant();
            var cfg = store.Load();
            var list = (cfg.Symbols ?? Array.Empty<SymbolConfig>()).ToList();
            var removed = list.RemoveAll(s => string.Equals(s.Symbol, upper, StringComparison.OrdinalIgnoreCase));

            if (removed == 0)
                return Results.NotFound(new { success = false, symbol = upper, error = $"Symbol '{upper}' not found" });

            var next = cfg with { Symbols = list.ToArray() };
            await store.SaveAsync(next);
            return Results.Json(new { success = true, symbol = upper }, jsonOptions);
        })
        .WithName("RemoveSymbol").RequirePermission(UserPermission.ModifyConfig)
        .Produces(200)
        .Produces(404)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // GET /api/symbols/{symbol}/trades — recent trade files for a symbol
        group.MapGet(UiApiRoutes.SymbolTrades, async (
            string symbol,
            IStorageSearchService? searchService,
            CancellationToken ct) =>
        {
            if (searchService is null)
                return Results.Json(new { symbol, files = Array.Empty<object>(), message = "Storage search not available" }, jsonOptions);

            var result = await searchService.SearchFilesAsync(
                new FileSearchQuery(
                    Symbols: new[] { symbol },
                    Types: new[] { MarketEventType.Trade },
                    Take: 20), ct);

            return Results.Json(new
            {
                symbol,
                totalFiles = result.TotalMatches,
                files = result.Results?.Select(f => new { f.Path, f.SizeBytes, f.EventCount, f.Date })
            }, jsonOptions);
        })
        .WithName("GetSymbolTrades").RequirePermission(UserPermission.ViewHistoricalData)
        .Produces(200);

        // GET /api/symbols/{symbol}/depth — recent depth files for a symbol
        group.MapGet(UiApiRoutes.SymbolDepth, async (
            string symbol,
            IStorageSearchService? searchService,
            CancellationToken ct) =>
        {
            if (searchService is null)
                return Results.Json(new { symbol, files = Array.Empty<object>(), message = "Storage search not available" }, jsonOptions);

            var result = await searchService.SearchFilesAsync(
                new FileSearchQuery(
                    Symbols: new[] { symbol },
                    Types: new[] { MarketEventType.L2Snapshot },
                    Take: 20), ct);

            return Results.Json(new
            {
                symbol,
                totalFiles = result.TotalMatches,
                files = result.Results?.Select(f => new { f.Path, f.SizeBytes, f.EventCount, f.Date })
            }, jsonOptions);
        })
        .WithName("GetSymbolDepth").RequirePermission(UserPermission.ViewHistoricalData)
        .Produces(200);

        // GET /api/symbols/statistics — aggregate stats matching SymbolStatistics UI type
        group.MapGet(UiApiRoutes.SymbolsStatistics, async (
            ConfigStore store,
            IStorageSearchService? searchService,
            CancellationToken ct) =>
        {
            var cfg = store.Load();
            var symbols = cfg.Symbols ?? Array.Empty<SymbolConfig>();

            var archivedCount = 0;
            if (searchService is not null)
            {
                try
                {
                    var catalog = await searchService.DiscoverAsync(new DiscoveryQuery(), ct);
                    archivedCount = catalog.Symbols?.Count ?? 0;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // A disconnect is not a non-critical discovery failure; swallowing it would
                    // answer 200 with archivedCount silently left at its default.
                    throw;
                }
                catch { /* non-critical */ }
            }

            return Results.Json(new
            {
                totalSymbols = symbols.Length,
                monitoredSymbols = symbols.Length,
                archivedSymbols = archivedCount,
                symbolsWithErrors = 0,
                totalEventsLast24h = 0
            }, jsonOptions);
        })
        .WithName("GetSymbolStatistics").RequirePermission(UserPermission.ViewHistoricalData)
        .Produces(200);

        // POST /api/symbols/validate — validate symbol identifiers
        group.MapPost(UiApiRoutes.SymbolsValidate, (SymbolValidateRequest req) =>
        {
            if (req.Symbols is null || req.Symbols.Length == 0)
                return Results.BadRequest(new { error = "At least one symbol is required" });

            var results = req.Symbols.Select(s => new
            {
                symbol = s,
                valid = !string.IsNullOrWhiteSpace(s) && s_symbolPattern.IsMatch(s.Trim().ToUpperInvariant()),
                normalized = s?.Trim().ToUpperInvariant()
            });

            return Results.Json(new { results }, jsonOptions);
        })
        .WithName("ValidateSymbols").RequirePermission(UserPermission.ModifyConfig)
        .Produces(200)
        .Produces(400);

        // POST /api/symbols/{symbol}/archive — archive a symbol (remove from monitoring, keep data)
        group.MapPost(UiApiRoutes.SymbolArchive, async (string symbol, ConfigStore store) =>
        {
            var cfg = store.Load();
            var list = (cfg.Symbols ?? Array.Empty<SymbolConfig>()).ToList();
            var removed = list.RemoveAll(s => string.Equals(s.Symbol, symbol, StringComparison.OrdinalIgnoreCase));

            if (removed == 0)
                return Results.NotFound(new { error = $"Symbol '{symbol}' not found" });

            var next = cfg with { Symbols = list.ToArray() };
            await store.SaveAsync(next);
            return Results.Ok(new { archived = symbol, message = "Symbol removed from monitoring. Historical data is preserved." });
        })
        .WithName("ArchiveSymbol").RequirePermission(UserPermission.ModifyConfig)
        .Produces(200)
        .Produces(404)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // POST /api/symbols/bulk-add — add multiple symbols; returns {added, skipped, errors}
        group.MapPost(UiApiRoutes.SymbolsBulkAdd, async (ConfigStore store, SymbolBulkAddRequest req) =>
        {
            if (req.Symbols is null || req.Symbols.Length == 0)
                return Results.BadRequest(new { added = 0, skipped = 0, errors = new[] { "At least one symbol is required" } });

            var cfg = store.Load();
            var list = (cfg.Symbols ?? Array.Empty<SymbolConfig>()).ToList();
            var added = new List<string>();
            var skipped = new List<string>();
            var errors = new List<string>();

            foreach (var sym in req.Symbols)
            {
                var upper = sym?.Trim().ToUpperInvariant() ?? "";
                if (string.IsNullOrWhiteSpace(upper) || !s_symbolPattern.IsMatch(upper))
                {
                    errors.Add($"'{sym}' is not a valid symbol");
                    continue;
                }

                if (list.Any(s => string.Equals(s.Symbol, upper, StringComparison.OrdinalIgnoreCase)))
                {
                    skipped.Add(upper);
                    continue;
                }

                list.Add(new SymbolConfig(Symbol: upper, SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10));
                added.Add(upper);
            }

            var next = cfg with { Symbols = list.ToArray() };
            await store.SaveAsync(next);
            return Results.Json(new { added = added.Count, skipped = skipped.Count, errors }, jsonOptions);
        })
        .WithName("BulkAddSymbols").RequirePermission(UserPermission.ModifyConfig)
        .Produces(200)
        .Produces(400)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // POST /api/symbols/bulk-remove — remove multiple symbols
        group.MapPost(UiApiRoutes.SymbolsBulkRemove, async (ConfigStore store, SymbolBulkRemoveRequest req) =>
        {
            if (req.Symbols is null || req.Symbols.Length == 0)
                return Results.BadRequest(new { error = "At least one symbol is required" });

            var cfg = store.Load();
            var list = (cfg.Symbols ?? Array.Empty<SymbolConfig>()).ToList();
            var toRemove = new HashSet<string>(req.Symbols.Select(s => s.Trim().ToUpperInvariant()), StringComparer.OrdinalIgnoreCase);
            var removed = list.Where(s => toRemove.Contains(s.Symbol)).Select(s => s.Symbol).ToList();
            list.RemoveAll(s => toRemove.Contains(s.Symbol));

            var next = cfg with { Symbols = list.ToArray() };
            await store.SaveAsync(next);
            return Results.Json(new { removed, count = removed.Count }, jsonOptions);
        })
        .WithName("BulkRemoveSymbols").RequirePermission(UserPermission.ModifyConfig)
        .Produces(200)
        .Produces(400)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // GET /api/symbols/search — search symbols by query string
        group.MapGet(UiApiRoutes.SymbolsSearch, (
            HttpContext ctx,
            ConfigStore store) =>
        {
            var query = ctx.Request.Query["q"].FirstOrDefault() ?? "";
            var cfg = store.Load();
            var symbols = cfg.Symbols ?? Array.Empty<SymbolConfig>();

            var matches = string.IsNullOrWhiteSpace(query)
                ? symbols
                : symbols.Where(s =>
                    s.Symbol.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (s.Exchange?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));

            return Results.Json(new
            {
                query,
                results = matches.Select(s => new { s.Symbol, s.Exchange, s.Currency, s.InstrumentType })
            }, jsonOptions);
        })
        .WithName("SearchSymbols").RequirePermission(UserPermission.ViewMarketData)
        .Produces(200);

        // POST /api/symbols/batch — batch operations (add/remove/update)
        group.MapPost(UiApiRoutes.SymbolsBatch, async (ConfigStore store, SymbolBatchRequest req) =>
        {
            if (req.Operations is null || req.Operations.Length == 0)
                return Results.BadRequest(new { error = "At least one operation is required" });

            var cfg = store.Load();
            var list = (cfg.Symbols ?? Array.Empty<SymbolConfig>()).ToList();
            var results = new List<object>();

            foreach (var op in req.Operations)
            {
                var upper = op.Symbol?.Trim().ToUpperInvariant() ?? "";
                switch (op.Action?.ToLowerInvariant())
                {
                    case "add":
                        if (!string.IsNullOrWhiteSpace(upper) && s_symbolPattern.IsMatch(upper) &&
                            !list.Any(s => string.Equals(s.Symbol, upper, StringComparison.OrdinalIgnoreCase)))
                        {
                            list.Add(new SymbolConfig(Symbol: upper));
                            results.Add(new { symbol = upper, action = "add", success = true });
                        }
                        else
                        {
                            results.Add(new { symbol = upper, action = "add", success = false, reason = "invalid or duplicate" });
                        }
                        break;

                    case "remove":
                        var removed = list.RemoveAll(s => string.Equals(s.Symbol, upper, StringComparison.OrdinalIgnoreCase));
                        results.Add(new { symbol = upper, action = "remove", success = removed > 0 });
                        break;

                    default:
                        results.Add(new { symbol = upper, action = op.Action, success = false, reason = "unknown action" });
                        break;
                }
            }

            var next = cfg with { Symbols = list.ToArray() };
            await store.SaveAsync(next);
            return Results.Json(new { results }, jsonOptions);
        })
        .WithName("BatchSymbolOperations").RequirePermission(UserPermission.ModifyConfig)
        .Produces(200)
        .Produces(400)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // POST /api/symbols/create — create a new symbol with full configuration
        group.MapPost(UiApiRoutes.SymbolCreate, async (ConfigStore store, SymbolUniverseRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req.Symbol))
                return Results.BadRequest(new { error = "Symbol is required" });

            var upper = req.Symbol.Trim().ToUpperInvariant();
            if (!s_symbolPattern.IsMatch(upper))
                return Results.BadRequest(new { error = $"Invalid symbol format: {req.Symbol}" });

            var cfg = store.Load();
            var list = (cfg.Symbols ?? Array.Empty<SymbolConfig>()).ToList();

            if (list.Any(s => string.Equals(s.Symbol, upper, StringComparison.OrdinalIgnoreCase)))
                return Results.BadRequest(new { error = $"Symbol '{upper}' already configured" });

            var symbolConfig = new SymbolConfig(
                Symbol: upper,
                SubscribeTrades: req.SubscribeTrades,
                SubscribeDepth: req.SubscribeDepth,
                DepthLevels: req.DepthLevels,
                Exchange: req.Exchange ?? "SMART",
                Currency: req.Currency ?? "USD"
            );

            list.Add(symbolConfig);
            var next = cfg with { Symbols = list.ToArray() };
            await store.SaveAsync(next);

            return Results.Created($"/api/symbols/{upper}", new SymbolUniverseResponse(
                Symbol: upper,
                Configured: true,
                Status: "Configured"
            ));
        })
        .WithName("CreateSymbol").RequirePermission(UserPermission.ModifyConfig)
        .Produces<SymbolUniverseResponse>(201)
        .Produces(400)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // POST /api/symbols/{symbol}/update — update symbol configuration
        group.MapPost(UiApiRoutes.SymbolUpdate, async (string symbol, ConfigStore store, SymbolUniverseRequest req) =>
        {
            var upper = symbol.Trim().ToUpperInvariant();
            var cfg = store.Load();
            var list = (cfg.Symbols ?? Array.Empty<SymbolConfig>()).ToList();
            var idx = list.FindIndex(s => string.Equals(s.Symbol, upper, StringComparison.OrdinalIgnoreCase));

            if (idx < 0)
                return Results.NotFound(new { error = $"Symbol '{symbol}' not found" });

            var existing = list[idx];
            var updated = new SymbolConfig(
                Symbol: upper,
                SubscribeTrades: req.SubscribeTrades,
                SubscribeDepth: req.SubscribeDepth,
                DepthLevels: req.DepthLevels,
                SecurityType: existing.SecurityType,
                Exchange: req.Exchange ?? existing.Exchange,
                Currency: req.Currency ?? existing.Currency,
                PrimaryExchange: existing.PrimaryExchange,
                LocalSymbol: existing.LocalSymbol,
                TradingClass: existing.TradingClass,
                ConId: existing.ConId,
                InstrumentType: existing.InstrumentType,
                LiquidityProfile: existing.LiquidityProfile,
                UseRelaxedValidation: existing.UseRelaxedValidation,
                Strike: existing.Strike,
                Right: existing.Right,
                LastTradeDateOrContractMonth: existing.LastTradeDateOrContractMonth,
                OptionStyle: existing.OptionStyle,
                Multiplier: existing.Multiplier,
                UnderlyingSymbol: existing.UnderlyingSymbol
            );

            list[idx] = updated;
            var next = cfg with { Symbols = list.ToArray() };
            await store.SaveAsync(next);

            return Results.Ok(new SymbolUniverseResponse(
                Symbol: upper,
                Configured: true,
                Status: "Updated"
            ));
        })
        .WithName("UpdateSymbol").RequirePermission(UserPermission.ModifyConfig)
        .Produces<SymbolUniverseResponse>(200)
        .Produces(404)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // DELETE /api/symbols/{symbol} — delete a symbol
        group.MapDelete(UiApiRoutes.SymbolDelete, async (string symbol, ConfigStore store) =>
        {
            var upper = symbol.Trim().ToUpperInvariant();
            var cfg = store.Load();
            var list = (cfg.Symbols ?? Array.Empty<SymbolConfig>()).ToList();
            var removed = list.RemoveAll(s => string.Equals(s.Symbol, upper, StringComparison.OrdinalIgnoreCase));

            if (removed == 0)
                return Results.NotFound(new { error = $"Symbol '{symbol}' not found" });

            var next = cfg with { Symbols = list.ToArray() };
            await store.SaveAsync(next);
            return Results.Ok(new { deleted = upper, message = "Symbol removed from monitoring" });
        })
        .WithName("DeleteSymbol").RequirePermission(UserPermission.ModifyConfig)
        .Produces(200)
        .Produces(404)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    /// <summary>
    /// Maps the /api/indices endpoints for index constituent data.
    /// </summary>
    public static void MapIndexEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Indices");

        // Index constituents
        group.MapGet(UiApiRoutes.IndicesConstituents, (string indexName) =>
        {
            return Results.Json(new
            {
                index = indexName,
                constituents = Array.Empty<object>(),
                message = $"Index '{indexName}' constituent data is not yet available. Configure an index data provider.",
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetIndexConstituents").RequirePermission(UserPermission.ViewMarketData)
        .Produces(200);
    }
}

// Request DTOs

// Single-symbol add: frontend sends { symbol, provider }
internal sealed record SymbolSingleAddRequest(string? Symbol, string? Provider = null);

// Bulk add: frontend sends { symbols: [...] }
internal sealed record SymbolBulkAddRequest(string[] Symbols);

internal sealed record SymbolValidateRequest(string[] Symbols);

internal sealed record SymbolBulkRemoveRequest(string[] Symbols);

internal sealed record SymbolBatchRequest(SymbolBatchOperation[] Operations);

internal sealed record SymbolBatchOperation(string Symbol, string Action);

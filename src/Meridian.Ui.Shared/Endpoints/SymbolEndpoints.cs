using System.Text.Json;
using System.Text.RegularExpressions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Domain.Enums;
using Meridian.Storage;
using Meridian.Storage.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

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

        // GET /api/symbols — all configured symbols
        group.MapGet(UiApiRoutes.Symbols, (ConfigStore store) =>
        {
            var cfg = store.Load();
            var symbols = cfg.Symbols ?? Array.Empty<SymbolConfig>();
            return Results.Json(symbols, jsonOptions);
        })
        .WithName("GetSymbols")
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
        .WithName("GetMonitoredSymbols")
        .Produces(200);

        // GET /api/symbols/archived — symbols that have stored data files
        group.MapGet(UiApiRoutes.SymbolsArchived, async (
            IStorageSearchService? searchService,
            StorageOptions storageOptions,
            CancellationToken ct) =>
        {
            if (searchService is null)
                return Results.Json(new { count = 0, symbols = Array.Empty<string>(), message = "Storage search service not available" }, jsonOptions);

            try
            {
                var catalog = await searchService.DiscoverAsync(new DiscoveryQuery(), ct);
                var symbols = catalog.Symbols?.Select(s => s.Symbol).ToArray() ?? Array.Empty<string>();
                return Results.Json(new { count = symbols.Length, symbols }, jsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to discover archived symbols: {ex.Message}");
            }
        })
        .WithName("GetArchivedSymbols")
        .Produces(200);

        // GET /api/symbols/{symbol}/status — detailed status for one symbol
        group.MapGet(UiApiRoutes.SymbolStatus, async (
            string symbol,
            ConfigStore store,
            IStorageSearchService? searchService,
            StorageOptions storageOptions,
            CancellationToken ct) =>
        {
            var cfg = store.Load();
            var symbolCfg = (cfg.Symbols ?? Array.Empty<SymbolConfig>())
                .FirstOrDefault(s => string.Equals(s.Symbol, symbol, StringComparison.OrdinalIgnoreCase));

            object? storageInfo = null;
            if (searchService is not null)
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
                config = symbolCfg,
                storage = storageInfo
            }, jsonOptions);
        })
        .WithName("GetSymbolStatus")
        .Produces(200);

        // POST /api/symbols/add — add one or more symbols
        group.MapPost(UiApiRoutes.SymbolsAdd, async (ConfigStore store, SymbolAddRequest req) =>
        {
            if (req.Symbols is null || req.Symbols.Length == 0)
                return Results.BadRequest(new { error = "At least one symbol is required" });

            var cfg = store.Load();
            var list = (cfg.Symbols ?? Array.Empty<SymbolConfig>()).ToList();
            var added = new List<string>();
            var skipped = new List<string>();

            foreach (var sym in req.Symbols)
            {
                var upper = sym.Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(upper) || !s_symbolPattern.IsMatch(upper))
                {
                    skipped.Add(sym);
                    continue;
                }

                if (list.Any(s => string.Equals(s.Symbol, upper, StringComparison.OrdinalIgnoreCase)))
                {
                    skipped.Add(upper);
                    continue;
                }

                list.Add(new SymbolConfig(
                    Symbol: upper,
                    SubscribeTrades: req.SubscribeTrades ?? true,
                    SubscribeDepth: req.SubscribeDepth ?? true,
                    DepthLevels: req.DepthLevels ?? 10));
                added.Add(upper);
            }

            var next = cfg with { Symbols = list.ToArray() };
            await store.SaveAsync(next);

            return Results.Json(new { added, skipped }, jsonOptions);
        })
        .WithName("AddSymbols")
        .Produces(200)
        .Produces(400)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // POST /api/symbols/{symbol}/remove — remove a symbol
        group.MapPost(UiApiRoutes.SymbolRemove, async (string symbol, ConfigStore store) =>
        {
            var cfg = store.Load();
            var list = (cfg.Symbols ?? Array.Empty<SymbolConfig>()).ToList();
            var removed = list.RemoveAll(s => string.Equals(s.Symbol, symbol, StringComparison.OrdinalIgnoreCase));

            if (removed == 0)
                return Results.NotFound(new { error = $"Symbol '{symbol}' not found in configuration" });

            var next = cfg with { Symbols = list.ToArray() };
            await store.SaveAsync(next);
            return Results.Ok(new { removed = symbol });
        })
        .WithName("RemoveSymbol")
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
        .WithName("GetSymbolTrades")
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
        .WithName("GetSymbolDepth")
        .Produces(200);

        // GET /api/symbols/statistics — aggregate stats across all symbols
        group.MapGet(UiApiRoutes.SymbolsStatistics, async (
            ConfigStore store,
            IStorageSearchService? searchService,
            CancellationToken ct) =>
        {
            var cfg = store.Load();
            var symbols = cfg.Symbols ?? Array.Empty<SymbolConfig>();

            object? storageStats = null;
            if (searchService is not null)
            {
                try
                {
                    var catalog = await searchService.DiscoverAsync(new DiscoveryQuery(), ct);
                    storageStats = new
                    {
                        archivedSymbols = catalog.Symbols?.Count ?? 0,
                        totalEvents = catalog.TotalEvents,
                        totalBytes = catalog.TotalBytes,
                        eventTypes = catalog.EventTypes
                    };
                }
                catch { /* non-critical */ }
            }

            return Results.Json(new
            {
                monitoredCount = symbols.Length,
                byInstrumentType = symbols.GroupBy(s => s.InstrumentType.ToString())
                    .Select(g => new { type = g.Key, count = g.Count() }),
                byExchange = symbols.GroupBy(s => s.Exchange)
                    .Select(g => new { exchange = g.Key, count = g.Count() }),
                tradesEnabled = symbols.Count(s => s.SubscribeTrades),
                depthEnabled = symbols.Count(s => s.SubscribeDepth),
                storage = storageStats
            }, jsonOptions);
        })
        .WithName("GetSymbolStatistics")
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
        .WithName("ValidateSymbols")
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
        .WithName("ArchiveSymbol")
        .Produces(200)
        .Produces(404)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // POST /api/symbols/bulk-add — add multiple symbols at once
        group.MapPost(UiApiRoutes.SymbolsBulkAdd, async (ConfigStore store, SymbolAddRequest req) =>
        {
            if (req.Symbols is null || req.Symbols.Length == 0)
                return Results.BadRequest(new { error = "At least one symbol is required" });

            var cfg = store.Load();
            var list = (cfg.Symbols ?? Array.Empty<SymbolConfig>()).ToList();
            var added = new List<string>();
            var skipped = new List<string>();

            foreach (var sym in req.Symbols)
            {
                var upper = sym.Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(upper) || !s_symbolPattern.IsMatch(upper))
                {
                    skipped.Add(sym);
                    continue;
                }

                if (list.Any(s => string.Equals(s.Symbol, upper, StringComparison.OrdinalIgnoreCase)))
                {
                    skipped.Add(upper);
                    continue;
                }

                list.Add(new SymbolConfig(
                    Symbol: upper,
                    SubscribeTrades: req.SubscribeTrades ?? true,
                    SubscribeDepth: req.SubscribeDepth ?? true,
                    DepthLevels: req.DepthLevels ?? 10));
                added.Add(upper);
            }

            var next = cfg with { Symbols = list.ToArray() };
            await store.SaveAsync(next);
            return Results.Json(new { added, skipped }, jsonOptions);
        })
        .WithName("BulkAddSymbols")
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
        .WithName("BulkRemoveSymbols")
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
        .WithName("SearchSymbols")
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
        .WithName("BatchSymbolOperations")
        .Produces(200)
        .Produces(400)
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
        .WithName("GetIndexConstituents")
        .Produces(200);
    }
}

// Request DTOs

internal sealed record SymbolAddRequest(
    string[] Symbols,
    bool? SubscribeTrades = null,
    bool? SubscribeDepth = null,
    int? DepthLevels = null);

internal sealed record SymbolValidateRequest(string[] Symbols);

internal sealed record SymbolBulkRemoveRequest(string[] Symbols);

internal sealed record SymbolBatchRequest(SymbolBatchOperation[] Operations);

internal sealed record SymbolBatchOperation(string Symbol, string Action);

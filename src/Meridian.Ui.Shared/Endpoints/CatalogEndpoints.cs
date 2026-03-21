using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Domain.Enums;
using Meridian.Storage.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Endpoints for the Data Catalog — unified search and discovery over stored market data.
/// Supports natural-language and structured queries, timeline coverage, and symbol summaries.
/// </summary>
public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Catalog");

        // GET /api/catalog/search — structured or natural-language search over stored data
        // Query params: q (natural language), symbol, type, from, to, skip, take
        group.MapGet(UiApiRoutes.CatalogSearch, async (
            HttpContext ctx,
            IStorageSearchService? searchService,
            CancellationToken ct) =>
        {
            if (searchService is null)
                return Results.Json(new { message = "Storage search not available" }, jsonOptions);

            var qs = ctx.Request.Query;
            var naturalQuery = qs["q"].ToString();
            var skip = int.TryParse(qs["skip"], out var s) ? Math.Max(0, s) : 0;
            var take = int.TryParse(qs["take"], out var t) ? Math.Clamp(t, 1, 500) : 50;

            // If a natural-language query is given, parse it first and then merge
            // any explicit query-string overrides on top.
            var parsed = !string.IsNullOrWhiteSpace(naturalQuery)
                ? searchService.ParseNaturalLanguageQuery(naturalQuery)
                : null;

            // Explicit query-string parameters override parsed NL results.
            var symbolParam = qs["symbol"].ToString();
            var typeParam = qs["type"].ToString();
            var fromParam = qs["from"].ToString();
            var toParam = qs["to"].ToString();

            string[]? symbols = !string.IsNullOrWhiteSpace(symbolParam)
                ? symbolParam.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : parsed?.Symbols;

            string[]? types = !string.IsNullOrWhiteSpace(typeParam)
                ? typeParam.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : null;

            DateTimeOffset? from = DateTimeOffset.TryParse(fromParam, out var fd) ? fd : parsed?.From;

            DateTimeOffset? to = DateTimeOffset.TryParse(toParam, out var td) ? td.AddDays(1) : parsed?.To;

            // Map type names to MarketEventType values when provided
            MarketEventType[]? eventTypes = null;
            if (types?.Length > 0)
            {
                var mapped = types
                    .Select(t => t.ToLowerInvariant() switch
                    {
                        "trade" or "trades" => (MarketEventType?)MarketEventType.Trade,
                        "quote" or "quotes" or "bbo" => MarketEventType.BboQuote,
                        "depth" or "l2" or "snapshot" => MarketEventType.L2Snapshot,
                        "bar" or "bars" => MarketEventType.AggregateBar,
                        _ => (MarketEventType?)null
                    })
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToArray();
                if (mapped.Length > 0)
                    eventTypes = mapped;
            }
            else if (parsed?.Types?.Length > 0)
            {
                eventTypes = parsed.Types;
            }

            var query = new FileSearchQuery(
                Symbols: symbols,
                Types: eventTypes,
                From: from,
                To: to,
                Skip: skip,
                Take: take,
                Descending: true
            );

            try
            {
                var result = await searchService.SearchFilesAsync(query, ct);
                return Results.Json(new
                {
                    totalCount = result.TotalMatches,
                    skip,
                    take,
                    parsedQuery = naturalQuery.Length > 0 ? new
                    {
                        symbols = query.Symbols,
                        types = eventTypes?.Select(e => e.ToString()),
                        from = query.From,
                        to = query.To
                    } : null,
                    files = result.Results.Select(f => new
                    {
                        path = f.Path,
                        symbol = f.Symbol,
                        eventType = f.EventType,
                        source = f.Source,
                        date = f.Date,
                        sizeBytes = f.SizeBytes,
                        eventCount = f.EventCount,
                        qualityScore = f.QualityScore
                    })
                }, jsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Catalog search failed: {ex.Message}");
            }
        })
        .WithName("CatalogSearch")
        .Produces(200);

        // GET /api/catalog/symbols — list all symbols with stored data coverage
        group.MapGet(UiApiRoutes.CatalogSymbols, async (
            IStorageSearchService? searchService,
            CancellationToken ct) =>
        {
            if (searchService is null)
                return Results.Json(new { message = "Storage search not available" }, jsonOptions);

            try
            {
                var catalog = await searchService.DiscoverAsync(new DiscoveryQuery(), ct);
                return Results.Json(new
                {
                    generatedAt = catalog.GeneratedAt,
                    totalSymbols = catalog.Symbols.Count,
                    symbols = catalog.Symbols.Select(s => new
                    {
                        symbol = s.Symbol,
                        firstDate = s.FirstDate,
                        lastDate = s.LastDate,
                        totalEvents = s.TotalEvents,
                        totalBytes = s.TotalBytes,
                        eventTypes = s.EventTypes,
                        sources = s.Sources
                    })
                }, jsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to list symbols: {ex.Message}");
            }
        })
        .WithName("CatalogSymbols")
        .Produces(200);

        // GET /api/catalog/timeline — per-symbol data coverage for timeline/Gantt visualization
        // Query params: symbol (optional filter), from (date), to (date)
        group.MapGet(UiApiRoutes.CatalogTimeline, async (
            HttpContext ctx,
            IStorageSearchService? searchService,
            CancellationToken ct) =>
        {
            if (searchService is null)
                return Results.Json(new { message = "Storage search not available" }, jsonOptions);

            var qs = ctx.Request.Query;
            var symbolFilter = qs["symbol"].ToString();
            DateTimeOffset? from = DateTimeOffset.TryParse(qs["from"].ToString(), out var fd) ? fd : null;
            DateTimeOffset? to = DateTimeOffset.TryParse(qs["to"].ToString(), out var td) ? td.AddDays(1) : null;

            string[]? symbols = !string.IsNullOrWhiteSpace(symbolFilter)
                ? symbolFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : null;

            try
            {
                // Fetch all matching file metadata to build day-by-day coverage
                var query = new FileSearchQuery(
                    Symbols: symbols,
                    From: from,
                    To: to,
                    Skip: 0,
                    Take: 10_000,
                    Descending: false
                );
                var result = await searchService.SearchFilesAsync(query, ct);

                // Group by symbol → then by date → event types present that day
                var timeline = result.Results
                    .GroupBy(f => f.Symbol)
                    .OrderBy(g => g.Key)
                    .Select(symbolGroup =>
                    {
                        var dailyCoverage = symbolGroup
                            .GroupBy(f => f.Date.ToString("yyyy-MM-dd"))
                            .OrderBy(g => g.Key)
                            .Select(dayGroup => new
                            {
                                date = dayGroup.Key,
                                eventTypes = dayGroup.Select(f => f.EventType).Distinct().OrderBy(t => t),
                                sources = dayGroup.Select(f => f.Source).Distinct().OrderBy(s => s),
                                totalEvents = dayGroup.Sum(f => f.EventCount),
                                totalBytes = dayGroup.Sum(f => f.SizeBytes)
                            })
                            .ToList();

                        var dates = symbolGroup.Select(f => f.Date).ToList();
                        return new
                        {
                            symbol = symbolGroup.Key,
                            firstDate = dates.Min(),
                            lastDate = dates.Max(),
                            coverageDays = dailyCoverage.Count,
                            dailyCoverage
                        };
                    });

                return Results.Json(new
                {
                    generatedAt = DateTimeOffset.UtcNow,
                    filter = new { symbols, from, to },
                    timeline
                }, jsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to build timeline: {ex.Message}");
            }
        })
        .WithName("CatalogTimeline")
        .Produces(200);

        // GET /api/catalog/coverage — aggregate coverage summary (totals, date ranges, sources)
        group.MapGet(UiApiRoutes.CatalogCoverage, async (
            IStorageSearchService? searchService,
            CancellationToken ct) =>
        {
            if (searchService is null)
                return Results.Json(new { message = "Storage search not available" }, jsonOptions);

            try
            {
                var catalog = await searchService.DiscoverAsync(new DiscoveryQuery(), ct);
                return Results.Json(new
                {
                    generatedAt = catalog.GeneratedAt,
                    rootPath = catalog.RootPath,
                    totalSymbols = catalog.Symbols.Count,
                    totalEvents = catalog.TotalEvents,
                    totalBytes = catalog.TotalBytes,
                    dateRange = new
                    {
                        start = catalog.DateRange.Start,
                        end = catalog.DateRange.End
                    },
                    eventTypes = catalog.EventTypes,
                    sources = catalog.Sources,
                    symbolSummary = catalog.Symbols.Select(s => new
                    {
                        symbol = s.Symbol,
                        firstDate = s.FirstDate,
                        lastDate = s.LastDate,
                        totalEvents = s.TotalEvents
                    })
                }, jsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to build coverage summary: {ex.Message}");
            }
        })
        .WithName("CatalogCoverage")
        .Produces(200);
    }
}

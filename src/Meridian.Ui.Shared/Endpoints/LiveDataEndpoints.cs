using System.Text.Json;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Api;
using Meridian.Domain.Collectors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering live data API endpoints.
/// Provides access to collected real-time market data (trades, quotes, order book, order flow).
/// </summary>
public static class LiveDataEndpoints
{
    /// <summary>
    /// Maps all live data API endpoints.
    /// </summary>
    public static void MapLiveDataEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Live Data");

        // GET /api/data/trades/{symbol} — recent trades for a symbol
        group.MapGet(UiApiRoutes.DataTrades, (string symbol, int? limit, HttpContext ctx) =>
        {
            var tradeCollector = ctx.RequestServices.GetService<TradeDataCollector>();
            if (tradeCollector is null)
            {
                return Results.Json(
                    new { error = "Trade collector not available", symbol },
                    jsonOptions,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var recentTrades = tradeCollector.GetRecentTrades(symbol, limit ?? 50);

            var tradeDtos = new List<TradeDataResponse>(recentTrades.Count);
            foreach (var t in recentTrades)
            {
                tradeDtos.Add(new TradeDataResponse(
                    Symbol: t.Symbol,
                    Timestamp: t.Timestamp,
                    Price: t.Price,
                    Size: t.Size,
                    Aggressor: t.Aggressor.ToString(),
                    SequenceNumber: t.SequenceNumber,
                    StreamId: t.StreamId,
                    Venue: t.Venue));
            }

            var response = new TradesResponse(
                Symbol: symbol,
                Trades: tradeDtos,
                Count: tradeDtos.Count,
                Timestamp: DateTimeOffset.UtcNow);

            return Results.Json(response, jsonOptions);
        })
        .WithName("GetTrades")
        .Produces(200)
        .Produces(503);

        // GET /api/data/quotes/{symbol} — latest quote for a symbol
        group.MapGet(UiApiRoutes.DataQuotes, (string symbol, HttpContext ctx) =>
        {
            var quoteCollector = ctx.RequestServices.GetService<QuoteCollector>();
            if (quoteCollector is null)
            {
                return Results.Json(
                    new { error = "Quote collector not available", symbol },
                    jsonOptions,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            QuoteDataResponse? quoteDto = null;
            if (quoteCollector.TryGet(symbol, out var bbo) && bbo is not null)
            {
                quoteDto = new QuoteDataResponse(
                    Symbol: bbo.Symbol,
                    Timestamp: bbo.Timestamp,
                    BidPrice: bbo.BidPrice,
                    BidSize: bbo.BidSize,
                    AskPrice: bbo.AskPrice,
                    AskSize: bbo.AskSize,
                    MidPrice: bbo.MidPrice,
                    Spread: bbo.Spread,
                    SequenceNumber: bbo.SequenceNumber,
                    StreamId: bbo.StreamId,
                    Venue: bbo.Venue);
            }

            var response = new QuotesResponse(
                Symbol: symbol,
                Quote: quoteDto,
                Timestamp: DateTimeOffset.UtcNow);

            return Results.Json(response, jsonOptions);
        })
        .WithName("GetQuotes")
        .Produces(200)
        .Produces(503);

        // GET /api/data/orderbook/{symbol} — full order book snapshot
        group.MapGet(UiApiRoutes.DataOrderbook, (string symbol, int? levels, HttpContext ctx) =>
        {
            var depthCollector = ctx.RequestServices.GetService<MarketDepthCollector>();
            if (depthCollector is null)
            {
                return Results.Json(
                    new { error = "Depth collector not available", symbol },
                    jsonOptions,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var snapshot = depthCollector.GetCurrentSnapshot(symbol);
            if (snapshot is null)
            {
                return Results.Json(new OrderBookResponse(
                    Symbol: symbol,
                    Timestamp: DateTimeOffset.UtcNow,
                    Bids: Array.Empty<OrderBookLevelDto>(),
                    Asks: Array.Empty<OrderBookLevelDto>(),
                    MidPrice: null,
                    Imbalance: null,
                    MarketState: "NoData",
                    SequenceNumber: 0,
                    IsStale: false,
                    StreamId: null,
                    Venue: null), jsonOptions);
            }

            var maxLevels = levels ?? 10;

            var bids = snapshot.Bids
                .Take(maxLevels)
                .Select(l => new OrderBookLevelDto(
                    Side: l.Side.ToString(),
                    Level: l.Level,
                    Price: l.Price,
                    Size: l.Size,
                    MarketMaker: l.MarketMaker))
                .ToList();

            var asks = snapshot.Asks
                .Take(maxLevels)
                .Select(l => new OrderBookLevelDto(
                    Side: l.Side.ToString(),
                    Level: l.Level,
                    Price: l.Price,
                    Size: l.Size,
                    MarketMaker: l.MarketMaker))
                .ToList();

            var isStale = depthCollector.IsSymbolStreamStale(symbol);

            var response = new OrderBookResponse(
                Symbol: symbol,
                Timestamp: snapshot.Timestamp,
                Bids: bids,
                Asks: asks,
                MidPrice: snapshot.MidPrice,
                Imbalance: snapshot.Imbalance,
                MarketState: snapshot.MarketState.ToString(),
                SequenceNumber: snapshot.SequenceNumber,
                IsStale: isStale,
                StreamId: snapshot.StreamId,
                Venue: snapshot.Venue);

            return Results.Json(response, jsonOptions);
        })
        .WithName("GetOrderBook")
        .Produces(200)
        .Produces(503);

        // GET /api/data/l3-orderbook/{symbol} — L3 order-book snapshot derived from order lifecycle events
        group.MapGet(UiApiRoutes.DataL3Orderbook, (string symbol, int? levels, HttpContext ctx) =>
        {
            var l3Collector = ctx.RequestServices.GetService<L3OrderBookCollector>();
            if (l3Collector is null)
            {
                return Results.Json(
                    new { error = "L3 order book collector not available", symbol },
                    jsonOptions,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var snapshot = l3Collector.GetCurrentSnapshot(symbol);
            if (snapshot is null)
            {
                return Results.Json(new OrderBookResponse(
                    Symbol: symbol,
                    Timestamp: DateTimeOffset.UtcNow,
                    Bids: Array.Empty<OrderBookLevelDto>(),
                    Asks: Array.Empty<OrderBookLevelDto>(),
                    MidPrice: null,
                    Imbalance: null,
                    MarketState: "NoData",
                    SequenceNumber: 0,
                    IsStale: false,
                    StreamId: null,
                    Venue: null), jsonOptions);
            }

            var maxLevels = levels ?? 10;

            var bids = snapshot.Bids
                .Take(maxLevels)
                .Select(l => new OrderBookLevelDto(
                    Side: l.Side.ToString(),
                    Level: l.Level,
                    Price: l.Price,
                    Size: l.Size,
                    MarketMaker: l.MarketMaker))
                .ToList();

            var asks = snapshot.Asks
                .Take(maxLevels)
                .Select(l => new OrderBookLevelDto(
                    Side: l.Side.ToString(),
                    Level: l.Level,
                    Price: l.Price,
                    Size: l.Size,
                    MarketMaker: l.MarketMaker))
                .ToList();

            var response = new OrderBookResponse(
                Symbol: symbol,
                Timestamp: snapshot.Timestamp,
                Bids: bids,
                Asks: asks,
                MidPrice: snapshot.MidPrice,
                Imbalance: snapshot.Imbalance,
                MarketState: "L3",
                SequenceNumber: snapshot.SequenceNumber,
                IsStale: false,
                StreamId: snapshot.StreamId,
                Venue: snapshot.Venue);

            return Results.Json(response, jsonOptions);
        })
        .WithName("GetL3OrderBook")
        .Produces(200)
        .Produces(503);

        // GET /api/data/bbo/{symbol} — best bid/offer
        group.MapGet(UiApiRoutes.DataBbo, (string symbol, HttpContext ctx) =>
        {
            var quoteCollector = ctx.RequestServices.GetService<QuoteCollector>();
            if (quoteCollector is null)
            {
                return Results.Json(
                    new { error = "Quote collector not available", symbol },
                    jsonOptions,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (!quoteCollector.TryGet(symbol, out var bbo) || bbo is null)
            {
                return Results.Json(
                    new { error = "No quote data available", symbol },
                    jsonOptions,
                    statusCode: StatusCodes.Status404NotFound);
            }

            var response = new BboResponse(
                Symbol: bbo.Symbol,
                Timestamp: bbo.Timestamp,
                BidPrice: bbo.BidPrice,
                BidSize: bbo.BidSize,
                AskPrice: bbo.AskPrice,
                AskSize: bbo.AskSize,
                MidPrice: bbo.MidPrice,
                Spread: bbo.Spread,
                SequenceNumber: bbo.SequenceNumber,
                StreamId: bbo.StreamId,
                Venue: bbo.Venue);

            return Results.Json(response, jsonOptions);
        })
        .WithName("GetBbo")
        .Produces(200)
        .Produces(404)
        .Produces(503);

        // GET /api/data/orderflow/{symbol} — order flow statistics
        group.MapGet(UiApiRoutes.DataOrderflow, (string symbol, HttpContext ctx) =>
        {
            var tradeCollector = ctx.RequestServices.GetService<TradeDataCollector>();
            if (tradeCollector is null)
            {
                return Results.Json(
                    new { error = "Trade collector not available", symbol },
                    jsonOptions,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var stats = tradeCollector.GetOrderFlowSnapshot(symbol);
            if (stats is null)
            {
                return Results.Json(
                    new { error = "No order flow data available", symbol },
                    jsonOptions,
                    statusCode: StatusCodes.Status404NotFound);
            }

            var response = new OrderFlowResponse(
                Symbol: stats.Symbol,
                Timestamp: stats.Timestamp,
                BuyVolume: stats.BuyVolume,
                SellVolume: stats.SellVolume,
                UnknownVolume: stats.UnknownVolume,
                VWAP: stats.VWAP,
                Imbalance: stats.Imbalance,
                TradeCount: stats.TradeCount,
                SequenceNumber: stats.SequenceNumber,
                StreamId: stats.StreamId,
                Venue: stats.Venue);

            return Results.Json(response, jsonOptions);
        })
        .WithName("GetOrderFlow")
        .Produces(200)
        .Produces(404)
        .Produces(503);

        // GET /api/data/health — live data health overview
        group.MapGet(UiApiRoutes.DataHealth, (HttpContext ctx) =>
        {
            var quoteCollector = ctx.RequestServices.GetService<QuoteCollector>();
            var depthCollector = ctx.RequestServices.GetService<MarketDepthCollector>();
            var pipeline = ctx.RequestServices.GetService<EventPipeline>();

            var collectorsAvailable = quoteCollector is not null || depthCollector is not null;

            // Gather quote state
            var quoteSnapshot = quoteCollector?.Snapshot();
            var symbolsWithQuotes = quoteSnapshot?.Count ?? 0;

            // Gather depth state
            var depthSymbols = depthCollector?.GetTrackedSymbols() ?? Array.Empty<string>();
            var symbolsWithDepth = depthSymbols.Count;

            // Pipeline stats
            var pipelineStats = pipeline?.GetStatistics();

            // Per-symbol health
            var allSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (quoteSnapshot is not null)
            {
                foreach (var sym in quoteSnapshot.Keys)
                    allSymbols.Add(sym);
            }
            foreach (var sym in depthSymbols)
                allSymbols.Add(sym);

            var symbolHealth = new List<SymbolDataHealthDto>();
            foreach (var sym in allSymbols.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
            {
                var hasQuotes = quoteSnapshot is not null && quoteSnapshot.ContainsKey(sym);
                var hasDepth = depthSymbols.Contains(sym);
                var isDepthStale = depthCollector?.IsSymbolStreamStale(sym) ?? false;

                DateTimeOffset? lastQuoteTs = null;
                if (hasQuotes && quoteSnapshot!.TryGetValue(sym, out var q))
                    lastQuoteTs = q.Timestamp;

                DateTimeOffset? lastBookTs = null;
                if (hasDepth)
                {
                    var snap = depthCollector!.GetCurrentSnapshot(sym);
                    lastBookTs = snap?.Timestamp;
                }

                symbolHealth.Add(new SymbolDataHealthDto(
                    Symbol: sym,
                    HasQuotes: hasQuotes,
                    HasDepth: hasDepth,
                    IsDepthStale: isDepthStale,
                    LastQuoteTimestamp: lastQuoteTs,
                    LastOrderBookTimestamp: lastBookTs));
            }

            var response = new LiveDataHealthResponse(
                Timestamp: DateTimeOffset.UtcNow,
                CollectorsAvailable: collectorsAvailable,
                SymbolsWithQuotes: symbolsWithQuotes,
                SymbolsWithDepth: symbolsWithDepth,
                PipelinePublished: pipelineStats?.PublishedCount ?? 0,
                PipelineDropped: pipelineStats?.DroppedCount ?? 0,
                PipelineConsumed: pipelineStats?.ConsumedCount ?? 0,
                PipelineQueueSize: pipelineStats?.CurrentQueueSize ?? 0,
                PipelineUtilization: pipelineStats?.QueueUtilization ?? 0,
                Symbols: symbolHealth);

            return Results.Json(response, jsonOptions);
        })
        .WithName("GetDataHealth")
        .Produces(200);
    }
}

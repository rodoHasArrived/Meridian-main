using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Api;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for accessing real-time market data streams.
/// </summary>
public sealed class LiveDataService
{
    private static readonly Lazy<LiveDataService> _instance = new(() => new LiveDataService());
    public static LiveDataService Instance => _instance.Value;

    private LiveDataService() { }

    /// <summary>
    /// Gets recent trades for a symbol.
    /// </summary>
    public async Task<List<TradeEvent>?> GetRecentTradesAsync(string symbol, int limit = 100, CancellationToken ct = default)
    {
        // The server answers with a TradesResponse envelope, not a bare array.
        var endpoint = UiApiRoutes.WithQuery(
            UiApiRoutes.WithParam(UiApiRoutes.DataTrades, "symbol", symbol),
            "limit=" + limit.ToString(CultureInfo.InvariantCulture));
        var response = (await ApiClientService.Instance.GetWithResponseAsync<TradesResponse>(endpoint, ct).ConfigureAwait(false)).DataOrLoggedNull("Get recent trades");
        return response is null ? null : MapTrades(response);
    }

    /// <summary>
    /// Gets the latest quote for a symbol. The server keeps only the current quote, so the list
    /// holds at most one entry and is empty until a quote has been observed.
    /// </summary>
    public async Task<List<QuoteEvent>?> GetRecentQuotesAsync(string symbol, int limit = 100, CancellationToken ct = default)
    {
        // The server answers with a QuotesResponse envelope carrying one nullable quote; it has
        // no quote history, so the limit only bounds the (at most one) result.
        var endpoint = UiApiRoutes.WithParam(UiApiRoutes.DataQuotes, "symbol", symbol);
        var response = (await ApiClientService.Instance.GetWithResponseAsync<QuotesResponse>(endpoint, ct).ConfigureAwait(false)).DataOrLoggedNull("Get recent quotes");
        return response is null ? null : MapQuotes(response, limit);
    }

    /// <summary>
    /// Gets the current order book snapshot for a symbol.
    /// </summary>
    public async Task<OrderBookSnapshot?> GetOrderBookAsync(string symbol, int levels = 10, CancellationToken ct = default)
    {
        var endpoint = UiApiRoutes.WithQuery(
            UiApiRoutes.WithParam(UiApiRoutes.DataOrderbook, "symbol", symbol),
            "levels=" + levels.ToString(CultureInfo.InvariantCulture));
        var response = (await ApiClientService.Instance.GetWithResponseAsync<OrderBookResponse>(endpoint, ct).ConfigureAwait(false)).DataOrLoggedNull("Get order book snapshot");
        return response is null ? null : MapOrderBook(response);
    }

    internal static List<TradeEvent> MapTrades(TradesResponse response)
        => (response.Trades ?? [])
            .Select(trade => new TradeEvent
            {
                Symbol = trade.Symbol,
                Price = trade.Price,
                Size = trade.Size,
                Timestamp = trade.Timestamp.UtcDateTime,
                Exchange = trade.Venue ?? string.Empty,
                SequenceNumber = trade.SequenceNumber,
                Side = trade.Aggressor ?? string.Empty
            })
            .ToList();

    internal static List<QuoteEvent> MapQuotes(QuotesResponse response, int limit)
    {
        if (response.Quote is not { } quote || limit <= 0)
            return [];

        var venue = quote.Venue ?? string.Empty;
        return
        [
            new QuoteEvent
            {
                Symbol = quote.Symbol,
                BidPrice = quote.BidPrice,
                BidSize = quote.BidSize,
                AskPrice = quote.AskPrice,
                AskSize = quote.AskSize,
                Timestamp = quote.Timestamp.UtcDateTime,
                BidExchange = venue,
                AskExchange = venue,
                Spread = quote.Spread ?? 0m,
                MidPrice = quote.MidPrice ?? 0m
            }
        ];
    }

    internal static OrderBookSnapshot MapOrderBook(OrderBookResponse response)
    {
        var venue = response.Venue ?? string.Empty;
        List<OrderBookLevel> MapLevels(IReadOnlyList<Meridian.Contracts.Api.OrderBookLevelDto>? levels)
            => (levels ?? [])
                .Select(level => new OrderBookLevel
                {
                    Price = level.Price,
                    Size = level.Size,
                    Exchange = level.MarketMaker ?? venue
                })
                .ToList();

        var bids = MapLevels(response.Bids);
        var asks = MapLevels(response.Asks);
        // The envelope carries levels, mid and imbalance; the top-of-book and volume totals the
        // snapshot exposes are derived from the levels (bids best-first, asks best-first).
        var bestBid = bids.Count > 0 ? bids.Max(level => level.Price) : 0m;
        var bestAsk = asks.Count > 0 ? asks.Min(level => level.Price) : 0m;
        var hasBothSides = bids.Count > 0 && asks.Count > 0;
        return new OrderBookSnapshot
        {
            Symbol = response.Symbol,
            Timestamp = response.Timestamp.UtcDateTime,
            Bids = bids,
            Asks = asks,
            BestBid = bestBid,
            BestAsk = bestAsk,
            Spread = hasBothSides ? bestAsk - bestBid : 0m,
            MidPrice = response.MidPrice ?? (hasBothSides ? (bestBid + bestAsk) / 2m : 0m),
            TotalBidVolume = bids.Sum(level => level.Size),
            TotalAskVolume = asks.Sum(level => level.Size),
            Imbalance = response.Imbalance ?? 0m
        };
    }

    /// <summary>
    /// Gets current BBO (Best Bid/Offer) for a symbol.
    /// </summary>
    public async Task<BboQuote?> GetBboAsync(string symbol, CancellationToken ct = default)
    {
        return (await ApiClientService.Instance.GetWithResponseAsync<BboQuote>($"/api/data/bbo/{symbol}", ct).ConfigureAwait(false)).DataOrLoggedNull("Get BBO quote");
    }

    /// <summary>
    /// Gets order flow statistics for a symbol.
    /// </summary>
    public async Task<OrderFlowStats?> GetOrderFlowStatsAsync(string symbol, CancellationToken ct = default)
    {
        return (await ApiClientService.Instance.GetWithResponseAsync<OrderFlowStats>($"/api/data/orderflow/{symbol}", ct).ConfigureAwait(false)).DataOrLoggedNull("Get order flow statistics");
    }

    /// <summary>
    /// Gets active subscriptions.
    /// </summary>
    public async Task<List<SubscriptionInfo>?> GetActiveSubscriptionsAsync(CancellationToken ct = default)
    {
        return (await ApiClientService.Instance.GetWithResponseAsync<List<SubscriptionInfo>>("/api/subscriptions/active", ct).ConfigureAwait(false)).DataOrLoggedNull("Get active subscriptions");
    }

    /// <summary>
    /// Subscribes to a symbol for real-time data.
    /// </summary>
    public async Task<SubscriptionResult?> SubscribeAsync(SubscribeRequest request, CancellationToken ct = default)
    {
        return (await ApiClientService.Instance.PostWithResponseAsync<SubscriptionResult>("/api/subscriptions/subscribe", request, ct).ConfigureAwait(false)).DataOrLoggedNull("Subscribe to symbol");
    }

    /// <summary>
    /// Unsubscribes from a symbol.
    /// </summary>
    public async Task<bool> UnsubscribeAsync(string symbol, CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<UnsubscribeResponse>($"/api/subscriptions/unsubscribe/{symbol}", null, ct).ConfigureAwait(false);
        return response.Success;
    }

    /// <summary>
    /// Gets data stream health for all active subscriptions.
    /// </summary>
    public async Task<DataStreamHealth?> GetStreamHealthAsync(CancellationToken ct = default)
    {
        return (await ApiClientService.Instance.GetWithResponseAsync<DataStreamHealth>("/api/data/health", ct).ConfigureAwait(false)).DataOrLoggedNull("Get data stream health");
    }
}

// DTOs for live data

/// <summary>
/// Carries a live trade's symbol, price, size, venue, sequence, and reported side.
/// </summary>
public sealed class TradeEvent
{
    public string Symbol { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal Size { get; set; }
    public DateTime Timestamp { get; set; }
    public string Exchange { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public long SequenceNumber { get; set; }
    public string Side { get; set; } = string.Empty; // Buy/Sell/Unknown
}

/// <summary>
/// Carries a live symbol quote with bid and ask sizes, venues, spread, and midpoint.
/// </summary>
public sealed class QuoteEvent
{
    public string Symbol { get; set; } = string.Empty;
    public decimal BidPrice { get; set; }
    public decimal BidSize { get; set; }
    public decimal AskPrice { get; set; }
    public decimal AskSize { get; set; }
    public DateTime Timestamp { get; set; }
    public string BidExchange { get; set; } = string.Empty;
    public string AskExchange { get; set; } = string.Empty;
    public decimal Spread { get; set; }
    public decimal MidPrice { get; set; }
}

/// <summary>
/// Groups bid and ask depth levels with top-of-book prices, volume totals, and imbalance.
/// </summary>
public sealed class OrderBookSnapshot
{
    public string Symbol { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public List<OrderBookLevel> Bids { get; set; } = new();
    public List<OrderBookLevel> Asks { get; set; } = new();
    public decimal BestBid { get; set; }
    public decimal BestAsk { get; set; }
    public decimal Spread { get; set; }
    public decimal MidPrice { get; set; }
    public decimal TotalBidVolume { get; set; }
    public decimal TotalAskVolume { get; set; }
    public decimal Imbalance { get; set; }
}

/// <summary>
/// Describes aggregated size, order count, and venue at one order-book price level.
/// </summary>
public sealed class OrderBookLevel
{
    public decimal Price { get; set; }
    public decimal Size { get; set; }
    public int OrderCount { get; set; }
    public string Exchange { get; set; } = string.Empty;
}

/// <summary>
/// Describes a symbol's best bid and offer with spread, midpoint, and observation time.
/// </summary>
public sealed class BboQuote
{
    public string Symbol { get; set; } = string.Empty;
    public decimal BidPrice { get; set; }
    public decimal BidSize { get; set; }
    public decimal AskPrice { get; set; }
    public decimal AskSize { get; set; }
    public decimal Spread { get; set; }
    public decimal SpreadBps { get; set; }
    public decimal MidPrice { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Summarizes a symbol's trade flow, volume imbalance, and VWAP over a period.
/// </summary>
public sealed class OrderFlowStats
{
    public string Symbol { get; set; } = string.Empty;
    public decimal Vwap { get; set; }
    public decimal BuyVolume { get; set; }
    public decimal SellVolume { get; set; }
    public decimal TotalVolume { get; set; }
    public decimal Imbalance { get; set; }
    public int TradeCount { get; set; }
    public decimal AvgTradeSize { get; set; }
    public decimal LargestTrade { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
}

/// <summary>
/// Describes a live symbol subscription's identity, type, activity, and received-event rate.
/// </summary>
public sealed class SubscriptionInfo
{
    public string Symbol { get; set; } = string.Empty;
    public int SubscriptionId { get; set; }
    public string SubscriptionType { get; set; } = string.Empty; // Trades, Depth, Quotes
    public bool IsActive { get; set; }
    public DateTime SubscribedAt { get; set; }
    public int EventCount { get; set; }
    public DateTime? LastEventAt { get; set; }
    public double EventsPerSecond { get; set; }
}

/// <summary>
/// Selects live trade, quote, and depth subscriptions and the requested depth levels.
/// </summary>
public sealed class SubscribeRequest
{
    public string Symbol { get; set; } = string.Empty;
    public bool SubscribeTrades { get; set; }
    public bool SubscribeDepth { get; set; }
    public bool SubscribeQuotes { get; set; }
    public int DepthLevels { get; set; } = 10;
}

/// <summary>
/// Reports subscription creation success, assigned identity, and an explanatory message.
/// </summary>
public sealed class SubscriptionResult
{
    public bool Success { get; set; }
    public int SubscriptionId { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Carries the API success flag for removing a live subscription.
/// </summary>
public sealed class UnsubscribeResponse
{
    public bool Success { get; set; }
}

/// <summary>
/// Summarizes live-stream health, stream counts, overall latency, and individual stream diagnostics.
/// </summary>
public sealed class DataStreamHealth
{
    public bool IsHealthy { get; set; }
    public int ActiveStreams { get; set; }
    public int HealthyStreams { get; set; }
    public int UnhealthyStreams { get; set; }
    public double OverallLatencyMs { get; set; }
    public List<StreamHealthInfo> Streams { get; set; } = new();
}

/// <summary>
/// Describes a symbol stream's health, latency, event rate, latest event, and issue.
/// </summary>
public sealed class StreamHealthInfo
{
    public string Symbol { get; set; } = string.Empty;
    public string StreamType { get; set; } = string.Empty;
    public bool IsHealthy { get; set; }
    public double LatencyMs { get; set; }
    public DateTime LastEventAt { get; set; }
    public double EventsPerSecond { get; set; }
    public string? Issue { get; set; }
}

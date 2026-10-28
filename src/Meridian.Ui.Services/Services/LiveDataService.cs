using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
        return (await ApiClientService.Instance.GetWithResponseAsync<List<TradeEvent>>($"/api/data/trades/{symbol}?limit={limit}", ct).ConfigureAwait(false)).DataOrLoggedNull("Get recent trades");
    }

    /// <summary>
    /// Gets recent quotes for a symbol.
    /// </summary>
    public async Task<List<QuoteEvent>?> GetRecentQuotesAsync(string symbol, int limit = 100, CancellationToken ct = default)
    {
        return (await ApiClientService.Instance.GetWithResponseAsync<List<QuoteEvent>>($"/api/data/quotes/{symbol}?limit={limit}", ct).ConfigureAwait(false)).DataOrLoggedNull("Get recent quotes");
    }

    /// <summary>
    /// Gets the current order book snapshot for a symbol.
    /// </summary>
    public async Task<OrderBookSnapshot?> GetOrderBookAsync(string symbol, int levels = 10, CancellationToken ct = default)
    {
        return (await ApiClientService.Instance.GetWithResponseAsync<OrderBookSnapshot>($"/api/data/orderbook/{symbol}?levels={levels}", ct).ConfigureAwait(false)).DataOrLoggedNull("Get order book snapshot");
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

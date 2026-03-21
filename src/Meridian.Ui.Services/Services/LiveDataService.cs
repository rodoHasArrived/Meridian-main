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
        return await ApiClientService.Instance.GetAsync<List<TradeEvent>>($"/api/data/trades/{symbol}?limit={limit}", ct);
    }

    /// <summary>
    /// Gets recent quotes for a symbol.
    /// </summary>
    public async Task<List<QuoteEvent>?> GetRecentQuotesAsync(string symbol, int limit = 100, CancellationToken ct = default)
    {
        return await ApiClientService.Instance.GetAsync<List<QuoteEvent>>($"/api/data/quotes/{symbol}?limit={limit}", ct);
    }

    /// <summary>
    /// Gets the current order book snapshot for a symbol.
    /// </summary>
    public async Task<OrderBookSnapshot?> GetOrderBookAsync(string symbol, int levels = 10, CancellationToken ct = default)
    {
        return await ApiClientService.Instance.GetAsync<OrderBookSnapshot>($"/api/data/orderbook/{symbol}?levels={levels}", ct);
    }

    /// <summary>
    /// Gets current BBO (Best Bid/Offer) for a symbol.
    /// </summary>
    public async Task<BboQuote?> GetBboAsync(string symbol, CancellationToken ct = default)
    {
        return await ApiClientService.Instance.GetAsync<BboQuote>($"/api/data/bbo/{symbol}", ct);
    }

    /// <summary>
    /// Gets order flow statistics for a symbol.
    /// </summary>
    public async Task<OrderFlowStats?> GetOrderFlowStatsAsync(string symbol, CancellationToken ct = default)
    {
        return await ApiClientService.Instance.GetAsync<OrderFlowStats>($"/api/data/orderflow/{symbol}", ct);
    }

    /// <summary>
    /// Gets active subscriptions.
    /// </summary>
    public async Task<List<SubscriptionInfo>?> GetActiveSubscriptionsAsync(CancellationToken ct = default)
    {
        return await ApiClientService.Instance.GetAsync<List<SubscriptionInfo>>("/api/subscriptions/active", ct);
    }

    /// <summary>
    /// Subscribes to a symbol for real-time data.
    /// </summary>
    public async Task<SubscriptionResult?> SubscribeAsync(SubscribeRequest request, CancellationToken ct = default)
    {
        return await ApiClientService.Instance.PostAsync<SubscriptionResult>("/api/subscriptions/subscribe", request, ct);
    }

    /// <summary>
    /// Unsubscribes from a symbol.
    /// </summary>
    public async Task<bool> UnsubscribeAsync(string symbol, CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<UnsubscribeResponse>($"/api/subscriptions/unsubscribe/{symbol}", null, ct);
        return response.Success;
    }

    /// <summary>
    /// Gets data stream health for all active subscriptions.
    /// </summary>
    public async Task<DataStreamHealth?> GetStreamHealthAsync(CancellationToken ct = default)
    {
        return await ApiClientService.Instance.GetAsync<DataStreamHealth>("/api/data/health", ct);
    }
}

// DTOs for live data

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

public sealed class OrderBookLevel
{
    public decimal Price { get; set; }
    public decimal Size { get; set; }
    public int OrderCount { get; set; }
    public string Exchange { get; set; } = string.Empty;
}

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

public sealed class SubscribeRequest
{
    public string Symbol { get; set; } = string.Empty;
    public bool SubscribeTrades { get; set; }
    public bool SubscribeDepth { get; set; }
    public bool SubscribeQuotes { get; set; }
    public int DepthLevels { get; set; } = 10;
}

public sealed class SubscriptionResult
{
    public bool Success { get; set; }
    public int SubscriptionId { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class UnsubscribeResponse
{
    public bool Success { get; set; }
}

public sealed class DataStreamHealth
{
    public bool IsHealthy { get; set; }
    public int ActiveStreams { get; set; }
    public int HealthyStreams { get; set; }
    public int UnhealthyStreams { get; set; }
    public double OverallLatencyMs { get; set; }
    public List<StreamHealthInfo> Streams { get; set; } = new();
}

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

using System.Collections.Concurrent;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for live order book visualization.
/// Provides real-time L2 depth data processing and visualization helpers.
/// </summary>
public sealed class OrderBookVisualizationService : IDisposable
{
    private readonly LiveDataService _liveDataService;
    private readonly ConcurrentDictionary<string, OrderBookState> _orderBooks = new();
    private readonly ConcurrentDictionary<string, List<OrderBookHistorySnapshot>> _snapshotHistory = new();
    private readonly Timer _aggregationTimer;
    private readonly int _maxHistorySize = 1000;

    public event EventHandler<OrderBookUpdateEventArgs>? OrderBookUpdated;
    public event EventHandler<TradeEventArgs>? TradeReceived;

    public OrderBookVisualizationService()
    {
        _liveDataService = LiveDataService.Instance;
        _aggregationTimer = new Timer(AggregateSnapshots, null, 1000, 1000);
    }

    /// <summary>
    /// Subscribes to order book updates for a symbol.
    /// </summary>
    public async Task SubscribeAsync(string symbol, int depthLevels = 10, CancellationToken ct = default)
    {
        if (!_orderBooks.ContainsKey(symbol))
        {
            _orderBooks[symbol] = new OrderBookState { Symbol = symbol, DepthLevels = depthLevels };
            _snapshotHistory[symbol] = new List<OrderBookHistorySnapshot>();
        }

        var request = new SubscribeRequest
        {
            Symbol = symbol,
            SubscribeDepth = true,
            DepthLevels = depthLevels
        };
        await _liveDataService.SubscribeAsync(request, ct);
    }

    /// <summary>
    /// Unsubscribes from order book updates.
    /// </summary>
    public async Task UnsubscribeAsync(string symbol, CancellationToken ct = default)
    {
        _orderBooks.TryRemove(symbol, out _);
        _snapshotHistory.TryRemove(symbol, out _);
        await _liveDataService.UnsubscribeAsync(symbol, ct);
    }

    /// <summary>
    /// Gets the current order book state for a symbol.
    /// </summary>
    public OrderBookState? GetOrderBook(string symbol)
    {
        return _orderBooks.TryGetValue(symbol, out var state) ? state : null;
    }

    /// <summary>
    /// Gets order book visualization data optimized for heatmap rendering.
    /// </summary>
    public OrderBookHeatmapData GetHeatmapData(string symbol, int priceLevels = 20)
    {
        var heatmap = new OrderBookHeatmapData { Symbol = symbol };

        if (!_orderBooks.TryGetValue(symbol, out var state))
            return heatmap;

        // Calculate price range
        var allPrices = state.Bids.Keys.Concat(state.Asks.Keys).ToList();
        if (allPrices.Count == 0)
            return heatmap;

        var midPrice = state.MidPrice;
        var tickSize = CalculateTickSize(midPrice);
        var halfRange = priceLevels / 2 * tickSize;

        // Generate price levels
        for (int i = priceLevels / 2; i >= -priceLevels / 2; i--)
        {
            var price = Math.Round(midPrice + i * tickSize, GetDecimalPlaces(tickSize));
            var level = new HeatmapLevel { Price = price };

            // Get bid/ask sizes at this level
            if (state.Bids.TryGetValue(price, out var bidSize))
            {
                level.BidSize = bidSize;
                level.BidIntensity = CalculateIntensity(bidSize, state.MaxBidSize);
            }

            if (state.Asks.TryGetValue(price, out var askSize))
            {
                level.AskSize = askSize;
                level.AskIntensity = CalculateIntensity(askSize, state.MaxAskSize);
            }

            level.IsMidPrice = Math.Abs(price - midPrice) < tickSize / 2;
            heatmap.Levels.Add(level);
        }

        heatmap.BestBid = state.BestBid;
        heatmap.BestAsk = state.BestAsk;
        heatmap.Spread = state.Spread;
        heatmap.SpreadBps = state.SpreadBps;
        heatmap.Imbalance = state.Imbalance;
        heatmap.LastUpdateTime = state.LastUpdateTime;

        return heatmap;
    }

    /// <summary>
    /// Gets depth chart data for bid/ask curve visualization.
    /// </summary>
    public DepthChartData GetDepthChartData(string symbol, int maxLevels = 50)
    {
        var chart = new DepthChartData { Symbol = symbol };

        if (!_orderBooks.TryGetValue(symbol, out var state))
            return chart;

        // Cumulative bid depth (sorted descending by price)
        var sortedBids = state.Bids.OrderByDescending(x => x.Key).Take(maxLevels).ToList();
        decimal cumulativeBid = 0;
        foreach (var (price, size) in sortedBids)
        {
            cumulativeBid += size;
            chart.BidPoints.Add(new DepthPoint { Price = price, CumulativeSize = cumulativeBid, Size = size });
        }

        // Cumulative ask depth (sorted ascending by price)
        var sortedAsks = state.Asks.OrderBy(x => x.Key).Take(maxLevels).ToList();
        decimal cumulativeAsk = 0;
        foreach (var (price, size) in sortedAsks)
        {
            cumulativeAsk += size;
            chart.AskPoints.Add(new DepthPoint { Price = price, CumulativeSize = cumulativeAsk, Size = size });
        }

        chart.TotalBidDepth = cumulativeBid;
        chart.TotalAskDepth = cumulativeAsk;
        chart.DepthImbalance = cumulativeBid > 0 || cumulativeAsk > 0
            ? (cumulativeBid - cumulativeAsk) / (cumulativeBid + cumulativeAsk)
            : 0;

        return chart;
    }

    /// <summary>
    /// Gets time and sales data.
    /// </summary>
    public TimeAndSalesData GetTimeAndSales(string symbol, int maxTrades = 100)
    {
        var tas = new TimeAndSalesData { Symbol = symbol };

        if (!_orderBooks.TryGetValue(symbol, out var state))
            return tas;

        tas.RecentTrades = state.RecentTrades.TakeLast(maxTrades).Reverse().ToList();
        tas.TotalVolume = state.TotalVolume;
        tas.BuyVolume = state.BuyVolume;
        tas.SellVolume = state.SellVolume;
        tas.VolumeImbalance = tas.TotalVolume > 0
            ? (tas.BuyVolume - tas.SellVolume) / tas.TotalVolume
            : 0;

        return tas;
    }

    /// <summary>
    /// Gets order flow statistics.
    /// </summary>
    public OrderBookFlowStats GetOrderFlowStats(string symbol)
    {
        var stats = new OrderBookFlowStats { Symbol = symbol };

        if (!_orderBooks.TryGetValue(symbol, out var state))
            return stats;

        stats.BidAskRatio = state.MaxAskSize > 0
            ? state.MaxBidSize / state.MaxAskSize
            : 0;

        stats.OrderImbalance = state.Imbalance;
        stats.SpreadBps = state.SpreadBps;
        stats.MidPrice = state.MidPrice;

        // Calculate VWAP from recent trades
        if (state.RecentTrades.Count > 0)
        {
            var totalValue = state.RecentTrades.Sum(t => t.Price * t.Size);
            var totalVolume = state.RecentTrades.Sum(t => t.Size);
            stats.Vwap = totalVolume > 0 ? totalValue / totalVolume : 0;
        }

        // Calculate delta (buy volume - sell volume)
        stats.Delta = state.BuyVolume - state.SellVolume;
        stats.CumulativeDelta = state.CumulativeDelta;

        return stats;
    }

    /// <summary>
    /// Updates the order book with new bid data.
    /// </summary>
    public void UpdateBids(string symbol, Dictionary<decimal, decimal> bids)
    {
        if (!_orderBooks.TryGetValue(symbol, out var state))
            return;

        foreach (var (price, size) in bids)
        {
            if (size > 0)
                state.Bids[price] = size;
            else
                state.Bids.TryRemove(price, out _);
        }

        state.MaxBidSize = state.Bids.Values.DefaultIfEmpty(0).Max();
        state.BestBid = state.Bids.Keys.DefaultIfEmpty(0).Max();
        state.LastUpdateTime = DateTime.UtcNow;
        UpdateDerivedMetrics(state);

        OnOrderBookUpdated(symbol);
    }

    /// <summary>
    /// Updates the order book with new ask data.
    /// </summary>
    public void UpdateAsks(string symbol, Dictionary<decimal, decimal> asks)
    {
        if (!_orderBooks.TryGetValue(symbol, out var state))
            return;

        foreach (var (price, size) in asks)
        {
            if (size > 0)
                state.Asks[price] = size;
            else
                state.Asks.TryRemove(price, out _);
        }

        state.MaxAskSize = state.Asks.Values.DefaultIfEmpty(0).Max();
        state.BestAsk = state.Asks.Keys.DefaultIfEmpty(decimal.MaxValue).Min();
        state.LastUpdateTime = DateTime.UtcNow;
        UpdateDerivedMetrics(state);

        OnOrderBookUpdated(symbol);
    }

    /// <summary>
    /// Records a trade for time and sales.
    /// </summary>
    public void RecordTrade(string symbol, decimal price, decimal size, TradeSide side)
    {
        if (!_orderBooks.TryGetValue(symbol, out var state))
            return;

        var trade = new TradeRecord
        {
            Timestamp = DateTime.UtcNow,
            Price = price,
            Size = size,
            Side = side
        };

        state.RecentTrades.Add(trade);
        if (state.RecentTrades.Count > 1000)
            state.RecentTrades.RemoveRange(0, state.RecentTrades.Count - 1000);

        state.TotalVolume += size;
        if (side == TradeSide.Buy)
            state.BuyVolume += size;
        else
            state.SellVolume += size;

        state.CumulativeDelta = state.BuyVolume - state.SellVolume;
        state.LastTradePrice = price;
        state.LastTradeTime = trade.Timestamp;

        OnTradeReceived(symbol, trade);
    }

    private void UpdateDerivedMetrics(OrderBookState state)
    {
        if (state.BestBid > 0 && state.BestAsk < decimal.MaxValue)
        {
            state.Spread = state.BestAsk - state.BestBid;
            state.MidPrice = (state.BestBid + state.BestAsk) / 2;
            state.SpreadBps = state.MidPrice > 0
                ? state.Spread / state.MidPrice * 10000
                : 0;
        }

        var totalBidSize = state.Bids.Values.Sum();
        var totalAskSize = state.Asks.Values.Sum();
        state.Imbalance = totalBidSize + totalAskSize > 0
            ? (totalBidSize - totalAskSize) / (totalBidSize + totalAskSize)
            : 0;
    }

    private void AggregateSnapshots(object? state)
    {
        foreach (var (symbol, orderBook) in _orderBooks)
        {
            if (!_snapshotHistory.TryGetValue(symbol, out var history))
                continue;

            var snapshot = new OrderBookHistorySnapshot
            {
                Timestamp = DateTime.UtcNow,
                BestBid = orderBook.BestBid,
                BestAsk = orderBook.BestAsk,
                MidPrice = orderBook.MidPrice,
                Spread = orderBook.Spread,
                Imbalance = orderBook.Imbalance,
                TotalBidSize = orderBook.Bids.Values.Sum(),
                TotalAskSize = orderBook.Asks.Values.Sum()
            };

            history.Add(snapshot);
            if (history.Count > _maxHistorySize)
                history.RemoveRange(0, history.Count - _maxHistorySize);
        }
    }

    private static decimal CalculateTickSize(decimal price)
    {
        if (price >= 1000) return 0.1m;
        if (price >= 100) return 0.05m;
        if (price >= 10) return 0.01m;
        return 0.001m;
    }

    private static int GetDecimalPlaces(decimal tickSize)
    {
        var str = tickSize.ToString().TrimEnd('0');
        var decimalIndex = str.IndexOf('.');
        return decimalIndex < 0 ? 0 : str.Length - decimalIndex - 1;
    }

    private static double CalculateIntensity(decimal size, decimal maxSize)
    {
        if (maxSize <= 0) return 0;
        return Math.Min(1.0, (double)(size / maxSize));
    }

    private void OnOrderBookUpdated(string symbol)
    {
        OrderBookUpdated?.Invoke(this, new OrderBookUpdateEventArgs { Symbol = symbol });
    }

    private void OnTradeReceived(string symbol, TradeRecord trade)
    {
        TradeReceived?.Invoke(this, new TradeEventArgs { Symbol = symbol, Trade = trade });
    }

    public void Dispose()
    {
        _aggregationTimer.Dispose();
    }
}

/// <summary>
/// Current state of an order book.
/// </summary>
public sealed class OrderBookState
{
    public string Symbol { get; set; } = string.Empty;
    public int DepthLevels { get; set; }
    public ConcurrentDictionary<decimal, decimal> Bids { get; } = new();
    public ConcurrentDictionary<decimal, decimal> Asks { get; } = new();
    public decimal BestBid { get; set; }
    public decimal BestAsk { get; set; }
    public decimal MidPrice { get; set; }
    public decimal Spread { get; set; }
    public decimal SpreadBps { get; set; }
    public decimal Imbalance { get; set; }
    public decimal MaxBidSize { get; set; }
    public decimal MaxAskSize { get; set; }
    public DateTime LastUpdateTime { get; set; }
    public List<TradeRecord> RecentTrades { get; } = new();
    public decimal TotalVolume { get; set; }
    public decimal BuyVolume { get; set; }
    public decimal SellVolume { get; set; }
    public decimal CumulativeDelta { get; set; }
    public decimal LastTradePrice { get; set; }
    public DateTime LastTradeTime { get; set; }
}

/// <summary>
/// Order book snapshot for history.
/// </summary>
public sealed class OrderBookHistorySnapshot
{
    public DateTime Timestamp { get; set; }
    public decimal BestBid { get; set; }
    public decimal BestAsk { get; set; }
    public decimal MidPrice { get; set; }
    public decimal Spread { get; set; }
    public decimal Imbalance { get; set; }
    public decimal TotalBidSize { get; set; }
    public decimal TotalAskSize { get; set; }
}

/// <summary>
/// Data for heatmap visualization.
/// </summary>
public sealed class OrderBookHeatmapData
{
    public string Symbol { get; set; } = string.Empty;
    public List<HeatmapLevel> Levels { get; } = new();
    public decimal BestBid { get; set; }
    public decimal BestAsk { get; set; }
    public decimal Spread { get; set; }
    public decimal SpreadBps { get; set; }
    public decimal Imbalance { get; set; }
    public DateTime LastUpdateTime { get; set; }
}

/// <summary>
/// Single level in the heatmap.
/// </summary>
public sealed class HeatmapLevel
{
    public decimal Price { get; set; }
    public decimal BidSize { get; set; }
    public decimal AskSize { get; set; }
    public double BidIntensity { get; set; }
    public double AskIntensity { get; set; }
    public bool IsMidPrice { get; set; }
}

/// <summary>
/// Data for depth chart visualization.
/// </summary>
public sealed class DepthChartData
{
    public string Symbol { get; set; } = string.Empty;
    public List<DepthPoint> BidPoints { get; } = new();
    public List<DepthPoint> AskPoints { get; } = new();
    public decimal TotalBidDepth { get; set; }
    public decimal TotalAskDepth { get; set; }
    public decimal DepthImbalance { get; set; }
}

/// <summary>
/// Point on the depth chart.
/// </summary>
public sealed class DepthPoint
{
    public decimal Price { get; set; }
    public decimal Size { get; set; }
    public decimal CumulativeSize { get; set; }
}

/// <summary>
/// Time and sales data.
/// </summary>
public sealed class TimeAndSalesData
{
    public string Symbol { get; set; } = string.Empty;
    public List<TradeRecord> RecentTrades { get; set; } = new();
    public decimal TotalVolume { get; set; }
    public decimal BuyVolume { get; set; }
    public decimal SellVolume { get; set; }
    public decimal VolumeImbalance { get; set; }
}

/// <summary>
/// Individual trade record.
/// </summary>
public sealed class TradeRecord
{
    public DateTime Timestamp { get; set; }
    public decimal Price { get; set; }
    public decimal Size { get; set; }
    public TradeSide Side { get; set; }
}

/// <summary>
/// Trade side indicator.
/// </summary>
public enum TradeSide : byte
{
    Unknown,
    Buy,
    Sell
}

/// <summary>
/// Order flow statistics for order book visualization.
/// </summary>
public sealed class OrderBookFlowStats
{
    public string Symbol { get; set; } = string.Empty;
    public decimal BidAskRatio { get; set; }
    public decimal OrderImbalance { get; set; }
    public decimal SpreadBps { get; set; }
    public decimal MidPrice { get; set; }
    public decimal Vwap { get; set; }
    public decimal Delta { get; set; }
    public decimal CumulativeDelta { get; set; }
}

/// <summary>
/// Event args for order book updates.
/// </summary>
public sealed class OrderBookUpdateEventArgs : EventArgs
{
    public string Symbol { get; set; } = string.Empty;
}

/// <summary>
/// Event args for trade events.
/// </summary>
public sealed class TradeEventArgs : EventArgs
{
    public string Symbol { get; set; } = string.Empty;
    public TradeRecord Trade { get; set; } = new();
}

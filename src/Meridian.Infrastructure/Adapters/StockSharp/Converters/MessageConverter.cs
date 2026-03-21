#if STOCKSHARP
using StockSharp.Messages;
#endif
using System.Buffers;
using Meridian.Contracts.Domain.Enums;
using Meridian.Domain.Models;
using Microsoft.Extensions.ObjectPool;

namespace Meridian.Infrastructure.Adapters.StockSharp.Converters;

/// <summary>
/// Converts StockSharp messages to Meridian domain models.
/// This converter bridges S# message types to Meridian's immutable domain records.
/// </summary>
public static class MessageConverter
{
    // Object pool for reducing GC pressure in hot message paths
    private static readonly ObjectPool<List<OrderBookLevel>> s_levelListPool =
        new DefaultObjectPoolProvider().Create(new ListPoolPolicy<OrderBookLevel>());

    private sealed class ListPoolPolicy<T> : PooledObjectPolicy<List<T>>
    {
        public override List<T> Create() => new(32);
        public override bool Return(List<T> obj)
        {
            obj.Clear();
            return true;
        }
    }

#if STOCKSHARP
    /// <summary>
    /// Convert StockSharp ExecutionMessage (trade tick) to Meridian Trade.
    /// </summary>
    /// <param name="msg">StockSharp execution message containing trade data.</param>
    /// <param name="symbol">Symbol identifier for the security.</param>
    /// <returns>Immutable Trade domain model.</returns>
    public static Trade ToTrade(ExecutionMessage msg, string symbol)
    {
        return new Trade(
            Timestamp: msg.ServerTime,
            Symbol: symbol,
            Price: msg.TradePrice ?? 0m,
            Size: (long)(msg.TradeVolume ?? 0m),
            Aggressor: ConvertSide(msg.OriginSide),
            SequenceNumber: msg.SeqNum ?? 0,
            StreamId: msg.TradeId?.ToString(),
            Venue: msg.SecurityId.BoardCode
        );
    }

    /// <summary>
    /// Convert StockSharp QuoteChangeMessage to Meridian LOBSnapshot.
    /// </summary>
    /// <param name="msg">StockSharp quote change message with bid/ask arrays.</param>
    /// <param name="symbol">Symbol identifier for the security.</param>
    /// <returns>Immutable LOBSnapshot domain model.</returns>
    public static LOBSnapshot ToLOBSnapshot(QuoteChangeMessage msg, string symbol)
    {
        // Use pooled lists to reduce GC pressure in hot path
        var bids = s_levelListPool.Get();
        var asks = s_levelListPool.Get();

        try
        {
            if (msg.Bids != null)
            {
                ushort i = 0;
                foreach (var q in msg.Bids)
                {
                    bids.Add(new OrderBookLevel(
                        Side: OrderBookSide.Bid,
                        Level: i++,
                        Price: q.Price,
                        Size: q.Volume));
                }
            }

            if (msg.Asks != null)
            {
                ushort i = 0;
                foreach (var q in msg.Asks)
                {
                    asks.Add(new OrderBookLevel(
                        Side: OrderBookSide.Ask,
                        Level: i++,
                        Price: q.Price,
                        Size: q.Volume));
                }
            }

            var bestBid = bids.Count > 0 ? bids[0].Price : 0;
            var bestAsk = asks.Count > 0 ? asks[0].Price : 0;
            var midPrice = bestBid > 0 && bestAsk > 0 ? (bestBid + bestAsk) / 2 : (decimal?)null;

            // Copy to immutable lists for the snapshot, then return pooled lists
            var snapshot = new LOBSnapshot(
                Timestamp: msg.ServerTime,
                Symbol: symbol,
                Bids: new List<OrderBookLevel>(bids),
                Asks: new List<OrderBookLevel>(asks),
                MidPrice: midPrice,
                SequenceNumber: msg.SeqNum ?? 0,
                Venue: msg.SecurityId.BoardCode
            );

            return snapshot;
        }
        finally
        {
            s_levelListPool.Return(bids);
            s_levelListPool.Return(asks);
        }
    }

    /// <summary>
    /// Convert StockSharp Level1ChangeMessage to Meridian BboQuotePayload.
    /// </summary>
    /// <param name="msg">StockSharp Level1 message with best bid/offer data.</param>
    /// <param name="symbol">Symbol identifier for the security.</param>
    /// <returns>Immutable BboQuotePayload domain model.</returns>
    public static BboQuotePayload ToBboQuote(Level1ChangeMessage msg, string symbol)
    {
        var bidPrice = GetDecimal(msg, Level1Fields.BestBidPrice);
        var askPrice = GetDecimal(msg, Level1Fields.BestAskPrice);
        var bidSize = (long)GetDecimal(msg, Level1Fields.BestBidVolume);
        var askSize = (long)GetDecimal(msg, Level1Fields.BestAskVolume);

        decimal? midPrice = null;
        decimal? spread = null;

        if (bidPrice > 0 && askPrice > 0 && askPrice >= bidPrice)
        {
            spread = askPrice - bidPrice;
            midPrice = bidPrice + (spread.Value / 2m);
        }

        return new BboQuotePayload(
            Timestamp: msg.ServerTime,
            Symbol: symbol,
            BidPrice: bidPrice,
            BidSize: bidSize,
            AskPrice: askPrice,
            AskSize: askSize,
            MidPrice: midPrice,
            Spread: spread,
            SequenceNumber: msg.SeqNum ?? 0,
            Venue: msg.SecurityId.BoardCode
        );
    }

    /// <summary>
    /// Convert StockSharp TimeFrameCandleMessage to Meridian HistoricalBar.
    /// </summary>
    /// <param name="msg">StockSharp candle message with OHLCV data.</param>
    /// <param name="symbol">Symbol identifier for the security.</param>
    /// <returns>Immutable HistoricalBar domain model.</returns>
    public static HistoricalBar ToHistoricalBar(TimeFrameCandleMessage msg, string symbol)
    {
        return new HistoricalBar(
            Symbol: symbol,
            SessionDate: DateOnly.FromDateTime(msg.OpenTime.Date),
            Open: msg.OpenPrice,
            High: msg.HighPrice,
            Low: msg.LowPrice,
            Close: msg.ClosePrice,
            Volume: (long)msg.TotalVolume,
            Source: "stocksharp",
            SequenceNumber: 0
        );
    }

    /// <summary>
    /// Convert S# Sides enum to Meridian AggressorSide enum.
    /// </summary>
    private static AggressorSide ConvertSide(Sides? side) => side switch
    {
        Sides.Buy => AggressorSide.Buy,
        Sides.Sell => AggressorSide.Sell,
        _ => AggressorSide.Unknown
    };

    /// <summary>
    /// Extract decimal value from Level1ChangeMessage changes dictionary.
    /// </summary>
    private static decimal GetDecimal(Level1ChangeMessage msg, Level1Fields field)
    {
        if (msg.Changes.TryGetValue(field, out var value) && value is decimal d)
            return d;
        return 0m;
    }
#else
    // Stub implementations when StockSharp is not available
    // These allow the code to compile without the StockSharp packages

    /// <summary>
    /// Centralizes the conditional-compilation failure path for non-StockSharp builds.
    /// </summary>
    private static Exception ThrowPlatformNotSupported(string message) => new NotSupportedException(message);

    /// <summary>
    /// Stub: StockSharp packages not installed.
    /// </summary>
    public static Trade ToTrade(object msg, string symbol)
        => throw ThrowPlatformNotSupported("StockSharp integration requires StockSharp.Algo NuGet package. Install with: dotnet add package StockSharp.Algo");

    /// <summary>
    /// Stub: StockSharp packages not installed.
    /// </summary>
    public static LOBSnapshot ToLOBSnapshot(object msg, string symbol)
        => throw ThrowPlatformNotSupported("StockSharp integration requires StockSharp.Messages NuGet package.");

    /// <summary>
    /// Stub: StockSharp packages not installed.
    /// </summary>
    public static BboQuotePayload ToBboQuote(object msg, string symbol)
        => throw ThrowPlatformNotSupported("StockSharp integration requires StockSharp.Messages NuGet package.");

    /// <summary>
    /// Stub: StockSharp packages not installed.
    /// </summary>
    public static HistoricalBar ToHistoricalBar(object msg, string symbol)
        => throw ThrowPlatformNotSupported("StockSharp integration requires StockSharp.Messages NuGet package.");
#endif
}

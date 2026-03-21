using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;

namespace Meridian.Contracts.Domain.Events;

/// <summary>
/// Top-level container for all market events with timestamp, symbol, type, and payload.
/// </summary>
public sealed record MarketEventDto(
    DateTimeOffset Timestamp,
    string Symbol,
    MarketEventType Type,
    MarketEventPayload Payload,
    long Sequence = 0,
    string Source = "IB",
    byte SchemaVersion = 1,
    MarketEventTier Tier = MarketEventTier.Raw,
    string? CanonicalSymbol = null,
    byte CanonicalizationVersion = 0,
    string? CanonicalVenue = null
)
{
    /// <summary>
    /// Returns the effective symbol for downstream consumers: <see cref="CanonicalSymbol"/>
    /// when available, otherwise the raw <see cref="Symbol"/>.
    /// </summary>
    public string EffectiveSymbol => CanonicalSymbol ?? Symbol;

    /// <summary>
    /// Creates a trade market event.
    /// </summary>
    public static MarketEventDto CreateTrade(DateTimeOffset ts, string symbol, Trade trade, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.Trade, trade, seq == 0 ? trade.SequenceNumber : seq, source);

    /// <summary>
    /// Creates an L2 snapshot market event.
    /// </summary>
    public static MarketEventDto CreateL2Snapshot(DateTimeOffset ts, string symbol, LOBSnapshot snap, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.L2Snapshot, snap, seq == 0 ? snap.SequenceNumber : seq, source);

    /// <summary>
    /// Creates a BBO quote market event.
    /// </summary>
    public static MarketEventDto CreateBboQuote(DateTimeOffset ts, string symbol, BboQuotePayload quote, long seq = 0, string source = "ALPACA")
        => new(ts, symbol, MarketEventType.BboQuote, quote, seq == 0 ? quote.SequenceNumber : seq, source);

    /// <summary>
    /// Creates an L2 snapshot payload market event.
    /// </summary>
    public static MarketEventDto CreateL2SnapshotPayload(DateTimeOffset ts, string symbol, L2SnapshotPayload payload, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.L2Snapshot, payload, seq == 0 ? payload.SequenceNumber : seq, source);

    /// <summary>
    /// Creates an order flow statistics market event.
    /// </summary>
    public static MarketEventDto CreateOrderFlow(DateTimeOffset ts, string symbol, OrderFlowStatistics stats, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.OrderFlow, stats, seq == 0 ? stats.SequenceNumber : seq, source);

    /// <summary>
    /// Creates an integrity event.
    /// </summary>
    public static MarketEventDto CreateIntegrity(DateTimeOffset ts, string symbol, IntegrityEvent integrity, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.Integrity, integrity, seq == 0 ? integrity.SequenceNumber : seq, source);

    /// <summary>
    /// Creates a depth integrity event.
    /// </summary>
    public static MarketEventDto CreateDepthIntegrity(DateTimeOffset ts, string symbol, DepthIntegrityEvent integrity, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.Integrity, integrity, seq == 0 ? integrity.SequenceNumber : seq, source);

    /// <summary>
    /// Creates a heartbeat market event.
    /// </summary>
    public static MarketEventDto CreateHeartbeat(DateTimeOffset ts, string source = "IB")
        => new(ts, "SYSTEM", MarketEventType.Heartbeat, new MarketEventPayload.HeartbeatPayload(), Sequence: 0, Source: source);

    /// <summary>
    /// Creates a historical bar market event.
    /// </summary>
    public static MarketEventDto CreateHistoricalBar(DateTimeOffset ts, string symbol, HistoricalBar bar, long seq = 0, string source = "stooq")
        => new(ts, symbol, MarketEventType.HistoricalBar, bar, seq == 0 ? bar.SequenceNumber : seq, source);

    /// <summary>
    /// Creates a historical quote market event.
    /// </summary>
    public static MarketEventDto CreateHistoricalQuote(DateTimeOffset ts, string symbol, HistoricalQuote quote, long seq = 0, string source = "alpaca")
        => new(ts, symbol, MarketEventType.HistoricalQuote, quote, seq == 0 ? quote.SequenceNumber : seq, source);

    /// <summary>
    /// Creates a historical trade market event.
    /// </summary>
    public static MarketEventDto CreateHistoricalTrade(DateTimeOffset ts, string symbol, HistoricalTrade trade, long seq = 0, string source = "alpaca")
        => new(ts, symbol, MarketEventType.HistoricalTrade, trade, seq == 0 ? trade.SequenceNumber : seq, source);

    /// <summary>
    /// Creates a historical auction market event.
    /// </summary>
    public static MarketEventDto CreateHistoricalAuction(DateTimeOffset ts, string symbol, HistoricalAuction auction, long seq = 0, string source = "alpaca")
        => new(ts, symbol, MarketEventType.HistoricalAuction, auction, seq == 0 ? auction.SequenceNumber : seq, source);

    /// <summary>
    /// Creates an option quote market event.
    /// </summary>
    public static MarketEventDto CreateOptionQuote(DateTimeOffset ts, string symbol, OptionQuote quote, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.OptionQuote, quote, seq == 0 ? quote.SequenceNumber : seq, source);

    /// <summary>
    /// Creates an option trade market event.
    /// </summary>
    public static MarketEventDto CreateOptionTrade(DateTimeOffset ts, string symbol, OptionTrade trade, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.OptionTrade, trade, seq == 0 ? trade.SequenceNumber : seq, source);

    /// <summary>
    /// Creates an option greeks snapshot market event.
    /// </summary>
    public static MarketEventDto CreateOptionGreeks(DateTimeOffset ts, string symbol, GreeksSnapshot greeks, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.OptionGreeks, greeks, seq == 0 ? greeks.SequenceNumber : seq, source);

    /// <summary>
    /// Creates an option chain snapshot market event.
    /// </summary>
    public static MarketEventDto CreateOptionChain(DateTimeOffset ts, string underlyingSymbol, OptionChainSnapshot chain, long seq = 0, string source = "IB")
        => new(ts, underlyingSymbol, MarketEventType.OptionChain, chain, seq == 0 ? chain.SequenceNumber : seq, source);

    /// <summary>
    /// Creates an open interest update market event.
    /// </summary>
    public static MarketEventDto CreateOpenInterest(DateTimeOffset ts, string symbol, OpenInterestUpdate oi, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.OpenInterest, oi, seq == 0 ? oi.SequenceNumber : seq, source);

    /// <summary>
    /// Creates an order-add market event.
    /// </summary>
    public static MarketEventDto CreateOrderAdd(DateTimeOffset ts, string symbol, OrderAdd order, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.OrderAdd, order, seq == 0 ? order.SequenceNumber : seq, source);

    /// <summary>
    /// Creates an order-modify market event.
    /// </summary>
    public static MarketEventDto CreateOrderModify(DateTimeOffset ts, string symbol, OrderModify modify, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.OrderModify, modify, seq == 0 ? modify.SequenceNumber : seq, source);

    /// <summary>
    /// Creates an order-cancel market event.
    /// </summary>
    public static MarketEventDto CreateOrderCancel(DateTimeOffset ts, string symbol, OrderCancel cancel, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.OrderCancel, cancel, seq == 0 ? cancel.SequenceNumber : seq, source);

    /// <summary>
    /// Creates an order-execute market event.
    /// </summary>
    public static MarketEventDto CreateOrderExecute(DateTimeOffset ts, string symbol, OrderExecute execute, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.OrderExecute, execute, seq == 0 ? execute.SequenceNumber : seq, source);

    /// <summary>
    /// Creates an order-replace market event.
    /// </summary>
    public static MarketEventDto CreateOrderReplace(DateTimeOffset ts, string symbol, OrderReplace replace, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.OrderReplace, replace, seq == 0 ? replace.SequenceNumber : seq, source);
}

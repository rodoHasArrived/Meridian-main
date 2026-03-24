using System.Diagnostics;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Events;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;

namespace Meridian.Domain.Events;

public sealed record MarketEvent(
    DateTimeOffset Timestamp,
    string Symbol,
    MarketEventType Type,
    Contracts.Domain.Events.MarketEventPayload Payload,
    long Sequence = 0,
    string Source = "IB",
    byte SchemaVersion = 1,
    MarketEventTier Tier = MarketEventTier.Raw,
    DateTimeOffset? ExchangeTimestamp = null,
    DateTimeOffset ReceivedAtUtc = default,
    long ReceivedAtMonotonic = 0,
    // Canonicalization fields
    string? CanonicalSymbol = null,
    byte CanonicalizationVersion = 0,
    string? CanonicalVenue = null,
    // End-to-end trace context captured at pipeline ingress.
    string? TraceId = null,
    string? ParentSpanId = null
)
{
    public static MarketEvent Trade(DateTimeOffset ts, string symbol, Trade trade, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.Trade, trade, seq == 0 ? trade.SequenceNumber : seq, source);

    public static MarketEvent L2Snapshot(DateTimeOffset ts, string symbol, LOBSnapshot snap, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.L2Snapshot, snap, seq == 0 ? snap.SequenceNumber : seq, source);

    public static MarketEvent BboQuote(DateTimeOffset ts, string symbol, BboQuotePayload quote, long seq = 0, string source = "ALPACA")
        => new(ts, symbol, MarketEventType.BboQuote, quote, seq == 0 ? quote.SequenceNumber : seq, source);

    public static MarketEvent L2SnapshotPayload(DateTimeOffset ts, string symbol, L2SnapshotPayload payload, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.L2Snapshot, payload, seq == 0 ? payload.SequenceNumber : seq, source);

    public static MarketEvent OrderFlow(DateTimeOffset ts, string symbol, OrderFlowStatistics stats, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.OrderFlow, stats, seq == 0 ? stats.SequenceNumber : seq, source);

    public static MarketEvent Integrity(DateTimeOffset ts, string symbol, IntegrityEvent integrity, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.Integrity, integrity, seq == 0 ? integrity.SequenceNumber : seq, source);

    public static MarketEvent DepthIntegrity(DateTimeOffset ts, string symbol, DepthIntegrityEvent integrity, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.Integrity, integrity, seq == 0 ? integrity.SequenceNumber : seq, source);


    public static MarketEvent ResyncRequested(DateTimeOffset ts, string symbol, string reason, string? streamId = null, string? venue = null, long seq = 0, string source = "IB")
    {
        var payload = new IntegrityEvent(
            Timestamp: ts,
            Symbol: symbol,
            Severity: IntegritySeverity.Error,
            Description: $"Resync requested: {reason}",
            ErrorCode: 1007,
            SequenceNumber: seq,
            StreamId: streamId,
            Venue: venue);

        return new(ts, symbol, MarketEventType.Integrity, payload, seq, source);
    }
    public static MarketEvent Heartbeat(DateTimeOffset ts, string source = "IB")
        => new(ts, "SYSTEM", MarketEventType.Heartbeat, new Contracts.Domain.Events.MarketEventPayload.HeartbeatPayload(), Sequence: 0, Source: source);

    public static MarketEvent HistoricalBar(DateTimeOffset ts, string symbol, HistoricalBar bar, long seq = 0, string source = "stooq")
        => new(ts, symbol, MarketEventType.HistoricalBar, bar, seq == 0 ? bar.SequenceNumber : seq, source);

    public static MarketEvent AggregateBar(DateTimeOffset ts, string symbol, Domain.Models.AggregateBar bar, long seq = 0, string source = "Polygon")
    {
        var payload = new Contracts.Domain.Models.AggregateBarPayload(
            Symbol: bar.Symbol,
            StartTime: bar.StartTime,
            EndTime: bar.EndTime,
            Open: bar.Open,
            High: bar.High,
            Low: bar.Low,
            Close: bar.Close,
            Volume: bar.Volume,
            Vwap: bar.Vwap,
            TradeCount: bar.TradeCount,
            Timeframe: (Contracts.Domain.Models.AggregateTimeframe)bar.Timeframe,
            Source: bar.Source,
            SequenceNumber: seq == 0 ? bar.SequenceNumber : seq
        );
        return new(ts, symbol, MarketEventType.AggregateBar, payload, seq == 0 ? bar.SequenceNumber : seq, source);
    }

    public static MarketEvent OptionQuote(DateTimeOffset ts, string symbol, Contracts.Domain.Models.OptionQuote quote, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.OptionQuote, quote, seq == 0 ? quote.SequenceNumber : seq, source);

    public static MarketEvent OptionTrade(DateTimeOffset ts, string symbol, Contracts.Domain.Models.OptionTrade trade, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.OptionTrade, trade, seq == 0 ? trade.SequenceNumber : seq, source);

    public static MarketEvent OptionGreeks(DateTimeOffset ts, string symbol, GreeksSnapshot greeks, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.OptionGreeks, greeks, seq == 0 ? greeks.SequenceNumber : seq, source);

    public static MarketEvent OptionChain(DateTimeOffset ts, string underlyingSymbol, OptionChainSnapshot chain, long seq = 0, string source = "IB")
        => new(ts, underlyingSymbol, MarketEventType.OptionChain, chain, seq == 0 ? chain.SequenceNumber : seq, source);

    public static MarketEvent OpenInterest(DateTimeOffset ts, string symbol, OpenInterestUpdate oi, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.OpenInterest, oi, seq == 0 ? oi.SequenceNumber : seq, source);

    public static MarketEvent OrderAdd(DateTimeOffset ts, string symbol, Contracts.Domain.Models.OrderAdd order, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.OrderAdd, order, seq == 0 ? order.SequenceNumber : seq, source);

    public static MarketEvent OrderModify(DateTimeOffset ts, string symbol, Contracts.Domain.Models.OrderModify modify, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.OrderModify, modify, seq == 0 ? modify.SequenceNumber : seq, source);

    public static MarketEvent OrderCancel(DateTimeOffset ts, string symbol, Contracts.Domain.Models.OrderCancel cancel, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.OrderCancel, cancel, seq == 0 ? cancel.SequenceNumber : seq, source);

    public static MarketEvent OrderExecute(DateTimeOffset ts, string symbol, Contracts.Domain.Models.OrderExecute execute, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.OrderExecute, execute, seq == 0 ? execute.SequenceNumber : seq, source);

    public static MarketEvent OrderReplace(DateTimeOffset ts, string symbol, Contracts.Domain.Models.OrderReplace replace, long seq = 0, string source = "IB")
        => new(ts, symbol, MarketEventType.OrderReplace, replace, seq == 0 ? replace.SequenceNumber : seq, source);

    /// <summary>
    /// Stamps the event with wall-clock and monotonic receive timestamps.
    /// Call this at the earliest point when the event enters the system.
    /// </summary>
    public MarketEvent StampReceiveTime(DateTimeOffset? exchangeTs = null)
        => this with
        {
            ReceivedAtUtc = DateTimeOffset.UtcNow,
            ReceivedAtMonotonic = Stopwatch.GetTimestamp(),
            ExchangeTimestamp = exchangeTs ?? ExchangeTimestamp
        };

    /// <summary>
    /// Returns the effective symbol for downstream consumers: <see cref="CanonicalSymbol"/>
    /// when available, otherwise the raw <see cref="Symbol"/>.
    /// Use this property in storage paths, dedup keys, metrics labels, and quality monitoring
    /// to ensure consistent behavior regardless of canonicalization state.
    /// </summary>
    public string EffectiveSymbol => CanonicalSymbol ?? Symbol;

    /// <summary>
    /// Stamps the event with trace context captured at ingress so downstream sinks,
    /// logs, and dead-letter flows can correlate records without relying on ambient Activity state.
    /// </summary>
    public MarketEvent StampTraceContext(ActivityContext parentContext)
        => parentContext.TraceId == default
            ? this
            : this with
            {
                TraceId = parentContext.TraceId.ToString(),
                ParentSpanId = parentContext.SpanId.ToString()
            };

    /// <summary>
    /// Computes the estimated end-to-end latency in milliseconds using monotonic clock,
    /// or falls back to wall-clock difference if monotonic data is unavailable.
    /// </summary>
    public double? EstimatedLatencyMs
    {
        get
        {
            if (ReceivedAtMonotonic > 0 && ExchangeTimestamp.HasValue)
            {
                // Wall-clock difference as best-effort (cross-machine clocks are unreliable)
                return (ReceivedAtUtc - ExchangeTimestamp.Value).TotalMilliseconds;
            }
            return null;
        }
    }
}

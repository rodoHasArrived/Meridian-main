using Meridian.Contracts.Domain.Events;

namespace Meridian.Contracts.Domain.Models;

/// <summary>
/// Represents a real-time OHLCV aggregate bar from streaming data providers.
/// Unlike <see cref="HistoricalBar"/> which uses DateOnly for daily bars,
/// this model uses DateTimeOffset to support intraday timeframes (second, minute, etc.).
/// </summary>
public sealed record AggregateBarPayload(
    string Symbol,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    decimal Vwap,
    int TradeCount,
    AggregateTimeframe Timeframe,
    string Source,
    long SequenceNumber
) : MarketEventPayload;

/// <summary>
/// Timeframe for aggregate bars.
/// </summary>
public enum AggregateTimeframe : byte
{
    /// <summary>
    /// Per-second aggregate (Polygon "A" event).
    /// </summary>
    Second = 0,

    /// <summary>
    /// Per-minute aggregate (Polygon "AM" event).
    /// </summary>
    Minute = 1,

    /// <summary>
    /// Hourly aggregate.
    /// </summary>
    Hour = 2,

    /// <summary>
    /// Daily aggregate.
    /// </summary>
    Day = 3
}

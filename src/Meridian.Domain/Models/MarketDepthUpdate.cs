using Meridian.Contracts.Domain.Enums;

namespace Meridian.Domain.Models;

/// <summary>
/// Normalized L2 depth delta update (adapter input into MarketDepthCollector).
/// Operation values align with IB: 0=Insert, 1=Update, 2=Delete.
/// Side values align with our OrderBookSide.
/// Uses decimal for financial precision.
/// </summary>
/// <remarks>
/// <paramref name="Source"/> is the canonical provider identity (see
/// <see cref="Meridian.Contracts.Domain.MarketDataSources"/>), stamped per event at the
/// adapter origin because the depth collector is a shared singleton serving every active
/// adapter. Sourceless updates are rejected at the collector seam with a missing-source
/// integrity event.
/// </remarks>
public sealed record MarketDepthUpdate(
    DateTimeOffset Timestamp,
    string Symbol,
    ushort Position,
    DepthOperation Operation,
    OrderBookSide Side,
    decimal Price,
    decimal Size,
    string? MarketMaker = null,
    long SequenceNumber = 0,
    string? StreamId = null,
    string? Venue = null,
    string? Source = null
);

using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Events;

namespace Meridian.Contracts.Domain.Models;

/// <summary>
/// Immutable Level-2 order book snapshot.
/// Bids/Asks should be sorted best-to-worst (Level 0 = best).
/// Uses decimal for financial precision.
/// </summary>
public sealed record LOBSnapshot(
    DateTimeOffset Timestamp,
    string Symbol,
    IReadOnlyList<OrderBookLevel> Bids,
    IReadOnlyList<OrderBookLevel> Asks,
    decimal? MidPrice = null,
    decimal? MicroPrice = null,
    decimal? Imbalance = null,
    MarketState MarketState = MarketState.Normal,
    long SequenceNumber = 0,
    string? StreamId = null,
    string? Venue = null
) : MarketEventPayload;

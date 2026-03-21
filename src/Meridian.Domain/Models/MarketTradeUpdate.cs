using Meridian.Contracts.Domain.Enums;

namespace Meridian.Domain.Models;

/// <summary>
/// Normalized tick-by-tick trade update (adapter input into TradeDataCollector).
/// This is NOT the stored Trade; it's the raw-ish input model.
/// </summary>
public sealed record MarketTradeUpdate(
    DateTimeOffset Timestamp,
    string Symbol,
    decimal Price,
    long Size,
    AggressorSide Aggressor,
    long SequenceNumber,
    string? StreamId = null,
    string? Venue = null,
    string[]? RawConditions = null
);

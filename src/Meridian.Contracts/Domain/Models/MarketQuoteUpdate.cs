namespace Meridian.Contracts.Domain.Models;

/// <summary>
/// Normalized best-bid/offer quote update (adapter input into QuoteCollector).
/// </summary>
public sealed record MarketQuoteUpdate(
    DateTimeOffset Timestamp,
    string Symbol,
    decimal BidPrice,
    long BidSize,
    decimal AskPrice,
    long AskSize,
    long? SequenceNumber = null,
    string? StreamId = null,
    string? Venue = null
);

namespace Meridian.Contracts.Domain.Models;

/// <summary>
/// Normalized best-bid/offer quote update (adapter input into QuoteCollector).
/// </summary>
/// <remarks>
/// <paramref name="Source"/> is the canonical provider identity (see
/// <see cref="MarketDataSources"/>). Collectors are shared singletons that serve every
/// concurrently active adapter, so provenance must be stamped per event at the adapter
/// origin; a sourceless update is rejected at the collector seam with a
/// missing-source integrity event instead of being silently attributed to a default vendor.
/// <paramref name="SequenceNumber"/> is the provider-supplied quote sequence when the feed
/// has one; null (or non-positive) means the provider does not sequence this stream and the
/// collector falls back to a locally assigned counter.
/// </remarks>
public sealed record MarketQuoteUpdate(
    DateTimeOffset Timestamp,
    string Symbol,
    decimal BidPrice,
    long BidSize,
    decimal AskPrice,
    long AskSize,
    long? SequenceNumber = null,
    string? StreamId = null,
    string? Venue = null,
    string? Source = null
);

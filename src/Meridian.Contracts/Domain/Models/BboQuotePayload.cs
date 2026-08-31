using Meridian.Contracts.Domain.Events;

namespace Meridian.Contracts.Domain.Models;

/// <summary>
/// Best-Bid/Offer snapshot payload.
/// </summary>
/// <remarks>
/// <paramref name="IsProviderSequence"/> records whether <paramref name="SequenceNumber"/>
/// came from the provider's own feed (gap detection is meaningful) or was assigned by a
/// local per-symbol counter because the provider does not sequence its quote stream.
/// Payloads persisted before this field existed carried locally assigned counters, so the
/// default of <see langword="false"/> is honest for old data.
/// </remarks>
public sealed record BboQuotePayload(
    DateTimeOffset Timestamp,
    string Symbol,
    decimal BidPrice,
    long BidSize,
    decimal AskPrice,
    long AskSize,
    decimal? MidPrice,
    decimal? Spread,
    long SequenceNumber,
    string? StreamId = null,
    string? Venue = null,
    bool IsProviderSequence = false
) : MarketEventPayload
{
    /// <summary>
    /// Creates a BboQuotePayload from a MarketQuoteUpdate (adapter input).
    /// Calculates mid-price and spread automatically.
    /// </summary>
    public static BboQuotePayload FromUpdate(
        DateTimeOffset timestamp,
        string symbol,
        decimal bidPrice,
        long bidSize,
        decimal askPrice,
        long askSize,
        long sequenceNumber,
        string? streamId = null,
        string? venue = null)
    {
        var midPrice = (bidPrice + askPrice) / 2m;
        var spread = askPrice - bidPrice;

        return new BboQuotePayload(
            Timestamp: timestamp,
            Symbol: symbol,
            BidPrice: bidPrice,
            BidSize: bidSize,
            AskPrice: askPrice,
            AskSize: askSize,
            MidPrice: midPrice,
            Spread: spread,
            SequenceNumber: sequenceNumber,
            StreamId: streamId,
            Venue: venue
        );
    }

    /// <summary>
    /// Creates a BboQuotePayload from a MarketQuoteUpdate with conditional spread/mid-price calculation.
    /// Returns null for spread and mid-price if:
    /// - Either bid or ask price is 0
    /// - Market is crossed (bid > ask)
    /// </summary>
    /// <param name="update">The market quote update</param>
    /// <param name="seq">The sequence number to use (overrides update.SequenceNumber)</param>
    /// <param name="isProviderSequence">
    /// True when <paramref name="seq"/> is the provider's own sequence number; false when it
    /// was assigned by a local counter.
    /// </param>
    /// <returns>A new BboQuotePayload</returns>
    public static BboQuotePayload FromUpdate(MarketQuoteUpdate update, long seq, bool isProviderSequence = false)
    {
        if (update is null)
            throw new ArgumentNullException(nameof(update));

        // Calculate spread and mid-price conditionally
        decimal? midPrice = null;
        decimal? spread = null;

        // Only calculate if both prices are non-zero and market is not crossed
        if (update.BidPrice > 0 && update.AskPrice > 0 && update.BidPrice <= update.AskPrice)
        {
            midPrice = (update.BidPrice + update.AskPrice) / 2m;
            spread = update.AskPrice - update.BidPrice;
        }

        return new BboQuotePayload(
            Timestamp: update.Timestamp,
            Symbol: update.Symbol,
            BidPrice: update.BidPrice,
            BidSize: update.BidSize,
            AskPrice: update.AskPrice,
            AskSize: update.AskSize,
            MidPrice: midPrice,
            Spread: spread,
            SequenceNumber: seq,
            StreamId: update.StreamId,
            Venue: update.Venue,
            IsProviderSequence: isProviderSequence
        );
    }
}

using Meridian.Contracts.Domain.Events;

namespace Meridian.Contracts.Domain.Models;

/// <summary>
/// Historical NBBO (National Best Bid and Offer) quote data from Alpaca Markets API.
/// Represents the best bid and ask prices available across all exchanges at a point in time.
/// </summary>
public sealed record HistoricalQuote : MarketEventPayload
{
    /// <summary>
    /// Gets the ticker symbol for the security.
    /// </summary>
    public string Symbol { get; }

    /// <summary>
    /// Gets the timestamp of the quote.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// Gets the exchange code for the best ask price.
    /// </summary>
    public string AskExchange { get; }

    /// <summary>
    /// Gets the best ask (offer) price.
    /// </summary>
    public decimal AskPrice { get; }

    /// <summary>
    /// Gets the size available at the ask price.
    /// </summary>
    public long AskSize { get; }

    /// <summary>
    /// Gets the exchange code for the best bid price.
    /// </summary>
    public string BidExchange { get; }

    /// <summary>
    /// Gets the best bid price.
    /// </summary>
    public decimal BidPrice { get; }

    /// <summary>
    /// Gets the size available at the bid price.
    /// </summary>
    public long BidSize { get; }

    /// <summary>
    /// Gets the quote condition codes.
    /// </summary>
    public string[]? Conditions { get; }

    /// <summary>
    /// Gets the tape identifier (A, B, or C).
    /// </summary>
    public string? Tape { get; }

    /// <summary>
    /// Gets the data source identifier (e.g., "alpaca").
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Gets the sequence number for ordering events.
    /// </summary>
    public long SequenceNumber { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HistoricalQuote"/> record.
    /// </summary>
    /// <param name="Symbol">The ticker symbol.</param>
    /// <param name="Timestamp">The timestamp of the quote.</param>
    /// <param name="AskExchange">The ask exchange code.</param>
    /// <param name="AskPrice">The best ask price.</param>
    /// <param name="AskSize">The ask size.</param>
    /// <param name="BidExchange">The bid exchange code.</param>
    /// <param name="BidPrice">The best bid price.</param>
    /// <param name="BidSize">The bid size.</param>
    /// <param name="Conditions">The quote conditions.</param>
    /// <param name="Tape">The tape identifier.</param>
    /// <param name="Source">The data source identifier.</param>
    /// <param name="SequenceNumber">The sequence number for ordering.</param>
    public HistoricalQuote(
        string Symbol,
        DateTimeOffset Timestamp,
        string AskExchange,
        decimal AskPrice,
        long AskSize,
        string BidExchange,
        decimal BidPrice,
        long BidSize,
        string[]? Conditions = null,
        string? Tape = null,
        string Source = "alpaca",
        long SequenceNumber = 0)
    {
        if (string.IsNullOrWhiteSpace(Symbol))
            throw new ArgumentException("Symbol is required", nameof(Symbol));

        if (AskPrice < 0)
            throw new ArgumentOutOfRangeException(nameof(AskPrice), "Ask price cannot be negative.");

        if (BidPrice < 0)
            throw new ArgumentOutOfRangeException(nameof(BidPrice), "Bid price cannot be negative.");

        if (AskSize < 0)
            throw new ArgumentOutOfRangeException(nameof(AskSize), "Ask size cannot be negative.");

        if (BidSize < 0)
            throw new ArgumentOutOfRangeException(nameof(BidSize), "Bid size cannot be negative.");

        this.Symbol = Symbol;
        this.Timestamp = Timestamp;
        this.AskExchange = AskExchange;
        this.AskPrice = AskPrice;
        this.AskSize = AskSize;
        this.BidExchange = BidExchange;
        this.BidPrice = BidPrice;
        this.BidSize = BidSize;
        this.Conditions = Conditions;
        this.Tape = Tape;
        this.Source = Source;
        this.SequenceNumber = SequenceNumber;
    }

    /// <summary>
    /// Calculated spread (Ask - Bid).
    /// </summary>
    public decimal Spread => AskPrice - BidPrice;

    /// <summary>
    /// Calculated mid-price ((Ask + Bid) / 2).
    /// </summary>
    public decimal MidPrice => (AskPrice + BidPrice) / 2m;

    /// <summary>
    /// Spread as a percentage of the mid-price in basis points.
    /// </summary>
    public decimal? SpreadBps => MidPrice > 0 ? (Spread / MidPrice) * 10000m : null;
}

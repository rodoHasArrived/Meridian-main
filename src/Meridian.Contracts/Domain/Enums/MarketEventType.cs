namespace Meridian.Contracts.Domain.Enums;

/// <summary>
/// Type of market event.
/// </summary>
public enum MarketEventType : byte
{
    /// <summary>
    /// Unknown event type.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Level 2 order book snapshot.
    /// </summary>
    L2Snapshot = 1,

    /// <summary>
    /// Best bid/offer quote.
    /// </summary>
    BboQuote = 2,

    /// <summary>
    /// Trade execution.
    /// </summary>
    Trade = 3,

    /// <summary>
    /// Order flow statistics.
    /// </summary>
    OrderFlow = 4,

    /// <summary>
    /// Heartbeat message.
    /// </summary>
    Heartbeat = 5,

    /// <summary>
    /// Connection status change.
    /// </summary>
    ConnectionStatus = 6,

    /// <summary>
    /// Data integrity event.
    /// </summary>
    Integrity = 7,

    /// <summary>
    /// Historical bar data.
    /// </summary>
    HistoricalBar = 8,

    /// <summary>
    /// Historical quote data.
    /// </summary>
    HistoricalQuote = 9,

    /// <summary>
    /// Historical trade data.
    /// </summary>
    HistoricalTrade = 10,

    /// <summary>
    /// Historical auction data.
    /// </summary>
    HistoricalAuction = 11,

    /// <summary>
    /// Real-time aggregate bar (OHLCV) from streaming providers.
    /// </summary>
    AggregateBar = 12,

    /// <summary>
    /// Quote update event.
    /// </summary>
    Quote = 13,

    /// <summary>
    /// Order book depth update.
    /// </summary>
    Depth = 14,

    /// <summary>
    /// Option quote with bid/ask, greeks, and implied volatility.
    /// </summary>
    OptionQuote = 15,

    /// <summary>
    /// Option trade execution.
    /// </summary>
    OptionTrade = 16,

    /// <summary>
    /// Option greeks snapshot (delta, gamma, theta, vega, rho, IV).
    /// </summary>
    OptionGreeks = 17,

    /// <summary>
    /// Option chain snapshot for an underlying and expiration.
    /// </summary>
    OptionChain = 18,

    /// <summary>
    /// Open interest update for an option contract.
    /// </summary>
    OpenInterest = 19,

    /// <summary>
    /// New order added to the matching engine's order book.
    /// </summary>
    OrderAdd = 20,

    /// <summary>
    /// Existing order modified (price, size, or flags changed).
    /// </summary>
    OrderModify = 21,

    /// <summary>
    /// Existing order cancelled (full or partial removal from the book).
    /// </summary>
    OrderCancel = 22,

    /// <summary>
    /// Resting order executed (full or partial fill against an aggressor).
    /// </summary>
    OrderExecute = 23,

    /// <summary>
    /// Venue-side order replace where the venue reassigns the order identifier.
    /// </summary>
    OrderReplace = 24
}

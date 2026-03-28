namespace Meridian.QuantScript.API;

/// <summary>
/// A snapshot of the order book (best bid/ask) at a point in time.
/// </summary>
public sealed record ScriptOrderBook(
    DateTime Timestamp,
    double BidPx,
    double BidSz,
    double AskPx,
    double AskSz);

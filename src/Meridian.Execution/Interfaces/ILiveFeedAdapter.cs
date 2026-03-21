using Meridian.Contracts.Domain.Models;
using Meridian.Execution.Models;

namespace Meridian.Execution.Interfaces;

/// <summary>
/// Live market data surface exposed to strategy code at execution time.
/// Wraps <c>IMarketDataClient</c> to provide a feed-agnostic view of real-time
/// prices that does not expose any provider-specific type.
/// </summary>
public interface ILiveFeedAdapter
{
    /// <summary>All symbols currently subscribed in this execution session.</summary>
    IReadOnlySet<string> SubscribedSymbols { get; }

    /// <summary>
    /// Returns the most recent trade for <paramref name="symbol"/>,
    /// or <c>null</c> if no tick has been received yet.
    /// </summary>
    Trade? GetLastTrade(string symbol);

    /// <summary>
    /// Returns the most recent best-bid/offer quote for <paramref name="symbol"/>,
    /// or <c>null</c> if no quote has been received yet.
    /// </summary>
    BboQuotePayload? GetLastQuote(string symbol);

    /// <summary>
    /// Returns the most recent Level-2 order book snapshot for <paramref name="symbol"/>,
    /// or <c>null</c> if no snapshot has been received yet.
    /// </summary>
    LOBSnapshot? GetLastOrderBook(string symbol);
}

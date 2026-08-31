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

    /// <summary>
    /// Returns the most recent completed bar for <paramref name="symbol"/>, or <c>null</c>
    /// when no bar has been observed. Default implementation returns <c>null</c> so
    /// tick-only adapters need not implement bar retention.
    /// </summary>
    HistoricalBar? GetLastBar(string symbol) => null;

    /// <summary>
    /// Returns one coherent view of the last-known quote, trade, and bar for
    /// <paramref name="symbol"/>. Consumers that price against a market-data envelope
    /// (paper matching) must read through here rather than pairing the individual getters:
    /// two independent reads can observe the quote from before a market update and the trade
    /// from after it, producing an envelope that was never in effect (#2676).
    /// <para>
    /// The default implementation composes the individual getters and is therefore NOT
    /// atomic; adapters that record fields independently under concurrency should override
    /// it with a genuinely consistent snapshot, as <c>LiveMarketDataCache</c> does.
    /// </para>
    /// </summary>
    MarketDataSnapshot GetSnapshot(string symbol) =>
        new(GetLastQuote(symbol), GetLastTrade(symbol), GetLastBar(symbol));
}

/// <summary>
/// A single coherent observation of the last-known market data for one symbol: the fields
/// were read together, so a consumer never sees a quote from before an update paired with a
/// trade from after it.
/// </summary>
public readonly record struct MarketDataSnapshot(
    BboQuotePayload? Quote,
    Trade? Trade,
    HistoricalBar? Bar);

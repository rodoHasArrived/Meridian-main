using Meridian.Infrastructure.CppTrader.Translation;

namespace Meridian.Infrastructure.CppTrader.Execution;

/// <summary>
/// Session-local feed cache populated from CppTrader execution events.
/// </summary>
public sealed class CppTraderLiveFeedAdapter : ILiveFeedAdapter
{
    private readonly ConcurrentDictionary<string, Trade> _trades = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, BboQuotePayload> _quotes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, LOBSnapshot> _books = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> SubscribedSymbols =>
        new HashSet<string>(_books.Keys.Concat(_trades.Keys).Concat(_quotes.Keys), StringComparer.OrdinalIgnoreCase);

    public Trade? GetLastTrade(string symbol) => _trades.GetValueOrDefault(symbol);

    public BboQuotePayload? GetLastQuote(string symbol) => _quotes.GetValueOrDefault(symbol);

    public LOBSnapshot? GetLastOrderBook(string symbol) => _books.GetValueOrDefault(symbol);

    public void ApplyTrade(Trade trade) => _trades[trade.Symbol] = trade;

    public void ApplySnapshot(LOBSnapshot snapshot)
    {
        _books[snapshot.Symbol] = snapshot;

        var bestBid = snapshot.Bids.OrderByDescending(level => level.Price).FirstOrDefault();
        var bestAsk = snapshot.Asks.OrderBy(level => level.Price).FirstOrDefault();
        if (bestBid is null || bestAsk is null)
            return;

        _quotes[snapshot.Symbol] = new BboQuotePayload(
            snapshot.Timestamp,
            snapshot.Symbol,
            bestBid.Price,
            decimal.ToInt64(decimal.Truncate(bestBid.Size)),
            bestAsk.Price,
            decimal.ToInt64(decimal.Truncate(bestAsk.Size)),
            snapshot.MidPrice,
            bestAsk.Price - bestBid.Price,
            snapshot.SequenceNumber,
            snapshot.StreamId,
            snapshot.Venue);
    }
}

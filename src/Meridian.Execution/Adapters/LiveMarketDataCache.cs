using System.Collections.Concurrent;
using Meridian.Contracts.Domain.Models;
using Meridian.Execution.Interfaces;

namespace Meridian.Execution.Adapters;

/// <summary>
/// Thread-safe last-known market data cache that implements <see cref="ILiveFeedAdapter"/>.
/// A host-side pump records live pipeline events here so execution-time consumers
/// (strategy contexts, paper fill pricing) can read real-time prices without any
/// provider-specific type crossing the execution boundary.
/// </summary>
public sealed class LiveMarketDataCache : ILiveFeedAdapter
{
    private readonly ConcurrentDictionary<string, Trade> _lastTrades = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, BboQuotePayload> _lastQuotes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, LOBSnapshot> _lastOrderBooks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _subscribedSymbols = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public IReadOnlySet<string> SubscribedSymbols =>
        new HashSet<string>(_subscribedSymbols.Keys, StringComparer.OrdinalIgnoreCase);

    /// <summary>Marks a symbol as part of the live session universe.</summary>
    public void Subscribe(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        _subscribedSymbols.TryAdd(symbol, 0);
    }

    /// <summary>Records the most recent trade tick for a symbol.</summary>
    public void RecordTrade(string symbol, Trade trade)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(trade);
        _subscribedSymbols.TryAdd(symbol, 0);
        _lastTrades[symbol] = trade;
    }

    /// <summary>Records the most recent best-bid/offer quote for a symbol.</summary>
    public void RecordQuote(string symbol, BboQuotePayload quote)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(quote);
        _subscribedSymbols.TryAdd(symbol, 0);
        _lastQuotes[symbol] = quote;
    }

    /// <summary>Records the most recent Level-2 order book snapshot for a symbol.</summary>
    public void RecordOrderBook(string symbol, LOBSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(snapshot);
        _subscribedSymbols.TryAdd(symbol, 0);
        _lastOrderBooks[symbol] = snapshot;
    }

    /// <inheritdoc/>
    public Trade? GetLastTrade(string symbol) =>
        _lastTrades.TryGetValue(symbol, out var trade) ? trade : null;

    /// <inheritdoc/>
    public BboQuotePayload? GetLastQuote(string symbol) =>
        _lastQuotes.TryGetValue(symbol, out var quote) ? quote : null;

    /// <inheritdoc/>
    public LOBSnapshot? GetLastOrderBook(string symbol) =>
        _lastOrderBooks.TryGetValue(symbol, out var snapshot) ? snapshot : null;

    /// <summary>
    /// Returns the best available reference price for a symbol: last trade price first,
    /// then best-bid/offer midpoint, or <c>null</c> when no live tick has been observed.
    /// </summary>
    public decimal? GetReferencePrice(string symbol)
    {
        if (GetLastTrade(symbol) is { Price: > 0m } trade)
        {
            return trade.Price;
        }

        if (GetLastQuote(symbol) is { } quote)
        {
            var mid = quote.MidPrice ?? (quote.BidPrice + quote.AskPrice) / 2m;
            if (mid > 0m)
            {
                return mid;
            }
        }

        return null;
    }
}

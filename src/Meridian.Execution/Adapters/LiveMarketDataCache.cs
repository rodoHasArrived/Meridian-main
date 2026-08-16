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
    /// <summary>
    /// All last-known fields for one symbol in a single immutable record. Every update swaps
    /// the whole record atomically, so a reader that takes one reference gets a view in which
    /// no field is newer than a field written before it — the guarantee the paper-matching
    /// envelope depends on (#2676). Storing the fields in separate dictionaries let a reader
    /// pair a pre-update quote with a post-update trade.
    /// </summary>
    private sealed record SymbolMarketState(
        BboQuotePayload? Quote,
        Trade? Trade,
        HistoricalBar? Bar,
        LOBSnapshot? OrderBook)
    {
        public static readonly SymbolMarketState Empty = new(null, null, null, null);
    }

    private readonly ConcurrentDictionary<string, SymbolMarketState> _states = new(StringComparer.OrdinalIgnoreCase);
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
        Mutate(symbol, trade, static (state, value) => state with { Trade = value });
    }

    /// <summary>Records the most recent best-bid/offer quote for a symbol.</summary>
    public void RecordQuote(string symbol, BboQuotePayload quote)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(quote);
        _subscribedSymbols.TryAdd(symbol, 0);
        Mutate(symbol, quote, static (state, value) => state with { Quote = value });
    }

    /// <summary>Records the most recent Level-2 order book snapshot for a symbol.</summary>
    public void RecordOrderBook(string symbol, LOBSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(snapshot);
        _subscribedSymbols.TryAdd(symbol, 0);
        Mutate(symbol, snapshot, static (state, value) => state with { OrderBook = value });
    }

    /// <summary>Records the most recent completed bar for a symbol.</summary>
    public void RecordBar(string symbol, HistoricalBar bar)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(bar);
        _subscribedSymbols.TryAdd(symbol, 0);
        Mutate(symbol, bar, static (state, value) => state with { Bar = value });
    }

    /// <inheritdoc/>
    public Trade? GetLastTrade(string symbol) => GetState(symbol).Trade;

    /// <inheritdoc/>
    public BboQuotePayload? GetLastQuote(string symbol) => GetState(symbol).Quote;

    /// <inheritdoc/>
    public LOBSnapshot? GetLastOrderBook(string symbol) => GetState(symbol).OrderBook;

    /// <inheritdoc/>
    public HistoricalBar? GetLastBar(string symbol) => GetState(symbol).Bar;

    /// <summary>
    /// Atomic override of the interface default: all three fields come from one state
    /// reference, so the snapshot can never pair a pre-update quote with a post-update trade.
    /// </summary>
    public MarketDataSnapshot GetSnapshot(string symbol)
    {
        var state = GetState(symbol);
        return new MarketDataSnapshot(state.Quote, state.Trade, state.Bar);
    }

    private SymbolMarketState GetState(string symbol) =>
        _states.TryGetValue(symbol, out var state) ? state : SymbolMarketState.Empty;

    // AddOrUpdate retries its compare-and-swap until the update wins, so two writers
    // mutating different fields of the same symbol both land: the loser re-reads the
    // winner's record and reapplies its own field on top.
    private void Mutate<T>(string symbol, T value, Func<SymbolMarketState, T, SymbolMarketState> update) =>
        _states.AddOrUpdate(
            symbol,
            static (_, arg) => arg.update(SymbolMarketState.Empty, arg.value),
            static (_, existing, arg) => arg.update(existing, arg.value),
            (value, update));

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

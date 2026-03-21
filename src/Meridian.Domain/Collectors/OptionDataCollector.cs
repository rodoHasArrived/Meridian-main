using System.Collections.Concurrent;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Contracts;

namespace Meridian.Domain.Collectors;

/// <summary>
/// Captures option quotes, trades, greeks, and chain snapshots,
/// maintains per-contract state, and emits unified MarketEvents.
/// </summary>
[ImplementsAdr("ADR-001", "Option data collector implementing domain event publication")]
[ImplementsAdr("ADR-006", "Polymorphic MarketEvent payloads for option data types")]
public sealed class OptionDataCollector
{
    private readonly IMarketEventPublisher _publisher;

    private readonly ConcurrentDictionary<string, OptionQuote> _latestQuotes =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, GreeksSnapshot> _latestGreeks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, OptionChainSnapshot> _latestChains =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, OpenInterestUpdate> _latestOpenInterest =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, RecentOptionTradeRing> _recentTrades =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, long> _quoteSeq =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maximum number of recent option trades to retain per contract.
    /// </summary>
    private const int MaxRecentTrades = 100;

    public OptionDataCollector(IMarketEventPublisher publisher)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
    }

    /// <summary>
    /// Processes an incoming option quote and emits an OptionQuote market event.
    /// </summary>
    public void OnOptionQuote(OptionQuote quote)
    {
        if (quote is null)
            throw new ArgumentNullException(nameof(quote));

        var key = BuildContractKey(quote.Contract);
        _latestQuotes[key] = quote;

        _publisher.TryPublish(MarketEvent.OptionQuote(quote.Timestamp, quote.Symbol, quote));
    }

    /// <summary>
    /// Processes an incoming option trade and emits an OptionTrade market event.
    /// </summary>
    public void OnOptionTrade(OptionTrade trade)
    {
        if (trade is null)
            throw new ArgumentNullException(nameof(trade));

        var key = BuildContractKey(trade.Contract);

        var ring = _recentTrades.GetOrAdd(key, _ => new RecentOptionTradeRing(MaxRecentTrades));
        ring.Add(trade);

        _publisher.TryPublish(MarketEvent.OptionTrade(trade.Timestamp, trade.Symbol, trade));
    }

    /// <summary>
    /// Processes an incoming greeks snapshot and emits an OptionGreeks market event.
    /// </summary>
    public void OnGreeksUpdate(GreeksSnapshot greeks)
    {
        if (greeks is null)
            throw new ArgumentNullException(nameof(greeks));

        var key = BuildContractKey(greeks.Contract);
        _latestGreeks[key] = greeks;

        _publisher.TryPublish(MarketEvent.OptionGreeks(greeks.Timestamp, greeks.Symbol, greeks));
    }

    /// <summary>
    /// Processes an incoming option chain snapshot and emits an OptionChain market event.
    /// </summary>
    public void OnChainSnapshot(OptionChainSnapshot chain)
    {
        if (chain is null)
            throw new ArgumentNullException(nameof(chain));

        var key = BuildChainKey(chain.UnderlyingSymbol, chain.Expiration);
        _latestChains[key] = chain;

        _publisher.TryPublish(MarketEvent.OptionChain(chain.Timestamp, chain.UnderlyingSymbol, chain));
    }

    /// <summary>
    /// Processes an incoming open interest update and emits an OpenInterest market event.
    /// </summary>
    public void OnOpenInterestUpdate(OpenInterestUpdate update)
    {
        if (update is null)
            throw new ArgumentNullException(nameof(update));

        var key = BuildContractKey(update.Contract);
        _latestOpenInterest[key] = update;

        _publisher.TryPublish(MarketEvent.OpenInterest(update.Timestamp, update.Symbol, update));
    }

    /// <summary>
    /// Returns the latest option quote for a contract, or null if none recorded.
    /// </summary>
    public OptionQuote? GetLatestQuote(OptionContractSpec contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var key = BuildContractKey(contract);
        return _latestQuotes.TryGetValue(key, out var quote) ? quote : null;
    }

    /// <summary>
    /// Returns the latest greeks snapshot for a contract, or null if none recorded.
    /// </summary>
    public GreeksSnapshot? GetLatestGreeks(OptionContractSpec contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var key = BuildContractKey(contract);
        return _latestGreeks.TryGetValue(key, out var greeks) ? greeks : null;
    }

    /// <summary>
    /// Returns the latest option chain snapshot for an underlying and expiration.
    /// </summary>
    public OptionChainSnapshot? GetLatestChain(string underlyingSymbol, DateOnly expiration)
    {
        var key = BuildChainKey(underlyingSymbol, expiration);
        return _latestChains.TryGetValue(key, out var chain) ? chain : null;
    }

    /// <summary>
    /// Returns all cached chain snapshots for an underlying symbol.
    /// </summary>
    public IReadOnlyList<OptionChainSnapshot> GetChainsForUnderlying(string underlyingSymbol)
    {
        if (string.IsNullOrWhiteSpace(underlyingSymbol))
            return Array.Empty<OptionChainSnapshot>();

        var prefix = underlyingSymbol.ToUpperInvariant() + ":";
        var result = new List<OptionChainSnapshot>();

        foreach (var kvp in _latestChains)
        {
            if (kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(kvp.Value);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the latest open interest update for a contract, or null if none recorded.
    /// </summary>
    public OpenInterestUpdate? GetLatestOpenInterest(OptionContractSpec contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var key = BuildContractKey(contract);
        return _latestOpenInterest.TryGetValue(key, out var oi) ? oi : null;
    }

    /// <summary>
    /// Returns recent option trades for a contract, newest first.
    /// </summary>
    public IReadOnlyList<OptionTrade> GetRecentTrades(OptionContractSpec contract, int limit = 50)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var key = BuildContractKey(contract);
        if (!_recentTrades.TryGetValue(key, out var ring))
            return Array.Empty<OptionTrade>();
        return ring.GetRecent(Math.Min(limit, MaxRecentTrades));
    }

    /// <summary>
    /// Returns all option quotes for a given underlying symbol.
    /// </summary>
    public IReadOnlyList<OptionQuote> GetQuotesForUnderlying(string underlyingSymbol)
    {
        if (string.IsNullOrWhiteSpace(underlyingSymbol))
            return Array.Empty<OptionQuote>();

        var result = new List<OptionQuote>();
        foreach (var kvp in _latestQuotes)
        {
            if (string.Equals(kvp.Value.Contract.UnderlyingSymbol, underlyingSymbol, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(kvp.Value);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns all underlying symbols that have active option data.
    /// </summary>
    public IReadOnlyList<string> GetTrackedUnderlyings()
    {
        var underlyings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in _latestQuotes)
            underlyings.Add(kvp.Value.Contract.UnderlyingSymbol);

        foreach (var kvp in _latestChains)
            underlyings.Add(kvp.Value.UnderlyingSymbol);

        return underlyings.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Returns a summary snapshot of all option data currently held.
    /// </summary>
    public OptionDataSummary GetSummary()
    {
        return new OptionDataSummary(
            TrackedContracts: _latestQuotes.Count,
            TrackedChains: _latestChains.Count,
            TrackedUnderlyings: GetTrackedUnderlyings().Count,
            ContractsWithGreeks: _latestGreeks.Count,
            ContractsWithOpenInterest: _latestOpenInterest.Count,
            Timestamp: DateTimeOffset.UtcNow);
    }

    private static string BuildContractKey(OptionContractSpec contract)
    {
        return $"{contract.UnderlyingSymbol}:{contract.Expiration:yyyyMMdd}:{contract.Right}:{contract.Strike:F2}";
    }

    private static string BuildChainKey(string underlyingSymbol, DateOnly expiration)
    {
        return $"{underlyingSymbol.ToUpperInvariant()}:{expiration:yyyyMMdd}";
    }

    /// <summary>
    /// Thread-safe fixed-capacity ring buffer for recent option trades.
    /// </summary>
    private sealed class RecentOptionTradeRing
    {
        private readonly OptionTrade[] _buffer;
        private readonly object _sync = new();
        private int _head;
        private int _count;

        public RecentOptionTradeRing(int capacity) => _buffer = new OptionTrade[capacity];

        public void Add(OptionTrade trade)
        {
            lock (_sync)
            {
                _buffer[_head] = trade;
                _head = (_head + 1) % _buffer.Length;
                if (_count < _buffer.Length)
                    _count++;
            }
        }

        public IReadOnlyList<OptionTrade> GetRecent(int limit)
        {
            lock (_sync)
            {
                var take = Math.Min(limit, _count);
                var result = new OptionTrade[take];
                for (int i = 0; i < take; i++)
                {
                    var idx = (_head - 1 - i + _buffer.Length) % _buffer.Length;
                    result[i] = _buffer[idx];
                }
                return result;
            }
        }
    }
}

/// <summary>
/// Summary of option data currently held by the collector.
/// </summary>
public sealed record OptionDataSummary(
    int TrackedContracts,
    int TrackedChains,
    int TrackedUnderlyings,
    int ContractsWithGreeks,
    int ContractsWithOpenInterest,
    DateTimeOffset Timestamp
);

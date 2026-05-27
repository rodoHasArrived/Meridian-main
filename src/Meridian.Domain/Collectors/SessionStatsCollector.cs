using System.Collections.Concurrent;
using Meridian.Contracts.Domain;
using Meridian.Contracts.Domain.Models;

namespace Meridian.Domain.Collectors;

/// <summary>
/// Maintains per-symbol intraday session statistics (open / high / low / last / volume / VWAP)
/// from observed trades. Resets a symbol's state automatically when a trade arrives whose UTC
/// calendar date differs from the date of the previously-stored session.
/// </summary>
/// <remarks>
/// The collector is intentionally narrow: it accepts trades and exposes a snapshot. It does
/// not subscribe to the publisher itself; <see cref="TradeDataCollector"/> calls
/// <see cref="OnTrade"/> for each accepted trade so session tracking participates in the same
/// integrity-checked path as the rest of the trade pipeline.
/// </remarks>
public sealed class SessionStatsCollector
{
    private readonly ConcurrentDictionary<SymbolId, SessionStateBox> _stats = new();

    /// <summary>
    /// Records a trade against the per-symbol session state. Negative or zero prices are ignored
    /// because they cannot meaningfully participate in open / high / low / VWAP arithmetic.
    /// </summary>
    public void OnTrade(string symbol, decimal price, long size, DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return;
        if (price <= 0m)
            return;

        var key = new SymbolId(symbol);
        var sessionDate = DateOnly.FromDateTime(timestamp.UtcDateTime);
        var box = _stats.GetOrAdd(key, _ => new SessionStateBox(symbol));
        box.Apply(symbol, sessionDate, price, size, timestamp);
    }

    /// <summary>
    /// Returns the most recent session statistics for <paramref name="symbol"/>, or
    /// <c>null</c> if no trades have been observed.
    /// </summary>
    public SessionStats? TryGet(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return null;
        return _stats.TryGetValue(new SymbolId(symbol), out var box) ? box.ToSnapshot() : null;
    }

    /// <summary>
    /// Returns a point-in-time copy of every symbol's current session statistics keyed by symbol.
    /// </summary>
    public IReadOnlyDictionary<string, SessionStats> Snapshot()
    {
        var result = new Dictionary<string, SessionStats>(_stats.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in _stats)
        {
            result[kvp.Key.Value] = kvp.Value.ToSnapshot();
        }
        return result;
    }

    /// <summary>
    /// Clears any retained session state for the given symbol. Used when a symbol is unsubscribed.
    /// </summary>
    public bool TryRemove(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return false;
        return _stats.TryRemove(new SymbolId(symbol), out _);
    }

    private sealed class SessionStateBox
    {
        private readonly object _sync = new();
        private string _symbol;
        private DateOnly _sessionDate;
        private decimal _open;
        private decimal _high;
        private decimal _low;
        private decimal _last;
        private long _volume;
        private decimal _vwapNumerator;
        private long _vwapDenominator;
        private long _tradeCount;
        private DateTimeOffset _firstTradeAt;
        private DateTimeOffset _lastTradeAt;
        private bool _hasTrade;

        public SessionStateBox(string symbol)
        {
            _symbol = symbol;
        }

        public void Apply(string symbol, DateOnly sessionDate, decimal price, long size, DateTimeOffset timestamp)
        {
            lock (_sync)
            {
                if (!_hasTrade || sessionDate != _sessionDate)
                {
                    _symbol = symbol;
                    _sessionDate = sessionDate;
                    _open = price;
                    _high = price;
                    _low = price;
                    _last = price;
                    _volume = Math.Max(0, size);
                    _vwapNumerator = price * Math.Max(0, size);
                    _vwapDenominator = Math.Max(0, size);
                    _tradeCount = 1;
                    _firstTradeAt = timestamp;
                    _lastTradeAt = timestamp;
                    _hasTrade = true;
                    return;
                }

                if (price > _high) _high = price;
                if (price < _low) _low = price;
                _last = price;
                _tradeCount++;
                _lastTradeAt = timestamp;

                if (size > 0)
                {
                    _volume += size;
                    _vwapNumerator += price * size;
                    _vwapDenominator += size;
                }
            }
        }

        public SessionStats ToSnapshot()
        {
            lock (_sync)
            {
                var vwap = _vwapDenominator == 0 ? _last : _vwapNumerator / _vwapDenominator;
                return new SessionStats(
                    Symbol: _symbol,
                    SessionDate: _sessionDate,
                    Open: _open,
                    High: _high,
                    Low: _low,
                    Last: _last,
                    Volume: _volume,
                    Vwap: vwap,
                    TradeCount: _tradeCount,
                    FirstTradeAt: _firstTradeAt,
                    LastTradeAt: _lastTradeAt);
            }
        }
    }
}

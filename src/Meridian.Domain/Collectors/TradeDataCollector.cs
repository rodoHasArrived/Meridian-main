using System.Collections.Concurrent;
using Meridian.Contracts.Domain;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Domain.Telemetry;

namespace Meridian.Domain.Collectors;

/// <summary>
/// Captures tick-by-tick trades, maintains rolling order-flow statistics,
/// and emits unified MarketEvents with strongly-typed payloads.
/// </summary>
public sealed class TradeDataCollector
{
    private readonly IMarketEventPublisher _publisher;
    private readonly IQuoteStateStore? _quotes;
    private readonly SessionStatsCollector? _sessionStats;

    /// <summary>
    /// Typed key that combines symbol, stream, and venue for per-stream continuity tracking.
    /// Using strongly-typed components prevents accidental construction from unrelated strings.
    /// Venue is normalized to uppercase to preserve case-insensitive semantics across sources.
    /// </summary>
    private readonly record struct StreamKey
    {
        public SymbolId Symbol { get; }
        public string? StreamId { get; }
        public string? Venue { get; }
        public DateOnly? SequenceSessionDate { get; }

        public StreamKey(
            SymbolId symbol,
            string? streamId,
            string? venue,
            DateOnly? sequenceSessionDate = null)
        {
            Symbol = symbol;
            StreamId = streamId;
            Venue = venue is null ? null : venue.ToUpperInvariant();
            SequenceSessionDate = sequenceSessionDate;
        }
    }
    // Per-stream rolling state. Providers can explicitly override the state scope when their
    // published trade identity or venue is narrower than the documented sequence domain.
    private readonly ConcurrentDictionary<StreamKey, SymbolTradeState> _stateBySymbol = new();

    // Per-symbol recent trade ring buffer (capped at MaxRecentTrades)
    private readonly ConcurrentDictionary<SymbolId, RecentTradeRing> _recentTrades = new();

    /// <summary>
    /// Maximum allowed length for a symbol. Covers most instrument types including options and futures.
    /// </summary>
    private const int MaxSymbolLength = 50;

    /// <summary>
    /// Maximum number of recent trades to retain per symbol for API access.
    /// </summary>
    private const int MaxRecentTrades = 200;
    private static readonly TimeSpan QuoteFreshnessThreshold = TimeSpan.FromMilliseconds(250);

    private readonly TimeSpan[] _rollingWindows =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(60)
    ];

    public TradeDataCollector(
        IMarketEventPublisher publisher,
        IQuoteStateStore? quotes = null,
        SessionStatsCollector? sessionStats = null)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _quotes = quotes;
        _sessionStats = sessionStats;
    }

    /// <summary>
    /// Validates a symbol format. Valid symbols contain only alphanumeric characters,
    /// dots, hyphens, underscores, colons, or slashes.
    /// </summary>
    /// <param name="symbol">The symbol to validate.</param>
    /// <param name="reason">When validation fails, contains the reason.</param>
    /// <returns>True if valid, false otherwise.</returns>
    private static bool IsValidSymbolFormat(string symbol, out string reason)
    {
        reason = string.Empty;

        if (symbol.Length > MaxSymbolLength)
        {
            reason = $"exceeds maximum length of {MaxSymbolLength} characters";
            return false;
        }

        foreach (char c in symbol)
        {
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '-' && c != '_' && c != ':' && c != '/')
            {
                reason = $"contains invalid character '{c}'";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Entry point from router/adapter layer.
    /// Performs sequence continuity checks, emits Integrity events on anomalies,
    /// emits Trade + OrderFlow events on accepted updates.
    /// </summary>
    public void OnTrade(MarketTradeUpdate update)
    {
        if (update is null)
            throw new ArgumentNullException(nameof(update));
        if (string.IsNullOrWhiteSpace(update.Symbol))
            return;

        var symbol = update.Symbol;
        using var publishActivity = MarketEventIngressTracing.StartCollectorActivity("trade-collector", "trade", symbol);

        // -------- Provenance validation --------
        // The collector is a shared singleton serving every active adapter, so provenance
        // must arrive per event. A sourceless update is rejected loudly rather than being
        // silently attributed to a default vendor; the integrity event itself carries the
        // honest UNKNOWN sentinel because no provider identity exists to stamp.
        if (MarketDataSources.IsMissing(update.Source))
        {
            var integrity = IntegrityEvent.MissingSource(
                update.Timestamp,
                symbol,
                "trade",
                update.SequenceNumber,
                update.StreamId,
                update.Venue);

            _publisher.TryPublish(MarketEvent.Integrity(update.Timestamp, symbol, integrity, MarketDataSources.Unknown));
            return;
        }

        var source = update.Source!;

        // -------- Symbol format validation --------
        if (!IsValidSymbolFormat(symbol, out var symbolValidationReason))
        {
            var integrity = IntegrityEvent.InvalidSymbol(
                update.Timestamp,
                symbol,
                symbolValidationReason,
                update.SequenceNumber,
                update.StreamId,
                update.Venue);

            _publisher.TryPublish(MarketEvent.Integrity(update.Timestamp, symbol, integrity, source));
            return;
        }

        // -------- SequenceNumber bounds validation --------
        var seq = update.SequenceNumber;
        if (seq < 0)
        {
            var integrity = IntegrityEvent.InvalidSequenceNumber(
                update.Timestamp,
                symbol,
                seq,
                "sequence number must be non-negative",
                update.StreamId,
                update.Venue);

            _publisher.TryPublish(MarketEvent.Integrity(update.Timestamp, symbol, integrity, source));
            return;
        }

        var symbolId = new SymbolId(symbol);
        var streamKey = BuildSequenceStreamKey(symbolId, update);
        var state = _stateBySymbol.GetOrAdd(streamKey, _ => new SymbolTradeState(_rollingWindows));

        // -------- Integrity / continuity --------
        // Rules:
        //  - A SequenceNumber of 0 means the provider does not sequence this stream
        //    (repo convention shared with MarketDepthCollector and the quality monitors):
        //    continuity checks are skipped and no sequence is fabricated.
        //  - Otherwise SequenceNumber must be strictly increasing per symbol stream.
        //  - If we detect out-of-order or gap, emit IntegrityEvent.
        //  - For gaps, we still accept the trade (configurable), but flag IsStale in stats.
        //  - For out-of-order or duplicates, we reject the trade (do not advance stats).

        if (seq > 0)
        {
            var sequenceCheck = state.CheckAndAdvanceSequence(seq);
            if (sequenceCheck.IsOutOfOrder)
            {
                var integrity = IntegrityEvent.OutOfOrder(
                    update.Timestamp,
                    symbol,
                    last: sequenceCheck.Last,
                    received: seq,
                    streamId: update.StreamId,
                    venue: update.Venue);

                _publisher.TryPublish(MarketEvent.Integrity(update.Timestamp, symbol, integrity, source));
                return;
            }

            // Gap inference is only meaningful when the provider's sequence domain is dense.
            // Feeds with sparse-but-increasing sequences (e.g. Polygon's per-ticker "q")
            // still get out-of-order/duplicate protection above, but a jump is not data loss.
            if (sequenceCheck.IsGap && update.SequenceIsContiguous)
            {
                var integrity = IntegrityEvent.SequenceGap(
                    update.Timestamp,
                    symbol,
                    expectedNext: sequenceCheck.Expected,
                    received: seq,
                    streamId: update.StreamId,
                    venue: update.Venue);

                _publisher.TryPublish(MarketEvent.Integrity(update.Timestamp, symbol, integrity, source));
            }
        }

        // -------- Aggressor inference (optional) --------
        // If upstream cannot classify aggressor, infer using latest BBO:
        //  - Price >= Ask => Buy
        //  - Price <= Bid => Sell
        //  - Otherwise Unknown
        var aggressor = update.Aggressor;
        if (aggressor == AggressorSide.Unknown && _quotes != null && _quotes.TryGet(symbol, out var bbo) && bbo != null)
        {
            var quoteAge = update.Timestamp - bbo.Timestamp;
            if (quoteAge <= QuoteFreshnessThreshold)
            {
                if (bbo.AskPrice > 0m && update.Price >= bbo.AskPrice)
                    aggressor = AggressorSide.Buy;
                else if (bbo.BidPrice > 0m && update.Price <= bbo.BidPrice)
                    aggressor = AggressorSide.Sell;
            }
        }

        // -------- Trade record --------
        var trade = new Trade(
            Timestamp: update.Timestamp,
            Symbol: symbol,
            Price: update.Price,
            Size: update.Size,
            Aggressor: aggressor,
            SequenceNumber: seq,
            StreamId: update.StreamId,
            Venue: update.Venue,
            RawConditions: update.RawConditions);

        // Combine RegisterTrade + BuildOrderFlowStats into a single lock acquisition
        // to eliminate the double lock overhead (was two separate lock(_sync) calls per trade).
        var stats = state.RegisterTradeAndBuildStats(
            trade: trade,
            timestamp: update.Timestamp,
            symbol: symbol,
            seq: seq,
            streamId: update.StreamId,
            venue: update.Venue);

        // Buffer for API access
        var ring = _recentTrades.GetOrAdd(symbolId, _ => new RecentTradeRing(MaxRecentTrades));
        ring.Add(trade);

        // Update intraday session statistics so the watchlist and quote screens can render
        // day open/high/low/last/volume/change without an extra round trip.
        _sessionStats?.OnTrade(symbol, trade.Price, trade.Size, trade.Timestamp);

        // Stamp the exchange timestamp so latency metrics are available after canonicalization.
        // update.Timestamp is the exchange-reported execution time.
        _publisher.TryPublish(MarketEvent.Trade(trade.Timestamp, trade.Symbol, trade, source)
            .StampReceiveTime(exchangeTs: update.Timestamp));

        // -------- OrderFlow statistics --------
        _publisher.TryPublish(MarketEvent.OrderFlow(update.Timestamp, symbol, stats, source));
    }

    /// <summary>
    /// Returns the most recent trades for a symbol (newest first), up to <paramref name="limit"/>.
    /// Returns an empty list if no trades have been recorded for the symbol.
    /// </summary>
    public IReadOnlyList<Trade> GetRecentTrades(string symbol, int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return Array.Empty<Trade>();
        var key = new SymbolId(symbol);
        if (!_recentTrades.TryGetValue(key, out var ring))
            return Array.Empty<Trade>();
        return ring.GetRecent(Math.Min(limit, MaxRecentTrades));
    }

    /// <summary>
    /// Returns the current rolling order-flow statistics for a symbol, or null if no trades recorded.
    /// </summary>
    public OrderFlowStatistics? GetOrderFlowSnapshot(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return null;
        var symbolId = new SymbolId(symbol);
        var states = _stateBySymbol
            .Where(kvp => kvp.Key.Symbol == symbolId)
            .Select(kvp => kvp.Value)
            .ToArray();
        if (states.Length == 0)
            return null;

        return states[0].BuildOrderFlowStats(
            timestamp: DateTimeOffset.UtcNow,
            symbol: symbol,
            seq: states[0].GetLastSequenceNumber(),
            streamId: null,
            venue: null);
    }

    /// <summary>
    /// Returns all symbols that currently have trade data.
    /// </summary>
    public IReadOnlyList<string> GetTrackedSymbols()
        => _stateBySymbol.Keys
            .Select(k => k.Symbol.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static StreamKey BuildStreamKey(
        SymbolId symbol,
        string? streamId,
        string? venue,
        DateOnly? sequenceSessionDate = null)
        => new(symbol, streamId, venue, sequenceSessionDate);

    private static StreamKey BuildSequenceStreamKey(SymbolId symbol, MarketTradeUpdate update)
        => string.IsNullOrWhiteSpace(update.SequenceStreamId)
            ? BuildStreamKey(symbol, update.StreamId, update.Venue)
            : BuildStreamKey(
                symbol,
                update.SequenceStreamId,
                venue: null,
                update.SequenceSessionDate);

    // =========================
    // Per-symbol state
    // =========================
    private sealed class SymbolTradeState
    {
        private readonly object _sync = new();
        private readonly Queue<Trade> _tradeWindow = new();
        private readonly Dictionary<TimeSpan, RollingWindowState> _rollingByWindow;

        private long? _lastSequenceNumber;

        private int _tradeCount;

        public SymbolTradeState(IEnumerable<TimeSpan> rollingWindows)
        {
            _rollingByWindow = rollingWindows.ToDictionary(w => w, _ => new RollingWindowState());
        }

        public SequenceCheckResult CheckAndAdvanceSequence(long sequenceNumber)
        {
            lock (_sync)
            {
                if (_lastSequenceNumber.HasValue)
                {
                    var last = _lastSequenceNumber.Value;
                    if (sequenceNumber <= last)
                    {
                        return new SequenceCheckResult(true, false, last, last + 1);
                    }

                    var expected = last + 1;
                    if (sequenceNumber > expected)
                    {
                        _lastSequenceNumber = sequenceNumber;
                        return new SequenceCheckResult(false, true, last, expected);
                    }
                }

                _lastSequenceNumber = sequenceNumber;
                return new SequenceCheckResult(false, false, _lastSequenceNumber ?? 0, (_lastSequenceNumber ?? 0) + 1);
            }
        }

        public long GetLastSequenceNumber()
        {
            lock (_sync)
                return _lastSequenceNumber ?? 0;
        }

        public void RegisterTrade(Trade trade)
        {
            lock (_sync)
            {
                _tradeCount++;
                _tradeWindow.Enqueue(trade);
                foreach (var rollingWindow in _rollingByWindow.Values)
                    AddToRollingWindow(rollingWindow, trade);

                TrimRollingWindows(trade.Timestamp);
            }
        }

        /// <summary>
        /// Registers a trade and builds order-flow statistics in a single lock acquisition,
        /// eliminating the double lock overhead of calling <see cref="RegisterTrade"/> followed
        /// by <see cref="BuildOrderFlowStats"/> separately.
        /// </summary>
        public OrderFlowStatistics RegisterTradeAndBuildStats(
            Trade trade,
            DateTimeOffset timestamp,
            string symbol,
            long seq,
            string? streamId,
            string? venue)
        {
            lock (_sync)
            {
                _tradeCount++;
                _tradeWindow.Enqueue(trade);
                foreach (var rollingWindow in _rollingByWindow.Values)
                    AddToRollingWindow(rollingWindow, trade);

                // Single TrimRollingWindows call covers both RegisterTrade and BuildOrderFlowStats.
                // trade.Timestamp == timestamp in the hot path so there is no double-trim.
                TrimRollingWindows(timestamp);

                var activeWindow = _rollingByWindow[TimeSpan.FromSeconds(10)];
                var total = activeWindow.BuyVolume + activeWindow.SellVolume + activeWindow.UnknownVolume;
                var imbalance = total == 0 ? 0m : (decimal)(activeWindow.BuyVolume - activeWindow.SellVolume) / total;
                var vwap = activeWindow.VwapDenominator == 0 ? 0m : activeWindow.VwapNumerator / activeWindow.VwapDenominator;

                return new OrderFlowStatistics(
                    Timestamp: timestamp,
                    Symbol: symbol,
                    BuyVolume: activeWindow.BuyVolume,
                    SellVolume: activeWindow.SellVolume,
                    UnknownVolume: activeWindow.UnknownVolume,
                    VWAP: vwap,
                    Imbalance: imbalance,
                    TradeCount: activeWindow.TradeCount,
                    SequenceNumber: seq,
                    StreamId: streamId,
                    Venue: venue);
            }
        }

        public OrderFlowStatistics BuildOrderFlowStats(
            DateTimeOffset timestamp,
            string symbol,
            long seq,
            string? streamId,
            string? venue)
        {
            lock (_sync)
            {
                TrimRollingWindows(timestamp);

                var activeWindow = _rollingByWindow[TimeSpan.FromSeconds(10)];
                var total = activeWindow.BuyVolume + activeWindow.SellVolume + activeWindow.UnknownVolume;
                var imbalance = total == 0 ? 0m : (decimal)(activeWindow.BuyVolume - activeWindow.SellVolume) / total;
                var vwap = activeWindow.VwapDenominator == 0 ? 0m : activeWindow.VwapNumerator / activeWindow.VwapDenominator;

                return new OrderFlowStatistics(
                    Timestamp: timestamp,
                    Symbol: symbol,
                    BuyVolume: activeWindow.BuyVolume,
                    SellVolume: activeWindow.SellVolume,
                    UnknownVolume: activeWindow.UnknownVolume,
                    VWAP: vwap,
                    Imbalance: imbalance,
                    TradeCount: activeWindow.TradeCount,
                    SequenceNumber: seq,
                    StreamId: streamId,
                    Venue: venue);
            }
        }

        private void TrimRollingWindows(DateTimeOffset now)
        {
            while (_tradeWindow.Count > 0)
            {
                var oldest = _tradeWindow.Peek();
                var shouldPop = false;

                foreach (var (window, state) in _rollingByWindow)
                {
                    if (now - oldest.Timestamp <= window)
                        continue;
                    RemoveFromRollingWindow(state, oldest);
                    shouldPop = true;
                }

                if (!shouldPop)
                    break;
                _tradeWindow.Dequeue();
            }
        }

        private static void AddToRollingWindow(RollingWindowState state, Trade trade)
        {
            state.TradeCount++;
            state.VwapNumerator += trade.Price * trade.Size;
            state.VwapDenominator += trade.Size;
            switch (trade.Aggressor)
            {
                case AggressorSide.Buy:
                    state.BuyVolume += trade.Size;
                    break;
                case AggressorSide.Sell:
                    state.SellVolume += trade.Size;
                    break;
                default:
                    state.UnknownVolume += trade.Size;
                    break;
            }
        }

        private static void RemoveFromRollingWindow(RollingWindowState state, Trade trade)
        {
            state.TradeCount = (ushort)Math.Max(0, state.TradeCount - 1);
            state.VwapNumerator -= trade.Price * trade.Size;
            state.VwapDenominator = Math.Max(0, state.VwapDenominator - trade.Size);
            switch (trade.Aggressor)
            {
                case AggressorSide.Buy:
                    state.BuyVolume = Math.Max(0, state.BuyVolume - trade.Size);
                    break;
                case AggressorSide.Sell:
                    state.SellVolume = Math.Max(0, state.SellVolume - trade.Size);
                    break;
                default:
                    state.UnknownVolume = Math.Max(0, state.UnknownVolume - trade.Size);
                    break;
            }
        }

        internal readonly record struct SequenceCheckResult(bool IsOutOfOrder, bool IsGap, long Last, long Expected);

        private sealed class RollingWindowState
        {
            public long BuyVolume;
            public long SellVolume;
            public long UnknownVolume;
            public decimal VwapNumerator;
            public long VwapDenominator;
            public ushort TradeCount;
        }
    }

    // =========================
    // Recent trade ring buffer
    // =========================

    /// <summary>
    /// Thread-safe fixed-capacity ring buffer for recent trades.
    /// </summary>
    private sealed class RecentTradeRing
    {
        private readonly Trade[] _buffer;
        private readonly object _sync = new();
        private int _head;
        private int _count;

        public RecentTradeRing(int capacity) => _buffer = new Trade[capacity];

        public void Add(Trade trade)
        {
            lock (_sync)
            {
                _buffer[_head] = trade;
                _head = (_head + 1) % _buffer.Length;
                if (_count < _buffer.Length)
                    _count++;
            }
        }

        /// <summary>
        /// Returns up to <paramref name="limit"/> recent trades, newest first.
        /// </summary>
        public IReadOnlyList<Trade> GetRecent(int limit)
        {
            lock (_sync)
            {
                var take = Math.Min(limit, _count);
                var result = new Trade[take];
                for (int i = 0; i < take; i++)
                {
                    // Walk backwards from head
                    var idx = (_head - 1 - i + _buffer.Length) % _buffer.Length;
                    result[i] = _buffer[idx];
                }
                return result;
            }
        }
    }
}

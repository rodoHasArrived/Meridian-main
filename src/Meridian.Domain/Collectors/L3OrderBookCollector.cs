using System.Collections.Concurrent;
using Meridian.Contracts.Domain;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;

namespace Meridian.Domain.Collectors;

/// <summary>
/// Maintains per-symbol L3 order books from individual order lifecycle events
/// and emits both the raw L3 events and derived L2 snapshots (dual-write).
/// </summary>
/// <remarks>
/// Use this collector for providers that supply order-level (ITCH/PITCH-style)
/// message feeds. Providers that only support MBP/L2 deltas should continue
/// using <see cref="MarketDepthCollector"/> instead.
///
/// Dual-write contract: every <c>On*</c> call publishes exactly two events in order —
/// the L3 event followed by a derived <see cref="LOBSnapshot"/> (L2).
/// Downstream consumers that only need L2 can filter by <see cref="MarketEventType.L2Snapshot"/>;
/// consumers that need order-level granularity subscribe to the L3 types (20–24).
/// </remarks>
public sealed class L3OrderBookCollector : SymbolSubscriptionTracker
{
    private readonly IMarketEventPublisher _publisher;
    private readonly string _defaultSource;
    private readonly ConcurrentDictionary<SymbolId, SymbolL3Book> _books = new();
    private const ushort MaxDepth = 50;

    /// <param name="publisher">Publisher to receive both L3 and derived L2 events.</param>
    /// <param name="requireExplicitSubscription">
    /// When <see langword="true"/>, symbols must be registered via
    /// <see cref="SymbolSubscriptionTracker.RegisterSubscription"/> before events are processed.
    /// </param>
    /// <param name="defaultSource">
    /// Source identifier stamped on published <see cref="MarketEvent"/>s, e.g. <c>"NYSE-ITCH"</c>.
    /// Defaults to <c>"IB"</c> for backward-compatibility with the rest of the Domain layer.
    /// </param>
    public L3OrderBookCollector(
        IMarketEventPublisher publisher,
        bool requireExplicitSubscription = true,
        string defaultSource = "IB")
        : base(requireExplicitSubscription)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _defaultSource = string.IsNullOrWhiteSpace(defaultSource) ? "IB" : defaultSource;
    }

    /// <summary>
    /// Returns the current L2 snapshot derived from the L3 book for a symbol,
    /// or null if no book exists for that symbol.
    /// Thread-safe.
    /// </summary>
    public LOBSnapshot? GetCurrentSnapshot(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return null;
        return _books.TryGetValue(new SymbolId(symbol.Trim()), out var book)
            ? book.BuildSnapshot(symbol.Trim())
            : null;
    }

    /// <summary>Returns all symbols that currently have L3 order book data.</summary>
    public IReadOnlyList<string> GetTrackedSymbols()
        => _books.Keys.Select(k => k.Value).ToList();

    /// <summary>
    /// Processes a new-order event: publishes the L3 <see cref="OrderAdd"/> event
    /// followed by a derived L2 snapshot.
    /// </summary>
    public void OnOrderAdd(OrderAdd order)
    {
        if (order is null)
            throw new ArgumentNullException(nameof(order));
        if (string.IsNullOrWhiteSpace(order.Symbol))
            return;

        var symbol = order.Symbol.Trim();
        if (!ShouldProcessUpdate(symbol))
            return;

        var book = _books.GetOrAdd(new SymbolId(symbol), _ => new SymbolL3Book());
        book.Add(order);

        _publisher.TryPublish(MarketEvent.OrderAdd(order.PriorityTimestamp, symbol, order, source: _defaultSource));
        PublishDerivedL2(symbol, order.PriorityTimestamp, order.SequenceNumber, order.StreamId, order.Venue, book);
    }

    /// <summary>
    /// Processes an order-modify event: publishes the L3 <see cref="OrderModify"/> event
    /// followed by a derived L2 snapshot.
    /// </summary>
    public void OnOrderModify(DateTimeOffset ts, string symbol, OrderModify modify)
    {
        if (modify is null)
            throw new ArgumentNullException(nameof(modify));
        if (string.IsNullOrWhiteSpace(symbol))
            return;

        var sym = symbol.Trim();
        if (!ShouldProcessUpdate(sym))
            return;

        var book = _books.GetOrAdd(new SymbolId(sym), _ => new SymbolL3Book());
        book.Modify(modify);

        _publisher.TryPublish(MarketEvent.OrderModify(ts, sym, modify, source: _defaultSource));
        PublishDerivedL2(sym, ts, modify.SequenceNumber, modify.StreamId, modify.Venue, book);
    }

    /// <summary>
    /// Processes an order-cancel event: publishes the L3 <see cref="OrderCancel"/> event
    /// followed by a derived L2 snapshot.
    /// </summary>
    public void OnOrderCancel(DateTimeOffset ts, string symbol, OrderCancel cancel)
    {
        if (cancel is null)
            throw new ArgumentNullException(nameof(cancel));
        if (string.IsNullOrWhiteSpace(symbol))
            return;

        var sym = symbol.Trim();
        if (!ShouldProcessUpdate(sym))
            return;

        var book = _books.GetOrAdd(new SymbolId(sym), _ => new SymbolL3Book());
        book.Cancel(cancel);

        _publisher.TryPublish(MarketEvent.OrderCancel(ts, sym, cancel, source: _defaultSource));
        PublishDerivedL2(sym, ts, cancel.SequenceNumber, cancel.StreamId, cancel.Venue, book);
    }

    /// <summary>
    /// Processes an order-execute event: publishes the L3 <see cref="OrderExecute"/> event
    /// followed by a derived L2 snapshot.
    /// </summary>
    public void OnOrderExecute(DateTimeOffset ts, string symbol, OrderExecute execute)
    {
        if (execute is null)
            throw new ArgumentNullException(nameof(execute));
        if (string.IsNullOrWhiteSpace(symbol))
            return;

        var sym = symbol.Trim();
        if (!ShouldProcessUpdate(sym))
            return;

        var book = _books.GetOrAdd(new SymbolId(sym), _ => new SymbolL3Book());
        book.Execute(execute);

        _publisher.TryPublish(MarketEvent.OrderExecute(ts, sym, execute, source: _defaultSource));
        PublishDerivedL2(sym, ts, execute.SequenceNumber, execute.StreamId, execute.Venue, book);
    }

    /// <summary>
    /// Processes an order-replace event: publishes the L3 <see cref="OrderReplace"/> event
    /// followed by a derived L2 snapshot.
    /// </summary>
    public void OnOrderReplace(DateTimeOffset ts, string symbol, OrderReplace replace)
    {
        if (replace is null)
            throw new ArgumentNullException(nameof(replace));
        if (string.IsNullOrWhiteSpace(symbol))
            return;

        var sym = symbol.Trim();
        if (!ShouldProcessUpdate(sym))
            return;

        var book = _books.GetOrAdd(new SymbolId(sym), _ => new SymbolL3Book());
        book.Replace(replace);

        _publisher.TryPublish(MarketEvent.OrderReplace(ts, sym, replace, source: _defaultSource));
        PublishDerivedL2(sym, ts, replace.SequenceNumber, replace.StreamId, replace.Venue, book);
    }

    private void PublishDerivedL2(string symbol, DateTimeOffset ts, long seq, string? streamId, string? venue, SymbolL3Book book)
    {
        // Always publish an L2 snapshot — even an empty one — so downstream consumers
        // learn that the book has been cleared (e.g. after a full cancel/execute).
        var snapshot = book.BuildSnapshot(symbol, ts, seq, streamId, venue, MaxDepth)
            ?? new LOBSnapshot(ts, symbol, [], [], SequenceNumber: seq, StreamId: streamId, Venue: venue);
        _publisher.TryPublish(MarketEvent.L2Snapshot(ts, symbol, snapshot));
    }

    /// <summary>
    /// Per-symbol L3 order book that maintains individual order state and derives L2 views.
    /// Thread-safe via lock.
    /// </summary>
    internal sealed class SymbolL3Book
    {
        /// <summary>Immutable state for a single resting order.</summary>
        private readonly record struct OrderState(OrderSide Side, decimal Price, long DisplayedSize);

        private readonly object _lock = new();

        // orderId → OrderState
        private readonly Dictionary<string, OrderState> _orders = new(StringComparer.Ordinal);

        internal int OrderCount
        {
            get { lock (_lock) return _orders.Count; }
        }

        public void Add(OrderAdd order)
        {
            lock (_lock)
                _orders[order.OrderId] = new OrderState(order.Side, order.Price, order.DisplayedSize);
        }

        public void Modify(OrderModify modify)
        {
            lock (_lock)
            {
                if (!_orders.TryGetValue(modify.OrderId, out var existing))
                    return;
                _orders[modify.OrderId] = existing with
                {
                    Price = modify.NewPrice ?? existing.Price,
                    DisplayedSize = modify.NewDisplayedSize ?? existing.DisplayedSize
                };
            }
        }

        public void Cancel(OrderCancel cancel)
        {
            lock (_lock)
            {
                if (!_orders.TryGetValue(cancel.OrderId, out var existing))
                    return;
                var remaining = existing.DisplayedSize - cancel.CanceledSize;
                if (remaining <= 0)
                    _orders.Remove(cancel.OrderId);
                else
                    _orders[cancel.OrderId] = existing with { DisplayedSize = remaining };
            }
        }

        public void Execute(OrderExecute execute)
        {
            lock (_lock)
            {
                if (!_orders.TryGetValue(execute.RestingOrderId, out var existing))
                    return;
                var remaining = existing.DisplayedSize - execute.ExecSize;
                if (remaining <= 0)
                    _orders.Remove(execute.RestingOrderId);
                else
                    _orders[execute.RestingOrderId] = existing with { DisplayedSize = remaining };
            }
        }

        public void Replace(OrderReplace replace)
        {
            lock (_lock)
            {
                if (!_orders.TryGetValue(replace.OldOrderId, out var existing))
                    return;
                _orders.Remove(replace.OldOrderId);
                _orders[replace.NewOrderId] = existing with
                {
                    Price = replace.NewPrice ?? existing.Price,
                    DisplayedSize = replace.NewDisplayedSize ?? existing.DisplayedSize
                };
            }
        }

        /// <summary>
        /// Derives an L2 <see cref="LOBSnapshot"/> by aggregating all resting orders at each
        /// price level. Bids are sorted best-to-worst (descending price); asks ascending.
        /// Returns null when the book is empty.
        /// </summary>
        public LOBSnapshot? BuildSnapshot(
            string symbol,
            DateTimeOffset? ts = null,
            long seq = 0,
            string? streamId = null,
            string? venue = null,
            ushort maxDepth = 50)
        {
            OrderState[] snapshot;
            lock (_lock)
            {
                if (_orders.Count == 0)
                    return null;
                snapshot = [.. _orders.Values];
            }

            // Aggregate displayed size per (side, price)
            var bidLevels = new SortedDictionary<decimal, long>(Comparer<decimal>.Create((a, b) => b.CompareTo(a)));
            var askLevels = new SortedDictionary<decimal, long>();

            foreach (var state in snapshot)
            {
                if (state.Side == OrderSide.Buy)
                    bidLevels[state.Price] = (bidLevels.TryGetValue(state.Price, out var b) ? b : 0L) + state.DisplayedSize;
                else if (state.Side == OrderSide.Sell)
                    askLevels[state.Price] = (askLevels.TryGetValue(state.Price, out var a) ? a : 0L) + state.DisplayedSize;
            }

            var bids = bidLevels
                .Take(maxDepth)
                .Select((kv, i) => new OrderBookLevel(OrderBookSide.Bid, (ushort)i, kv.Key, kv.Value))
                .ToArray();

            var asks = askLevels
                .Take(maxDepth)
                .Select((kv, i) => new OrderBookLevel(OrderBookSide.Ask, (ushort)i, kv.Key, kv.Value))
                .ToArray();

            decimal? mid = bids.Length > 0 && asks.Length > 0
                ? (bids[0].Price + asks[0].Price) / 2m
                : null;

            decimal? imb = null;
            if (bids.Length > 0 && asks.Length > 0)
            {
                var b = bids[0].Size;
                var a = asks[0].Size;
                var tot = b + a;
                if (tot > 0)
                    imb = (b - a) / tot;
            }

            return new LOBSnapshot(
                Timestamp: ts ?? DateTimeOffset.UtcNow,
                Symbol: symbol,
                Bids: bids,
                Asks: asks,
                MidPrice: mid,
                Imbalance: imb,
                SequenceNumber: seq,
                StreamId: streamId,
                Venue: venue);
        }
    }
}

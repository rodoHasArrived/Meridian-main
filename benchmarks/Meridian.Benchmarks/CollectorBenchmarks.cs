using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Domain.Models;

namespace Meridian.Benchmarks;

/// <summary>
/// Benchmarks for TradeDataCollector — the hot path that processes every incoming trade tick.
/// Measures sequence validation, rolling window maintenance, and event publishing overhead.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class TradeCollectorBenchmarks
{
    private MarketTradeUpdate[] _updates = null!;
    // Pre-computed single-symbol and gappy arrays are built once in GlobalSetup so that
    // the LINQ + array-copy cost is excluded from the measured benchmark paths.
    private MarketTradeUpdate[] _singleSymbolUpdates = null!;
    private MarketTradeUpdate[] _gappyUpdates = null!;
    private NoOpPublisher _publisher = null!;

    [Params(1000, 10000)]
    public int TradeCount;

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(42);
        var basePrice = 450m;

        _updates = new MarketTradeUpdate[TradeCount];
        for (var i = 0; i < TradeCount; i++)
        {
            basePrice += (decimal)(random.NextDouble() - 0.5) * 0.1m;
            _updates[i] = new MarketTradeUpdate(
                Timestamp: DateTimeOffset.UtcNow.AddMilliseconds(i),
                Symbol: $"SYM{i % 10}",
                Price: basePrice,
                Size: random.Next(100, 5000),
                Aggressor: i % 2 == 0 ? AggressorSide.Buy : AggressorSide.Sell,
                SequenceNumber: i,
                StreamId: "BENCH",
                Venue: "TEST"
            );
        }

        // Pre-build derived arrays to avoid LINQ allocation inside the benchmark methods.
        _singleSymbolUpdates = new MarketTradeUpdate[TradeCount];
        for (var i = 0; i < TradeCount; i++)
            _singleSymbolUpdates[i] = _updates[i] with { Symbol = "SPY" };

        // Gaps every other trade: sequence 0, 2, 4, …
        _gappyUpdates = new MarketTradeUpdate[TradeCount];
        for (var i = 0; i < TradeCount; i++)
            _gappyUpdates[i] = _updates[i] with { SequenceNumber = i * 2 };
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _publisher = new NoOpPublisher();
    }

    /// <summary>
    /// Full TradeDataCollector pipeline: symbol validation, sequence check,
    /// rolling window update, ring buffer add, event publishing.
    /// 10 distinct symbols spread evenly across the update array.
    /// </summary>
    [Benchmark(Baseline = true)]
    public int ProcessTrades_MultiSymbol()
    {
        var collector = new TradeDataCollector(_publisher);
        foreach (var update in _updates)
            collector.OnTrade(update);
        return _publisher.Count;
    }

    /// <summary>
    /// Single symbol — measures lock contention on SymbolTradeState.
    /// All trades go to the same per-symbol lock, making contention maximal.
    /// </summary>
    [Benchmark]
    public int ProcessTrades_SingleSymbol()
    {
        var collector = new TradeDataCollector(_publisher);
        foreach (var update in _singleSymbolUpdates)
            collector.OnTrade(update);
        return _publisher.Count;
    }

    /// <summary>
    /// Measures overhead with sequence gaps (triggers integrity events).
    /// Gaps occur on every other trade (sequence numbers 0, 2, 4, …).
    /// </summary>
    [Benchmark]
    public int ProcessTrades_WithGaps()
    {
        var collector = new TradeDataCollector(_publisher);
        foreach (var update in _gappyUpdates)
            collector.OnTrade(update);
        return _publisher.Count;
    }
}

/// <summary>
/// Benchmarks for MarketDepthCollector — L2 order book maintenance.
/// Measures the cost of insert/update/delete operations on the book
/// and snapshot creation overhead.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class DepthCollectorBenchmarks
{
    private MarketDepthUpdate[] _insertUpdates = null!;
    private MarketDepthUpdate[] _mixedUpdates = null!;
    private NoOpPublisher _publisher = null!;

    [Params(100, 500)]
    public int DepthUpdates;

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(42);

        // Pure inserts to build a book
        _insertUpdates = new MarketDepthUpdate[DepthUpdates];
        for (var i = 0; i < DepthUpdates; i++)
        {
            var side = i % 2 == 0 ? OrderBookSide.Bid : OrderBookSide.Ask;
            var pos = Math.Min(i / 2, 49); // max depth 50
            _insertUpdates[i] = new MarketDepthUpdate(
                Timestamp: DateTimeOffset.UtcNow.AddMilliseconds(i),
                Symbol: "SPY",
                Position: (ushort)(pos > 0 ? random.Next(0, Math.Min(pos, 49)) : 0),
                Operation: DepthOperation.Insert,
                Side: side,
                Price: side == OrderBookSide.Bid ? 450m - pos * 0.01m : 450.01m + pos * 0.01m,
                Size: random.Next(100, 10000),
                SequenceNumber: i + 1,
                StreamId: "BENCH",
                Venue: "TEST"
            );
        }

        // Mixed operations (insert, update, delete)
        _mixedUpdates = new MarketDepthUpdate[DepthUpdates];
        for (var i = 0; i < DepthUpdates; i++)
        {
            var side = i % 2 == 0 ? OrderBookSide.Bid : OrderBookSide.Ask;
            DepthOperation op;
            int pos;

            if (i < 20)
            {
                // First 20: build up the book with inserts
                op = DepthOperation.Insert;
                pos = Math.Min(i / 2, 9);
            }
            else
            {
                // Mix of updates and inserts (avoid deletes that would empty the book)
                op = i % 3 == 0 ? DepthOperation.Update : DepthOperation.Insert;
                pos = op == DepthOperation.Update ? random.Next(0, Math.Min(10, i / 2)) : random.Next(0, Math.Min(10, i / 2) + 1);
            }

            _mixedUpdates[i] = new MarketDepthUpdate(
                Timestamp: DateTimeOffset.UtcNow.AddMilliseconds(i),
                Symbol: "SPY",
                Position: (ushort)pos,
                Operation: op,
                Side: side,
                Price: side == OrderBookSide.Bid ? 450m - pos * 0.01m : 450.01m + pos * 0.01m,
                Size: random.Next(100, 10000),
                SequenceNumber: i + 1,
                StreamId: "BENCH",
                Venue: "TEST"
            );
        }
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _publisher = new NoOpPublisher();
    }

    /// <summary>
    /// Pure insert operations — builds order book from scratch.
    /// Measures insert + reindex + snapshot creation cost.
    /// </summary>
    [Benchmark(Baseline = true)]
    public int InsertOnly()
    {
        var collector = new MarketDepthCollector(_publisher, requireExplicitSubscription: false);
        foreach (var update in _insertUpdates)
            collector.OnDepth(update);
        return _publisher.Count;
    }

    /// <summary>
    /// Mixed insert/update operations — realistic order book maintenance.
    /// </summary>
    [Benchmark]
    public int MixedOperations()
    {
        var collector = new MarketDepthCollector(_publisher, requireExplicitSubscription: false);
        foreach (var update in _mixedUpdates)
            collector.OnDepth(update);
        return _publisher.Count;
    }

    /// <summary>
    /// Snapshot retrieval after building a book — measures the cost of
    /// ToArray() copies and mid/imbalance calculations.
    /// </summary>
    [Benchmark]
    public LOBSnapshot? SnapshotRetrieval()
    {
        var collector = new MarketDepthCollector(_publisher, requireExplicitSubscription: false);
        // Build up the book first
        for (var i = 0; i < Math.Min(40, _insertUpdates.Length); i++)
            collector.OnDepth(_insertUpdates[i]);

        return collector.GetCurrentSnapshot("SPY");
    }
}

/// <summary>
/// Minimal IMarketEventPublisher that counts events without I/O.
/// Isolates collector CPU cost from pipeline/storage.
/// </summary>
/// <remarks>
/// Non-atomic increment is intentional: all collector benchmarks run single-threaded
/// (one collector instance per iteration), so no synchronization is needed and the
/// Interlocked overhead would skew the per-event allocation numbers.
/// </remarks>
internal sealed class NoOpPublisher : IMarketEventPublisher
{
    private int _count;

    public int Count => _count;

    public bool TryPublish(in MarketEvent evt)
    {
        _count++;
        return true;
    }
}

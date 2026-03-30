using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Meridian.Application.Canonicalization;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;

namespace Meridian.Benchmarks;

/// <summary>
/// Benchmarks for the CanonicalizingPublisher hot path.
///
/// <para>
/// BOTTLENECK_REPORT #8 identified that in dual-write mode each event triggers up to
/// 4 <see cref="System.Threading.Interlocked"/> operations:
/// <list type="bullet">
///   <item><c>Interlocked.Increment(ref _dualWriteCount)</c></item>
///   <item><c>Interlocked.Add(ref _totalDurationTicks, elapsed)</c></item>
///   <item><c>Interlocked.Increment(ref _canonicalizedCount)</c></item>
///   <item><c>Interlocked.Increment(ref _skippedCount)</c> (non-pilot path)</item>
/// </list>
/// Each <c>Interlocked</c> call emits a full memory barrier and can cause
/// cache-line bouncing under multi-producer load.
/// </para>
///
/// <para>
/// These benchmarks measure the publisher in three configurations to isolate where
/// the overhead comes from:
/// <list type="number">
///   <item><see cref="TryPublish_CanonicalOnly"/> — single publish + 2 Interlocked ops</item>
///   <item><see cref="TryPublish_DualWrite"/> — raw + canonical publish + 3 Interlocked ops</item>
///   <item><see cref="TryPublish_PilotFilter_Skip"/> — non-pilot skip + 1 Interlocked op (cheapest path)</item>
/// </list>
/// </para>
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class CanonicalizingPublisherBenchmarks
{
    private CanonicalizingPublisher _canonicalOnlyPublisher = null!;
    private CanonicalizingPublisher _dualWritePublisher = null!;
    private CanonicalizingPublisher _pilotFilterPublisher = null!;
    private MarketEvent _tradeEvent = null!;
    private MarketEvent _quoteEvent = null!;

    [GlobalSetup]
    public void Setup()
    {
        var canonicalizer = new PassThroughCanonicalizer();
        var inner = new CountingPublisher();

        // Canonical-only: single publish path (no raw event forwarding)
        _canonicalOnlyPublisher = new CanonicalizingPublisher(
            inner, canonicalizer, dualWrite: false);

        // Dual-write: raw event forwarded first, then canonical — triggers an extra TryPublish
        _dualWritePublisher = new CanonicalizingPublisher(
            inner, canonicalizer, dualWrite: true);

        // Pilot filter with a non-matching symbol — goes through the skip path (cheapest)
        _pilotFilterPublisher = new CanonicalizingPublisher(
            inner, canonicalizer,
            pilotSymbols: new[] { "TSLA" },   // SPY will not match → skip
            dualWrite: true);

        _tradeEvent = MarketEvent.Trade(
            DateTimeOffset.UtcNow,
            "SPY",
            new Trade(
                Timestamp: DateTimeOffset.UtcNow,
                Symbol: "SPY",
                Price: 450.25m,
                Size: 100,
                Aggressor: AggressorSide.Buy,
                SequenceNumber: 12345,
                StreamId: "ALPACA",
                Venue: "NYSE"
            ));

        _quoteEvent = MarketEvent.BboQuote(
            DateTimeOffset.UtcNow,
            "SPY",
            BboQuotePayload.FromUpdate(
                timestamp: DateTimeOffset.UtcNow,
                symbol: "SPY",
                bidPrice: 450.24m,
                bidSize: 200,
                askPrice: 450.26m,
                askSize: 150,
                sequenceNumber: 12345,
                streamId: "ALPACA",
                venue: "NYSE"
            ));
    }

    /// <summary>
    /// Canonical-only path (no dual write):
    /// 1 x TryPublish (inner) + canonicalize + 2 x Interlocked ops.
    /// Baseline for comparing against dual-write overhead.
    /// </summary>
    [Benchmark(Baseline = true)]
    public bool TryPublish_CanonicalOnly()
    {
        return _canonicalOnlyPublisher.TryPublish(in _tradeEvent);
    }

    /// <summary>
    /// Dual-write path: raw event forwarded first, then canonicalized.
    /// 2 x TryPublish (inner) + canonicalize + 3 x Interlocked ops.
    /// Shows extra overhead of the dual-write rollout phase.
    /// </summary>
    [Benchmark]
    public bool TryPublish_DualWrite()
    {
        return _dualWritePublisher.TryPublish(in _tradeEvent);
    }

    /// <summary>
    /// Pilot filter skip: symbol does not match pilot set, so only
    /// 1 x TryPublish (inner) + 1 x Interlocked.Increment is executed.
    /// Cheapest path through the publisher — useful for confirming that
    /// non-pilot events don't pay canonicalization cost.
    /// </summary>
    [Benchmark]
    public bool TryPublish_PilotFilter_Skip()
    {
        return _pilotFilterPublisher.TryPublish(in _tradeEvent);
    }

    /// <summary>
    /// Quote event on the dual-write path — confirms payload type doesn't change
    /// the overhead profile of the Interlocked operations.
    /// </summary>
    [Benchmark]
    public bool TryPublish_DualWrite_Quote()
    {
        return _dualWritePublisher.TryPublish(in _quoteEvent);
    }
}

/// <summary>
/// Benchmarks for a batch of events through CanonicalizingPublisher,
/// simulating a realistic throughput scenario.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class CanonicalizingPublisherThroughputBenchmarks
{
    private CanonicalizingPublisher _canonicalOnlyPublisher = null!;
    private CanonicalizingPublisher _dualWritePublisher = null!;
    private MarketEvent[] _events = null!;

    [Params(1000, 10000)]
    public int EventCount;

    [GlobalSetup]
    public void Setup()
    {
        var canonicalizer = new PassThroughCanonicalizer();
        var inner = new CountingPublisher();

        _canonicalOnlyPublisher = new CanonicalizingPublisher(
            inner, canonicalizer, dualWrite: false);

        _dualWritePublisher = new CanonicalizingPublisher(
            inner, canonicalizer, dualWrite: true);

        var random = new Random(42);
        _events = new MarketEvent[EventCount];
        for (var i = 0; i < EventCount; i++)
        {
            var symbol = $"SYM{i % 50}";
            _events[i] = MarketEvent.Trade(
                DateTimeOffset.UtcNow.AddMilliseconds(i),
                symbol,
                new Trade(
                    Timestamp: DateTimeOffset.UtcNow.AddMilliseconds(i),
                    Symbol: symbol,
                    Price: 100m + (decimal)(random.NextDouble() * 400),
                    Size: random.Next(100, 10000),
                    Aggressor: i % 2 == 0 ? AggressorSide.Buy : AggressorSide.Sell,
                    SequenceNumber: i,
                    StreamId: "BENCH",
                    Venue: "TEST"
                ));
        }
    }

    /// <summary>
    /// Canonical-only batch throughput — baseline with 2 Interlocked ops/event.
    /// </summary>
    [Benchmark(Baseline = true)]
    public int ProcessBatch_CanonicalOnly()
    {
        var count = 0;
        foreach (var evt in _events)
        {
            if (_canonicalOnlyPublisher.TryPublish(in evt))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Dual-write batch throughput — 3 Interlocked ops/event.
    /// Delta vs baseline shows the Interlocked overhead cost.
    /// </summary>
    [Benchmark]
    public int ProcessBatch_DualWrite()
    {
        var count = 0;
        foreach (var evt in _events)
        {
            if (_dualWritePublisher.TryPublish(in evt))
                count++;
        }
        return count;
    }
}

/// <summary>
/// Pass-through canonicalizer that returns the event unchanged.
/// Used to isolate the CanonicalizingPublisher wrapper overhead
/// from actual canonicalization work.
/// </summary>
internal sealed class PassThroughCanonicalizer : IEventCanonicalizer
{
    public MarketEvent Canonicalize(MarketEvent raw, CancellationToken ct = default)
        => raw with { CanonicalizationVersion = 1 };
}

/// <summary>
/// Publisher that counts events atomically without I/O.
/// Thread-safe unlike <see cref="NoOpPublisher"/> so that it can be
/// shared across publisher instances in the same benchmark instance.
/// </summary>
internal sealed class CountingPublisher : IMarketEventPublisher
{
    private long _count;

    public long Count => System.Threading.Interlocked.Read(ref _count);

    public bool TryPublish(in MarketEvent evt)
    {
        System.Threading.Interlocked.Increment(ref _count);
        return true;
    }
}

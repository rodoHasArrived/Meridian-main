using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;

namespace Meridian.Benchmarks;

/// <summary>
/// Benchmarks for the <see cref="PersistentDedupLedger"/> key computation and cache-check hot paths.
/// <para>
/// BOTTLENECK_REPORT.md #1 (P0) identified the original key computation as a 5–6 alloc/event path
/// using string interpolation + <c>Encoding.UTF8.GetBytes</c> + <c>SHA256.HashData</c>.
/// The production implementation now uses a prefix cache, <c>stackalloc</c> buffers, and
/// <see cref="SHA256.TryHashData"/>.  This benchmark retains the legacy path as a historical
/// baseline so the improvement remains measurable over time.
/// </para>
/// <para>
/// Three methods are measured:
/// <list type="bullet">
///   <item><see cref="StringConcatKey_Legacy"/> — original 5-6 alloc path (historical baseline)</item>
///   <item><see cref="ComputeKey_CacheMiss"/> — current production path, first call for this event</item>
///   <item><see cref="IsDuplicate_CacheHit"/> — cache-already-warm lookup only (no SHA256)</item>
/// </list>
/// </para>
/// <para>
/// Run with: <c>dotnet run -c Release -- --filter "*DeduplicationKeyBenchmarks*" --memory --job short</c>
/// </para>
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class DeduplicationKeyBenchmarks
{
    private PersistentDedupLedger _ledger = null!;
    private MarketEvent _tradeEvent = null!;
    private MarketEvent _quoteEvent = null!;

    [Params("Trade", "Quote")]
    public string EventType { get; set; } = "Trade";

    [GlobalSetup]
    public void Setup()
    {
        // Use a temp directory that exists for the ledger path — no real I/O happens
        // during benchmarks because we never call InitializeAsync (no _writer).
        _ledger = new PersistentDedupLedger(Path.Combine(Path.GetTempPath(), "bench_dedup_" + Guid.NewGuid()));

        _tradeEvent = MarketEvent.Trade(
            DateTimeOffset.UtcNow,
            "AAPL",
            new Trade(
                Timestamp: DateTimeOffset.UtcNow,
                Symbol: "AAPL",
                Price: 174.53m,
                Size: 100,
                Aggressor: AggressorSide.Buy,
                SequenceNumber: 1234567,
                StreamId: "ALPACA",
                Venue: "XNAS"));

        _quoteEvent = MarketEvent.BboQuote(
            DateTimeOffset.UtcNow,
            "AAPL",
            new BboQuotePayload(
                Timestamp: DateTimeOffset.UtcNow,
                Symbol: "AAPL",
                BidPrice: 174.52m,
                BidSize: 200,
                AskPrice: 174.54m,
                AskSize: 150,
                MidPrice: 174.53m,
                Spread: 0.02m,
                SequenceNumber: 9876543,
                StreamId: "ALPACA",
                Venue: "XNAS"));
    }

    [IterationSetup(Targets = [nameof(IsDuplicate_CacheHit)])]
    public void WarmCacheForHitBenchmark()
    {
        // Seed the cache so IsDuplicate_CacheHit measures only the lookup, not SHA256
        _ledger.SeedCacheEntry(CurrentEvent());
    }

    [IterationSetup(Targets = [nameof(ComputeKey_CacheMiss)])]
    public void ClearCacheForMissBenchmark()
    {
        // Nothing to do — a new ledger is created in GlobalSetup and the prefix cache
        // will be populated on the first call, not counted here because [IterationSetup]
        // runs once per iteration warmup. The first real iteration is a true cold miss.
    }

    /// <summary>
    /// Historical baseline: original string-concat + <c>SHA256.HashData</c> path from
    /// BOTTLENECK_REPORT.md before the fix.  Allocates ~5-6 objects per call.
    /// </summary>
    [Benchmark(Baseline = true)]
    public string StringConcatKey_Legacy()
    {
        const string source = "ALPACA";
        const string symbol = "AAPL";
        const string type = "Trade";

        // Simulates original prefix computation (1 string alloc)
        var prefix = $"{source}:{symbol}:{type}:";

        // Simulates original identity computation (1 string alloc)
        var identity = $"{1234567L}|{174.53m}|{100}|{AggressorSide.Buy}|XNAS";

        // Original SHA256 path (2+ allocs: GetBytes byte[], HashData byte[])
        var combined = prefix + identity;
        var bytes = Encoding.UTF8.GetBytes(combined);
        var hash = SHA256.HashData(bytes);
        return prefix + Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

    /// <summary>
    /// Current production path: prefix from <c>_prefixCache</c> (GetOrAdd) +
    /// <c>stackalloc</c> SHA256 via <c>SHA256.TryHashData</c>.
    /// Expected allocations: ~1 (the new cache key string on first-ever call for this
    /// (source, symbol, type) triple; zero after the prefix is cached).
    /// </summary>
    [Benchmark]
    public string ComputeKey_CacheMiss()
    {
        return _ledger.ComputeKeyForBenchmark(CurrentEvent());
    }

    /// <summary>
    /// Cache-already-warm path: only <c>ConcurrentDictionary.TryGetValue</c> + ticks
    /// comparison.  Expected managed allocations: zero.
    /// </summary>
    [Benchmark]
    public bool IsDuplicate_CacheHit()
    {
        return _ledger.IsDuplicateCacheCheck(CurrentEvent());
    }

    private MarketEvent CurrentEvent() => EventType == "Trade" ? _tradeEvent : _quoteEvent;
}

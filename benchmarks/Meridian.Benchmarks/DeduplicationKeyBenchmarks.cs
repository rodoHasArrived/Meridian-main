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
/// <see cref="SHA256.TryHashData"/>.
/// </para>
/// <para>
/// Three methods are measured:
/// <list type="bullet">
///   <item><see cref="StringConcatKey_Legacy"/> — original 5-6 alloc path (historical baseline)</item>
///   <item><see cref="ComputeKey_CacheMiss"/> — matrix-driven current production path, cache miss</item>
///   <item><see cref="IsDuplicate_CacheHit"/> — matrix-driven cache-already-warm lookup path</item>
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
    private static readonly MarketEventType[] SupportedEventTypes =
    [
        MarketEventType.Trade,
        MarketEventType.BboQuote,
        MarketEventType.L2Snapshot,
        MarketEventType.OrderFlow,
        MarketEventType.OptionTrade
    ];

    private PersistentDedupLedger _ledger = null!;
    private MarketEvent[] _matrixEvents = [];
    private int _eventCursor;

    [Params(1, 4, 16, 64)]
    public int SymbolCardinality { get; set; } = 16;

    [Params(1, 2, 4, 5)]
    public int EventTypeCardinality { get; set; } = 2;

    [GlobalSetup]
    public void Setup()
    {
        // Use a temp directory that exists for the ledger path — no real I/O happens
        // during benchmarks because we never call InitializeAsync (no _writer).
        _ledger = new PersistentDedupLedger(Path.Combine(Path.GetTempPath(), "bench_dedup_" + Guid.NewGuid()));

        _matrixEvents = BuildEventMatrix(SymbolCardinality, EventTypeCardinality);
        _eventCursor = 0;
    }

    [IterationSetup(Targets = [nameof(IsDuplicate_CacheHit)])]
    public void WarmCacheForHitBenchmark()
    {
        // Seed every matrix event so IsDuplicate_CacheHit measures cache hit lookup only.
        for (var i = 0; i < _matrixEvents.Length; i++)
        {
            _ledger.SeedCacheEntry(_matrixEvents[i]);
        }

        _eventCursor = 0;
    }

    [IterationSetup(Targets = [nameof(ComputeKey_CacheMiss)])]
    public void ClearCacheForMissBenchmark()
    {
        // Miss-path benchmark uses ComputeKeyForBenchmark() directly, which is cache-independent.
        _eventCursor = 0;
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
    /// Production path: matrix-driven cache-miss simulation for key computation.
    /// Includes type/symbol cardinality mix while preserving the production control flow.
    /// </summary>
    [Benchmark]
    public string ComputeKey_CacheMiss()
    {
        return _ledger.ComputeKeyForBenchmark(CurrentEvent());
    }

    /// <summary>
    /// Matrix-driven cache-already-warm path: only <c>ConcurrentDictionary.TryGetValue</c> + ticks
    /// comparison.  Expected managed allocations: zero.
    /// </summary>
    [Benchmark]
    public bool IsDuplicate_CacheHit()
    {
        return _ledger.IsDuplicateCacheCheck(CurrentEvent());
    }

    private MarketEvent CurrentEvent()
    {
        if (_matrixEvents.Length == 0)
        {
            return BuildEventMatrix(1, 1)[0];
        }

        var index = _eventCursor;
        _eventCursor = index + 1;
        if (_eventCursor >= _matrixEvents.Length)
        {
            _eventCursor = 0;
        }

        return _matrixEvents[index];
    }

    private static MarketEvent[] BuildEventMatrix(int symbolCount, int typeCardinality)
    {
        var safeSymbolCount = Math.Max(1, symbolCount);
        var safeTypeCount = Math.Clamp(typeCardinality, 1, SupportedEventTypes.Length);

        var events = new MarketEvent[safeSymbolCount * safeTypeCount];
        var start = new DateTimeOffset(2026, 1, 1, 9, 30, 0, TimeSpan.Zero);
        var eventIndex = 0;
        var sequence = 1L;

        for (var typeIndex = 0; typeIndex < safeTypeCount; typeIndex++)
        {
            var eventType = SupportedEventTypes[typeIndex];
            for (var symbolIndex = 0; symbolIndex < safeSymbolCount; symbolIndex++)
            {
                var symbol = SymbolForIndex(symbolIndex);
                var source = SourceForIndex(typeIndex + symbolIndex);
                events[eventIndex++] = BuildEvent(
                    eventType,
                    symbol,
                    start.AddTicks(sequence),
                    sequence,
                    source);

                sequence++;
            }
        }

        return events;
    }

    private static string SymbolForIndex(int index)
        => index switch
        {
            0 => "AAPL",
            1 => "MSFT",
            2 => "SPY",
            3 => "QQQ",
            4 => "NVDA",
            5 => "META",
            6 => "AMD",
            7 => "TSLA",
            8 => "AMZN",
            9 => "GOOG",
            10 => "NFLX",
            11 => "INTC",
            12 => "CRM",
            13 => "ADBE",
            14 => "AVGO",
            15 => "ORCL",
            _ => $"SYM{index:D4}"
        };

    private static string SourceForIndex(int index)
    {
        var sourceIndex = index % 3;

        return sourceIndex switch
        {
            0 => "ALPACA",
            1 => "IBKR",
            _ => "ROBINHOOD"
        };
    }

    private static MarketEvent BuildEvent(MarketEventType type, string symbol, DateTimeOffset timestamp, long sequence, string source)
    {
        return type switch
        {
            MarketEventType.BboQuote => MarketEvent.BboQuote(
                timestamp,
                symbol,
                new BboQuotePayload(
                    Timestamp: timestamp,
                    Symbol: symbol,
                    BidPrice: 100m,
                    BidSize: 100,
                    AskPrice: 100.05m,
                    AskSize: 150,
                    MidPrice: 100.025m,
                    Spread: 0.05m,
                    SequenceNumber: sequence,
                    StreamId: source,
                    Venue: "XNAS"), source: "BENCH"),
            MarketEventType.L2Snapshot => MarketEvent.L2Snapshot(
                timestamp,
                symbol,
                BuildSnapshot(timestamp, symbol, sequence, source),
                source,
                sequence),
            MarketEventType.OrderFlow => MarketEvent.OrderFlow(
                timestamp,
                symbol,
                new OrderFlowStatistics(
                    Timestamp: timestamp,
                    Symbol: symbol,
                    BuyVolume: sequence * 2,
                    SellVolume: sequence,
                    UnknownVolume: 0,
                    VWAP: 100.01m,
                    Imbalance: 0m,
                    TradeCount: 1,
                    SequenceNumber: sequence,
                    StreamId: source,
                    Venue: "XNAS"),
                source,
                sequence),
            MarketEventType.OptionTrade => MarketEvent.OptionTrade(
                timestamp,
                symbol,
                new OptionTrade(
                    Timestamp: timestamp,
                    Symbol: symbol,
                    Contract: new OptionContractSpec(
                        UnderlyingSymbol: symbol,
                        Strike: 100m,
                        Expiration: new DateOnly(2026, 12, 31),
                        Right: OptionRight.Call),
                    Price: 1.23m,
                    Size: 10,
                    Aggressor: AggressorSide.Buy,
                    UnderlyingPrice: 150m,
                    SequenceNumber: sequence,
                    Source: source),
                source,
                sequence),
            _ => MarketEvent.Trade(
                timestamp,
                symbol,
                new Trade(
                    Timestamp: timestamp,
                    Symbol: symbol,
                    Price: 100.10m,
                    Size: 100,
                    Aggressor: AggressorSide.Buy,
                    SequenceNumber: sequence,
                    StreamId: source,
                    Venue: "XNAS"),
                source,
                sequence)
        };
    }

    private static LOBSnapshot BuildSnapshot(
        DateTimeOffset timestamp,
        string symbol,
        long sequence,
        string source)
    {
        var bids = new OrderBookLevel[]
        {
            new(OrderBookSide.Bid, 0, 99.95m, 100m, "MM"),
            new(OrderBookSide.Bid, 1, 99.90m, 200m, "MM")
        };

        var asks = new OrderBookLevel[]
        {
            new(OrderBookSide.Ask, 0, 100.05m, 90m, "MM"),
            new(OrderBookSide.Ask, 1, 100.10m, 180m, "MM")
        };

        return new LOBSnapshot(
            Timestamp: timestamp,
            Symbol: symbol,
            Bids: bids,
            Asks: asks,
            MidPrice: 100.00m,
            MicroPrice: 100.00m,
            Imbalance: 0m,
            SequenceNumber: sequence,
            StreamId: source,
            Venue: "XNAS");
    }
}

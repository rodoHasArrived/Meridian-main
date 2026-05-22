using System.IO;
using Meridian.Application.Pipeline;
using Meridian.Application.Serialization;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Storage.Archival;
using Xunit;

namespace Meridian.Tests.Performance;

/// <summary>
/// Allocation-budget integration tests that measure per-event managed heap allocations
/// using <see cref="GC.GetAllocatedBytesForCurrentThread"/>.
/// <para>
/// These tests run in the <c>"PerformanceSolo"</c> xUnit collection
/// (<see cref="PerformanceSoloCollection"/>), which disables parallelization so that
/// background-thread GC activity cannot inflate the per-thread allocation measurements.
/// </para>
/// <para>
/// Budget values come from <c>benchmarks/Meridian.Benchmarks/Budget/PerformanceBudgetRegistry.cs</c>
/// (source of truth). The same values are enforced by <c>validate_budget.py</c> in CI.
/// </para>
/// <para>
/// Run in isolation:
/// <code>dotnet test tests/Meridian.Tests --filter "Category=Performance" -c Release /p:EnableWindowsTargeting=true</code>
/// </para>
/// </summary>
[Collection("PerformanceSolo")]
[Trait("Category", "Performance")]
public sealed class AllocationBudgetIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private PersistentDedupLedger? _ledger;
    private static readonly MarketEventType[] SupportedEventTypes =
    [
        MarketEventType.Trade,
        MarketEventType.BboQuote,
        MarketEventType.L2Snapshot,
        MarketEventType.OrderFlow,
        MarketEventType.OptionTrade
    ];

    // -----------------------------------------------------------------------
    // Budget constants — must stay in sync with PerformanceBudgetRegistry.cs
    // in benchmarks/Meridian.Benchmarks/Budget/.
    // Do NOT auto-tighten these. See blueprint for rationale.
    // -----------------------------------------------------------------------
    private const long DedupCacheHitMaxBytes = 0;
    private const long DedupCacheMissMaxBytes = 256;
    private const long WalChecksumSmallMaxBytes = 0;
    private const long WalChecksumMediumMaxBytes = 0;
    private const long WalChecksumLargeMaxBytes = 1024;
    private const long AlpacaParseTradeSourceGeneratedMaxBytes = 512;
    private const long AlpacaParseQuoteSourceGeneratedMaxBytes = 640;

    public AllocationBudgetIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"alloc_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        // Pre-JIT the WAL checksum path (including the SIMD UTF-8 encoding path
        // that triggers ~120 bytes of one-time lazy-init for medium-sized strings)
        // before any allocation measurement window opens.
        WriteAheadLog.WarmChecksumPath();
    }

    public void Dispose()
    {
        _ledger?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // -----------------------------------------------------------------------
    // DedupLedger — cache-hit path
    // -----------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Performance")]
    public void DedupLedger_CacheHit_Matrix_AllocatesWithinBudget()
    {
        // Arrange
        const int symbolCardinality = 16;
        const int eventTypeCardinality = 4;
        _ledger = new PersistentDedupLedger(_tempDir);
        var events = BuildEventMatrix(symbolCardinality, eventTypeCardinality);
        foreach (var evt in events)
        {
            _ledger.SeedCacheEntry(evt);
        }

        ForceGc();

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        foreach (var evt in events)
        {
            _ = _ledger.IsDuplicateCacheCheck(evt);
        }

        var after = GC.GetAllocatedBytesForCurrentThread();
        var allocatedPerEvent = (after - before) / (double)events.Length;

        // Assert
        Assert.True(
            allocatedPerEvent <= DedupCacheHitMaxBytes,
            $"DedupLedger matrix cache-hit allocated {allocatedPerEvent:N0} avg bytes/event over {events.Length} events; budget is {DedupCacheHitMaxBytes} bytes/event. " +
            $"The IsDuplicateCacheCheck() path must not allocate any managed objects.");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void DedupLedger_CacheHit_AllocatesWithinBudget()
    {
        // Arrange
        _ledger = new PersistentDedupLedger(_tempDir);
        var evt = BuildTradeEvent();
        _ledger.SeedCacheEntry(evt);                       // warm cache without measuring
        ForceGc();

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = _ledger.IsDuplicateCacheCheck(evt);
        var after = GC.GetAllocatedBytesForCurrentThread();

        // Assert
        var allocated = after - before;
        Assert.True(
            allocated <= DedupCacheHitMaxBytes,
            $"DedupLedger cache-hit allocated {allocated} bytes; budget is {DedupCacheHitMaxBytes} bytes. " +
            $"The IsDuplicateCacheCheck() path must not allocate any managed objects.");
    }

    // -----------------------------------------------------------------------
    // DedupLedger — cache-miss key computation
    // -----------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Performance")]
    public void DedupLedger_CacheMiss_Matrix_AllocatesWithinBudget()
    {
        // Arrange
        const int symbolCardinality = 16;
        const int eventTypeCardinality = 4;
        _ledger = new PersistentDedupLedger(_tempDir);
        var events = BuildEventMatrix(symbolCardinality, eventTypeCardinality);
        ForceGc();

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        foreach (var evt in events)
        {
            _ = _ledger.ComputeKeyForBenchmark(evt);
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        // Assert
        var allocated = after - before;
        var allocatedPerEvent = allocated / (double)events.Length;
        Assert.True(
            allocatedPerEvent <= DedupCacheMissMaxBytes,
            $"DedupLedger matrix cache-miss allocated {allocatedPerEvent:N0} avg bytes/event over {events.Length} events; budget is {DedupCacheMissMaxBytes} bytes/event.");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void DedupLedger_CacheMiss_AllocatesWithinBudget()
    {
        // Arrange
        _ledger = new PersistentDedupLedger(_tempDir);
        var evt = BuildTradeEvent();
        ForceGc();

        // Act — first call computes key from scratch (SHA256 + prefix cache population)
        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = _ledger.ComputeKeyForBenchmark(evt);
        var after = GC.GetAllocatedBytesForCurrentThread();

        // Assert
        var allocated = after - before;
        Assert.True(
            allocated <= DedupCacheMissMaxBytes,
            $"DedupLedger cache-miss allocated {allocated} bytes; budget is {DedupCacheMissMaxBytes} bytes. " +
            $"The prefix-cache + SHA256.TryHashData path should produce at most one short string allocation.");
    }

    // -----------------------------------------------------------------------
    // WAL checksum — small payload (stackalloc path, zero allocs)
    // -----------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Performance")]
    public void WalChecksum_Small_AllocatesWithinBudget()
    {
        // Arrange — 64-byte payload: well within the 1024-byte stackalloc threshold
        var payload = new string('x', 64);
        ForceGc();

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = WriteAheadLog.ComputeChecksumForBenchmark(
            sequence: 1,
            timestamp: DateTime.UtcNow,
            recordType: "Trade",
            payload: payload);
        var after = GC.GetAllocatedBytesForCurrentThread();

        // Assert
        var allocated = after - before;
        Assert.True(
            allocated <= WalChecksumSmallMaxBytes,
            $"WalChecksum_Small allocated {allocated} bytes; budget is {WalChecksumSmallMaxBytes} bytes. " +
            $"The stackalloc path for ≤512-byte payloads must produce zero managed allocations.");
    }

    // -----------------------------------------------------------------------
    // WAL checksum — medium payload (~1 KB, still stackalloc)
    // -----------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Performance")]
    public void WalChecksum_Medium_AllocatesWithinBudget()
    {
        // Arrange — 900-byte payload: within the 1024-byte stackalloc limit
        var payload = new string('x', 900);
        ForceGc();

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = WriteAheadLog.ComputeChecksumForBenchmark(
            sequence: 2,
            timestamp: DateTime.UtcNow,
            recordType: "L2Snapshot",
            payload: payload);
        var after = GC.GetAllocatedBytesForCurrentThread();

        // Assert
        var allocated = after - before;
        Assert.True(
            allocated <= WalChecksumMediumMaxBytes,
            $"WalChecksum_Medium allocated {allocated} bytes; budget is {WalChecksumMediumMaxBytes} bytes. " +
            $"Payloads ≤1024 bytes use stackalloc and must produce zero managed allocations.");
    }

    // -----------------------------------------------------------------------
    // WAL checksum — large payload (ArrayPool rent path)
    // -----------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Performance")]
    public void WalChecksum_Large_AllocatesWithinBudget()
    {
        // Arrange — 4096-byte payload: exceeds the 1024-byte threshold, triggers ArrayPool.Rent
        var payload = new string('x', 4096);
        ForceGc();

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = WriteAheadLog.ComputeChecksumForBenchmark(
            sequence: 3,
            timestamp: DateTime.UtcNow,
            recordType: "L2Snapshot",
            payload: payload);
        var after = GC.GetAllocatedBytesForCurrentThread();

        // Assert
        var allocated = after - before;
        Assert.True(
            allocated <= WalChecksumLargeMaxBytes,
            $"WalChecksum_Large allocated {allocated} bytes; budget is {WalChecksumLargeMaxBytes} bytes. " +
            $"ArrayPool.Rent should not materially increase managed heap allocations beyond the array header.");
    }

    // -----------------------------------------------------------------------
    // Alpaca wire-message parse — source-generated System.Text.Json context
    // -----------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Performance")]
    public void AlpacaParse_Trade_SourceGenerated_AllocatesWithinBudget()
    {
        // Arrange — matches JsonParsingBenchmarks.ParseTrade_SourceGenerated.
        var json = """
            {"T":"t","S":"SPY","p":450.25,"s":100,"t":"2024-01-15T14:30:00Z","x":"NYSE","i":12345}
            """u8.ToArray();
        _ = HighPerformanceJson.ParseAlpacaTrade(json);
        ForceGc();

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = HighPerformanceJson.ParseAlpacaTrade(json);
        var after = GC.GetAllocatedBytesForCurrentThread();

        // Assert
        var allocated = after - before;
        Assert.True(
            allocated <= AlpacaParseTradeSourceGeneratedMaxBytes,
            $"AlpacaParse_Trade_SourceGenerated allocated {allocated} bytes; budget is {AlpacaParseTradeSourceGeneratedMaxBytes} bytes. " +
            $"The source-generated Alpaca trade parser should stay near the 20260521 benchmark result of 384 B/op.");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void AlpacaParse_Quote_SourceGenerated_AllocatesWithinBudget()
    {
        // Arrange — matches JsonParsingBenchmarks.ParseQuote_SourceGenerated.
        var json = """
            {"T":"q","S":"SPY","bp":450.24,"bs":200,"ap":450.26,"as":150,"t":"2024-01-15T14:30:00Z","bx":"NYSE","ax":"ARCA"}
            """u8.ToArray();
        _ = HighPerformanceJson.ParseAlpacaQuote(json);
        ForceGc();

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = HighPerformanceJson.ParseAlpacaQuote(json);
        var after = GC.GetAllocatedBytesForCurrentThread();

        // Assert
        var allocated = after - before;
        Assert.True(
            allocated <= AlpacaParseQuoteSourceGeneratedMaxBytes,
            $"AlpacaParse_Quote_SourceGenerated allocated {allocated} bytes; budget is {AlpacaParseQuoteSourceGeneratedMaxBytes} bytes. " +
            $"The source-generated Alpaca quote parser should stay near the 20260521 benchmark result of 472 B/op.");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void ForceGc()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
    }

    private static MarketEvent BuildTradeEvent() =>
        MarketEvent.Trade(
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
        => index % 3 switch
        {
            0 => "ALPACA",
            1 => "IBKR",
            _ => "ROBINHOOD"
        };

    private static MarketEvent BuildEvent(
        MarketEventType type,
        string symbol,
        DateTimeOffset timestamp,
        long sequence,
        string source)
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
                    Venue: "XNAS")),
            MarketEventType.L2Snapshot => MarketEvent.L2Snapshot(
                timestamp,
                symbol,
                BuildSnapshot(timestamp, symbol, sequence, source),
                sequence,
                source),
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
                sequence,
                source),
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
                sequence,
                source),
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
                sequence,
                source)
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

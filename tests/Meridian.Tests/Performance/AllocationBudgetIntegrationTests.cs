using System.IO;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
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

    // P1 combined-lock fix budgets (BOTTLENECK_REPORT.md P1 items)
    private const long TradeCollectorPerTradeMaxBytes = 1024;
    private const long DepthCollectorSnapshot10LevelsMaxBytes = 2048;

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
    // TradeDataCollector — per-trade combined lock (P1 fix)
    // -----------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Performance")]
    public void TradeCollector_PerTrade_AllocatesWithinBudget()
    {
        // Arrange — pre-warm the collector with one trade so that SymbolTradeState
        // is already allocated for "SPY" before the measurement window opens.
        var publisher = new CountingPublisher();
        var collector = new TradeDataCollector(publisher);
        var warmUpdate = BuildTradeUpdate("SPY", sequence: 1);
        collector.OnTrade(warmUpdate);
        ForceGc();

        // Hot-path update (cache-warm: SymbolTradeState already exists)
        var hotUpdate = BuildTradeUpdate("SPY", sequence: 2);

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        collector.OnTrade(hotUpdate);
        var after = GC.GetAllocatedBytesForCurrentThread();

        // Assert — only essential domain objects (Trade + 2 × MarketEvent);
        // no extra allocations from the combined RegisterTradeAndBuildStats lock.
        var allocated = after - before;
        Assert.True(
            allocated <= TradeCollectorPerTradeMaxBytes,
            $"TradeCollector per-trade hot path allocated {allocated} bytes; budget is {TradeCollectorPerTradeMaxBytes} bytes. " +
            $"The combined RegisterTradeAndBuildStats lock should not add allocations beyond the Trade and MarketEvent domain objects.");

        // Verify functional correctness: each OnTrade call emits Trade + OrderFlow = 2 events each.
        Assert.Equal(4, publisher.Count);
    }

    // -----------------------------------------------------------------------
    // MarketDepthCollector — snapshot creation with ArrayPool (P1 fix)
    // -----------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Performance")]
    public void DepthCollector_Snapshot_AllocatesWithinBudget()
    {
        // Arrange — build a 10-level book so that the snapshot allocates 2 × OrderBookLevel[10]
        var publisher = new CountingPublisher();
        var collector = new MarketDepthCollector(publisher, requireExplicitSubscription: false);

        // Use a single monotonic sequence counter shared across both sides so the
        // sequence-gap validator (which rejects jumps > 1) does not reject any update.
        var seq = 0;
        for (var i = 0; i < 10; i++)
        {
            collector.OnDepth(new MarketDepthUpdate(
                Timestamp: DateTimeOffset.UtcNow,
                Symbol: "SPY",
                Position: (ushort)i,
                Operation: DepthOperation.Insert,
                Side: OrderBookSide.Bid,
                Price: 450m - i * 0.01m,
                Size: 1000,
                SequenceNumber: ++seq,
                StreamId: "BENCH",
                Venue: "TEST"));

            collector.OnDepth(new MarketDepthUpdate(
                Timestamp: DateTimeOffset.UtcNow,
                Symbol: "SPY",
                Position: (ushort)i,
                Operation: DepthOperation.Insert,
                Side: OrderBookSide.Ask,
                Price: 450.01m + i * 0.01m,
                Size: 1000,
                SequenceNumber: ++seq,
                StreamId: "BENCH",
                Venue: "TEST"));
        }

        ForceGc();

        // Act — measure a single snapshot retrieval; ArrayPool.Rent/Return should
        // not appear as managed allocations.
        var before = GC.GetAllocatedBytesForCurrentThread();
        var snapshot = collector.GetCurrentSnapshot("SPY");
        var after = GC.GetAllocatedBytesForCurrentThread();

        // Assert
        Assert.NotNull(snapshot);
        Assert.Equal(10, snapshot.Bids.Count);
        Assert.Equal(10, snapshot.Asks.Count);
        var allocated = after - before;
        Assert.True(
            allocated <= DepthCollectorSnapshot10LevelsMaxBytes,
            $"DepthCollector 10-level snapshot allocated {allocated} bytes; budget is {DepthCollectorSnapshot10LevelsMaxBytes} bytes. " +
            $"Only the LOBSnapshot record and its two OrderBookLevel[] arrays should be present (no ArrayPool overhead).");
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

    private static MarketTradeUpdate BuildTradeUpdate(string symbol, long sequence) =>
        new MarketTradeUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            Price: 450m,
            Size: 100,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: sequence,
            StreamId: "BENCH",
            Venue: "TEST");

    /// <summary>
    /// Minimal <see cref="IMarketEventPublisher"/> that only increments a counter.
    /// Used in allocation tests to isolate the collector's own allocations from
    /// any publisher-side overhead.
    /// </summary>
    private sealed class CountingPublisher : IMarketEventPublisher
    {
        public int Count { get; private set; }

        public bool TryPublish(in MarketEvent evt)
        {
            Count++;
            return true;
        }
    }
}

using System.IO;
using Meridian.Application.Pipeline;
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
}

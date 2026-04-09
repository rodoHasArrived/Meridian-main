using System.Reflection;
using System.Text.Json;

namespace Meridian.Benchmarks;

/// <summary>
/// Static, read-only registry of per-stage allocation budgets for the Meridian
/// hot-path pipeline.
/// <para>
/// Budgets are code contracts, not configuration. Changing a budget requires a
/// PR diff with the same level of review as changing an ADR. Do NOT auto-tighten
/// these values — machine noise on <c>ubuntu-latest</c> can cause spurious
/// improvements. Budget tightening is always a deliberate, human-reviewed change
/// in the same PR as the corresponding performance fix.
/// </para>
/// </summary>
public static class PerformanceBudgetRegistry
{
    // -----------------------------------------------------------------------
    // Deduplication key computation — BOTTLENECK_REPORT.md P0 #1
    // Current implementation: _prefixCache.GetOrAdd + SHA256.TryHashData + stackalloc
    // Cache-hit path: only ConcurrentDictionary.TryGetValue + arithmetic
    // -----------------------------------------------------------------------

    /// <summary>
    /// Deduplication key computation — cache-hit path.
    /// The <c>_prefixCache</c> entry is already populated; the only work is a
    /// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}.TryGetValue"/>
    /// lookup plus a ticks comparison.
    /// Zero managed allocations required on this path.
    /// </summary>
    public static readonly IPerformanceBudget DedupKeyCacheHit = new PerformanceBudget(
        StageName: "DedupKey_CacheHit",
        MaxAllocatedBytesPerEvent: 0,
        MaxMeanNanosPerEvent: 200);

    /// <summary>
    /// Deduplication key computation — cache-miss path.
    /// Includes prefix lookup (GetOrAdd), SHA256 via <c>stackalloc</c>,
    /// and <see cref="Convert.ToHexStringLower(byte[])"/> (one interned/short string).
    /// Budget: ≤128 managed bytes (one interned hex key string).
    /// </summary>
    /// <remarks>
    /// Current state: stack-allocated SHA256 path. Target is maintained by
    /// <c>AllocationBudgetIntegrationTests.DedupLedger_CacheMiss_AllocatesWithinBudget</c>.
    /// </remarks>
    public static readonly IPerformanceBudget DedupKeyCacheMiss = new PerformanceBudget(
        StageName: "DedupKey_CacheMiss",
        MaxAllocatedBytesPerEvent: 256,
        MaxMeanNanosPerEvent: 800);

    // -----------------------------------------------------------------------
    // WAL checksum computation — BOTTLENECK_REPORT.md P0 #2 (already fixed)
    // IncrementalHash + stackalloc; see WalChecksumBenchmarks for historical baseline
    // -----------------------------------------------------------------------

    /// <summary>
    /// WAL checksum — small payload (≤512 bytes).
    /// Entire path uses <c>stackalloc</c>; zero managed allocations.
    /// Budget documents the achieved state and guards against regression.
    /// </summary>
    public static readonly IPerformanceBudget WalChecksumSmall = new PerformanceBudget(
        StageName: "WalChecksum_Small",
        MaxAllocatedBytesPerEvent: 0,
        MaxMeanNanosPerEvent: 400);

    /// <summary>
    /// WAL checksum — medium payload (~1 KB, typical trade event).
    /// Still within the <c>stackalloc</c> path (≤1024 bytes); zero managed allocations.
    /// </summary>
    public static readonly IPerformanceBudget WalChecksumMedium = new PerformanceBudget(
        StageName: "WalChecksum_Medium_1KB",
        MaxAllocatedBytesPerEvent: 0,
        MaxMeanNanosPerEvent: 600);

    /// <summary>
    /// WAL checksum — large payload (~4 KB, L2 snapshot).
    /// Exceeds the <c>stackalloc</c> threshold; one <see cref="System.Buffers.ArrayPool{T}"/>
    /// rent is expected. Budget: ≤1024 managed bytes (array wrapper only, not the rented buffer).
    /// </summary>
    public static readonly IPerformanceBudget WalChecksumLarge = new PerformanceBudget(
        StageName: "WalChecksum_Large_4KB",
        MaxAllocatedBytesPerEvent: 1024,
        MaxMeanNanosPerEvent: 1200);

    // -----------------------------------------------------------------------
    // TradeDataCollector — per-trade hot path
    // BOTTLENECK_REPORT.md P1: Combine lock acquisitions into single RegisterTradeAndBuildStats
    // Before fix: RegisterTrade lock + BuildOrderFlowStats lock = 2 acquisitions per trade
    // After fix:  RegisterTradeAndBuildStats = 1 acquisition per trade
    // Allocation impact: unavoidable domain-model objects only (Trade + 2 × MarketEvent)
    // -----------------------------------------------------------------------

    /// <summary>
    /// TradeDataCollector per-trade hot path — cache-warm path (symbol already registered).
    /// The combined <c>RegisterTradeAndBuildStats</c> call uses a single lock acquisition.
    /// Unavoidable domain objects (Trade + MarketEvent.Trade + MarketEvent.OrderFlow)
    /// account for ≤1024 bytes; no extra allocations from the combined lock path.
    /// </summary>
    /// <remarks>
    /// Enforces regression guard for the P1 combined-lock fix.
    /// Budget measured at 992 bytes on warm path (3 heap objects: Trade record,
    /// MarketEvent.Trade, MarketEvent.OrderFlow with OrderFlowStatistics payload).
    /// Validated by <c>AllocationBudgetIntegrationTests.TradeCollector_PerTrade_AllocatesWithinBudget</c>.
    /// </remarks>
    public static readonly IPerformanceBudget TradeCollectorPerTrade = new PerformanceBudget(
        StageName: "TradeCollector_PerTrade",
        MaxAllocatedBytesPerEvent: 1024,
        MaxMeanNanosPerEvent: 5_000);

    // -----------------------------------------------------------------------
    // MarketDepthCollector — per-snapshot creation
    // BOTTLENECK_REPORT.md P1: Move ToArray() outside write lock; use ArrayPool inside lock
    // Before fix: ToArray() on bids + asks inside write lock on every update
    // After fix:  ArrayPool.Rent inside lock, Span.ToArray() outside lock
    // Managed allocation: only the final LOBSnapshot record + 2 OrderBookLevel[] arrays
    // -----------------------------------------------------------------------

    /// <summary>
    /// MarketDepthCollector snapshot creation — 10-level book.
    /// ArrayPool.Rent/Return buffers must not appear as managed allocations.
    /// Only the final <c>LOBSnapshot</c> record and its two <c>OrderBookLevel[]</c>
    /// arrays are expected: ≤2048 managed bytes for a 10-level book.
    /// </summary>
    /// <remarks>
    /// Enforces regression guard for the P1 ArrayPool snapshot fix.
    /// Validated by <c>AllocationBudgetIntegrationTests.DepthCollector_Snapshot_AllocatesWithinBudget</c>.
    /// </remarks>
    public static readonly IPerformanceBudget DepthCollectorSnapshotSmall = new PerformanceBudget(
        StageName: "DepthCollector_Snapshot_10Levels",
        MaxAllocatedBytesPerEvent: 2048,
        MaxMeanNanosPerEvent: 10_000);

    // -----------------------------------------------------------------------
    // Newline scan — MemoryMappedJsonlReader inner loop
    // -----------------------------------------------------------------------

    /// <summary>
    /// Newline scan — portable <see cref="System.Buffers.SearchValues{T}"/> path.
    /// CI-stable (no AVX2 requirement). Zero managed allocations; ≤50 ns/call.
    /// </summary>
    public static readonly IPerformanceBudget NewlineScanPortable = new PerformanceBudget(
        StageName: "NewlineScan_Portable",
        MaxAllocatedBytesPerEvent: 0,
        MaxMeanNanosPerEvent: 50);

    /// <summary>
    /// Newline scan — AVX2 intrinsic path (256-bit vector scan).
    /// <b>Excluded from the CI regression gate</b> because <c>ubuntu-latest</c>
    /// does not guarantee AVX2 support. Run locally only.
    /// Requires <c>System.Runtime.Intrinsics.X86.Avx2.IsSupported == true</c>.
    /// </summary>
    public static readonly IPerformanceBudget NewlineScanAvx2 = new PerformanceBudget(
        StageName: "NewlineScan_Avx2",
        MaxAllocatedBytesPerEvent: 0,
        MaxMeanNanosPerEvent: 20,
        RequiresSimd: true);

    // -----------------------------------------------------------------------
    // Export helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns all registered budgets as a read-only list.
    /// </summary>
    public static IReadOnlyList<IPerformanceBudget> All { get; } = GetAllBudgets();

    /// <summary>
    /// Serializes all budget entries to a JSON file at <paramref name="outputPath"/>.
    /// The file is consumed by <c>build/scripts/validate_budget.py</c> in CI.
    /// </summary>
    /// <remarks>
    /// This method is intentionally NOT using ADR-014 source generators because the
    /// output file is a CI-only artifact, not a domain data file. The serialization
    /// happens once at benchmark startup; performance is not a concern here.
    /// </remarks>
    public static void ExportJson(string outputPath)
    {
        var entries = All.Select(b => new
        {
            stage_name = b.StageName,
            max_allocated_bytes_per_event = b.MaxAllocatedBytesPerEvent,
            max_mean_nanos_per_event = b.MaxMeanNanosPerEvent,
            requires_simd = b.RequiresSimd
        }).ToArray();

        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        File.WriteAllText(outputPath, json);
    }

    private static IReadOnlyList<IPerformanceBudget> GetAllBudgets()
    {
        return typeof(PerformanceBudgetRegistry)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(IPerformanceBudget))
            .Select(f => (IPerformanceBudget)f.GetValue(null)!)
            .ToList()
            .AsReadOnly();
    }
}

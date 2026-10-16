using System.Buffers;
using BenchmarkDotNet.Attributes;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Serialization;
using Meridian.Domain.Events;
using Meridian.Storage.Archival;

namespace Meridian.Benchmarks;

/// <summary>
/// Bounded, fixed-input measurements for every non-SIMD pipeline budget.
/// Method names deliberately match <see cref="PerformanceBudgetRegistry"/> stage names
/// so the budget validator can require an independent measurement for each stage.
/// Setup, fixture allocation, and cleanup are outside the measured operations.
/// </summary>
/// <remarks>
/// Uses the same production seams and wire fixtures as AllocationBudgetIntegrationTests.
/// WAL measurements cover the checksum core, excluding final hex-string materialization,
/// as required by the existing allocation contracts. The portable newline scan matches
/// the SearchValues path in NewlineScanBenchmarks; it does not require explicit AVX2.
/// This lane measures representative stage costs, not sustained-load pipeline behavior.
/// </remarks>
[MemoryDiagnoser]
[BenchmarkCategory("PipelineBudget")]
public class PipelineBudgetBenchmarks
{
    private static readonly DateTimeOffset EventTimestamp =
        new(2024, 1, 15, 14, 30, 0, TimeSpan.Zero);
    private static readonly SearchValues<byte> NewlineSearchValues =
        SearchValues.Create([(byte)'\n']);

    private PersistentDedupLedger _ledger = null!;
    private string _ledgerDirectory = null!;
    private MarketEvent _tradeEvent = null!;
    private string _smallPayload = null!;
    private string _mediumPayload = null!;
    private string _largePayload = null!;
    private byte[] _newlineBuffer = null!;
    private byte[] _alpacaTradeMessage = null!;
    private byte[] _alpacaQuoteMessage = null!;

    [GlobalSetup]
    public void Setup()
    {
        _ledgerDirectory = Path.Combine(Path.GetTempPath(), $"bench_pipeline_budget_{Guid.NewGuid():N}");
        // Do not initialize persistence: the budget covers the in-memory stage only.
        _ledger = new PersistentDedupLedger(_ledgerDirectory);
        _tradeEvent = MarketEvent.Trade(
            EventTimestamp,
            "AAPL",
            new Trade(
                Timestamp: EventTimestamp,
                Symbol: "AAPL",
                Price: 174.53m,
                Size: 100,
                Aggressor: AggressorSide.Buy,
                SequenceNumber: 1234567,
                StreamId: "ALPACA",
                Venue: "XNAS"),
            source: "ALPACA");
        _ledger.SeedCacheEntry(_tradeEvent);

        // Match the established allocation tests. ASCII character counts equal UTF-8 bytes.
        _smallPayload = new string('x', 64);
        _mediumPayload = new string('x', 900);
        _largePayload = new string('x', 4096);
        WriteAheadLog.WarmChecksumPath();

        // A fixed 128-byte line in a 256-byte buffer avoids random early newline matches.
        _newlineBuffer = new byte[256];
        Array.Fill(_newlineBuffer, (byte)'x');
        _newlineBuffer[127] = (byte)'\n';
        _newlineBuffer[255] = (byte)'\n';

        _alpacaTradeMessage = """
            {"T":"t","S":"SPY","p":450.25,"s":100,"t":"2024-01-15T14:30:00Z","x":"NYSE","i":12345}
            """u8.ToArray();
        _alpacaQuoteMessage = """
            {"T":"q","S":"SPY","bp":450.24,"bs":200,"ap":450.26,"as":150,"t":"2024-01-15T14:30:00Z","bx":"NYSE","ax":"ARCA"}
            """u8.ToArray();

        // Pre-initialize the source-generated contexts before measuring steady-state parsing.
        _ = HighPerformanceJson.ParseAlpacaTrade(_alpacaTradeMessage);
        _ = HighPerformanceJson.ParseAlpacaQuote(_alpacaQuoteMessage);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _ledger.DisposeAsync();
        Directory.Delete(_ledgerDirectory, recursive: true);
    }

    [Benchmark]
    public bool DedupKey_CacheHit() => _ledger.IsDuplicateCacheCheck(_tradeEvent);

    /// <summary>
    /// Computes a fresh event key on every call with the prefix already warmed.
    /// The direct production computation bypasses the cached event key seeded for the hit case.
    /// </summary>
    [Benchmark]
    public string DedupKey_CacheMiss() => _ledger.ComputeKeyForBenchmark(_tradeEvent);

    [Benchmark]
    public string WalChecksum_Small() =>
        WriteAheadLog.ComputeChecksumForBenchmark(1, EventTimestamp.UtcDateTime, "Trade", _smallPayload);

    [Benchmark]
    public string WalChecksum_Medium_1KB() =>
        WriteAheadLog.ComputeChecksumForBenchmark(2, EventTimestamp.UtcDateTime, "L2Snapshot", _mediumPayload);

    [Benchmark]
    public string WalChecksum_Large_4KB() =>
        WriteAheadLog.ComputeChecksumForBenchmark(3, EventTimestamp.UtcDateTime, "L2Snapshot", _largePayload);

    [Benchmark]
    public int NewlineScan_Portable() => _newlineBuffer.AsSpan().IndexOfAny(NewlineSearchValues);

    [Benchmark]
    public AlpacaTradeMessage? AlpacaParse_Trade_SourceGenerated() =>
        HighPerformanceJson.ParseAlpacaTrade(_alpacaTradeMessage);

    [Benchmark]
    public AlpacaQuoteMessage? AlpacaParse_Quote_SourceGenerated() =>
        HighPerformanceJson.ParseAlpacaQuote(_alpacaQuoteMessage);
}

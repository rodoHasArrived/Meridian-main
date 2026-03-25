using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Sinks;

namespace Meridian.Benchmarks;

/// <summary>
/// Benchmarks for CompositeSink fan-out overhead.
///
/// <para>
/// The BOTTLENECK_REPORT (HIGH severity) originally identified that CompositeSink fanned
/// out to sinks sequentially, meaning total append latency equalled the sum of all sink
/// latencies.  The implementation has since been updated to use <c>Task.WhenAll</c> so
/// that total latency equals <c>max(sink latencies)</c> instead.
/// </para>
///
/// <para>
/// These benchmarks quantify the orchestration cost introduced by the parallel fan-out
/// pattern and confirm the expected behaviour at 1, 2, and 4 sinks:
/// <list type="bullet">
///   <item><see cref="SingleSink"/> — fast path, no <c>Task.WhenAll</c> allocation.</item>
///   <item><see cref="TwoSinks_Parallel"/> — <c>Task[]</c> allocation + <c>Task.WhenAll</c>.</item>
///   <item><see cref="FourSinks_Parallel"/> — larger <c>Task[]</c>; shows whether allocation
///         scales linearly with sink count.</item>
///   <item><see cref="TwoSinks_FixedDelay"/> — simulates real I/O latency (500 µs per sink)
///         to confirm the parallel path beats sequential in a realistic scenario.</item>
/// </list>
/// </para>
///
/// <para>
/// Run with: <c>dotnet run -c Release -- --filter "*CompositeSink*" --memory</c>
/// </para>
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class CompositeSinkBenchmarks
{
    private CompositeSink _single = null!;
    private CompositeSink _twoSinks = null!;
    private CompositeSink _fourSinks = null!;
    private CompositeSink _twoSinksWithDelay = null!;
    private MarketEvent _event = null!;

    [GlobalSetup]
    public void Setup()
    {
        _single = new CompositeSink(new IStorageSink[] { new NoOpStorageSink() });
        _twoSinks = new CompositeSink(new IStorageSink[]
        {
            new NoOpStorageSink(),
            new NoOpStorageSink()
        });
        _fourSinks = new CompositeSink(new IStorageSink[]
        {
            new NoOpStorageSink(),
            new NoOpStorageSink(),
            new NoOpStorageSink(),
            new NoOpStorageSink()
        });
        _twoSinksWithDelay = new CompositeSink(new IStorageSink[]
        {
            new DelayedStorageSink(microseconds: 500),
            new DelayedStorageSink(microseconds: 500)
        });

        _event = MarketEvent.Trade(
            DateTimeOffset.UtcNow,
            "SPY",
            new Trade(
                Timestamp: DateTimeOffset.UtcNow,
                Symbol: "SPY",
                Price: 450.25m,
                Size: 100,
                Aggressor: AggressorSide.Buy,
                SequenceNumber: 12345,
                StreamId: "BENCH",
                Venue: "TEST"
            ));
    }

    /// <summary>
    /// Single-sink fast path — bypasses <c>Task.WhenAll</c> entirely.
    /// Baseline to measure the pure CompositeSink wrapper overhead.
    /// </summary>
    [Benchmark(Baseline = true)]
    public async ValueTask SingleSink()
    {
        await _single.AppendAsync(_event);
    }

    /// <summary>
    /// Two sinks in parallel — allocates a <c>Task[2]</c> and calls <c>Task.WhenAll</c>.
    /// Typical production configuration (JSONL + Parquet).
    /// </summary>
    [Benchmark]
    public async ValueTask TwoSinks_Parallel()
    {
        await _twoSinks.AppendAsync(_event);
    }

    /// <summary>
    /// Four sinks in parallel — confirms allocation scales with sink count,
    /// not event count.
    /// </summary>
    [Benchmark]
    public async ValueTask FourSinks_Parallel()
    {
        await _fourSinks.AppendAsync(_event);
    }

    /// <summary>
    /// Two sinks each taking ~500 µs (simulated I/O).
    /// In the parallel path, wall-clock time ≈ 500 µs (max).
    /// In a hypothetical sequential path it would be ≈ 1000 µs (sum).
    /// This benchmark validates the key benefit of the <c>Task.WhenAll</c> refactor.
    /// </summary>
    [Benchmark]
    public async ValueTask TwoSinks_FixedDelay()
    {
        await _twoSinksWithDelay.AppendAsync(_event);
    }
}

/// <summary>
/// No-op storage sink that completes immediately with no I/O.
/// Used to isolate CompositeSink's orchestration and allocation cost.
/// </summary>
internal sealed class NoOpStorageSink : IStorageSink
{
    public ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
        => ValueTask.CompletedTask;

    public Task FlushAsync(CancellationToken ct = default)
        => Task.CompletedTask;

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;
}

/// <summary>
/// Storage sink that introduces a configurable fixed delay to simulate I/O latency.
/// Enables benchmarks to validate the wall-clock benefit of parallel fan-out.
/// </summary>
internal sealed class DelayedStorageSink : IStorageSink
{
    private readonly TimeSpan _delay;

    public DelayedStorageSink(int microseconds)
    {
        _delay = TimeSpan.FromMicroseconds(microseconds);
    }

    public async ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
    {
        await Task.Delay(_delay, ct).ConfigureAwait(false);
    }

    public Task FlushAsync(CancellationToken ct = default)
        => Task.CompletedTask;

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;
}

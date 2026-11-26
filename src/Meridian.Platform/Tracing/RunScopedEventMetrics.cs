using System.Diagnostics;
using System.Threading;
using Meridian.Contracts.Monitoring;

namespace Meridian.Platform.Tracing;

/// <summary>
/// An <see cref="IEventMetrics"/> with its own counters, independent of the process-wide
/// static <see cref="Metrics"/>.
/// </summary>
/// <remarks>
/// Use it for a pipeline whose events are not part of the live feed, such as a backfill run.
/// <see cref="DefaultEventMetrics"/> writes to the shared static counters that
/// <c>/metrics</c> and the live-feed alerts read. A backfill pipeline counting into those
/// would add historical bars to <c>mdc_events_published_total</c>, and calling
/// <see cref="Reset"/> on them zeroes the live rate, drop-rate and latency gauges.
/// <para>
/// Rates are averages since construction or the last <see cref="Reset"/>. Process-level
/// fields (GC and memory) are left at zero because they are not per-run quantities.
/// </para>
/// </remarks>
public sealed class RunScopedEventMetrics : IEventMetrics
{
    private long _published;
    private long _dropped;
    private long _integrity;
    private long _trades;
    private long _depthUpdates;
    private long _quotes;
    private long _historicalBars;
    private long _latencyTicksTotal;
    private long _latencySamples;
    private long _minLatencyTicks = long.MaxValue;
    private long _maxLatencyTicks;
    private long _startTimestamp = Stopwatch.GetTimestamp();

    /// <inheritdoc />
    public long Published => Interlocked.Read(ref _published);

    /// <inheritdoc />
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <inheritdoc />
    public long Integrity => Interlocked.Read(ref _integrity);

    /// <inheritdoc />
    public long Trades => Interlocked.Read(ref _trades);

    /// <inheritdoc />
    public long DepthUpdates => Interlocked.Read(ref _depthUpdates);

    /// <inheritdoc />
    public long Quotes => Interlocked.Read(ref _quotes);

    /// <inheritdoc />
    public long HistoricalBars => Interlocked.Read(ref _historicalBars);

    /// <inheritdoc />
    public double EventsPerSecond => PerSecond(Published);

    /// <inheritdoc />
    public double DropRate
    {
        get
        {
            var published = Published;
            var dropped = Dropped;
            var total = published + dropped;
            return total == 0 ? 0d : dropped * 100d / total;
        }
    }

    /// <inheritdoc />
    public void IncPublished() => Interlocked.Increment(ref _published);

    /// <inheritdoc />
    public void IncDropped() => Interlocked.Increment(ref _dropped);

    /// <inheritdoc />
    public void IncIntegrity() => Interlocked.Increment(ref _integrity);

    /// <inheritdoc />
    public void IncTrades() => Interlocked.Increment(ref _trades);

    /// <inheritdoc />
    public void IncDepthUpdates() => Interlocked.Increment(ref _depthUpdates);

    /// <inheritdoc />
    public void IncQuotes() => Interlocked.Increment(ref _quotes);

    /// <inheritdoc />
    public void IncHistoricalBars() => Interlocked.Increment(ref _historicalBars);

    /// <inheritdoc />
    public void RecordLatency(long startTimestamp)
    {
        var elapsed = Stopwatch.GetTimestamp() - startTimestamp;
        if (elapsed < 0)
            return;

        Interlocked.Add(ref _latencyTicksTotal, elapsed);
        Interlocked.Increment(ref _latencySamples);

        long current;
        while (elapsed < (current = Interlocked.Read(ref _minLatencyTicks)) &&
               Interlocked.CompareExchange(ref _minLatencyTicks, elapsed, current) != current)
        {
        }

        while (elapsed > (current = Interlocked.Read(ref _maxLatencyTicks)) &&
               Interlocked.CompareExchange(ref _maxLatencyTicks, elapsed, current) != current)
        {
        }
    }

    /// <summary>
    /// Clears this instance's counters only. Process-wide metrics are unaffected.
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _published, 0);
        Interlocked.Exchange(ref _dropped, 0);
        Interlocked.Exchange(ref _integrity, 0);
        Interlocked.Exchange(ref _trades, 0);
        Interlocked.Exchange(ref _depthUpdates, 0);
        Interlocked.Exchange(ref _quotes, 0);
        Interlocked.Exchange(ref _historicalBars, 0);
        Interlocked.Exchange(ref _latencyTicksTotal, 0);
        Interlocked.Exchange(ref _latencySamples, 0);
        Interlocked.Exchange(ref _minLatencyTicks, long.MaxValue);
        Interlocked.Exchange(ref _maxLatencyTicks, 0);
        Interlocked.Exchange(ref _startTimestamp, Stopwatch.GetTimestamp());
    }

    /// <inheritdoc />
    public MetricsSnapshot GetSnapshot()
    {
        var samples = Interlocked.Read(ref _latencySamples);
        var minTicks = Interlocked.Read(ref _minLatencyTicks);
        return new MetricsSnapshot(
            Published: Published,
            Dropped: Dropped,
            Integrity: Integrity,
            Trades: Trades,
            DepthUpdates: DepthUpdates,
            Quotes: Quotes,
            HistoricalBars: HistoricalBars,
            EventsPerSecond: EventsPerSecond,
            TradesPerSecond: PerSecond(Trades),
            DepthUpdatesPerSecond: PerSecond(DepthUpdates),
            HistoricalBarsPerSecond: PerSecond(HistoricalBars),
            DropRate: DropRate,
            AverageLatencyUs: samples == 0 ? 0d : TicksToMicroseconds(Interlocked.Read(ref _latencyTicksTotal)) / samples,
            MinLatencyUs: samples == 0 || minTicks == long.MaxValue ? 0d : TicksToMicroseconds(minTicks),
            MaxLatencyUs: samples == 0 ? 0d : TicksToMicroseconds(Interlocked.Read(ref _maxLatencyTicks)),
            LatencySampleCount: samples,
            Gc0Collections: 0,
            Gc1Collections: 0,
            Gc2Collections: 0,
            Gc0Delta: 0,
            Gc1Delta: 0,
            Gc2Delta: 0,
            MemoryUsageMb: 0,
            HeapSizeMb: 0,
            Timestamp: DateTimeOffset.UtcNow);
    }

    private double PerSecond(long count)
    {
        var elapsed = Stopwatch.GetElapsedTime(Interlocked.Read(ref _startTimestamp)).TotalSeconds;
        return elapsed <= 0 ? 0d : count / elapsed;
    }

    private static double TicksToMicroseconds(long ticks) => ticks * 1_000_000d / Stopwatch.Frequency;
}

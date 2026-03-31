using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Meridian.Application.Subscriptions.Models;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Minimal hot-path safe counters (no allocations; thread-safe).
/// Enhanced with latency tracking and GC monitoring.
/// </summary>
public static class Metrics
{
    // Event counters
    private static long _published;
    private static long _dropped;
    private static long _integrity;
    private static long _trades;
    private static long _depthUpdates;
    private static long _quotes;
    private static long _historicalBars;

    // Latency tracking (in ticks for high precision)
    private static long _totalProcessingTicks;
    private static long _minLatencyTicks = long.MaxValue;
    private static long _maxLatencyTicks;
    private static long _latencySampleCount;

    // GC monitoring
    private static long _lastGc0Count;
    private static long _lastGc1Count;
    private static long _lastGc2Count;
    private static long _gc0Delta;
    private static long _gc1Delta;
    private static long _gc2Delta;

    // Timestamp for rate calculations
    private static long _startTimestamp;
    private static long _lastResetTimestamp;

    static Metrics()
    {
        _startTimestamp = Stopwatch.GetTimestamp();
        _lastResetTimestamp = _startTimestamp;
        UpdateGcCounts();
    }


    public static long Published => Interlocked.Read(ref _published);
    public static long Dropped => Interlocked.Read(ref _dropped);
    public static long Integrity => Interlocked.Read(ref _integrity);
    public static long Trades => Interlocked.Read(ref _trades);
    public static long DepthUpdates => Interlocked.Read(ref _depthUpdates);
    public static long Quotes => Interlocked.Read(ref _quotes);
    public static long HistoricalBars => Interlocked.Read(ref _historicalBars);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncPublished() => Interlocked.Increment(ref _published);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncDropped() => Interlocked.Increment(ref _dropped);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncIntegrity() => Interlocked.Increment(ref _integrity);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncTrades() => Interlocked.Increment(ref _trades);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncDepthUpdates() => Interlocked.Increment(ref _depthUpdates);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncQuotes() => Interlocked.Increment(ref _quotes);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncHistoricalBars() => Interlocked.Increment(ref _historicalBars);



    /// <summary>
    /// Records a latency sample in ticks.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordLatency(long startTimestamp)
    {
        var elapsed = Stopwatch.GetTimestamp() - startTimestamp;
        if (elapsed < 0)
            return;

        Interlocked.Add(ref _totalProcessingTicks, elapsed);
        Interlocked.Increment(ref _latencySampleCount);

        // Update min (optimistic read-compare-exchange)
        var currentMin = Interlocked.Read(ref _minLatencyTicks);
        while (elapsed < currentMin)
        {
            var previous = Interlocked.CompareExchange(ref _minLatencyTicks, elapsed, currentMin);
            if (previous == currentMin)
                break;
            currentMin = previous;
        }

        // Update max
        var currentMax = Interlocked.Read(ref _maxLatencyTicks);
        while (elapsed > currentMax)
        {
            var previous = Interlocked.CompareExchange(ref _maxLatencyTicks, elapsed, currentMax);
            if (previous == currentMax)
                break;
            currentMax = previous;
        }
    }

    /// <summary>
    /// Gets the average latency in microseconds.
    /// </summary>
    public static double AverageLatencyUs
    {
        get
        {
            var samples = Interlocked.Read(ref _latencySampleCount);
            if (samples == 0)
                return 0;

            var totalTicks = Interlocked.Read(ref _totalProcessingTicks);
            return (double)totalTicks / samples / Stopwatch.Frequency * 1_000_000;
        }
    }

    /// <summary>
    /// Gets the minimum latency in microseconds.
    /// </summary>
    public static double MinLatencyUs
    {
        get
        {
            var ticks = Interlocked.Read(ref _minLatencyTicks);
            if (ticks == long.MaxValue)
                return 0;
            return (double)ticks / Stopwatch.Frequency * 1_000_000;
        }
    }

    /// <summary>
    /// Gets the maximum latency in microseconds.
    /// </summary>
    public static double MaxLatencyUs
    {
        get
        {
            var ticks = Interlocked.Read(ref _maxLatencyTicks);
            return (double)ticks / Stopwatch.Frequency * 1_000_000;
        }
    }

    /// <summary>
    /// Gets the total number of latency samples recorded.
    /// </summary>
    public static long LatencySampleCount => Interlocked.Read(ref _latencySampleCount);



    /// <summary>
    /// Gets the events per second rate.
    /// </summary>
    public static double EventsPerSecond
    {
        get
        {
            var elapsed = GetElapsedSeconds();
            if (elapsed <= 0)
                return 0;
            return Published / elapsed;
        }
    }

    /// <summary>
    /// Gets the trades per second rate.
    /// </summary>
    public static double TradesPerSecond
    {
        get
        {
            var elapsed = GetElapsedSeconds();
            if (elapsed <= 0)
                return 0;
            return Trades / elapsed;
        }
    }

    /// <summary>
    /// Gets the depth updates per second rate.
    /// </summary>
    public static double DepthUpdatesPerSecond
    {
        get
        {
            var elapsed = GetElapsedSeconds();
            if (elapsed <= 0)
                return 0;
            return DepthUpdates / elapsed;
        }
    }

    /// <summary>
    /// Gets the historical bars per second rate (useful during backfills).
    /// </summary>
    public static double HistoricalBarsPerSecond
    {
        get
        {
            var elapsed = GetElapsedSeconds();
            if (elapsed <= 0)
                return 0;
            return HistoricalBars / elapsed;
        }
    }

    /// <summary>
    /// Gets the drop rate as a percentage.
    /// </summary>
    public static double DropRate
    {
        get
        {
            var total = Published + Dropped;
            if (total == 0)
                return 0;
            return (double)Dropped / total * 100;
        }
    }

    private static double GetElapsedSeconds()
    {
        var elapsed = Stopwatch.GetTimestamp() - Interlocked.Read(ref _startTimestamp);
        return (double)elapsed / Stopwatch.Frequency;
    }



    /// <summary>
    /// Updates GC collection counts and calculates deltas since last update.
    /// </summary>
    public static void UpdateGcCounts()
    {
        var gc0 = GC.CollectionCount(0);
        var gc1 = GC.CollectionCount(1);
        var gc2 = GC.CollectionCount(2);

        Interlocked.Exchange(ref _gc0Delta, gc0 - Interlocked.Read(ref _lastGc0Count));
        Interlocked.Exchange(ref _gc1Delta, gc1 - Interlocked.Read(ref _lastGc1Count));
        Interlocked.Exchange(ref _gc2Delta, gc2 - Interlocked.Read(ref _lastGc2Count));

        Interlocked.Exchange(ref _lastGc0Count, gc0);
        Interlocked.Exchange(ref _lastGc1Count, gc1);
        Interlocked.Exchange(ref _lastGc2Count, gc2);
    }

    public static long Gc0Collections => GC.CollectionCount(0);
    public static long Gc1Collections => GC.CollectionCount(1);
    public static long Gc2Collections => GC.CollectionCount(2);
    public static long Gc0Delta => Interlocked.Read(ref _gc0Delta);
    public static long Gc1Delta => Interlocked.Read(ref _gc1Delta);
    public static long Gc2Delta => Interlocked.Read(ref _gc2Delta);

    /// <summary>
    /// Gets the current memory usage in megabytes.
    /// </summary>
    public static double MemoryUsageMb => GC.GetTotalMemory(false) / (1024.0 * 1024.0);

    /// <summary>
    /// Gets the GC heap size in megabytes.
    /// </summary>
    public static double HeapSizeMb => GC.GetGCMemoryInfo().HeapSizeBytes / (1024.0 * 1024.0);



    /// <summary>
    /// Resets all counters to zero.
    /// </summary>
    public static void Reset()
    {
        Interlocked.Exchange(ref _published, 0);
        Interlocked.Exchange(ref _dropped, 0);
        Interlocked.Exchange(ref _integrity, 0);
        Interlocked.Exchange(ref _trades, 0);
        Interlocked.Exchange(ref _depthUpdates, 0);
        Interlocked.Exchange(ref _quotes, 0);
        Interlocked.Exchange(ref _historicalBars, 0);
        Interlocked.Exchange(ref _totalProcessingTicks, 0);
        Interlocked.Exchange(ref _minLatencyTicks, long.MaxValue);
        Interlocked.Exchange(ref _maxLatencyTicks, 0);
        Interlocked.Exchange(ref _latencySampleCount, 0);
        Interlocked.Exchange(ref _lastResetTimestamp, Stopwatch.GetTimestamp());
        UpdateGcCounts();
    }

    /// <summary>
    /// Gets a snapshot of all current metrics.
    /// </summary>
    public static MetricsSnapshot GetSnapshot()
    {
        UpdateGcCounts();

        return new MetricsSnapshot(
            Published: Published,
            Dropped: Dropped,
            Integrity: Integrity,
            Trades: Trades,
            DepthUpdates: DepthUpdates,
            Quotes: Quotes,
            HistoricalBars: HistoricalBars,
            EventsPerSecond: EventsPerSecond,
            TradesPerSecond: TradesPerSecond,
            DepthUpdatesPerSecond: DepthUpdatesPerSecond,
            HistoricalBarsPerSecond: HistoricalBarsPerSecond,
            DropRate: DropRate,
            AverageLatencyUs: AverageLatencyUs,
            MinLatencyUs: MinLatencyUs,
            MaxLatencyUs: MaxLatencyUs,
            LatencySampleCount: LatencySampleCount,
            Gc0Collections: Gc0Collections,
            Gc1Collections: Gc1Collections,
            Gc2Collections: Gc2Collections,
            Gc0Delta: Gc0Delta,
            Gc1Delta: Gc1Delta,
            Gc2Delta: Gc2Delta,
            MemoryUsageMb: MemoryUsageMb,
            HeapSizeMb: HeapSizeMb,
            Timestamp: DateTimeOffset.UtcNow
        );
    }

    /// <summary>
    /// Gets a combined snapshot of all metrics including resubscription metrics.
    /// </summary>
    public static CombinedMetricsSnapshot GetCombinedSnapshot()
    {
        return new CombinedMetricsSnapshot(
            Core: GetSnapshot(),
            Resubscription: ResubscriptionMetrics.GetSnapshot()
        );
    }

}

/// <summary>
/// Immutable snapshot of all metrics at a point in time.
/// </summary>
public readonly record struct MetricsSnapshot(
    long Published,
    long Dropped,
    long Integrity,
    long Trades,
    long DepthUpdates,
    long Quotes,
    long HistoricalBars,
    double EventsPerSecond,
    double TradesPerSecond,
    double DepthUpdatesPerSecond,
    double HistoricalBarsPerSecond,
    double DropRate,
    double AverageLatencyUs,
    double MinLatencyUs,
    double MaxLatencyUs,
    long LatencySampleCount,
    long Gc0Collections,
    long Gc1Collections,
    long Gc2Collections,
    long Gc0Delta,
    long Gc1Delta,
    long Gc2Delta,
    double MemoryUsageMb,
    double HeapSizeMb,
    DateTimeOffset Timestamp
);

/// <summary>
/// Combined snapshot including core metrics and resubscription metrics.
/// </summary>
public readonly record struct CombinedMetricsSnapshot(
    MetricsSnapshot Core,
    ResubscriptionMetricsSnapshot Resubscription
);

namespace Meridian.Contracts.Monitoring;

/// <summary>
/// Immutable snapshot of event-pipeline metrics at a point in time.
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
    DateTimeOffset Timestamp);

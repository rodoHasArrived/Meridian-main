namespace Meridian.Contracts.Monitoring;

/// <summary>
/// Abstraction over event pipeline counters so that hot-path metrics
/// can be injected rather than accessed through a static class.
/// Default implementations should preserve zero-allocation / thread-safe behavior on the hot path.
/// </summary>
public interface IEventMetrics
{
    long Published { get; }
    long Dropped { get; }
    long Integrity { get; }
    long Trades { get; }
    long DepthUpdates { get; }
    long Quotes { get; }
    long HistoricalBars { get; }
    double EventsPerSecond { get; }
    double DropRate { get; }

    void IncPublished();
    void IncDropped();
    void IncIntegrity();
    void IncTrades();
    void IncDepthUpdates();
    void IncQuotes();
    void IncHistoricalBars();
    void RecordLatency(long startTimestamp);

    void Reset();
    MetricsSnapshot GetSnapshot();
}

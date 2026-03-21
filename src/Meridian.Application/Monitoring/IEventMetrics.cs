using System.Runtime.CompilerServices;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Abstraction over event pipeline counters so that hot-path metrics
/// can be injected rather than accessed through a static class.
/// The default implementation delegates to the existing <see cref="Metrics"/> statics,
/// preserving zero-allocation / thread-safe behavior on the hot path.
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

/// <summary>
/// Default implementation that delegates to the existing static <see cref="Metrics"/> class.
/// Registered as a singleton in DI so all consumers share the same counters.
/// </summary>
public sealed class DefaultEventMetrics : IEventMetrics
{
    public long Published => Metrics.Published;
    public long Dropped => Metrics.Dropped;
    public long Integrity => Metrics.Integrity;
    public long Trades => Metrics.Trades;
    public long DepthUpdates => Metrics.DepthUpdates;
    public long Quotes => Metrics.Quotes;
    public long HistoricalBars => Metrics.HistoricalBars;
    public double EventsPerSecond => Metrics.EventsPerSecond;
    public double DropRate => Metrics.DropRate;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IncPublished() => Metrics.IncPublished();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IncDropped() => Metrics.IncDropped();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IncIntegrity() => Metrics.IncIntegrity();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IncTrades() => Metrics.IncTrades();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IncDepthUpdates() => Metrics.IncDepthUpdates();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IncQuotes() => Metrics.IncQuotes();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IncHistoricalBars() => Metrics.IncHistoricalBars();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordLatency(long startTimestamp) => Metrics.RecordLatency(startTimestamp);

    public void Reset() => Metrics.Reset();

    public MetricsSnapshot GetSnapshot() => Metrics.GetSnapshot();
}

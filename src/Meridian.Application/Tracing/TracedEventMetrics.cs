using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Meridian.Application.Monitoring;
using Meridian.Infrastructure.Contracts;

namespace Meridian.Application.Tracing;

/// <summary>
/// Decorates an <see cref="IEventMetrics"/> implementation with OpenTelemetry-compatible
/// <see cref="System.Diagnostics.Metrics"/> counters and histograms, enabling export to
/// OTLP, Prometheus, or any configured metrics backend without modifying hot-path code.
/// </summary>
/// <remarks>
/// This decorator follows the same zero-allocation pattern as <see cref="DefaultEventMetrics"/>.
/// All counter increments use <see cref="MethodImplOptions.AggressiveInlining"/>.
/// The underlying <see cref="IEventMetrics"/> is always called first to preserve existing behavior.
/// </remarks>
[ImplementsAdr("ADR-012", "OpenTelemetry metrics instrumentation for pipeline observability")]
public sealed class TracedEventMetrics : IEventMetrics
{
    private readonly IEventMetrics _inner;

    // System.Diagnostics.Metrics instruments (OTLP-compatible)
    private static readonly Meter PipelineMeter = new("Meridian.Pipeline", "1.0.0");
    private static readonly Counter<long> PublishedCounter = PipelineMeter.CreateCounter<long>(
        "mdc.pipeline.events.published", "events", "Total events published to pipeline");
    private static readonly Counter<long> DroppedCounter = PipelineMeter.CreateCounter<long>(
        "mdc.pipeline.events.dropped", "events", "Total events dropped due to backpressure");
    private static readonly Counter<long> TradesCounter = PipelineMeter.CreateCounter<long>(
        "mdc.pipeline.events.trades", "events", "Total trade events processed");
    private static readonly Counter<long> DepthCounter = PipelineMeter.CreateCounter<long>(
        "mdc.pipeline.events.depth", "events", "Total depth update events processed");
    private static readonly Counter<long> QuotesCounter = PipelineMeter.CreateCounter<long>(
        "mdc.pipeline.events.quotes", "events", "Total quote events processed");
    private static readonly Counter<long> IntegrityCounter = PipelineMeter.CreateCounter<long>(
        "mdc.pipeline.events.integrity", "events", "Total integrity events");
    private static readonly Counter<long> HistoricalBarsCounter = PipelineMeter.CreateCounter<long>(
        "mdc.pipeline.events.historical_bars", "events", "Total historical bar events");
    private static readonly Histogram<double> LatencyHistogram = PipelineMeter.CreateHistogram<double>(
        "mdc.pipeline.latency", "ms", "Event processing latency");

    /// <summary>
    /// Creates a traced metrics decorator wrapping the specified inner metrics implementation.
    /// </summary>
    /// <param name="inner">The underlying metrics implementation to delegate to.</param>
    public TracedEventMetrics(IEventMetrics inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public long Published => _inner.Published;
    public long Dropped => _inner.Dropped;
    public long Integrity => _inner.Integrity;
    public long Trades => _inner.Trades;
    public long DepthUpdates => _inner.DepthUpdates;
    public long Quotes => _inner.Quotes;
    public long HistoricalBars => _inner.HistoricalBars;
    public double EventsPerSecond => _inner.EventsPerSecond;
    public double DropRate => _inner.DropRate;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IncPublished()
    {
        _inner.IncPublished();
        PublishedCounter.Add(1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IncDropped()
    {
        _inner.IncDropped();
        DroppedCounter.Add(1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IncIntegrity()
    {
        _inner.IncIntegrity();
        IntegrityCounter.Add(1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IncTrades()
    {
        _inner.IncTrades();
        TradesCounter.Add(1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IncDepthUpdates()
    {
        _inner.IncDepthUpdates();
        DepthCounter.Add(1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IncQuotes()
    {
        _inner.IncQuotes();
        QuotesCounter.Add(1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IncHistoricalBars()
    {
        _inner.IncHistoricalBars();
        HistoricalBarsCounter.Add(1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordLatency(long startTimestamp)
    {
        _inner.RecordLatency(startTimestamp);

        var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
        LatencyHistogram.Record(elapsed.TotalMilliseconds);
    }

    public void Reset() => _inner.Reset();

    public MetricsSnapshot GetSnapshot() => _inner.GetSnapshot();
}

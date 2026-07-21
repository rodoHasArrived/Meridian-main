using System.Threading.Channels;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.DataIntegration.Monitoring.DataQuality;
using Meridian.Domain.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Application.DataQuality;

/// <summary>
/// <see cref="IMarketEventPublisher"/> decorator that feeds live <see cref="MarketEventType.Trade"/>
/// and <see cref="MarketEventType.BboQuote"/> ingress into the
/// <see cref="DataQualityMonitoringService"/> (completeness, gap analysis, sequence tracking,
/// anomaly detection, latency histograms, and cross-provider comparison).
/// </summary>
/// <remarks>
/// <para>
/// The publish result of the inner publisher is always returned unchanged, so pipeline
/// backpressure semantics are unaffected. Quality observation happens on a dedicated background
/// consumer fed by a bounded drop-on-full channel: a slow quality monitor can never stall or
/// block the market data hot path. Samples are recorded even when the pipeline sheds the event
/// under backpressure, because feed-quality metrics describe what the provider delivered, not
/// what storage accepted.
/// </para>
/// </remarks>
public sealed class QualityMonitoringPublisher : IMarketEventPublisher, IAsyncDisposable
{
    private const int DefaultSampleCapacity = 8_192;
    private const long DropLogInterval = 10_000;

    private readonly IMarketEventPublisher _inner;
    private readonly DataQualityMonitoringService _quality;
    private readonly ILogger<QualityMonitoringPublisher> _logger;
    private readonly Channel<MarketEvent> _samples;
    private readonly Task _consumer;
    private readonly CancellationTokenSource _cts = new();

    private long _tradesObserved;
    private long _quotesObserved;
    private long _samplesDropped;
    private int _disposed;

    public QualityMonitoringPublisher(
        IMarketEventPublisher inner,
        DataQualityMonitoringService quality,
        ILogger<QualityMonitoringPublisher>? logger = null,
        int sampleCapacity = DefaultSampleCapacity)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _quality = quality ?? throw new ArgumentNullException(nameof(quality));
        _logger = logger ?? NullLogger<QualityMonitoringPublisher>.Instance;

        if (sampleCapacity < 1)
            throw new ArgumentOutOfRangeException(nameof(sampleCapacity), sampleCapacity, "Sample capacity must be at least 1.");

        _samples = Channel.CreateBounded<MarketEvent>(new BoundedChannelOptions(sampleCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });

        _consumer = Task.Run(() => ConsumeAsync(_cts.Token));
    }

    /// <summary>Gets the number of trade events recorded into the quality monitors.</summary>
    public long TradesObserved => Interlocked.Read(ref _tradesObserved);

    /// <summary>Gets the number of quote events recorded into the quality monitors.</summary>
    public long QuotesObserved => Interlocked.Read(ref _quotesObserved);

    /// <summary>Gets the number of quality samples dropped because the sample buffer was full.</summary>
    public long SamplesDropped => Interlocked.Read(ref _samplesDropped);

    /// <inheritdoc/>
    public bool TryPublish(in MarketEvent evt)
    {
        var accepted = _inner.TryPublish(in evt);

        if (evt.Type is MarketEventType.Trade or MarketEventType.BboQuote
            && Volatile.Read(ref _disposed) == 0
            && !_samples.Writer.TryWrite(evt))
        {
            var dropped = Interlocked.Increment(ref _samplesDropped);
            if (dropped == 1 || dropped % DropLogInterval == 0)
            {
                _logger.LogWarning(
                    "Quality-monitoring sample buffer is full; {DroppedCount} samples dropped so far. " +
                    "Dashboards remain live but under-count during the overload window.",
                    dropped);
            }
        }

        return accepted;
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var evt in _samples.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    Observe(evt);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to record {EventType} quality sample for {Symbol}",
                        evt.Type, evt.EffectiveSymbol);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Disposal requested a hard stop before the channel drained.
        }
    }

    private void Observe(MarketEvent evt)
    {
        switch (evt.Payload)
        {
            case Trade trade:
                _quality.ProcessTrade(
                    evt.EffectiveSymbol,
                    evt.Timestamp,
                    trade.Price,
                    trade.Size,
                    // Sequence 0 means "provider does not sequence this stream"; passing it
                    // through would register false sequence errors for every event.
                    evt.Sequence > 0 ? evt.Sequence : null,
                    evt.Source,
                    evt.EstimatedLatencyMs);
                Interlocked.Increment(ref _tradesObserved);
                break;

            case BboQuotePayload quote:
                _quality.ProcessQuote(
                    evt.EffectiveSymbol,
                    evt.Timestamp,
                    quote.BidPrice,
                    quote.AskPrice,
                    quote.BidSize,
                    quote.AskSize,
                    evt.Source,
                    evt.EstimatedLatencyMs);
                Interlocked.Increment(ref _quotesObserved);
                break;
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _samples.Writer.TryComplete();

        var drainTimeout = Task.Delay(TimeSpan.FromSeconds(5));
        if (await Task.WhenAny(_consumer, drainTimeout).ConfigureAwait(false) == drainTimeout)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            await _consumer.ConfigureAwait(false);
        }

        _cts.Dispose();
    }
}

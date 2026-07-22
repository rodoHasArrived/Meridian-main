using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Execution.Live;

/// <summary>
/// In-process fan-out hub for live market events. The host publishes each pipeline event
/// once via <see cref="Publish"/>; every active subscription receives the events matching
/// its symbol set through its own bounded queue. Slow subscribers drop their oldest queued
/// events (the hot publish path never blocks), mirroring the platform's observer-channel
/// backpressure policy.
/// </summary>
public sealed class LiveMarketEventHub : ILiveMarketEventFeed
{
    private const int DefaultSubscriberCapacity = 4096;

    private readonly ILogger<LiveMarketEventHub> _logger;
    private readonly int _subscriberCapacity;
    private readonly ConcurrentDictionary<Guid, Subscription> _subscriptions = new();

    public LiveMarketEventHub(
        ILogger<LiveMarketEventHub>? logger = null,
        int subscriberCapacity = DefaultSubscriberCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(subscriberCapacity);
        _logger = logger ?? NullLogger<LiveMarketEventHub>.Instance;
        _subscriberCapacity = subscriberCapacity;
    }

    /// <summary>Number of active subscriptions (diagnostics only).</summary>
    public int ActiveSubscriptionCount => _subscriptions.Count;

    /// <summary>
    /// Publishes one live event to all subscriptions whose symbol set contains the
    /// event's symbol. Never blocks; full subscriber queues drop their oldest event.
    /// </summary>
    public void Publish(LiveMarketEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        foreach (var subscription in _subscriptions.Values)
        {
            subscription.TryWrite(evt);
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<LiveMarketEvent> SubscribeAsync(
        IReadOnlyCollection<string> symbols,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        if (symbols.Count == 0)
        {
            throw new ArgumentException("A live market event subscription requires at least one symbol.", nameof(symbols));
        }

        var subscription = new Subscription(symbols, _subscriberCapacity, _logger);
        var subscriptionId = Guid.NewGuid();
        _subscriptions[subscriptionId] = subscription;
        try
        {
            await foreach (var evt in subscription.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                yield return evt;
            }
        }
        finally
        {
            _subscriptions.TryRemove(subscriptionId, out _);
            subscription.Complete();
        }
    }

    private sealed class Subscription
    {
        private readonly HashSet<string> _symbols;
        private readonly Channel<LiveMarketEvent> _channel;
        private readonly ILogger _logger;
        private long _droppedEvents;

        public Subscription(IReadOnlyCollection<string> symbols, int capacity, ILogger logger)
        {
            _symbols = new HashSet<string>(
                symbols.Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
                       .Select(static symbol => symbol.Trim()),
                StringComparer.OrdinalIgnoreCase);
            _logger = logger;
            _channel = Channel.CreateBounded<LiveMarketEvent>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });
        }

        public ChannelReader<LiveMarketEvent> Reader => _channel.Reader;

        public void TryWrite(LiveMarketEvent evt)
        {
            if (!_symbols.Contains(evt.Symbol))
            {
                return;
            }

            if (!_channel.Writer.TryWrite(evt))
            {
                // DropOldest means TryWrite only fails once the channel is completed; count
                // it as a dropped event so the shutdown race is still observable.
                var dropped = Interlocked.Increment(ref _droppedEvents);
                if (dropped == 1)
                {
                    _logger.LogWarning(
                        "Live market event subscription dropped an event for {Symbol}; subscription is completing.",
                        evt.Symbol);
                }
            }
        }

        public void Complete() => _channel.Writer.TryComplete();
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Meridian.Contracts.Api;
using Meridian.Ui.Shared.Endpoints;

namespace Meridian.Ui.Shared.Streaming;

/// <summary>
/// Event-driven quote fan-out. Woken by the collector via <see cref="NotifyQuoteChanged"/>, it
/// rebuilds each active topic's snapshot on a coalescing loop (latest-wins) and pushes it to every
/// subscriber of that topic over a bounded, drop-oldest channel — so one snapshot build is shared
/// across all subscribers of a topic and a slow client never blocks the loop. The market-data
/// storage hot path is untouched: <see cref="NotifyQuoteChanged"/> only sets a wake signal.
/// </summary>
public sealed class QuoteStreamBroadcaster : IQuoteStreamBroadcaster, IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly StreamConnectionRegistry _registry;
    private readonly QuoteStreamOptions _options;
    private readonly Func<IServiceProvider, string?, QuotesSnapshotResponse?> _snapshotFactory;
    private readonly ConcurrentDictionary<string, TopicState> _topics = new(StringComparer.Ordinal);
    private readonly Channel<byte> _wake = Channel.CreateBounded<byte>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    public QuoteStreamBroadcaster(IServiceProvider services, StreamConnectionRegistry registry, QuoteStreamOptions options)
        : this(services, registry, options, LiveDataEndpoints.TryBuildQuotesSnapshotResponse)
    {
    }

    /// <summary>Test seam: inject the snapshot builder so tests don't need the full DI graph.</summary>
    internal QuoteStreamBroadcaster(
        IServiceProvider services,
        StreamConnectionRegistry registry,
        QuoteStreamOptions options,
        Func<IServiceProvider, string?, QuotesSnapshotResponse?> snapshotFactory)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _snapshotFactory = snapshotFactory ?? throw new ArgumentNullException(nameof(snapshotFactory));
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    /// <inheritdoc />
    public void NotifyQuoteChanged(string symbol)
    {
        // Non-blocking wake. All rebuild work happens on the loop, never on the ingestion thread.
        _wake.Writer.TryWrite(0);
    }

    /// <inheritdoc />
    public QuoteStreamSubscription? TrySubscribe(StreamTopic topic, string sessionId)
    {
        if (!_registry.TryReserve(sessionId))
        {
            return null;
        }

        var subscriber = new Subscriber(_options.SubscriberChannelCapacity);
        var state = _topics.GetOrAdd(topic.Key, _ => new TopicState(topic));
        state.Subscribers[subscriber] = 0;

        // Seed the new subscriber with the current snapshot so it has data immediately.
        var initial = BuildSnapshot(topic);
        if (initial is not null)
        {
            subscriber.Writer.TryWrite(initial);
        }

        return new QuoteStreamSubscription(subscriber.Reader, () =>
        {
            if (subscriber.TryClaimDispose())
            {
                state.Subscribers.TryRemove(subscriber, out _);
                subscriber.Writer.TryComplete();
                _registry.Release(sessionId);
            }

            return ValueTask.CompletedTask;
        });
    }

    /// <summary>
    /// Rebuild and fan out one coalesced round for every topic with subscribers. Exposed for tests;
    /// the background loop calls this on each coalesce tick.
    /// </summary>
    internal void PublishPending()
    {
        foreach (var state in _topics.Values)
        {
            // Empty topics are retained (keyed by a bounded universe of canonical symbol sets) but
            // never rebuilt — skipping them here avoids a subscribe/remove race with no wasted work.
            if (state.Subscribers.IsEmpty)
            {
                continue;
            }

            var snapshot = BuildSnapshot(state.Topic);
            if (snapshot is null || QuoteRowsEqual(state.LastQuotes, snapshot.Quotes))
            {
                continue;
            }

            state.LastQuotes = snapshot.Quotes;
            foreach (var subscriber in state.Subscribers.Keys)
            {
                // Bounded drop-oldest: latest snapshot wins, never blocks the loop.
                subscriber.Writer.TryWrite(snapshot);
            }
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _wake.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                // Coalesce: drain any pending wake tokens, wait the window, then publish once.
                while (_wake.Reader.TryRead(out _))
                {
                }

                if (_options.CoalesceIntervalMs > 0)
                {
                    await Task.Delay(_options.CoalesceIntervalMs, cancellationToken).ConfigureAwait(false);
                    while (_wake.Reader.TryRead(out _))
                    {
                    }
                }

                PublishPending();
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private QuotesSnapshotResponse? BuildSnapshot(StreamTopic topic)
    {
        try
        {
            return _snapshotFactory(_services, topic.SymbolFilter);
        }
        catch
        {
            // A snapshot build failure must not tear down the loop or the subscriber.
            return null;
        }
    }

    private static bool QuoteRowsEqual(IReadOnlyList<QuotesSnapshotItem>? previous, IReadOnlyList<QuotesSnapshotItem> current)
    {
        if (previous is null || previous.Count != current.Count)
        {
            return false;
        }

        for (var i = 0; i < current.Count; i++)
        {
            if (!current[i].Equals(previous[i]))
            {
                return false;
            }
        }

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch
        {
            // Ignore shutdown races.
        }

        _cts.Dispose();
    }

    private sealed class TopicState
    {
        public TopicState(StreamTopic topic)
        {
            Topic = topic;
        }

        public StreamTopic Topic { get; }

        public ConcurrentDictionary<Subscriber, byte> Subscribers { get; } = new();

        public IReadOnlyList<QuotesSnapshotItem>? LastQuotes { get; set; }
    }

    private sealed class Subscriber
    {
        private readonly Channel<QuotesSnapshotResponse> _channel;
        private int _disposed;

        public Subscriber(int capacity)
        {
            _channel = Channel.CreateBounded<QuotesSnapshotResponse>(
                new BoundedChannelOptions(capacity <= 0 ? 1 : capacity)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = false
                });
        }

        public ChannelReader<QuotesSnapshotResponse> Reader => _channel.Reader;

        public ChannelWriter<QuotesSnapshotResponse> Writer => _channel.Writer;

        public bool TryClaimDispose() => Interlocked.Exchange(ref _disposed, 1) == 0;
    }
}

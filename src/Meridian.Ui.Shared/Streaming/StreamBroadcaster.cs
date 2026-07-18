using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Streaming;

/// <summary>
/// Event-driven fan-out engine shared by all workstation stream kinds. Woken via <see cref="WakeAll"/>
/// (or <see cref="Wake"/>), it rebuilds each active topic's payload on a coalescing loop
/// (latest-wins) and pushes it to every subscriber of that topic over a bounded, drop-oldest channel
/// — so one build is shared across all subscribers of a topic and a slow client never blocks the
/// loop. The payload build, coalesce comparison, and empty-topic lifecycle are supplied by the owning
/// broadcaster (quotes, report-run, …).
/// </summary>
/// <remarks>
/// Empty-topic lifecycle differs by kind. Quote topics are a bounded universe (canonical symbol
/// sets) and are retained when empty (<paramref name="evictEmptyTopics"/> = false), which also avoids
/// a subscribe/evict race. Report-run topics are keyed by an unbounded set of run ids and MUST evict
/// on last unsubscribe (true), guarded by a per-topic lock so a concurrent subscribe never attaches
/// to a state that is being removed.
/// </remarks>
public sealed class StreamBroadcaster<TPayload> : IAsyncDisposable
    where TPayload : class
{
    private readonly StreamConnectionRegistry _registry;
    private readonly Func<StreamTopic, TPayload?> _build;
    private readonly IEqualityComparer<TPayload> _comparer;
    private readonly int _coalesceIntervalMs;
    private readonly int _subscriberChannelCapacity;
    private readonly bool _evictEmptyTopics;
    private readonly ConcurrentDictionary<string, TopicState> _topics = new(StringComparer.Ordinal);
    private readonly Channel<byte> _wake = Channel.CreateBounded<byte>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private readonly ILogger? _logger;
    private long _lastFailureLogTicks;
    private int _disposed;

    /// <summary>Minimum spacing between logged build/publish failures, so a persistently
    /// broken payload builder surfaces without flooding the log on every coalesce tick.</summary>
    private static readonly TimeSpan FailureLogInterval = TimeSpan.FromSeconds(30);

    public StreamBroadcaster(
        StreamConnectionRegistry registry,
        Func<StreamTopic, TPayload?> build,
        IEqualityComparer<TPayload> coalesceComparer,
        int coalesceIntervalMs,
        int subscriberChannelCapacity,
        bool evictEmptyTopics,
        ILogger? logger = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _build = build ?? throw new ArgumentNullException(nameof(build));
        _comparer = coalesceComparer ?? throw new ArgumentNullException(nameof(coalesceComparer));
        _coalesceIntervalMs = coalesceIntervalMs;
        _subscriberChannelCapacity = subscriberChannelCapacity;
        _evictEmptyTopics = evictEmptyTopics;
        _logger = logger;
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    /// <summary>Test hook: number of live topic states (asserts empty-topic retention vs. eviction).</summary>
    internal int ActiveTopicCount => _topics.Count;

    /// <summary>Wake the loop to rebuild every active topic (any topic may have changed).</summary>
    public void WakeAll() => _wake.Writer.TryWrite(0);

    /// <summary>
    /// Wake the loop for a change to a single topic. The loop currently rebuilds all active topics
    /// and coalesces unchanged ones to no-ops, so this is equivalent to <see cref="WakeAll"/> today;
    /// the topic argument is accepted for a future targeted-rebuild optimization.
    /// </summary>
    public void Wake(StreamTopic topic) => _wake.Writer.TryWrite(0);

    /// <summary>
    /// Subscribe to a topic's coalesced payload feed. Returns null when the session's
    /// concurrent-stream cap is exceeded. Dispose the subscription to unsubscribe and release the
    /// session's stream slot.
    /// </summary>
    public StreamSubscription<TPayload>? TrySubscribe(StreamTopic topic, string sessionId)
    {
        if (!_registry.TryReserve(sessionId))
        {
            return null;
        }

        var subscriber = new Subscriber(_subscriberChannelCapacity);

        TopicState state;
        while (true)
        {
            state = _topics.GetOrAdd(topic.Key, _ => new TopicState(topic));
            lock (state.Gate)
            {
                if (state.Evicted)
                {
                    // Raced with eviction of this exact state — GetOrAdd a fresh one and retry.
                    continue;
                }

                state.Subscribers[subscriber] = 0;
                break;
            }
        }

        // Seed the new subscriber with the current payload so it has data immediately.
        var initial = BuildPayload(topic);
        if (initial is not null)
        {
            subscriber.Writer.TryWrite(initial);
        }

        return new StreamSubscription<TPayload>(subscriber.Reader, () =>
        {
            if (subscriber.TryClaimDispose())
            {
                RemoveSubscriber(topic, state, subscriber);
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
            // Empty topics are skipped (and, when eviction is off, retained) — no wasted build.
            if (state.Subscribers.IsEmpty)
            {
                continue;
            }

            var payload = BuildPayload(state.Topic);
            if (payload is null || (state.Last is not null && _comparer.Equals(state.Last, payload)))
            {
                continue;
            }

            state.Last = payload;
            foreach (var subscriber in state.Subscribers.Keys)
            {
                // Bounded drop-oldest: latest payload wins, never blocks the loop.
                subscriber.Writer.TryWrite(payload);
            }
        }
    }

    private void RemoveSubscriber(StreamTopic topic, TopicState state, Subscriber subscriber)
    {
        lock (state.Gate)
        {
            state.Subscribers.TryRemove(subscriber, out _);
            subscriber.Writer.TryComplete();

            if (_evictEmptyTopics && state.Subscribers.IsEmpty && !state.Evicted)
            {
                state.Evicted = true;
                // Remove only if this exact state is still mapped (a concurrent subscribe that raced
                // and lost would have created a different state instance under the same key).
                ((ICollection<KeyValuePair<string, TopicState>>)_topics)
                    .Remove(new KeyValuePair<string, TopicState>(topic.Key, state));
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

                if (_coalesceIntervalMs > 0)
                {
                    await Task.Delay(_coalesceIntervalMs, cancellationToken).ConfigureAwait(false);
                    while (_wake.Reader.TryRead(out _))
                    {
                    }
                }

                try
                {
                    PublishPending();
                }
                catch (Exception ex)
                {
                    // A single publish failure must never terminate the fan-out loop,
                    // but a persistent one must not stall a topic invisibly either.
                    LogFailureRateLimited(ex, "Stream fan-out publish round failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private TPayload? BuildPayload(StreamTopic topic)
    {
        try
        {
            return _build(topic);
        }
        catch (Exception ex)
        {
            // A build failure must not tear down the loop or the subscriber.
            LogFailureRateLimited(ex, $"Stream payload build failed for topic '{topic.Key}'");
            return null;
        }
    }

    private void LogFailureRateLimited(Exception ex, string message)
    {
        if (_logger is null)
        {
            return;
        }

        var now = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref _lastFailureLogTicks);
        if (now - last < FailureLogInterval.Ticks ||
            Interlocked.CompareExchange(ref _lastFailureLogTicks, now, last) != last)
        {
            return;
        }

        _logger.LogWarning(ex, "{Message} (further failures suppressed for {Interval})", message, FailureLogInterval);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

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

        /// <summary>Guards subscriber add vs. eviction so a subscribe never attaches to a removed state.</summary>
        public object Gate { get; } = new();

        public bool Evicted { get; set; }

        public ConcurrentDictionary<Subscriber, byte> Subscribers { get; } = new();

        public TPayload? Last { get; set; }
    }

    private sealed class Subscriber
    {
        private readonly Channel<TPayload> _channel;
        private int _disposed;

        public Subscriber(int capacity)
        {
            _channel = Channel.CreateBounded<TPayload>(
                new BoundedChannelOptions(capacity <= 0 ? 1 : capacity)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = false
                });
        }

        public ChannelReader<TPayload> Reader => _channel.Reader;

        public ChannelWriter<TPayload> Writer => _channel.Writer;

        public bool TryClaimDispose() => Interlocked.Exchange(ref _disposed, 1) == 0;
    }
}

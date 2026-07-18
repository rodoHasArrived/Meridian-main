using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meridian.Contracts.Api;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Streaming;

/// <summary>
/// Event-driven quote fan-out. A thin quote-typed configuration of <see cref="StreamBroadcaster{T}"/>:
/// woken by the collector via <see cref="NotifyQuoteChanged"/>, it rebuilds each active topic's
/// snapshot on the shared coalescing loop (latest-wins) and pushes it to every subscriber of that
/// topic. One snapshot build is shared across all subscribers of a topic, and the market-data storage
/// hot path is untouched: <see cref="NotifyQuoteChanged"/> only sets a wake signal.
/// </summary>
public sealed class QuoteStreamBroadcaster : IQuoteStreamBroadcaster, IAsyncDisposable
{
    private readonly StreamBroadcaster<QuotesSnapshotResponse> _engine;

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
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(snapshotFactory);

        _engine = new StreamBroadcaster<QuotesSnapshotResponse>(
            registry,
            topic => snapshotFactory(services, topic.SymbolFilter),
            QuotesSnapshotResponseComparer.Instance,
            options.CoalesceIntervalMs,
            options.SubscriberChannelCapacity,
            // Quote topics are a bounded universe of canonical symbol sets — retain empty topics
            // (the shipped skip-build-when-empty optimization; also avoids a subscribe/evict race).
            evictEmptyTopics: false,
            logger: services.GetService<ILoggerFactory>()?.CreateLogger<QuoteStreamBroadcaster>());
    }

    /// <inheritdoc />
    public void NotifyQuoteChanged(string symbol) => _engine.WakeAll();

    /// <inheritdoc />
    public QuoteStreamSubscription? TrySubscribe(StreamTopic topic, string sessionId)
    {
        var subscription = _engine.TrySubscribe(topic, sessionId);
        return subscription is null
            ? null
            : new QuoteStreamSubscription(subscription.Reader, subscription.DisposeAsync);
    }

    /// <summary>Test hook: drive one coalesced fan-out round without waiting for the loop timer.</summary>
    internal void PublishPending() => _engine.PublishPending();

    public ValueTask DisposeAsync() => _engine.DisposeAsync();

    /// <summary>
    /// Coalesce comparison for quote snapshots: two snapshots are equal when their quote rows are
    /// equal, so a change to only the envelope timestamp does not force a push (parity with the prior
    /// per-connection <c>QuoteRowsEqual</c> diff).
    /// </summary>
    private sealed class QuotesSnapshotResponseComparer : IEqualityComparer<QuotesSnapshotResponse>
    {
        public static readonly QuotesSnapshotResponseComparer Instance = new();

        public bool Equals(QuotesSnapshotResponse? x, QuotesSnapshotResponse? y)
        {
            var previous = x?.Quotes;
            var current = y?.Quotes;
            if (previous is null || current is null)
            {
                return previous is null && current is null;
            }

            if (previous.Count != current.Count)
            {
                return false;
            }

            for (var i = 0; i < current.Count; i++)
            {
                // Null-safe element comparison (records are reference types).
                if (!EqualityComparer<QuotesSnapshotItem>.Default.Equals(current[i], previous[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(QuotesSnapshotResponse obj) => obj.Quotes?.Count ?? 0;
    }
}

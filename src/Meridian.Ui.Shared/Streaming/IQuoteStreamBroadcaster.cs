using Meridian.Domain.Collectors;

namespace Meridian.Ui.Shared.Streaming;

/// <summary>
/// Fan-out hub for workstation quote streams. Implements <see cref="IQuoteUpdateNotifier"/> so the
/// collector can wake it on quote changes, and hands SSE connections a per-topic, coalesced snapshot
/// feed via <see cref="TrySubscribe"/>.
/// </summary>
public interface IQuoteStreamBroadcaster : IQuoteUpdateNotifier
{
    /// <summary>
    /// Subscribe to a topic's coalesced snapshot feed. Returns null when the session's
    /// concurrent-stream cap is exceeded. Dispose the subscription to unsubscribe and release the
    /// session's stream slot.
    /// </summary>
    QuoteStreamSubscription? TrySubscribe(StreamTopic topic, string sessionId);
}

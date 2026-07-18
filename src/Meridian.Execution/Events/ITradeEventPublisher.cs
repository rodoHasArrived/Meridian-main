namespace Meridian.Execution.Events;

/// <summary>
/// Abstraction for publishing <see cref="TradeExecutedEvent"/> to interested consumers.
/// Decouples the portfolio/execution layer from the double-entry accounting layer so each
/// can evolve and scale independently.
/// </summary>
public interface ITradeEventPublisher
{
    /// <summary>
    /// Publishes a <see cref="TradeExecutedEvent"/> to all registered consumers.
    /// Implementations must be thread-safe and must never drop events; they may block the
    /// caller to apply backpressure when internal buffers are full.
    /// </summary>
    /// <param name="tradeEvent">The trade event to publish.</param>
    void Publish(TradeExecutedEvent tradeEvent);

    /// <summary>
    /// Asynchronous variant of <see cref="Publish"/> with identical durability and
    /// backpressure semantics. Async callers (e.g. the fill-processing pipeline) should
    /// prefer this so buffer backpressure awaits instead of blocking a thread.
    /// </summary>
    Task PublishAsync(TradeExecutedEvent tradeEvent)
    {
        Publish(tradeEvent);
        return Task.CompletedTask;
    }
}

/// <summary>
/// A trade-event publisher bound to one exact durable accounting scope.
/// Consumers use this contract to reject recovery stores from another book or period.
/// </summary>
public interface IScopedTradeEventPublisher : ITradeEventPublisher
{
    /// <summary>The exact ledger posting scope owned by this publisher.</summary>
    string PostingScope { get; }
}

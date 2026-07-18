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
}

/// <summary>
/// A trade-event publisher bound to one durable accounting scope.
/// Consumers use this contract to reject recovery stores from another book or period.
/// </summary>
public interface IScopedTradeEventPublisher : ITradeEventPublisher
{
    /// <summary>The durable ledger posting scope label owned by this publisher.</summary>
    string PostingScope { get; }

    /// <summary>
    /// Gets the accounting identity owned by this publisher. Legacy implementations that expose
    /// only <see cref="PostingScope"/> remain label-only and can compose only with another
    /// label-only participant.
    /// </summary>
    TradeFillPostingScopeIdentity ScopeIdentity => new(PostingScope);
}

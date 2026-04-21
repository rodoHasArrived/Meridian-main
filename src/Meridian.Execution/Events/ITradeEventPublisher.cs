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
    /// Implementations must be thread-safe and non-blocking from the caller's perspective.
    /// </summary>
    /// <param name="tradeEvent">The trade event to publish.</param>
    void Publish(TradeExecutedEvent tradeEvent);
}

namespace Meridian.Execution.Events;

/// <summary>
/// Assembly-internal recovery port for events loaded from an independently retained handoff.
/// The accounting coordinator supplies the store's complete validated accounting identity;
/// consumers must reject foreign scopes before accepting historical event contracts.
/// New broker fills always use <see cref="ITradeEventPublisher.PublishAsync"/>.
/// </summary>
internal interface IRetainedTradeEventPublisher
{
    Task PublishRecoveredAsync(
        TradeExecutedEvent tradeEvent,
        TradeFillPostingScopeIdentity retainedScopeIdentity);
}

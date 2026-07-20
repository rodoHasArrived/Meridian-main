using Meridian.Contracts.Domain.Events;

namespace Meridian.Execution.Live;

/// <summary>
/// Provider-agnostic live market event carried into execution-time consumers.
/// Wraps the shared contract payload types (<c>Trade</c>, <c>BboQuotePayload</c>,
/// <c>HistoricalBar</c>, <c>LOBSnapshot</c>) without exposing pipeline or provider types.
/// </summary>
public sealed record LiveMarketEvent(
    DateTimeOffset Timestamp,
    string Symbol,
    MarketEventPayload Payload);

/// <summary>
/// In-process subscription surface for the live market event stream. The hosted app
/// publishes every pipeline event into this feed so live strategy sessions can consume
/// real-time events the same way <c>BacktestEngine</c> consumes replayed events.
/// </summary>
public interface ILiveMarketEventFeed
{
    /// <summary>
    /// Subscribes to live events for the given symbols. The returned stream completes
    /// when <paramref name="ct"/> is cancelled. Slow consumers drop the oldest queued
    /// events rather than back-pressuring the market data pipeline.
    /// </summary>
    IAsyncEnumerable<LiveMarketEvent> SubscribeAsync(
        IReadOnlyCollection<string> symbols,
        CancellationToken ct = default);
}

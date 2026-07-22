using Meridian.Infrastructure.Resilience;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Provider contract for observing data gaps caused by WebSocket disconnect/reconnect cycles.
/// </summary>
/// <remarks>
/// Streaming adapters whose lifecycle is supervised by <see cref="WebSocketConnectionManager"/>
/// surface the manager's <see cref="WebSocketConnectionManager.GapDetected"/> event through this
/// contract so gap backfill can be triggered without reaching into provider internals.
/// </remarks>
public interface IReconnectionGapSource
{
    /// <summary>
    /// Raised after a reconnection completes, carrying the window during which streaming
    /// data may have been missed. Subscribers should trigger backfill covering the window.
    /// </summary>
    event Action<ReconnectionGap>? ReconnectionGapDetected;
}

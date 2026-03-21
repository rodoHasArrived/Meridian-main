using System.Collections.Concurrent;

namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

/// <summary>
/// IB connection manager that owns the socket/EReader loop and forwards raw callbacks into <see cref="IBCallbackRouter"/>.
///
/// This file is buildable out-of-the-box:
/// - When compiled WITHOUT the official IB API reference, it exposes a small stub implementation.
/// - When compiled WITH the IB API (define the compilation constant IBAPI and reference IBApi),
///   it provides a full EWrapper implementation with depth routing.
///
/// Connection Resilience (IBAPI build):
/// - Uses ExponentialBackoffRetry for connection retry with configurable delays (1s initial, 2min max)
/// - Implements HeartbeatMonitor for connection health detection
/// - Automatic reconnection on connection loss with jitter to prevent thundering herd
/// - Unlimited retry attempts by default (configurable via maxRetries parameter)
/// - See EnhancedIBConnectionManager.IBApi.cs and Infrastructure/Performance/ConnectionWarmUp.cs for implementation
/// </summary>
public sealed partial class EnhancedIBConnectionManager
{
#if !IBAPI
    private readonly IBCallbackRouter _router;

    public EnhancedIBConnectionManager(IBCallbackRouter router)
    {
        _router = router;
    }

    public bool IsConnected => false;

    /// <summary>
    /// Centralizes the platform guard used by the non-IBAPI build so each stub entry point stays concise.
    /// </summary>
    private static Exception ThrowPlatformNotSupported() => new NotSupportedException(
        "Build with IBAPI defined and reference the official IBApi package/dll. " +
        "The IBAPI build includes exponential backoff retry, heartbeat monitoring, and automatic reconnection.");

    public Task ConnectAsync() => Task.FromException(ThrowPlatformNotSupported());

    public Task DisconnectAsync() => Task.CompletedTask;

    public int SubscribeMarketDepth(string symbol, int depthLevels = 10) => throw ThrowPlatformNotSupported();

    public int SubscribeMarketDepth(SymbolConfig cfg, bool smartDepth = true)
        => throw ThrowPlatformNotSupported();

    public int SubscribeTrades(SymbolConfig cfg)
        => throw ThrowPlatformNotSupported();

    public void UnsubscribeTrades(int tickerId)
        => throw ThrowPlatformNotSupported();

    public void UnsubscribeMarketDepth(int tickerId) => throw ThrowPlatformNotSupported();
#endif
}

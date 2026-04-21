using System.Collections.Concurrent;
using Meridian.Execution.Sdk;

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
public sealed partial class EnhancedIBConnectionManager : IIBBrokerageClient
{
#if !IBAPI
    private readonly IBCallbackRouter _router;

    public EnhancedIBConnectionManager(
        IBCallbackRouter router,
        string host = "127.0.0.1",
        int port = 7497,
        int clientId = 1,
        bool enableAutoReconnect = true,
        bool enableHeartbeat = true)
    {
        _router = router;
        Host = host;
        Port = port;
        ClientId = clientId;
        EnableAutoReconnect = enableAutoReconnect;
        EnableHeartbeat = enableHeartbeat;
    }

    public string Host { get; }
    public int Port { get; }
    public int ClientId { get; }
    public bool EnableAutoReconnect { get; set; }
    public bool EnableHeartbeat { get; set; }
    public bool IsConnected => false;

    public event EventHandler<int>? NextValidIdReceived;
    public event EventHandler<IBOrderStatusUpdate>? OrderStatusReceived;
    public event EventHandler<IBOpenOrderUpdate>? OpenOrderReceived;
    public event EventHandler? OpenOrdersCompleted;
    public event EventHandler<IBExecutionUpdate>? ExecutionDetailsReceived;
    public event EventHandler<IBPositionUpdate>? PositionReceived;
    public event EventHandler? PositionsCompleted;
    public event EventHandler<IBAccountSummaryUpdate>? AccountSummaryReceived;
    public event EventHandler<int>? AccountSummaryCompleted;
    public event EventHandler<IBApiError>? ErrorOccurred;

    /// <summary>
    /// Centralizes the platform guard used by the non-IBAPI build so each stub entry point stays concise.
    /// </summary>
    private static Exception ThrowPlatformNotSupported() => new NotSupportedException(
        IBBuildGuidance.BuildRealProviderMessage("EnhancedIBConnectionManager")
        + " The IBAPI build includes exponential backoff retry, heartbeat monitoring, and automatic reconnection.");

    public Task ConnectAsync(CancellationToken ct = default) => Task.FromException(ThrowPlatformNotSupported());

    public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    public int SubscribeMarketDepth(string symbol, int depthLevels = 10) => throw ThrowPlatformNotSupported();

    public int SubscribeMarketDepth(SymbolConfig cfg, bool smartDepth = true)
        => throw ThrowPlatformNotSupported();

    public int SubscribeTrades(SymbolConfig cfg)
        => throw ThrowPlatformNotSupported();

    public void UnsubscribeTrades(int tickerId)
        => throw ThrowPlatformNotSupported();

    public void UnsubscribeMarketDepth(int tickerId) => throw ThrowPlatformNotSupported();

    public void RequestNextValidId() => throw ThrowPlatformNotSupported();

    public Task PlaceOrderAsync(int orderId, OrderRequest request, CancellationToken ct = default)
        => Task.FromException(ThrowPlatformNotSupported());

    public Task CancelOrderAsync(int orderId, CancellationToken ct = default)
        => Task.FromException(ThrowPlatformNotSupported());

    public int RequestAccountSummary() => throw ThrowPlatformNotSupported();

    public void CancelAccountSummary(int requestId) => throw ThrowPlatformNotSupported();

    public void RequestPositions() => throw ThrowPlatformNotSupported();

    public void CancelPositions() => throw ThrowPlatformNotSupported();

    public void RequestOpenOrders() => throw ThrowPlatformNotSupported();

    public void Dispose()
    {
    }
#endif
}

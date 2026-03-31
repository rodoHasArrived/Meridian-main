using System.Net.WebSockets;
using System.Threading;
using Meridian.Application.Logging;
using Meridian.Application.Monitoring;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Resilience;
using Meridian.Infrastructure.Shared;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Abstract base class for WebSocket-based streaming market data providers.
/// Consolidates connection lifecycle, heartbeat monitoring, and automatic reconnection
/// logic that was previously duplicated across Alpaca, Polygon, NYSE, and StockSharp.
///
/// Derived classes implement protocol-specific hooks:
/// <list type="bullet">
///   <item><see cref="AuthenticateAsync"/> — send provider-specific auth messages</item>
///   <item><see cref="HandleMessageAsync"/> — parse and route incoming messages</item>
///   <item><see cref="ResubscribeAsync"/> — re-subscribe symbols after reconnection</item>
///   <item><see cref="BuildWebSocketUri"/> — construct the provider-specific endpoint URI</item>
///   <item><see cref="ConfigureWebSocket"/> — set provider-specific headers/options</item>
/// </list>
/// </summary>
/// <remarks>
/// Connection management is delegated to <see cref="WebSocketConnectionManager"/>
/// which provides resilience (retry + circuit breaker), heartbeat monitoring,
/// and gated reconnection to prevent reconnection storms.
/// </remarks>
[ImplementsAdr("ADR-001", "Unified WebSocket provider base class for streaming data providers")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
public abstract class WebSocketProviderBase : IMarketDataClient
{
    private readonly WebSocketConnectionManager _connectionManager;
    private Uri? _wsUri;

    /// <summary>
    /// Logger instance for the derived provider.
    /// </summary>
    protected readonly ILogger Log;

    /// <summary>
    /// Centralized subscription manager with provider-specific ID range.
    /// </summary>
    protected readonly SubscriptionManager Subscriptions;

    /// <summary>
    /// Whether the connection is currently established.
    /// </summary>
    protected bool Connected => _connectionManager.IsConnected;

    /// <summary>
    /// Creates a new WebSocket provider base.
    /// </summary>
    /// <param name="providerName">Display name for logging.</param>
    /// <param name="config">WebSocket connection configuration profile.</param>
    /// <param name="subscriptionStartId">Starting subscription ID for this provider's range.</param>
    /// <param name="logger">Optional logger instance.</param>
    protected WebSocketProviderBase(
        string providerName,
        WebSocketConnectionConfig? config = null,
        int subscriptionStartId = 1,
        ILogger? logger = null)
    {
        Log = logger ?? LoggingSetup.ForContext(GetType());
        Subscriptions = new SubscriptionManager(startingId: subscriptionStartId);

        _connectionManager = new WebSocketConnectionManager(
            providerName,
            config ?? WebSocketConnectionConfig.Default,
            Log);

        _connectionManager.ConnectionLost += OnConnectionLostAsync;
    }


    /// <inheritdoc/>
    public abstract bool IsEnabled { get; }

    /// <inheritdoc/>
    public virtual async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_connectionManager.IsConnected)
            return;

        _wsUri = BuildWebSocketUri();

        await _connectionManager.ConnectAsync(
            _wsUri,
            configureSocket: ConfigureWebSocket,
            ct: ct).ConfigureAwait(false);

        MigrationDiagnostics.IncStreamingFactoryHit(ProviderId);

        await AuthenticateAsync(ct).ConfigureAwait(false);

        _connectionManager.StartReceiveLoop(HandleMessageAsync, ct);

        Log.Information("{Provider} connected and receive loop started", ProviderId);
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await _connectionManager.DisconnectAsync(ct).ConfigureAwait(false);
        Subscriptions.Clear();
    }

    /// <inheritdoc/>
    public abstract int SubscribeMarketDepth(SymbolConfig cfg);

    /// <inheritdoc/>
    public abstract void UnsubscribeMarketDepth(int subscriptionId);

    /// <inheritdoc/>
    public abstract int SubscribeTrades(SymbolConfig cfg);

    /// <inheritdoc/>
    public abstract void UnsubscribeTrades(int subscriptionId);



    /// <inheritdoc/>
    public abstract string ProviderId { get; }

    /// <inheritdoc/>
    public abstract string ProviderDisplayName { get; }

    /// <inheritdoc/>
    public abstract string ProviderDescription { get; }

    /// <inheritdoc/>
    public abstract int ProviderPriority { get; }

    /// <inheritdoc/>
    public abstract ProviderCapabilities ProviderCapabilities { get; }



    /// <summary>
    /// Builds the WebSocket endpoint URI for this provider.
    /// </summary>
    protected abstract Uri BuildWebSocketUri();

    /// <summary>
    /// Sends provider-specific authentication messages after WebSocket connection.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    protected abstract Task AuthenticateAsync(CancellationToken ct);

    /// <summary>
    /// Handles an incoming WebSocket text message.
    /// Implementations should parse JSON and route to appropriate collectors.
    /// </summary>
    /// <param name="message">The raw text message from the WebSocket.</param>
    protected abstract Task HandleMessageAsync(string message);

    /// <summary>
    /// Re-subscribes to all active symbols after a reconnection.
    /// Called automatically when the connection manager reconnects successfully.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    protected abstract Task ResubscribeAsync(CancellationToken ct);

    /// <summary>
    /// Optionally configures the <see cref="ClientWebSocket"/> before connecting
    /// (e.g., setting authorization headers, sub-protocols).
    /// Default implementation does nothing.
    /// </summary>
    /// <param name="webSocket">The WebSocket to configure.</param>
    protected virtual void ConfigureWebSocket(ClientWebSocket webSocket) { }



    /// <summary>
    /// Sends a text message through the WebSocket connection.
    /// </summary>
    protected Task SendAsync(string message, CancellationToken ct = default)
        => _connectionManager.SendAsync(message, ct);

    /// <summary>
    /// Reads a single text message directly from the WebSocket.
    /// Call only during <see cref="AuthenticateAsync"/> — before the receive loop starts —
    /// to handle initial handshake messages (e.g., connection confirmation, auth response).
    /// </summary>
    protected Task<string?> ReadOneMessageAsync(CancellationToken ct = default)
        => _connectionManager.ReadOneMessageAsync(ct);

    /// <summary>
    /// Records heartbeat activity. Call this when data is received to prevent
    /// stale connection detection.
    /// </summary>
    protected void RecordActivity()
        => _connectionManager.RecordPongReceived();



    private Task OnConnectionLostAsync()
        => OnConnectionLostAsync(CancellationToken.None);

    private async Task OnConnectionLostAsync(CancellationToken ct)
    {
        if (_wsUri == null)
            return;

        MigrationDiagnostics.IncReconnectAttempt(ProviderId);

        var success = await _connectionManager.TryReconnectAsync(
            _wsUri,
            configureSocket: ConfigureWebSocket,
            onReconnected: async () =>
            {
                await AuthenticateAsync(ct).ConfigureAwait(false);
                _connectionManager.StartReceiveLoop(HandleMessageAsync, ct);
                await ResubscribeAsync(ct).ConfigureAwait(false);

                MigrationDiagnostics.IncResubscribeAttempt();
                MigrationDiagnostics.IncResubscribeSuccess();
            },
            ct: ct).ConfigureAwait(false);

        if (success)
            MigrationDiagnostics.IncReconnectSuccess(ProviderId);
        else
            MigrationDiagnostics.IncReconnectFailure(ProviderId);
    }



    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        _connectionManager.ConnectionLost -= OnConnectionLostAsync;
        await _connectionManager.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

}

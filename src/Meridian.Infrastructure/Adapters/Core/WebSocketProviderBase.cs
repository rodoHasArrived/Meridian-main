using System.Net.WebSockets;
using System.Threading;
using Meridian.Core.Logging;
using Meridian.Core.Monitoring;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Resilience;
using Meridian.Infrastructure.Shared;
using Meridian.ProviderSdk;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Abstract base class for WebSocket-based streaming market data providers.
/// Consolidates connection lifecycle, heartbeat monitoring, and automatic reconnection
/// logic that was previously duplicated across Alpaca, Polygon, and NYSE.
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
public abstract class WebSocketProviderBase :
    IMarketDataClient,
    IProviderConnectionDiagnosticsSource,
    IProviderRateLimitDiagnosticsSource
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
    /// Whether the WebSocket transport is open for protocol hooks. Operator-facing
    /// diagnostics remain non-connected until authentication and replay complete.
    /// </summary>
    protected bool Connected => _connectionManager.IsTransportConnected;

    /// <inheritdoc/>
    public event Action<WebSocketConnectionDiagnostics>? ConnectionDiagnosticsChanged
    {
        add => _connectionManager.DiagnosticsChanged += value;
        remove => _connectionManager.DiagnosticsChanged -= value;
    }

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
    public WebSocketConnectionDiagnostics GetConnectionDiagnosticsSnapshot()
    {
        var connection = _connectionManager.GetDiagnosticsSnapshot();
        var subscriptions = Subscriptions.GetSnapshot();

        return connection with
        {
            ActiveSubscriptions = subscriptions.TotalSubscriptions,
            FailedSubscriptions = subscriptions.FailedSubscriptions,
            RecoveringSubscriptions = subscriptions.RecoveringSubscriptions,
            LastSubscriptionMessageAt = GetLastSubscriptionMessageAt(subscriptions)
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The shared WebSocket transport does not receive coherent request-window counters from
    /// Alpaca, Polygon, or other derived feeds. Expose that absence explicitly so operator
    /// endpoints include the streaming surface without presenting configured capacity as live
    /// runtime evidence. A derived provider may override this method when it owns a real tracker.
    /// </remarks>
    public virtual ProviderRateLimitDiagnosticSnapshot GetRateLimitDiagnosticsSnapshot()
    {
        return new ProviderRateLimitDiagnosticSnapshot(
            ProviderId: ProviderId,
            Surface: ProviderRateLimitSurfaces.Streaming,
            ObservedAt: DateTimeOffset.UtcNow,
            RequestsInWindow: 0,
            // Capabilities expose these as nullable; this "diagnostics unavailable" snapshot marks
            // StateAvailable: false, so an unset limit collapses to a neutral zero placeholder.
            MaxRequestsPerWindow: ProviderCapabilities.MaxRequestsPerWindow ?? 0,
            Window: ProviderCapabilities.RateLimitWindow ?? TimeSpan.Zero,
            IsRateLimited: false,
            ResetAt: null,
            UsageRatio: 0,
            Reason: "runtime-diagnostics-unavailable",
            StateAvailable: false);
    }

    /// <inheritdoc/>
    public virtual async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_connectionManager.IsConnected)
            return;

        _wsUri = BuildWebSocketUri();

        await _connectionManager.ConnectAsync(
            _wsUri,
            configureSocket: ConfigureWebSocket,
            initializeConnection: async token =>
            {
                await AuthenticateAsync(token).ConfigureAwait(false);
                _connectionManager.StartReceiveLoop(HandleMessageAsync);
            },
            ct: ct).ConfigureAwait(false);

        MigrationDiagnostics.IncStreamingFactoryHit(ProviderId);

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



    private async Task OnConnectionLostAsync()
    {
        if (_wsUri == null)
            return;

        MigrationDiagnostics.IncReconnectAttempt(ProviderId);

        var success = await _connectionManager.TryReconnectAsync(
            _wsUri,
            configureSocket: ConfigureWebSocket,
            initializeConnection: async token =>
            {
                await AuthenticateAsync(token).ConfigureAwait(false);
                _connectionManager.StartReceiveLoop(HandleMessageAsync);
                await ResubscribeAsync(token).ConfigureAwait(false);

                MigrationDiagnostics.IncResubscribeAttempt();
                MigrationDiagnostics.IncResubscribeSuccess();
            },
            ct: CancellationToken.None).ConfigureAwait(false);

        if (success)
            MigrationDiagnostics.IncReconnectSuccess(ProviderId);
        else
            MigrationDiagnostics.IncReconnectFailure(ProviderId);
    }

    private static DateTimeOffset? GetLastSubscriptionMessageAt(SubscriptionSnapshot snapshot)
    {
        DateTimeOffset? latest = null;
        foreach (var subscription in snapshot.Subscriptions)
        {
            if (subscription.LastMessageAt is not { } lastMessageAt)
                continue;

            if (latest is null || lastMessageAt > latest.Value)
                latest = lastMessageAt;
        }

        return latest;
    }



    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        _connectionManager.ConnectionLost -= OnConnectionLostAsync;
        await _connectionManager.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

}

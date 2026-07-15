using System.Net.WebSockets;
using System.Net;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using Meridian.Core.Logging;
using Polly;
using Serilog;

namespace Meridian.Infrastructure.Resilience;

/// <summary>
/// Centralized WebSocket connection manager that handles connection lifecycle,
/// resilience, heartbeat monitoring, and automatic reconnection.
///
/// This class eliminates duplicate connection management code across providers
/// (Alpaca, Polygon, NYSE) by centralizing:
/// - Connection with resilience pipeline (retry + circuit breaker)
/// - Heartbeat monitoring for stale connection detection
/// - Reconnection gating (prevents reconnection storms)
/// - Clean disposal and state management
/// </summary>
/// <remarks>
/// Based on patterns from DataSourceBase, Marfusios/websocket-client, and
/// production WebSocket implementations.
/// </remarks>
public sealed class WebSocketConnectionManager : IAsyncDisposable
{
    private static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(10);
    private readonly WebSocketConnectionConfig _config;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly ProviderConnectionSupervisor _supervisor;
    private readonly ILogger _log;
    private readonly string _providerName;
    private readonly TimeSpan _shutdownTimeout;
    private readonly object _disposeSync = new();

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _connectionCts;
    private CancellationTokenSource? _receiveLoopCts;
    private Task? _receiveTask;
    private WebSocketHeartbeat? _heartbeat;
    private Task? _disposeTask;

    // Test seam for proving bounded cleanup when a heartbeat implementation ignores
    // shutdown. Production always uses WebSocketHeartbeat.DisposeAsync directly.
    internal Func<WebSocketHeartbeat, Task> HeartbeatDisposer { get; set; }
        = static heartbeat => heartbeat.DisposeAsync().AsTask();

    // Transport activity complements the supervisor's lifecycle diagnostics.
    private DateTimeOffset? _lastMessageReceivedAt;
    private DateTimeOffset? _lastHeartbeatReceivedAt;
    private int _lastPublishedLifecycleState = (int)ProviderConnectionLifecycleState.Configured;
    private int _disposed;

    /// <summary>
    /// Event raised when connection is lost (heartbeat timeout or WebSocket close).
    /// Subscribers should handle reconnection logic if desired.
    /// </summary>
    public event Func<Task>? ConnectionLost;

    /// <summary>
    /// Event raised after a successful reconnection (including any onReconnected callback).
    /// Subscribers can use this for monitoring/logging reconnection events.
    /// The int parameter is the number of reconnect attempts it took.
    /// </summary>
    public event Action<int>? Reconnected;

    /// <summary>
    /// Event raised when a reconnection gap is detected, providing the time window
    /// during which data may have been missed. Subscribers should trigger gap backfill
    /// for all active subscriptions covering this time range.
    /// </summary>
    public event Action<ReconnectionGap>? GapDetected;

    /// <summary>
    /// Event raised when connection state changes.
    /// </summary>
    public event Action<WebSocketState>? StateChanged;

    /// <summary>
    /// Event raised when the provider lifecycle phase changes.
    /// </summary>
    public event Action<WebSocketConnectionDiagnostics>? DiagnosticsChanged;

    /// <summary>
    /// Gets whether the WebSocket is currently connected and open.
    /// </summary>
    public bool IsConnected => _supervisor.IsConnected && IsTransportConnected;

    internal bool IsTransportConnected => _webSocket?.State == WebSocketState.Open;

    /// <summary>
    /// Gets the current WebSocket state.
    /// </summary>
    public WebSocketState State => _webSocket?.State ?? WebSocketState.None;

    /// <summary>
    /// Gets whether a reconnection is currently in progress.
    /// </summary>
    public bool IsReconnecting => _supervisor.IsReconnecting;

    /// <summary>
    /// Gets the current provider lifecycle phase.
    /// </summary>
    public ProviderConnectionLifecycleState LifecycleState => _supervisor.LifecycleState;

    /// <summary>
    /// Gets a safe diagnostics snapshot for logs, health surfaces, and tests.
    /// </summary>
    public WebSocketConnectionDiagnostics GetDiagnosticsSnapshot()
    {
        var supervisor = _supervisor.GetSnapshot();
        var now = DateTimeOffset.UtcNow;
        var lastActivity = _lastMessageReceivedAt ?? supervisor.LastConnectedAt;

        return new WebSocketConnectionDiagnostics(
            ProviderName: _providerName,
            LifecycleState: supervisor.LifecycleState,
            WebSocketState: State,
            IsConnected: IsConnected,
            IsReconnecting: supervisor.IsReconnecting,
            ReconnectAttempts: supervisor.ReconnectAttempts,
            LastConnectedAt: supervisor.LastConnectedAt,
            LastDisconnectedAt: supervisor.LastDisconnectedAt,
            LastHeartbeatReceivedAt: _lastHeartbeatReceivedAt,
            LastMessageReceivedAt: _lastMessageReceivedAt,
            LastReconnectAttemptAt: supervisor.LastReconnectAttemptAt,
            LastError: supervisor.LastError,
            LastFailureKind: supervisor.LastFailureKind,
            ConnectionAge: supervisor.ConnectionAge,
            IdleDuration: lastActivity.HasValue ? now - lastActivity.Value : null);
    }

    /// <summary>
    /// Creates a new WebSocket connection manager.
    /// </summary>
    /// <param name="providerName">Name of the provider (for logging).</param>
    /// <param name="config">Connection configuration (uses Default if null).</param>
    /// <param name="logger">Optional logger instance.</param>
    public WebSocketConnectionManager(
        string providerName,
        WebSocketConnectionConfig? config = null,
        ILogger? logger = null)
        : this(providerName, config, logger, DefaultShutdownTimeout)
    {
    }

    internal WebSocketConnectionManager(
        string providerName,
        WebSocketConnectionConfig? config,
        ILogger? logger,
        TimeSpan shutdownTimeout)
    {
        if (shutdownTimeout <= TimeSpan.Zero || shutdownTimeout == Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(shutdownTimeout));

        _providerName = providerName ?? throw new ArgumentNullException(nameof(providerName));
        _config = config ?? WebSocketConnectionConfig.Default;
        _log = logger ?? LoggingSetup.ForContext<WebSocketConnectionManager>();
        _shutdownTimeout = shutdownTimeout;
        _supervisor = new ProviderConnectionSupervisor(
            _providerName,
            _config.MaxReconnectAttempts,
            _config.RetryBaseDelay,
            _config.MaxRetryDelay,
            _log);
        _supervisor.StateChanged += OnSupervisorStateChanged;

        // Create resilience pipeline using centralized configuration
        _resiliencePipeline = WebSocketResiliencePolicy.CreateComprehensivePipeline(
            maxRetries: _config.MaxRetries,
            retryBaseDelay: _config.RetryBaseDelay,
            maxRetryDelay: _config.MaxRetryDelay,
            circuitBreakerFailureThreshold: _config.CircuitBreakerFailureThreshold,
            circuitBreakerDuration: _config.CircuitBreakerDuration,
            operationTimeout: _config.OperationTimeout);
    }

    /// <summary>
    /// Connects to the specified WebSocket endpoint with resilience.
    /// A semaphore ensures only one concurrent connection attempt proceeds at a time,
    /// preventing duplicate connections from concurrent callers.
    /// </summary>
    /// <param name="uri">The WebSocket URI to connect to.</param>
    /// <param name="configureSocket">Optional action to configure the ClientWebSocket before connecting.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task ConnectAsync(
        Uri uri,
        Action<ClientWebSocket>? configureSocket = null,
        CancellationToken ct = default)
        => ConnectAsync(uri, configureSocket, initializeConnection: null, ct);

    internal Task ConnectAsync(
        Uri uri,
        Action<ClientWebSocket>? configureSocket,
        Func<CancellationToken, Task>? initializeConnection,
        CancellationToken ct)
        => ConnectAsync(
            uri,
            configureSocket,
            prepareConnection: null,
            initializeConnection,
            ct);

    internal async Task ConnectAsync(
        Uri uri,
        Action<ClientWebSocket>? configureSocket,
        Func<CancellationToken, Task>? prepareConnection,
        Func<CancellationToken, Task>? initializeConnection,
        CancellationToken ct)
    {
        if (IsConnected)
        {
            _log.Debug("{Provider} WebSocket already connected", _providerName);
            return;
        }

        _log.Information("Connecting to {Provider} WebSocket at {Uri}", _providerName, uri);

        await _supervisor.ConnectAsync(
            token => ConnectTransportTransactionAsync(
                uri,
                configureSocket,
                prepareConnection,
                initializeConnection,
                token),
            ct).ConfigureAwait(false);
    }

    private async Task ConnectTransportTransactionAsync(
        Uri uri,
        Action<ClientWebSocket>? configureSocket,
        Func<CancellationToken, Task>? prepareConnection,
        Func<CancellationToken, Task>? initializeConnection,
        CancellationToken ct)
    {
        try
        {
            await CleanupConnectionAsync(CancellationToken.None).ConfigureAwait(false);

            if (prepareConnection != null)
                await prepareConnection(ct).ConfigureAwait(false);

            await _resiliencePipeline.ExecuteAsync(async token =>
            {
                await CleanupConnectionAsync(CancellationToken.None).ConfigureAwait(false);

                _connectionCts = CancellationTokenSource.CreateLinkedTokenSource(
                    _supervisor.ConnectionLifetimeToken);
                _webSocket = new ClientWebSocket();
                configureSocket?.Invoke(_webSocket);

                try
                {
                    await _webSocket.ConnectAsync(uri, token).ConfigureAwait(false);
                }
                catch
                {
                    await CleanupConnectionAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
            }, ct).ConfigureAwait(false);

            _log.Information(
                "{Provider} WebSocket transport connected; completing provider initialization",
                _providerName);

            if (initializeConnection != null)
                await initializeConnection(ct).ConfigureAwait(false);

            if (_webSocket?.State != WebSocketState.Open)
                throw new WebSocketException("WebSocket closed before provider initialization completed.");

            StartHeartbeat();
        }
        catch (OperationCanceledException)
        {
            await CleanupConnectionAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            _supervisor.RecordFailure(ex);
            _log.Warning(
                ex,
                "Complete connection attempt to {Provider} failed. Will retry when policy permits.",
                _providerName);
            await CleanupConnectionAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private void StartHeartbeat()
    {
        if (_webSocket == null)
            throw new InvalidOperationException("WebSocket transport is not connected.");

        _heartbeat = new WebSocketHeartbeat(
            _webSocket,
            _config.HeartbeatInterval,
            _config.HeartbeatTimeout);
        _lastHeartbeatReceivedAt = _heartbeat.LastPongReceived;
        _heartbeat.ConnectionLost += OnConnectionLostAsync;
    }

    /// <summary>
    /// Starts the receive loop with the provided message handler.
    /// </summary>
    /// <param name="messageHandler">Handler called for each received text message.</param>
    /// <param name="ct">Cancellation token.</param>
    public void StartReceiveLoop(Func<string, Task> messageHandler, CancellationToken ct = default)
    {
        if (_webSocket == null)
            throw new InvalidOperationException("WebSocket not connected. Call ConnectAsync first.");

        // Dispose previous receive loop CTS if any
        _receiveLoopCts?.Dispose();

        _receiveLoopCts = _connectionCts != null
            ? CancellationTokenSource.CreateLinkedTokenSource(_connectionCts.Token, ct)
            : CancellationTokenSource.CreateLinkedTokenSource(ct);

        var token = _receiveLoopCts.Token;
        _receiveTask = ReceiveLoopAsync(messageHandler, token);
    }

    /// <summary>
    /// Reads a single text message from the WebSocket.
    /// Must only be called after <see cref="ConnectAsync(Uri, Action{ClientWebSocket}, CancellationToken)"/> and before
    /// <see cref="StartReceiveLoop"/> — it reads directly from the socket
    /// for initial handshake sequences (e.g., Polygon "connected" + "auth_success").
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The message text, or <c>null</c> if the connection closed.</returns>
    public async Task<string?> ReadOneMessageAsync(CancellationToken ct = default)
    {
        if (_webSocket == null)
            throw new InvalidOperationException("WebSocket not connected. Call ConnectAsync first.");

        // Enforce documented invariant: must only be used before StartReceiveLoop.
        if (_receiveTask != null || _receiveLoopCts != null)
            throw new InvalidOperationException("ReadOneMessageAsync can only be called before StartReceiveLoop is started.");
        var buffer = new byte[4096];
        var sb = new StringBuilder();

        WebSocketReceiveResult result;
        do
        {
            result = await _webSocket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            if (result.MessageType == WebSocketMessageType.Text)
                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        }
        while (!result.EndOfMessage);

        return sb.ToString();
    }

    /// <summary>
    /// Sends a text message through the WebSocket.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task SendAsync(string message, CancellationToken ct = default)
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            _log.Warning("Cannot send message - {Provider} WebSocket not open (state: {State})",
                _providerName, _webSocket?.State);
            throw new WebSocketException(
                $"Cannot send message because the {_providerName} WebSocket transport is not open.");
        }

        var bytes = Encoding.UTF8.GetBytes(message);
        await _webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Disconnects from the WebSocket gracefully. Calls without a caller cancellation token use
    /// the manager shutdown timeout so non-cooperative heartbeat cleanup cannot block forever.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        _log.Information("Disconnecting from {Provider} WebSocket", _providerName);

        if (ct.CanBeCanceled)
        {
            await _supervisor.DisconnectAsync(DisconnectTransportAsync, ct).ConfigureAwait(false);
            _log.Information("Disconnected from {Provider} WebSocket", _providerName);
            return;
        }

        Task? transportCleanupTask = null;
        using var shutdownCts = new CancellationTokenSource(_shutdownTimeout);
        try
        {
            await _supervisor.DisconnectAsync(
                    token => transportCleanupTask = DisconnectTransportAsync(token),
                    shutdownCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (shutdownCts.IsCancellationRequested)
        {
            if (transportCleanupTask is not null)
            {
                // The supervisor may stop waiting as soon as its timeout token is cancelled.
                // Wait for the transport transaction's non-blocking finally block so its
                // detached socket and cancellation sources are released before we return.
                await ObserveForcedCleanupAsync(transportCleanupTask, "disconnect transport")
                    .ConfigureAwait(false);
            }
            else
            {
                // Cancellation can occur while a reconnect operation still owns the supervisor
                // gate, before the transport transaction starts.
                ForceDetachTransport();
            }

            _log.Warning(
                "Timed out disconnecting {Provider}; completed forced transport cleanup",
                _providerName);
        }

        _log.Information("Disconnected from {Provider} WebSocket", _providerName);
    }

    private async Task DisconnectTransportAsync(CancellationToken ct)
    {
        var heartbeat = _heartbeat;
        var connectionCts = _connectionCts;
        var receiveLoopCts = _receiveLoopCts;
        var receiveTask = _receiveTask;
        var webSocket = _webSocket;
        Task? heartbeatDisposeTask = null;

        _heartbeat = null;
        _connectionCts = null;
        _receiveLoopCts = null;
        _receiveTask = null;
        _webSocket = null;

        try
        {
            if (heartbeat != null)
            {
                heartbeat.ConnectionLost -= OnConnectionLostAsync;
                heartbeatDisposeTask = HeartbeatDisposer(heartbeat);
                await heartbeatDisposeTask.WaitAsync(ct).ConfigureAwait(false);
            }

            try
            {
                connectionCts?.Cancel();
                receiveLoopCts?.Cancel();
            }
            catch (Exception ex)
            {
                _log.Debug(ex, "CancellationTokenSource.Cancel failed during {Provider} disconnect", _providerName);
            }

            if (webSocket != null)
            {
                try
                {
                    if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", ct)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    _log.Warning("{Provider} WebSocket close was cancelled; forcing transport cleanup", _providerName);
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "Error during {Provider} WebSocket close", _providerName);
                }
            }

            if (receiveTask != null)
            {
                try
                {
                    await receiveTask.WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    _log.Warning(
                        "Cancellation ended the wait for {Provider} receive loop; forcing transport cleanup",
                        _providerName);
                    throw;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.Debug(ex, "Receive loop completion error during {Provider} disconnect", _providerName);
                }
            }

            ct.ThrowIfCancellationRequested();
        }
        finally
        {
            if (heartbeatDisposeTask is { IsCompleted: false })
                ObserveForcedCleanup(heartbeatDisposeTask, "heartbeat");

            try
            {
                connectionCts?.Cancel();
                receiveLoopCts?.Cancel();
            }
            catch (Exception ex)
            {
                _log.Debug(ex, "Cancellation source failed during final {Provider} transport cleanup", _providerName);
            }

            try
            {
                if (ct.IsCancellationRequested)
                    webSocket?.Abort();
                webSocket?.Dispose();
            }
            catch (Exception ex)
            {
                _log.Debug(ex, "WebSocket failed during final {Provider} transport cleanup", _providerName);
            }

            receiveLoopCts?.Dispose();
            connectionCts?.Dispose();
            if (receiveTask is { IsCompleted: false })
                ObserveForcedCleanup(receiveTask, "receive loop");
        }
    }

    /// <summary>
    /// Attempts automatic reconnection with exponential backoff.
    /// Uses gating to prevent reconnection storms.
    /// </summary>
    /// <param name="uri">The WebSocket URI to reconnect to.</param>
    /// <param name="configureSocket">Optional socket configuration.</param>
    /// <param name="onReconnected">Action to execute after successful reconnection (e.g., resubscribe).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if reconnection succeeded, false otherwise.</returns>
    public Task<bool> TryReconnectAsync(
        Uri uri,
        Action<ClientWebSocket>? configureSocket = null,
        Func<Task>? onReconnected = null,
        CancellationToken ct = default)
        => TryReconnectAsync(
            uri,
            configureSocket,
            onReconnected == null
                ? null
                : new Func<CancellationToken, Task>(_ => onReconnected()),
            ct);

    internal Task<bool> TryReconnectAsync(
        Uri uri,
        Action<ClientWebSocket>? configureSocket,
        Func<CancellationToken, Task>? initializeConnection,
        CancellationToken ct)
        => TryReconnectAsync(
            uri,
            configureSocket,
            prepareConnection: null,
            initializeConnection,
            ct);

    internal async Task<bool> TryReconnectAsync(
        Uri uri,
        Action<ClientWebSocket>? configureSocket,
        Func<CancellationToken, Task>? prepareConnection,
        Func<CancellationToken, Task>? initializeConnection,
        CancellationToken ct)
    {
        if (!_supervisor.IsReconnecting)
            _log.Warning("{Provider} WebSocket connection lost, initiating automatic reconnection", _providerName);

        return await _supervisor.ReconnectAsync(
            async token =>
            {
                await ConnectTransportTransactionAsync(
                    uri,
                    configureSocket,
                    prepareConnection,
                    initializeConnection,
                    token).ConfigureAwait(false);
            },
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Records that a pong/heartbeat response was received.
    /// Call this when receiving data to indicate connection is alive.
    /// </summary>
    public void RecordPongReceived()
    {
        _heartbeat?.RecordPongReceived();
        _lastHeartbeatReceivedAt = DateTimeOffset.UtcNow;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        using var shutdownCts = new CancellationTokenSource(_shutdownTimeout);
        try
        {
            await DisconnectAsync(shutdownCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (shutdownCts.IsCancellationRequested)
        {
            _log.Warning(
                "Timed out disconnecting {Provider}; completing forced manager disposal",
                _providerName);
        }
        finally
        {
            _supervisor.StateChanged -= OnSupervisorStateChanged;
            try
            {
                await _supervisor.DisposeAsync(shutdownCts.Token).ConfigureAwait(false);
            }
            finally
            {
                ForceDetachTransport();
            }
        }
    }

    private void ForceDetachTransport()
    {
        var heartbeat = _heartbeat;
        var connectionCts = _connectionCts;
        var receiveLoopCts = _receiveLoopCts;
        var receiveTask = _receiveTask;
        var webSocket = _webSocket;

        _heartbeat = null;
        _connectionCts = null;
        _receiveLoopCts = null;
        _receiveTask = null;
        _webSocket = null;

        if (heartbeat is not null)
        {
            heartbeat.ConnectionLost -= OnConnectionLostAsync;
            ObserveForcedCleanup(HeartbeatDisposer(heartbeat), "heartbeat");
        }

        try
        {
            connectionCts?.Cancel();
            receiveLoopCts?.Cancel();
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Cancellation source failed during forced {Provider} transport cleanup", _providerName);
        }

        try
        {
            webSocket?.Abort();
            webSocket?.Dispose();
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "WebSocket failed during forced {Provider} transport cleanup", _providerName);
        }

        connectionCts?.Dispose();
        receiveLoopCts?.Dispose();
        if (receiveTask is not null)
            ObserveForcedCleanup(receiveTask, "receive loop");
    }

    private void ObserveForcedCleanup(Task task, string operation)
        => _ = ObserveForcedCleanupAsync(task, operation);

    private async Task ObserveForcedCleanupAsync(Task task, string operation)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Deferred {Provider} {Operation} cleanup failed", _providerName, operation);
        }
    }


    private async Task ReceiveLoopAsync(Func<string, Task> messageHandler, CancellationToken ct)
    {
        if (_webSocket == null)
            return;

        var buffer = new byte[64 * 1024];
        var messageBuilder = new StringBuilder(128 * 1024);

        try
        {
            while (!ct.IsCancellationRequested && _webSocket.State == WebSocketState.Open)
            {
                messageBuilder.Clear();
                WebSocketReceiveResult result;
                var oversized = false;

                do
                {
                    result = await _webSocket.ReceiveAsync(buffer, ct).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _log.Information("{Provider} WebSocket closed by server", _providerName);
                        StateChanged?.Invoke(WebSocketState.CloseReceived);
                        await RaiseConnectionLostIfActiveAsync().ConfigureAwait(false);
                        return;
                    }

                    // Guard against unbounded message accumulation: if the assembled
                    // message has already exceeded the configured limit, continue
                    // draining frames but discard the content.
                    if (!oversized)
                    {
                        messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                        if (messageBuilder.Length > _config.MaxMessageSizeBytes)
                        {
                            _log.Warning(
                                "{Provider} WebSocket message exceeds max size {MaxBytes} bytes — discarding",
                                _providerName, _config.MaxMessageSizeBytes);
                            messageBuilder.Clear();
                            oversized = true;
                        }
                    }
                }
                while (!result.EndOfMessage);

                if (oversized)
                    continue;

                var message = messageBuilder.ToString();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    // Record activity for heartbeat monitoring
                    RecordPongReceived();
                    _lastMessageReceivedAt = DateTimeOffset.UtcNow;

                    try
                    {
                        await messageHandler(message).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _supervisor.RecordFailure(ex);
                        _log.Warning(ex, "{Provider} error processing message", _providerName);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _log.Debug("{Provider} receive loop cancelled", _providerName);
        }
        catch (WebSocketException ex)
        {
            _supervisor.RecordFailure(ex);
            _log.Error(ex, "{Provider} WebSocket error in receive loop", _providerName);
            StateChanged?.Invoke(WebSocketState.Aborted);
            await RaiseConnectionLostIfActiveAsync(ex).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _supervisor.RecordFailure(ex);
            _log.Error(ex, "{Provider} unexpected error in receive loop", _providerName);
            await RaiseConnectionLostIfActiveAsync(ex).ConfigureAwait(false);
        }
    }

    private Task OnConnectionLostAsync()
        => RaiseConnectionLostIfActiveAsync();

    private Task RaiseConnectionLostIfActiveAsync(Exception? error = null)
    {
        if (!_supervisor.MarkConnectionLost(error))
            return Task.CompletedTask;

        var handler = ConnectionLost;
        if (handler == null)
            return Task.CompletedTask;

        // Do not execute reconnect cleanup on the receive or heartbeat task that
        // detected the loss. The supervisor tracks and gates the reconnect itself.
        _ = Task.Run(async () =>
        {
            try
            {
                await handler.Invoke().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                _supervisor.LifecycleState is ProviderConnectionLifecycleState.Disconnecting or
                    ProviderConnectionLifecycleState.Disconnected)
            {
                // Explicit shutdown cancelled the supervised reconnect.
            }
            catch (Exception ex)
            {
                _supervisor.RecordFailure(ex);
                _log.Error(ex, "{Provider} connection-lost handler failed", _providerName);
            }
        });

        return Task.CompletedTask;
    }

    private void OnSupervisorStateChanged(ProviderConnectionSupervisorSnapshot snapshot)
    {
        var previousState = (ProviderConnectionLifecycleState)Interlocked.Exchange(
            ref _lastPublishedLifecycleState,
            (int)snapshot.LifecycleState);

        if (previousState != snapshot.LifecycleState)
        {
            _log.Information(
                "{Provider} WebSocket lifecycle state changed to {LifecycleState}",
                _providerName,
                snapshot.LifecycleState);

            if (snapshot.LifecycleState == ProviderConnectionLifecycleState.Connected &&
                _webSocket?.State == WebSocketState.Open)
            {
                StateChanged?.Invoke(WebSocketState.Open);

                if (previousState == ProviderConnectionLifecycleState.Reconnecting)
                    PublishReconnectCompletion(snapshot);
            }
            else if (snapshot.LifecycleState == ProviderConnectionLifecycleState.Disconnected)
            {
                StateChanged?.Invoke(WebSocketState.Closed);
            }
        }

        DiagnosticsChanged?.Invoke(GetDiagnosticsSnapshot());
    }

    private void PublishReconnectCompletion(ProviderConnectionSupervisorSnapshot snapshot)
    {
        _log.Information(
            "{Provider} successfully completed reconnection after {Attempts} attempts",
            _providerName,
            snapshot.ReconnectAttempts);

        try
        {
            Reconnected?.Invoke(snapshot.ReconnectAttempts);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "{Provider} error in reconnected handler", _providerName);
        }

        if (snapshot.LastDisconnectedAt is not { } disconnectedAt ||
            snapshot.LastConnectedAt is not { } connectedAt)
        {
            return;
        }

        var gap = new ReconnectionGap(
            _providerName,
            disconnectedAt,
            connectedAt,
            snapshot.ReconnectAttempts);
        _log.Information(
            "{Provider} reconnection gap: {GapDuration}s ({DisconnectTime} to {ReconnectTime})",
            _providerName,
            gap.Duration.TotalSeconds,
            gap.DisconnectedAt.ToString("HH:mm:ss.fff"),
            gap.ReconnectedAt.ToString("HH:mm:ss.fff"));

        try
        {
            GapDetected?.Invoke(gap);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "{Provider} error in gap detection handler", _providerName);
        }
    }

    private async Task CleanupConnectionAsync(CancellationToken ct = default)
    {
        var ws = _webSocket;
        var cts = _connectionCts;
        var heartbeat = _heartbeat;
        var receiveLoopCts = _receiveLoopCts;
        var receiveTask = _receiveTask;

        _webSocket = null;
        _connectionCts = null;
        _receiveLoopCts = null;
        _heartbeat = null;
        _receiveTask = null;

        // 1. Stop heartbeat to prevent new reconnection attempts
        if (heartbeat != null)
        {
            heartbeat.ConnectionLost -= OnConnectionLostAsync;
            await heartbeat.DisposeAsync();
        }

        // 2. Cancel tokens to signal the receive loop to stop
        if (cts != null)
        {
            try
            { cts.Cancel(); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _log.Debug(ex, "{Provider} CTS cancel failed during cleanup", _providerName); }
        }

        // 3. Wait for the receive task to complete before disposing resources it uses.
        // Connection-loss callbacks are queued off the receive/heartbeat task, so this
        // cleanup never waits on the task that is currently executing it.
        if (receiveTask != null)
        {
            try
            { await receiveTask.WaitAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _log.Debug(ex, "{Provider} receive task failed during cleanup", _providerName); }
        }

        // 4. Now safe to dispose CTS and WebSocket — receive loop has exited
        if (receiveLoopCts != null)
        {
            try
            { receiveLoopCts.Dispose(); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _log.Debug(ex, "{Provider} receive loop CTS dispose failed during cleanup", _providerName); }
        }

        if (cts != null)
        {
            try
            { cts.Dispose(); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _log.Debug(ex, "{Provider} CTS dispose failed during cleanup", _providerName); }
        }

        if (ws != null)
        {
            try
            { ws.Dispose(); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _log.Debug(ex, "{Provider} WebSocket dispose failed during cleanup", _providerName); }
        }
    }

}

/// <summary>
/// Classifies provider failures so reconnection loops can distinguish transient errors
/// from credential/configuration failures that need operator action.
/// </summary>
public static class ProviderFailureClassifier
{
    public static ProviderFailureKind Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            OperationCanceledException => ProviderFailureKind.Cancelled,
            UnauthorizedAccessException => ProviderFailureKind.AuthenticationOrAuthorizationFailure,
            AuthenticationException => ProviderFailureKind.AuthenticationOrAuthorizationFailure,
            HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } => ProviderFailureKind.AuthenticationOrAuthorizationFailure,
            HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => ProviderFailureKind.ProviderRateLimit,
            HttpRequestException { StatusCode: { } statusCode } when (int)statusCode >= 500 => ProviderFailureKind.ProviderOutage,
            HttpRequestException => ProviderFailureKind.TransientNetworkFailure,
            WebSocketException => ProviderFailureKind.TransientNetworkFailure,
            TimeoutException => ProviderFailureKind.TransientNetworkFailure,
            InvalidOperationException invalidOperation when LooksLikeCredentialOrConfigFailure(invalidOperation.Message) => ProviderFailureKind.LocalConfigurationError,
            FormatException => ProviderFailureKind.MalformedProviderResponse,
            System.Text.Json.JsonException => ProviderFailureKind.MalformedProviderResponse,
            _ => ProviderFailureKind.Unknown
        };
    }

    public static bool IsRetryable(ProviderFailureKind failureKind)
        => failureKind is
            ProviderFailureKind.TransientNetworkFailure or
            ProviderFailureKind.ProviderRateLimit or
            ProviderFailureKind.ProviderOutage or
            ProviderFailureKind.Unknown;

    private static bool LooksLikeCredentialOrConfigFailure(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return message.Contains("credential", StringComparison.OrdinalIgnoreCase)
            || message.Contains("api key", StringComparison.OrdinalIgnoreCase)
            || message.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || message.Contains("token", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not configured", StringComparison.OrdinalIgnoreCase)
            || message.Contains("configuration", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Represents a gap in data caused by a WebSocket disconnection and reconnection.
/// Subscribers should use this to trigger backfill for the missed time window.
/// </summary>
public readonly record struct ReconnectionGap(
    string ProviderName,
    DateTimeOffset DisconnectedAt,
    DateTimeOffset ReconnectedAt,
    int ReconnectAttempts)
{
    /// <summary>
    /// Duration of the gap (time without data).
    /// </summary>
    public TimeSpan Duration => ReconnectedAt - DisconnectedAt;
}

using System.Net.WebSockets;
using System.Net;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using Meridian.Core.Logging;
using Meridian.Core.Resilience;
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
    private readonly WebSocketConnectionConfig _config;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly ILogger _log;
    private readonly string _providerName;

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _connectionCts;
    private CancellationTokenSource? _receiveLoopCts;
    private Task? _receiveTask;
    private WebSocketHeartbeat? _heartbeat;

    // Reconnection gating - prevents reconnection storms
    private volatile bool _isReconnecting;
    private readonly SemaphoreSlim _reconnectGate = new(1, 1);
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private int _reconnectAttempts;

    // Gap tracking for reconnection-aware backfill
    private DateTimeOffset _lastDisconnectTime;
    private DateTimeOffset _lastConnectTime;
    private DateTimeOffset? _lastMessageReceivedAt;
    private DateTimeOffset? _lastHeartbeatReceivedAt;
    private DateTimeOffset? _lastReconnectAttemptAt;
    private string? _lastError;
    private ProviderFailureKind? _lastFailureKind;
    private volatile ProviderConnectionLifecycleState _lifecycleState = ProviderConnectionLifecycleState.Configured;
    private volatile bool _isDisconnecting;

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
    public bool IsConnected => _webSocket?.State == WebSocketState.Open;

    /// <summary>
    /// Gets the current WebSocket state.
    /// </summary>
    public WebSocketState State => _webSocket?.State ?? WebSocketState.None;

    /// <summary>
    /// Gets whether a reconnection is currently in progress.
    /// </summary>
    public bool IsReconnecting => _isReconnecting;

    /// <summary>
    /// Gets the current provider lifecycle phase.
    /// </summary>
    public ProviderConnectionLifecycleState LifecycleState => _lifecycleState;

    /// <summary>
    /// Gets a safe diagnostics snapshot for logs, health surfaces, and tests.
    /// </summary>
    public WebSocketConnectionDiagnostics GetDiagnosticsSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        var lastActivity = _lastMessageReceivedAt ?? _lastConnectTime;

        return new WebSocketConnectionDiagnostics(
            ProviderName: _providerName,
            LifecycleState: _lifecycleState,
            WebSocketState: State,
            IsConnected: IsConnected,
            IsReconnecting: _isReconnecting,
            ReconnectAttempts: _reconnectAttempts,
            LastConnectedAt: _lastConnectTime == default ? null : _lastConnectTime,
            LastDisconnectedAt: _lastDisconnectTime == default ? null : _lastDisconnectTime,
            LastHeartbeatReceivedAt: _lastHeartbeatReceivedAt,
            LastMessageReceivedAt: _lastMessageReceivedAt,
            LastReconnectAttemptAt: _lastReconnectAttemptAt,
            LastError: _lastError,
            LastFailureKind: _lastFailureKind,
            ConnectionAge: IsConnected && _lastConnectTime != default ? now - _lastConnectTime : null,
            IdleDuration: lastActivity == default ? null : now - lastActivity);
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
    {
        _providerName = providerName ?? throw new ArgumentNullException(nameof(providerName));
        _config = config ?? WebSocketConnectionConfig.Default;
        _log = logger ?? LoggingSetup.ForContext<WebSocketConnectionManager>();

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
    public async Task ConnectAsync(
        Uri uri,
        Action<ClientWebSocket>? configureSocket = null,
        CancellationToken ct = default)
    {
        if (IsConnected)
        {
            _log.Debug("{Provider} WebSocket already connected", _providerName);
            return;
        }

        await _connectLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Re-check after acquiring the lock — another thread may have connected first.
            if (IsConnected)
            {
                _log.Debug("{Provider} WebSocket connected by concurrent caller, skipping", _providerName);
                return;
            }

            _log.Information("Connecting to {Provider} WebSocket at {Uri}", _providerName, uri);
            SetLifecycleState(ProviderConnectionLifecycleState.Connecting);

            await _resiliencePipeline.ExecuteAsync(async token =>
            {
                _connectionCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                _webSocket = new ClientWebSocket();

                // Allow provider-specific configuration (headers, options, etc.)
                configureSocket?.Invoke(_webSocket);

                try
                {
                    await _webSocket.ConnectAsync(uri, token).ConfigureAwait(false);
                    _log.Information("Successfully connected to {Provider} WebSocket", _providerName);
                    _reconnectAttempts = 0;
                    _lastConnectTime = DateTimeOffset.UtcNow;
                    _lastError = null;
                    _lastFailureKind = null;
                    SetLifecycleState(ProviderConnectionLifecycleState.Connected);
                    StateChanged?.Invoke(WebSocketState.Open);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                    _lastFailureKind = ProviderFailureClassifier.Classify(ex);
                    SetLifecycleState(ProviderConnectionLifecycleState.Failed);
                    _log.Warning(ex, "Connection attempt to {Provider} WebSocket failed. Will retry per policy.", _providerName);
                    CleanupFailedConnection();
                    throw;
                }
            }, ct).ConfigureAwait(false);

            // Start heartbeat monitoring after successful connection
            if (_webSocket != null)
            {
                _heartbeat = new WebSocketHeartbeat(
                    _webSocket,
                    _config.HeartbeatInterval,
                    _config.HeartbeatTimeout);
                _lastHeartbeatReceivedAt = _heartbeat.LastPongReceived;
                _heartbeat.ConnectionLost += OnConnectionLostAsync;
            }
        }
        finally
        {
            _connectLock.Release();
        }
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
    /// Must only be called after <see cref="ConnectAsync"/> and before
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
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(message);
        await _webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Disconnects from the WebSocket gracefully.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (_isDisconnecting)
        {
            _log.Debug("{Provider} disconnect already in progress", _providerName);
            return;
        }

        _log.Information("Disconnecting from {Provider} WebSocket", _providerName);
        _isDisconnecting = true;
        SetLifecycleState(ProviderConnectionLifecycleState.Disconnecting);

        try
        {
            // Dispose heartbeat first to prevent reconnection attempts
            if (_heartbeat != null)
            {
                _heartbeat.ConnectionLost -= OnConnectionLostAsync;
                await _heartbeat.DisposeAsync();
                _heartbeat = null;
            }

            // Cancel receive loop
            if (_connectionCts != null)
            {
                try
                { _connectionCts.Cancel(); }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.Debug(ex, "CancellationTokenSource.Cancel failed during {Provider} disconnect", _providerName);
                }
            }

            // Close WebSocket gracefully
            if (_webSocket != null)
            {
                try
                {
                    if (_webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", ct)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "Error during {Provider} WebSocket close", _providerName);
                }
                finally
                {
                    _webSocket.Dispose();
                    _webSocket = null;
                }
            }

            // Wait for receive loop to complete
            if (_receiveTask != null)
            {
                try
                { await _receiveTask.WaitAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    _log.Warning("{Provider} receive loop did not finish before disconnect timeout", _providerName);
                }
                catch (Exception ex)
                {
                    _log.Debug(ex, "Receive loop completion error during {Provider} disconnect", _providerName);
                }
                _receiveTask = null;
            }

            _receiveLoopCts?.Dispose();
            _receiveLoopCts = null;
            _connectionCts?.Dispose();
            _connectionCts = null;

            _lastDisconnectTime = DateTimeOffset.UtcNow;
            SetLifecycleState(ProviderConnectionLifecycleState.Disconnected);

            StateChanged?.Invoke(WebSocketState.Closed);
            _log.Information("Disconnected from {Provider} WebSocket", _providerName);
        }
        finally
        {
            _isDisconnecting = false;
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
    public async Task<bool> TryReconnectAsync(
        Uri uri,
        Action<ClientWebSocket>? configureSocket = null,
        Func<Task>? onReconnected = null,
        CancellationToken ct = default)
    {
        // Use the semaphore as the sole gating mechanism.
        // The previous fast-path check on _isReconnecting without holding
        // the semaphore allowed two threads to both see false and race,
        // potentially causing duplicate reconnection attempts.
        if (!await _reconnectGate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            _log.Debug("{Provider} reconnection already in progress, skipping duplicate attempt", _providerName);
            return false;
        }

        try
        {
            _isReconnecting = true;
            SetLifecycleState(ProviderConnectionLifecycleState.Reconnecting);
            _log.Warning("{Provider} WebSocket connection lost, initiating automatic reconnection", _providerName);

            // Clean up existing connection
            await CleanupConnectionAsync(ct);

            // Attempt reconnection with backoff
            while (_reconnectAttempts < _config.MaxReconnectAttempts && !ct.IsCancellationRequested)
            {
                _reconnectAttempts++;
                _lastReconnectAttemptAt = DateTimeOffset.UtcNow;
                var delay = CalculateReconnectDelay(_reconnectAttempts);

                _log.Information("{Provider} reconnection attempt {Attempt}/{Max} in {Delay}ms",
                    _providerName, _reconnectAttempts, _config.MaxReconnectAttempts, delay.TotalMilliseconds);

                await Task.Delay(delay, ct).ConfigureAwait(false);

                try
                {
                    await ConnectAsync(uri, configureSocket, ct).ConfigureAwait(false);

                    if (IsConnected && onReconnected != null)
                    {
                        await onReconnected().ConfigureAwait(false);
                    }

                    _lastConnectTime = DateTimeOffset.UtcNow;
                    _log.Information("{Provider} successfully reconnected after {Attempts} attempts",
                        _providerName, _reconnectAttempts);
                    Reconnected?.Invoke(_reconnectAttempts);

                    // Emit gap event so subscribers can trigger backfill
                    if (_lastDisconnectTime != default)
                    {
                        var gap = new ReconnectionGap(
                            _providerName,
                            _lastDisconnectTime,
                            _lastConnectTime,
                            _reconnectAttempts);
                        _log.Information(
                            "{Provider} reconnection gap: {GapDuration}s ({DisconnectTime} to {ReconnectTime})",
                            _providerName, gap.Duration.TotalSeconds,
                            gap.DisconnectedAt.ToString("HH:mm:ss.fff"),
                            gap.ReconnectedAt.ToString("HH:mm:ss.fff"));

                        try
                        {
                            GapDetected?.Invoke(gap);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception gapEx)
                        {
                            _log.Error(gapEx, "{Provider} error in gap detection handler", _providerName);
                        }
                    }

                    return true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                    _lastFailureKind = ProviderFailureClassifier.Classify(ex);
                    if (!ProviderFailureClassifier.IsRetryable(_lastFailureKind.Value))
                    {
                        _log.Error(
                            ex,
                            "{Provider} reconnection stopped after non-retryable {FailureKind} failure on attempt {Attempt}",
                            _providerName,
                            _lastFailureKind,
                            _reconnectAttempts);
                        SetLifecycleState(ProviderConnectionLifecycleState.Failed);
                        return false;
                    }

                    _log.Warning(ex, "{Provider} reconnection attempt {Attempt} failed with {FailureKind}", _providerName, _reconnectAttempts, _lastFailureKind);
                }
            }

            _log.Error("{Provider} failed to reconnect after {Attempts} attempts. Manual intervention may be required.",
                _providerName, _reconnectAttempts);
            SetLifecycleState(ProviderConnectionLifecycleState.Failed);
            return false;
        }
        finally
        {
            _isReconnecting = false;
            _reconnectGate.Release();
        }
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
    public async ValueTask DisposeAsync()
    {
        using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await DisconnectAsync(shutdownCts.Token).ConfigureAwait(false);
        _reconnectGate.Dispose();
        _connectLock.Dispose();
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
                        _lastError = ex.Message;
                        _lastFailureKind = ProviderFailureClassifier.Classify(ex);
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
            _lastError = ex.Message;
            _lastFailureKind = ProviderFailureClassifier.Classify(ex);
            _log.Error(ex, "{Provider} WebSocket error in receive loop", _providerName);
            StateChanged?.Invoke(WebSocketState.Aborted);
            await RaiseConnectionLostIfActiveAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _lastFailureKind = ProviderFailureClassifier.Classify(ex);
            _log.Error(ex, "{Provider} unexpected error in receive loop", _providerName);
            await RaiseConnectionLostIfActiveAsync().ConfigureAwait(false);
        }
    }

    private async Task OnConnectionLostAsync()
    {
        if (_isDisconnecting)
            return;

        if (ConnectionLost != null)
        {
            await ConnectionLost.Invoke();
        }
    }

    private async Task RaiseConnectionLostIfActiveAsync()
    {
        if (_isDisconnecting)
            return;

        SetLifecycleState(ProviderConnectionLifecycleState.Degraded);
        await OnConnectionLostAsync().ConfigureAwait(false);
    }

    private void SetLifecycleState(ProviderConnectionLifecycleState state)
    {
        if (_lifecycleState == state)
            return;

        _lifecycleState = state;
        _log.Information("{Provider} WebSocket lifecycle state changed to {LifecycleState}", _providerName, state);
        DiagnosticsChanged?.Invoke(GetDiagnosticsSnapshot());
    }

    private async Task CleanupConnectionAsync(CancellationToken ct = default)
    {
        // Record when the disconnect happened for gap tracking
        _lastDisconnectTime = DateTimeOffset.UtcNow;

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
        // Avoid self-await if cleanup is executing on the receive loop task itself.
        if (receiveTask != null)
        {
            var currentTaskId = Task.CurrentId;
            if (currentTaskId.HasValue && receiveTask.Id == currentTaskId.Value)
            {
                _log.Debug("{Provider} skipping receive task self-await during cleanup", _providerName);
            }
            else
            {
                try
                { await receiveTask.WaitAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { _log.Debug(ex, "{Provider} receive task failed during cleanup", _providerName); }
            }
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

    private void CleanupFailedConnection()
    {
        try
        { _webSocket?.Dispose(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { _log.Debug(ex, "{Provider} WebSocket dispose failed during connection cleanup", _providerName); }
        _webSocket = null;

        try
        { _connectionCts?.Dispose(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { _log.Debug(ex, "{Provider} CTS dispose failed during connection cleanup", _providerName); }
        _connectionCts = null;
    }

    private TimeSpan CalculateReconnectDelay(int attempt)
        => Backoff.ExponentialDelay(attempt, _config.RetryBaseDelay, _config.MaxRetryDelay, jitterFraction: 0.2);

}

/// <summary>
/// Shared lifecycle states for provider connection supervision.
/// </summary>
public enum ProviderConnectionLifecycleState
{
    NotConfigured = 0,
    Configured = 1,
    Connecting = 2,
    Connected = 3,
    Degraded = 4,
    Reconnecting = 5,
    Disconnecting = 6,
    Disconnected = 7,
    Failed = 8,
    Disabled = 9
}

/// <summary>
/// Normalized provider failure categories used by retry policy, diagnostics, and health surfaces.
/// </summary>
public enum ProviderFailureKind
{
    Unknown = 0,
    TransientNetworkFailure = 1,
    ProviderRateLimit = 2,
    AuthenticationOrAuthorizationFailure = 3,
    InvalidSubscription = 4,
    ProviderOutage = 5,
    MalformedProviderResponse = 6,
    LocalConfigurationError = 7,
    Cancelled = 8
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
/// Safe provider WebSocket diagnostics snapshot. It intentionally excludes URIs,
/// credentials, headers, account IDs, and payload data.
/// </summary>
public sealed record WebSocketConnectionDiagnostics(
    string ProviderName,
    ProviderConnectionLifecycleState LifecycleState,
    WebSocketState WebSocketState,
    bool IsConnected,
    bool IsReconnecting,
    int ReconnectAttempts,
    DateTimeOffset? LastConnectedAt,
    DateTimeOffset? LastDisconnectedAt,
    DateTimeOffset? LastHeartbeatReceivedAt,
    DateTimeOffset? LastMessageReceivedAt,
    DateTimeOffset? LastReconnectAttemptAt,
    string? LastError,
    ProviderFailureKind? LastFailureKind,
    TimeSpan? ConnectionAge,
    TimeSpan? IdleDuration,
    int ActiveSubscriptions = 0,
    int FailedSubscriptions = 0,
    int RecoveringSubscriptions = 0,
    DateTimeOffset? LastSubscriptionMessageAt = null);

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

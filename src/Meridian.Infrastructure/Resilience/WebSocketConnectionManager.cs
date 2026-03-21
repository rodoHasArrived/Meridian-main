using System.Net.WebSockets;
using System.Text;
using System.Threading;
using Meridian.Application.Logging;
using Polly;
using Serilog;

namespace Meridian.Infrastructure.Resilience;

/// <summary>
/// Centralized WebSocket connection manager that handles connection lifecycle,
/// resilience, heartbeat monitoring, and automatic reconnection.
///
/// This class eliminates duplicate connection management code across providers
/// (Alpaca, Polygon, StockSharp) by centralizing:
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
                    StateChanged?.Invoke(WebSocketState.Open);
                }
                catch (Exception ex)
                {
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
        _log.Information("Disconnecting from {Provider} WebSocket", _providerName);

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
            { await _receiveTask.ConfigureAwait(false); }
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

        StateChanged?.Invoke(WebSocketState.Closed);
        _log.Information("Disconnected from {Provider} WebSocket", _providerName);
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
            _log.Warning("{Provider} WebSocket connection lost, initiating automatic reconnection", _providerName);

            // Clean up existing connection
            await CleanupConnectionAsync();

            // Attempt reconnection with backoff
            while (_reconnectAttempts < _config.MaxReconnectAttempts && !ct.IsCancellationRequested)
            {
                _reconnectAttempts++;
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
                        catch (Exception gapEx)
                        {
                            _log.Error(gapEx, "{Provider} error in gap detection handler", _providerName);
                        }
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "{Provider} reconnection attempt {Attempt} failed", _providerName, _reconnectAttempts);
                }
            }

            _log.Error("{Provider} failed to reconnect after {Attempts} attempts. Manual intervention may be required.",
                _providerName, _reconnectAttempts);
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
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _reconnectGate.Dispose();
        _connectLock.Dispose();
    }

    #region Private Methods

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
                    _heartbeat?.RecordPongReceived();

                    try
                    {
                        await messageHandler(message).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
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
            _log.Error(ex, "{Provider} WebSocket error in receive loop", _providerName);
            StateChanged?.Invoke(WebSocketState.Aborted);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "{Provider} unexpected error in receive loop", _providerName);
        }
    }

    private async Task OnConnectionLostAsync()
    {
        if (ConnectionLost != null)
        {
            await ConnectionLost.Invoke();
        }
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
            catch (Exception ex) { _log.Debug(ex, "{Provider} CTS cancel failed during cleanup", _providerName); }
        }

        // 3. Wait for the receive task to complete before disposing resources it uses
        if (receiveTask != null)
        {
            try
            { await receiveTask.ConfigureAwait(false); }
            catch (Exception ex) { _log.Debug(ex, "{Provider} receive task failed during cleanup", _providerName); }
        }

        // 4. Now safe to dispose CTS and WebSocket — receive loop has exited
        if (receiveLoopCts != null)
        {
            try
            { receiveLoopCts.Dispose(); }
            catch (Exception ex) { _log.Debug(ex, "{Provider} receive loop CTS dispose failed during cleanup", _providerName); }
        }

        if (cts != null)
        {
            try
            { cts.Dispose(); }
            catch (Exception ex) { _log.Debug(ex, "{Provider} CTS dispose failed during cleanup", _providerName); }
        }

        if (ws != null)
        {
            try
            { ws.Dispose(); }
            catch (Exception ex) { _log.Debug(ex, "{Provider} WebSocket dispose failed during cleanup", _providerName); }
        }
    }

    private void CleanupFailedConnection()
    {
        try
        { _webSocket?.Dispose(); }
        catch (Exception ex) { _log.Debug(ex, "{Provider} WebSocket dispose failed during connection cleanup", _providerName); }
        _webSocket = null;

        try
        { _connectionCts?.Dispose(); }
        catch (Exception ex) { _log.Debug(ex, "{Provider} CTS dispose failed during connection cleanup", _providerName); }
        _connectionCts = null;
    }

    private TimeSpan CalculateReconnectDelay(int attempt)
    {
        // Exponential backoff with jitter
        var baseDelay = _config.RetryBaseDelay.TotalMilliseconds;
        var maxDelay = _config.MaxRetryDelay.TotalMilliseconds;
        var delay = Math.Min(baseDelay * Math.Pow(2, attempt - 1), maxDelay);

        // Add jitter (±20%)
        var jitter = delay * 0.2 * (Random.Shared.NextDouble() * 2 - 1);
        return TimeSpan.FromMilliseconds(delay + jitter);
    }

    #endregion
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

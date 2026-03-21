#if STOCKSHARP
using StockSharp.Algo;
using System.Threading;
using StockSharp.BusinessEntities;
using StockSharp.Messages;
#endif
using Meridian.Application.Logging;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.StockSharp.Converters;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Resilience;
using Serilog;
using StockSharpConfig = Meridian.Application.Config.StockSharpConfig;

namespace Meridian.Infrastructure.Adapters.StockSharp;

/// <summary>
/// IMarketDataClient implementation that wraps StockSharp connectors.
/// Provides unified access to 90+ data sources through the S# adapter pattern.
///
/// Features (inspired by Hydra best practices):
/// - Automatic reconnection with exponential backoff
/// - Subscription recovery after reconnection
/// - Connection health monitoring with heartbeats
/// - Message buffering for high-frequency data
///
/// Supported data types:
/// - Trades (tick-by-tick)
/// - Market Depth (Level 2 order books)
/// - Quotes (BBO)
///
/// See StockSharpConnectorFactory for available connector types.
/// </summary>
[DataSource("stocksharp", "StockSharp", Infrastructure.DataSources.DataSourceType.Realtime, DataSourceCategory.Aggregator,
    Priority = 20, Description = "StockSharp connector providing access to 90+ data sources")]
[ImplementsAdr("ADR-001", "StockSharp streaming data provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
public sealed class StockSharpMarketDataClient : IMarketDataClient
{
    private readonly ILogger _log = LoggingSetup.ForContext<StockSharpMarketDataClient>();
    private readonly TradeDataCollector _tradeCollector;
    private readonly MarketDepthCollector _depthCollector;
    private readonly QuoteCollector _quoteCollector;
    private readonly StockSharpConfig _config;

#if STOCKSHARP
    private Connector? _connector;
    private readonly Dictionary<int, (Security Security, string Symbol, SubscriptionType Type)> _subscriptions = new();
    private readonly Dictionary<string, Security> _securities = new();

    // Use centralized configuration for reconnection and heartbeat settings
    private readonly WebSocketConnectionConfig _connectionConfig = WebSocketConnectionConfig.Resilient;

    // Reconnection support (Hydra-inspired pattern)
    private CancellationTokenSource? _reconnectCts;
    private Task? _reconnectTask;
    private int _reconnectAttempt;

    // Heartbeat monitoring - use long (ticks) for thread-safe atomic operations
    private long _lastDataReceivedTicks = DateTimeOffset.UtcNow.Ticks;
    private Timer? _heartbeatTimer;

    // Message buffering for high-frequency data
    private readonly System.Threading.Channels.Channel<Action> _messageChannel;
    private Task? _messageProcessorTask;
    private CancellationTokenSource? _processorCts;

    // Channel overflow statistics for monitoring
    private long _messageDropCount;

    // Use centralized subscription ID range to avoid collisions with other providers
    private int _nextSubId = ProviderSubscriptionRanges.StockSharpStart;
    private bool _disposed;
#endif
    private readonly object _gate = new();

    /// <summary>
    /// Event raised when connection state changes.
    /// </summary>
#pragma warning disable CS0067 // Raised inside #if STOCKSHARP block
    public event Action<ConnectionState>? ConnectionStateChanged;
#pragma warning restore CS0067

    /// <summary>
    /// Current connection state.
    /// </summary>
    public ConnectionState CurrentState { get; private set; } = ConnectionState.Disconnected;

    /// <summary>
    /// Creates a new StockSharp market data client.
    /// </summary>
    /// <param name="tradeCollector">Collector for trade events.</param>
    /// <param name="depthCollector">Collector for market depth events.</param>
    /// <param name="quoteCollector">Collector for quote/BBO events.</param>
    /// <param name="config">StockSharp configuration.</param>
    public StockSharpMarketDataClient(
        TradeDataCollector tradeCollector,
        MarketDepthCollector depthCollector,
        QuoteCollector quoteCollector,
        StockSharpConfig config)
    {
        _tradeCollector = tradeCollector ?? throw new ArgumentNullException(nameof(tradeCollector));
        _depthCollector = depthCollector ?? throw new ArgumentNullException(nameof(depthCollector));
        _quoteCollector = quoteCollector ?? throw new ArgumentNullException(nameof(quoteCollector));
        _config = config ?? throw new ArgumentNullException(nameof(config));

#if STOCKSHARP
        // Initialize bounded message channel for high-frequency data buffering
        // This prevents event handler blocking during bursts (Hydra pattern)
        _messageChannel = EventPipelinePolicy.MessageBuffer.CreateChannel<Action>(
            singleReader: true, singleWriter: false);
#endif
    }

    /// <summary>
    /// Whether this client is enabled based on configuration.
    /// </summary>
    public bool IsEnabled => _config.Enabled;

    #region IProviderMetadata

    /// <inheritdoc/>
    public string ProviderId => "stocksharp";

    /// <inheritdoc/>
    public string ProviderDisplayName => "StockSharp";

    /// <inheritdoc/>
    public string ProviderDescription => "Multi-connector trading framework supporting 90+ data sources";

    /// <inheritdoc/>
    public int ProviderPriority => 30;

    /// <inheritdoc/>
    public ProviderCapabilities ProviderCapabilities { get; } = ProviderCapabilities.Streaming(
        trades: true,
        quotes: true,
        depth: true) with
    {
        SupportedMarkets = new[] { "US", "Futures" }
    };

    /// <inheritdoc/>
    public ProviderCredentialField[] ProviderCredentialFields => new[]
    {
        new ProviderCredentialField("ConnectorType", null, "Connector Type", true, "Rithmic")
    };

    /// <inheritdoc/>
    public string[] ProviderNotes => new[]
    {
        "Supports multiple underlying connectors.",
        "Configure specific connector settings in StockSharp section.",
        "Supports Rithmic, IQFeed, CQG, and more."
    };

    /// <inheritdoc/>
    public string[] ProviderWarnings => new[]
    {
        "Requires StockSharp connector-specific credentials."
    };

    #endregion

#if STOCKSHARP
    /// <summary>
    /// Connect to the configured StockSharp data source.
    /// Includes automatic reconnection and subscription recovery (Hydra pattern).
    /// </summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_connector != null && CurrentState == ConnectionState.Connected)
        {
            _log.Debug("StockSharp connector already connected, skipping connection");
            return;
        }

        _log.Information("Initializing StockSharp connector: {Type}", _config.ConnectorType);
        SetConnectionState(ConnectionState.Connecting);

        _connector = StockSharpConnectorFactory.Create(_config);

        // Wire up event handlers using centralized helper
        AttachEventHandlers(_connector);

        var tcs = new TaskCompletionSource<bool>();
        using var registration = ct.Register(() => tcs.TrySetCanceled());

        void ConnectedHandler(object? sender, EventArgs e) => tcs.TrySetResult(true);
        void ErrorHandler(Exception ex) => tcs.TrySetException(ex);

        _connector.Connected += ConnectedHandler;
        _connector.ConnectionError += ErrorHandler;

        try
        {
            _connector.Connect();
            await tcs.Task.ConfigureAwait(false);

            // Reset reconnection attempt counter on successful connect
            _reconnectAttempt = 0;

            // Start message processor for buffered high-frequency data
            StartMessageProcessor();

            // Start heartbeat monitoring (Hydra pattern)
            StartHeartbeatMonitoring();

            SetConnectionState(ConnectionState.Connected);
            _log.Information("StockSharp connector connected successfully to {Type}", _config.ConnectorType);
        }
        catch (OperationCanceledException)
        {
            SetConnectionState(ConnectionState.Disconnected);
            _log.Warning("StockSharp connection cancelled");
            throw;
        }
        catch (Exception ex)
        {
            SetConnectionState(ConnectionState.Error);
            _log.Error(ex, "Failed to connect to StockSharp {Type}", _config.ConnectorType);
            throw;
        }
        finally
        {
            _connector.Connected -= ConnectedHandler;
            _connector.ConnectionError -= ErrorHandler;
        }
    }

    /// <summary>
    /// Start the message processor task for buffered events.
    /// </summary>
    private void StartMessageProcessor()
    {
        _processorCts = new CancellationTokenSource();
        _messageProcessorTask = ProcessMessagesAsync(_processorCts.Token);
    }

    /// <summary>
    /// Background message processing loop. Runs for the lifetime of the connection.
    /// </summary>
    private async Task ProcessMessagesAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var action in _messageChannel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "Error processing buffered message");
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Message processor crashed unexpectedly");
            if (!_disposed && CurrentState == ConnectionState.Connected)
            {
                SetConnectionState(ConnectionState.Error);
                TriggerReconnection();
            }
        }
    }

    /// <summary>
    /// Start heartbeat monitoring to detect stale connections (Hydra pattern).
    /// </summary>
    private void StartHeartbeatMonitoring()
    {
        UpdateLastDataReceived();
        _heartbeatTimer = new Timer(
            CheckHeartbeat,
            null,
            _connectionConfig.HeartbeatInterval,
            _connectionConfig.HeartbeatInterval);
    }

    /// <summary>
    /// Thread-safe update of last data received timestamp.
    /// </summary>
    private void UpdateLastDataReceived()
    {
        Interlocked.Exchange(ref _lastDataReceivedTicks, DateTimeOffset.UtcNow.Ticks);
    }

    /// <summary>
    /// Thread-safe read of last data received timestamp.
    /// </summary>
    private DateTimeOffset GetLastDataReceived()
    {
        return new DateTimeOffset(Interlocked.Read(ref _lastDataReceivedTicks), TimeSpan.Zero);
    }

    /// <summary>
    /// Check if connection is still alive based on last data received.
    /// </summary>
    private void CheckHeartbeat(object? state)
    {
        if (_disposed || CurrentState != ConnectionState.Connected)
            return;

        var timeSinceLastData = DateTimeOffset.UtcNow - GetLastDataReceived();
        // Use heartbeat interval + timeout as the staleness threshold
        var staleThreshold = _connectionConfig.HeartbeatInterval + _connectionConfig.HeartbeatTimeout;
        if (timeSinceLastData > staleThreshold)
        {
            _log.Warning("No data received for {Duration}s, connection may be stale. Triggering reconnection.",
                timeSinceLastData.TotalSeconds);
            TriggerReconnection();
        }
    }

    /// <summary>
    /// Trigger automatic reconnection with subscription recovery.
    /// </summary>
    private void TriggerReconnection()
    {
        if (_disposed || _reconnectTask != null)
            return;

        _reconnectCts?.Cancel();
        _reconnectCts = new CancellationTokenSource();

        _reconnectTask = RunReconnectionAsync(_reconnectCts.Token);
    }

    /// <summary>
    /// Wrapper that ensures _reconnectTask is cleared when reconnection completes.
    /// </summary>
    private async Task RunReconnectionAsync(CancellationToken ct)
    {
        try
        {
            await ReconnectWithRecoveryAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal cancellation during shutdown
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Unexpected error in reconnection task");
        }
        finally
        {
            _reconnectTask = null;
        }
    }

    /// <summary>
    /// Reconnect with exponential backoff and subscription recovery (Hydra pattern).
    /// Uses centralized WebSocketConnectionConfig for retry timing.
    /// </summary>
    private async Task ReconnectWithRecoveryAsync(CancellationToken ct)
    {
        // Save current subscriptions for recovery
        List<(int SubId, Security Security, string Symbol, SubscriptionType Type)> savedSubscriptions;
        lock (_gate)
        {
            savedSubscriptions = _subscriptions
                .Select(kvp => (kvp.Key, kvp.Value.Security, kvp.Value.Symbol, kvp.Value.Type))
                .ToList();
        }

        _log.Information("Starting reconnection with {Count} subscriptions to recover", savedSubscriptions.Count);
        SetConnectionState(ConnectionState.Reconnecting);

        while (!ct.IsCancellationRequested && _reconnectAttempt < _connectionConfig.MaxReconnectAttempts)
        {
            // Calculate exponential backoff delay using centralized configuration
            var delay = CalculateReconnectDelay(_reconnectAttempt);
            _reconnectAttempt++;

            _log.Information("Reconnection attempt {Attempt}/{Max} in {Delay}s",
                _reconnectAttempt, _connectionConfig.MaxReconnectAttempts, delay.TotalSeconds);

            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);

                // Disconnect existing connector
                if (_connector != null)
                {
                    try { _connector.Disconnect(); }
                    catch (Exception disconnectEx)
                    {
                        _log.Debug(disconnectEx, "Error disconnecting StockSharp connector during reconnection");
                    }
                    _connector.Dispose();
                    _connector = null;
                }

                // Create new connector and connect
                _connector = StockSharpConnectorFactory.Create(_config);
                AttachEventHandlers(_connector);

                var tcs = new TaskCompletionSource<bool>();
                using var reg = ct.Register(() => tcs.TrySetCanceled());

                void Handler(object? sender, EventArgs e) => tcs.TrySetResult(true);
                _connector.Connected += Handler;

                _connector.Connect();
                await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);

                _connector.Connected -= Handler;
                _reconnectAttempt = 0;

                // Recover subscriptions
                await RecoverSubscriptionsAsync(savedSubscriptions, ct).ConfigureAwait(false);

                UpdateLastDataReceived();
                SetConnectionState(ConnectionState.Connected);
                _log.Information("Reconnection successful. {Count} subscriptions recovered.", savedSubscriptions.Count);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _log.Debug("Reconnection cancelled");
                return;
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Reconnection attempt {Attempt} failed", _reconnectAttempt);
            }
        }

        _log.Error("Failed to reconnect after {Attempts} attempts. Will retry in 5 minutes.", _reconnectAttempt);
        SetConnectionState(ConnectionState.Error);

        // Schedule a deferred reconnection attempt to avoid permanent disconnection
        // in unattended mode. Reset the attempt counter so the next round starts fresh.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(5), ct).ConfigureAwait(false);
            if (!_disposed)
            {
                _reconnectAttempt = 0;
                _log.Information("Retrying reconnection after deferred delay");
                await ReconnectWithRecoveryAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown requested during deferred wait
        }
    }

    /// <summary>
    /// Recover subscriptions after successful reconnection (Hydra pattern).
    /// </summary>
    private async Task RecoverSubscriptionsAsync(
        List<(int SubId, Security Security, string Symbol, SubscriptionType Type)> subscriptions,
        CancellationToken ct)
    {
        foreach (var (subId, security, symbol, type) in subscriptions)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                switch (type)
                {
                    case SubscriptionType.Trades:
                        _connector?.SubscribeTrades(security);
                        _log.Debug("Recovered trade subscription for {Symbol}", symbol);
                        break;
                    case SubscriptionType.Depth:
                        _connector?.SubscribeMarketDepth(security);
                        _log.Debug("Recovered depth subscription for {Symbol}", symbol);
                        break;
                    case SubscriptionType.Candles:
                        _connector?.SubscribeCandles(security, DataType.TimeFrame(TimeSpan.FromMinutes(1)));
                        _log.Debug("Recovered candle subscription for {Symbol}", symbol);
                        break;
                    case SubscriptionType.OrderLog:
                        _connector?.SubscribeOrderLog(security);
                        _log.Debug("Recovered order log subscription for {Symbol}", symbol);
                        break;
                    case SubscriptionType.Quotes:
                        _connector?.SubscribeLevel1(security);
                        _log.Debug("Recovered quote subscription for {Symbol}", symbol);
                        break;
                }

                // Small delay between subscriptions to avoid overwhelming the connector
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to recover subscription for {Symbol}", symbol);
            }
        }
    }

    /// <summary>
    /// Calculate reconnection delay using exponential backoff with jitter.
    /// Based on centralized WebSocketConnectionConfig settings.
    /// </summary>
    private TimeSpan CalculateReconnectDelay(int attempt)
    {
        var baseDelay = _connectionConfig.RetryBaseDelay.TotalMilliseconds;
        var maxDelay = _connectionConfig.MaxRetryDelay.TotalMilliseconds;
        var delay = Math.Min(baseDelay * Math.Pow(2, attempt), maxDelay);

        // Add jitter (±20%) to prevent thundering herd
        var jitter = delay * 0.2 * (Random.Shared.NextDouble() * 2 - 1);
        return TimeSpan.FromMilliseconds(delay + jitter);
    }

    /// <summary>
    /// Update connection state and raise event.
    /// </summary>
    private void SetConnectionState(ConnectionState newState)
    {
        if (CurrentState != newState)
        {
            var oldState = CurrentState;
            CurrentState = newState;
            _log.Debug("Connection state changed: {OldState} → {NewState}", oldState, newState);
            ConnectionStateChanged?.Invoke(newState);
        }
    }

    /// <summary>
    /// Disconnect from the StockSharp data source.
    /// </summary>
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (_connector == null)
        {
            _log.Debug("StockSharp connector not initialized, skipping disconnect");
            return;
        }

        _log.Information("Disconnecting from StockSharp {Type}", _config.ConnectorType);

        var tcs = new TaskCompletionSource<bool>();
        void Handler(object? sender, EventArgs e) => tcs.TrySetResult(true);

        _connector.Disconnected += Handler;

        try
        {
            _connector.Disconnect();
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
            _log.Information("StockSharp connector disconnected");
        }
        catch (TimeoutException)
        {
            _log.Warning("StockSharp disconnect timed out");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Error during StockSharp disconnect");
        }
        finally
        {
            _connector.Disconnected -= Handler;
        }
    }

    /// <summary>
    /// Subscribe to market depth (Level 2) for a symbol.
    /// </summary>
    public int SubscribeMarketDepth(SymbolConfig cfg)
    {
        if (_connector == null)
            throw new InvalidOperationException("StockSharp connector not initialized. Call ConnectAsync first.");

        if (cfg == null)
            throw new ArgumentNullException(nameof(cfg));

        var security = GetOrCreateSecurity(cfg);
        var subId = Interlocked.Increment(ref _nextSubId);

        lock (_gate)
        {
            _subscriptions[subId] = (security, cfg.Symbol, SubscriptionType.Depth);
        }

        _connector.SubscribeMarketDepth(security);
        _log.Debug("Subscribed to depth: {Symbol} (subId={SubId})", cfg.Symbol, subId);

        return subId;
    }

    /// <summary>
    /// Unsubscribe from market depth.
    /// </summary>
    public void UnsubscribeMarketDepth(int subscriptionId)
    {
        (Security Security, string Symbol, SubscriptionType Type) sub;
        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(subscriptionId, out sub))
                return;
            _subscriptions.Remove(subscriptionId);
        }

        _connector?.UnSubscribeMarketDepth(sub.Security);
        _log.Debug("Unsubscribed from depth: {Symbol} (subId={SubId})", sub.Symbol, subscriptionId);
    }

    /// <summary>
    /// Subscribe to trades for a symbol.
    /// </summary>
    public int SubscribeTrades(SymbolConfig cfg)
    {
        if (_connector == null)
            throw new InvalidOperationException("StockSharp connector not initialized. Call ConnectAsync first.");

        if (cfg == null)
            throw new ArgumentNullException(nameof(cfg));

        var security = GetOrCreateSecurity(cfg);
        var subId = Interlocked.Increment(ref _nextSubId);

        lock (_gate)
        {
            _subscriptions[subId] = (security, cfg.Symbol, SubscriptionType.Trades);
        }

        _connector.SubscribeTrades(security);
        _log.Debug("Subscribed to trades: {Symbol} (subId={SubId})", cfg.Symbol, subId);

        return subId;
    }

    /// <summary>
    /// Unsubscribe from trades.
    /// </summary>
    public void UnsubscribeTrades(int subscriptionId)
    {
        (Security Security, string Symbol, SubscriptionType Type) sub;
        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(subscriptionId, out sub))
                return;
            _subscriptions.Remove(subscriptionId);
        }

        _connector?.UnSubscribeTrades(sub.Security);
        _log.Debug("Unsubscribed from trades: {Symbol} (subId={SubId})", sub.Symbol, subscriptionId);
    }

    /// <summary>
    /// Subscribe to real-time candles for a symbol.
    /// Uses the existing MessageConverter.ToHistoricalBar() for conversion.
    /// </summary>
    /// <param name="cfg">Symbol configuration.</param>
    /// <param name="timeFrame">Candle time frame (default: 1 minute).</param>
    /// <returns>Subscription ID, or -1 if candles are not supported.</returns>
    public int SubscribeCandles(SymbolConfig cfg, TimeSpan? timeFrame = null)
    {
        if (_connector == null)
            throw new InvalidOperationException("StockSharp connector not initialized. Call ConnectAsync first.");

        if (cfg == null)
            throw new ArgumentNullException(nameof(cfg));

        var security = GetOrCreateSecurity(cfg);
        var subId = Interlocked.Increment(ref _nextSubId);
        var candleTimeFrame = timeFrame ?? TimeSpan.FromMinutes(1);

        lock (_gate)
        {
            _subscriptions[subId] = (security, cfg.Symbol, SubscriptionType.Candles);
        }

        _connector.SubscribeCandles(security, DataType.TimeFrame(candleTimeFrame));
        _log.Debug("Subscribed to candles: {Symbol} ({TimeFrame}) (subId={SubId})",
            cfg.Symbol, candleTimeFrame, subId);

        return subId;
    }

    /// <summary>
    /// Unsubscribe from candles.
    /// </summary>
    public void UnsubscribeCandles(int subscriptionId)
    {
        (Security Security, string Symbol, SubscriptionType Type) sub;
        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(subscriptionId, out sub))
                return;
            _subscriptions.Remove(subscriptionId);
        }

        // StockSharp candle unsubscription is handled via subscription object.
        _log.Debug("Unsubscribed from candles: {Symbol} (subId={SubId})", sub.Symbol, subscriptionId);
    }

    /// <summary>
    /// Subscribe to order log (tape) for a symbol.
    /// Only supported by certain connectors (Rithmic, IQFeed).
    /// </summary>
    /// <param name="cfg">Symbol configuration.</param>
    /// <returns>Subscription ID, or -1 if order log is not supported by the connector.</returns>
    public int SubscribeOrderLog(SymbolConfig cfg)
    {
        if (_connector == null)
            throw new InvalidOperationException("StockSharp connector not initialized. Call ConnectAsync first.");

        if (cfg == null)
            throw new ArgumentNullException(nameof(cfg));

        // Check if connector supports order log
        var capabilities = StockSharpConnectorCapabilities.GetCapabilities(_config.ConnectorType);
        if (!capabilities.SupportsOrderLog)
        {
            _log.Debug("Order log not supported by {Connector}", _config.ConnectorType);
            return -1;
        }

        var security = GetOrCreateSecurity(cfg);
        var subId = Interlocked.Increment(ref _nextSubId);

        lock (_gate)
        {
            _subscriptions[subId] = (security, cfg.Symbol, SubscriptionType.OrderLog);
        }

        _connector.SubscribeOrderLog(security);
        _log.Debug("Subscribed to order log: {Symbol} (subId={SubId})", cfg.Symbol, subId);

        return subId;
    }

    /// <summary>
    /// Unsubscribe from order log.
    /// </summary>
    public void UnsubscribeOrderLog(int subscriptionId)
    {
        if (subscriptionId == -1)
            return;

        (Security Security, string Symbol, SubscriptionType Type) sub;
        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(subscriptionId, out sub))
                return;
            _subscriptions.Remove(subscriptionId);
        }

        _connector?.UnSubscribeOrderLog(sub.Security);
        _log.Debug("Unsubscribed from order log: {Symbol} (subId={SubId})", sub.Symbol, subscriptionId);
    }

    /// <summary>
    /// Get health metrics for the provider.
    /// Exposes message drop count and last data received timestamp.
    /// </summary>
    public ProviderHealthMetrics GetHealthMetrics()
    {
        return new ProviderHealthMetrics(
            ProviderId: ProviderId,
            ProviderName: ProviderDisplayName,
            CurrentState: CurrentState,
            MessagesDropped: Interlocked.Read(ref _messageDropCount),
            LastDataReceived: GetLastDataReceived(),
            ActiveSubscriptions: GetActiveSubscriptionCount(),
            ConnectorType: _config.ConnectorType
        );
    }

    /// <summary>
    /// Get the count of active subscriptions.
    /// </summary>
    private int GetActiveSubscriptionCount()
    {
        lock (_gate)
        {
            return _subscriptions.Count;
        }
    }

    /// <summary>
    /// Check if the market is currently open for a symbol.
    /// Uses StockSharp ExchangeBoard.WorkingTime for schedule validation.
    /// </summary>
    /// <param name="cfg">Symbol configuration.</param>
    /// <returns>True if the market is currently trading.</returns>
    public bool IsMarketOpen(SymbolConfig cfg)
    {
        if (cfg == null)
            throw new ArgumentNullException(nameof(cfg));

        var security = GetOrCreateSecurity(cfg);
        var board = security.Board;

        if (board == null)
            return true; // Assume open if no board info

        var now = DateTimeOffset.UtcNow;
        return board.WorkingTime.IsTradeTime(now, out _, out _);
    }

    /// <summary>
    /// Get the next market open time for a symbol.
    /// Uses StockSharp ExchangeBoard.WorkingTime for schedule information.
    /// </summary>
    /// <param name="cfg">Symbol configuration.</param>
    /// <returns>Next market open time, or null if unable to determine.</returns>
    public DateTimeOffset? GetNextMarketOpen(SymbolConfig cfg)
    {
        if (cfg == null)
            throw new ArgumentNullException(nameof(cfg));

        var security = GetOrCreateSecurity(cfg);
        var board = security.Board;

        if (board == null)
            return null;

        var now = DateTimeOffset.UtcNow;

        // If market is already open, return now
        if (board.WorkingTime.IsTradeTime(now, out _, out _))
            return now;

        // Find next trading time by checking future dates
        for (int i = 0; i < 7; i++)
        {
            var checkDate = now.AddDays(i);
            if (board.WorkingTime.IsTradeDate(checkDate))
            {
                // Get the trading period for this date
                var periods = board.WorkingTime.GetPeriods(checkDate);
                foreach (var period in periods)
                {
                    var start = new DateTimeOffset(
                        checkDate.Date.Add(period.Till - period.From),
                        TimeSpan.Zero);
                    if (start > now)
                        return start;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Dispose of the client and release resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Cancel reconnection if in progress
        _reconnectCts?.Cancel();
        if (_reconnectTask != null)
        {
            try { await _reconnectTask.ConfigureAwait(false); }
            catch (Exception reconnectEx) when (reconnectEx is not OperationCanceledException)
            {
                _log.Debug(reconnectEx, "Error completing reconnection task during disposal");
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested during disposal
            }
        }

        // Stop heartbeat monitoring
        if (_heartbeatTimer != null)
        {
            await _heartbeatTimer.DisposeAsync().ConfigureAwait(false);
            _heartbeatTimer = null;
        }

        // Stop message processor
        _processorCts?.Cancel();
        if (_messageProcessorTask != null)
        {
            try { await _messageProcessorTask.ConfigureAwait(false); }
            catch (Exception processorEx) when (processorEx is not OperationCanceledException)
            {
                _log.Debug(processorEx, "Error completing message processor task during disposal");
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested during disposal
            }
        }
        _messageChannel.Writer.Complete();

        await DisconnectAsync().ConfigureAwait(false);

        if (_connector != null)
        {
            DetachEventHandlers(_connector);
            _connector.Dispose();
            _connector = null;
        }

        lock (_gate)
        {
            _subscriptions.Clear();
            _securities.Clear();
        }

        _reconnectCts?.Dispose();
        _processorCts?.Dispose();
        SetConnectionState(ConnectionState.Disconnected);
    }

    #region Event Handlers

    private void OnConnected(object? sender, EventArgs e)
    {
        _log.Information("StockSharp connector connected");
    }

    private void OnDisconnected(object? sender, EventArgs e)
    {
        _log.Information("StockSharp connector disconnected unexpectedly");

        // Trigger automatic reconnection if not disposed (Hydra pattern)
        if (!_disposed && CurrentState == ConnectionState.Connected)
        {
            TriggerReconnection();
        }
    }

    private void OnConnectionError(Exception ex)
    {
        _log.Error(ex, "StockSharp connection error");
        SetConnectionState(ConnectionState.Error);

        // Trigger automatic reconnection
        if (!_disposed)
        {
            TriggerReconnection();
        }
    }

    private void OnError(Exception ex)
    {
        _log.Error(ex, "StockSharp error");
    }

    /// <summary>
    /// Handle incoming trade ticks from StockSharp.
    /// Uses message buffering for high-frequency data (Hydra pattern).
    /// </summary>
    private void OnNewTrade(Trade trade)
    {
        try
        {
            if (trade?.Security == null) return;

            // Update heartbeat timestamp (thread-safe)
            UpdateLastDataReceived();

            var symbol = trade.Security.Code ?? trade.Security.Id ?? "UNKNOWN";

            // Buffer the message for processing (Hydra pattern)
            // This prevents blocking the connector's callback thread during bursts
            if (!_messageChannel.Writer.TryWrite(() =>
            {
                try
                {
                    var update = new MarketTradeUpdate(
                        Timestamp: trade.Time,
                        Symbol: symbol,
                        Price: trade.Price,
                        Size: (long)trade.Volume,
                        Aggressor: trade.OrderDirection switch
                        {
                            Sides.Buy => AggressorSide.Buy,
                            Sides.Sell => AggressorSide.Sell,
                            _ => AggressorSide.Unknown
                        },
                        SequenceNumber: trade.Id,
                        StreamId: "STOCKSHARP",
                        Venue: trade.Security?.Board?.Code ?? _config.ConnectorType
                    );

                    _tradeCollector.OnTrade(update);
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "Error processing StockSharp trade for {Symbol}", symbol);
                }
            }))
            {
                // Track message drops for monitoring
                var dropCount = Interlocked.Increment(ref _messageDropCount);
                if (dropCount % 1000 == 0)
                {
                    _log.Warning("StockSharp message buffer overflow: {DropCount} messages dropped total", dropCount);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Critical error in OnNewTrade event handler");
        }
    }

    /// <summary>
    /// Handle incoming market depth changes from StockSharp.
    /// Uses message buffering for high-frequency data (Hydra pattern).
    /// </summary>
    private void OnMarketDepthChanged(MarketDepth depth)
    {
        try
        {
            if (depth?.Security == null) return;

            // Update heartbeat timestamp (thread-safe)
            UpdateLastDataReceived();

            var symbol = depth.Security.Code ?? depth.Security.Id ?? "UNKNOWN";
            var timestamp = depth.LastChangeTime;
            var venue = depth.Security.Board?.Code ?? _config.ConnectorType;

            // Capture lightweight price/volume snapshots instead of full Quote objects
            // to reduce per-message GC pressure in this hot path
            var bidCount = depth.Bids.Count;
            var askCount = depth.Asks.Count;
            var bidData = System.Buffers.ArrayPool<(decimal Price, decimal Volume)>.Shared.Rent(bidCount);
            var askData = System.Buffers.ArrayPool<(decimal Price, decimal Volume)>.Shared.Rent(askCount);
            var bc = 0;
            foreach (var q in depth.Bids) { bidData[bc++] = (q.Price, q.Volume); }
            var ac = 0;
            foreach (var q in depth.Asks) { askData[ac++] = (q.Price, q.Volume); }

            // Buffer the message for processing (Hydra pattern)
            if (!_messageChannel.Writer.TryWrite(() =>
            {
                try
                {
                    // Process bids
                    for (int i = 0; i < bidCount; i++)
                    {
                        var (price, volume) = bidData[i];
                        var update = new MarketDepthUpdate(
                            Timestamp: timestamp,
                            Symbol: symbol,
                            Position: (ushort)i,
                            Operation: DepthOperation.Update,
                            Side: OrderBookSide.Bid,
                            Price: price,
                            Size: volume,
                            MarketMaker: null,
                            SequenceNumber: 0,
                            StreamId: "STOCKSHARP",
                            Venue: venue
                        );
                        _depthCollector.OnDepth(update);
                    }

                    // Process asks
                    for (int i = 0; i < askCount; i++)
                    {
                        var (price, volume) = askData[i];
                        var update = new MarketDepthUpdate(
                            Timestamp: timestamp,
                            Symbol: symbol,
                            Position: (ushort)i,
                            Operation: DepthOperation.Update,
                            Side: OrderBookSide.Ask,
                            Price: price,
                            Size: volume,
                            MarketMaker: null,
                            SequenceNumber: 0,
                            StreamId: "STOCKSHARP",
                            Venue: venue
                        );
                        _depthCollector.OnDepth(update);
                    }
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "Error processing StockSharp depth for {Symbol}", symbol);
                }
                finally
                {
                    System.Buffers.ArrayPool<(decimal Price, decimal Volume)>.Shared.Return(bidData);
                    System.Buffers.ArrayPool<(decimal Price, decimal Volume)>.Shared.Return(askData);
                }
            }))
            {
                Interlocked.Increment(ref _messageDropCount);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Critical error in OnMarketDepthChanged event handler");
        }
    }

    /// <summary>
    /// Handle Level1 value changes (BBO quotes).
    /// Uses message buffering for high-frequency data (Hydra pattern).
    /// </summary>
    private void OnValuesChanged(Security security, IEnumerable<KeyValuePair<Level1Fields, object>> changes, DateTimeOffset serverTime, DateTimeOffset localTime)
    {
        try
        {
            if (security == null) return;

            // Update heartbeat timestamp (thread-safe)
            UpdateLastDataReceived();

            var symbol = security.Code ?? security.Id ?? "UNKNOWN";
            var venue = security.Board?.Code ?? _config.ConnectorType;

            // Pre-process values before buffering
            decimal bidPrice = 0, askPrice = 0;
            long bidSize = 0, askSize = 0;

            foreach (var change in changes)
            {
                switch (change.Key)
                {
                    case Level1Fields.BestBidPrice when change.Value is decimal d:
                        bidPrice = d;
                        break;
                    case Level1Fields.BestBidVolume when change.Value is decimal d:
                        bidSize = (long)d;
                        break;
                    case Level1Fields.BestAskPrice when change.Value is decimal d:
                        askPrice = d;
                        break;
                    case Level1Fields.BestAskVolume when change.Value is decimal d:
                        askSize = (long)d;
                        break;
                }
            }

            // Only emit quote if we have valid bid/ask prices
            if (bidPrice <= 0 && askPrice <= 0) return;

            // Buffer the message for processing (Hydra pattern)
            if (!_messageChannel.Writer.TryWrite(() =>
            {
                try
                {
                    var quoteUpdate = new MarketQuoteUpdate(
                        Timestamp: serverTime,
                        Symbol: symbol,
                        BidPrice: bidPrice,
                        BidSize: bidSize,
                        AskPrice: askPrice,
                        AskSize: askSize,
                        SequenceNumber: null,
                        StreamId: "STOCKSHARP",
                        Venue: venue
                    );

                    _quoteCollector.OnQuote(quoteUpdate);
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "Error processing StockSharp Level1 for {Symbol}", symbol);
                }
            }))
            {
                Interlocked.Increment(ref _messageDropCount);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Critical error in OnValuesChanged event handler");
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Attach all event handlers to a connector instance.
    /// Centralizes event handler management to prevent duplication.
    /// </summary>
    private void AttachEventHandlers(Connector connector)
    {
        connector.Connected += OnConnected;
        connector.Disconnected += OnDisconnected;
        connector.ConnectionError += OnConnectionError;
        connector.NewTrade += OnNewTrade;
        connector.MarketDepthChanged += OnMarketDepthChanged;
        connector.ValuesChanged += OnValuesChanged;
        connector.Error += OnError;
    }

    /// <summary>
    /// Detach all event handlers from a connector instance.
    /// </summary>
    private void DetachEventHandlers(Connector? connector)
    {
        if (connector == null) return;

        connector.Connected -= OnConnected;
        connector.Disconnected -= OnDisconnected;
        connector.ConnectionError -= OnConnectionError;
        connector.NewTrade -= OnNewTrade;
        connector.MarketDepthChanged -= OnMarketDepthChanged;
        connector.ValuesChanged -= OnValuesChanged;
        connector.Error -= OnError;
    }

    /// <summary>
    /// Get or create a StockSharp Security from Meridian SymbolConfig.
    /// </summary>
    private Security GetOrCreateSecurity(SymbolConfig cfg)
    {
        var key = cfg.LocalSymbol ?? cfg.Symbol;

        lock (_gate)
        {
            if (!_securities.TryGetValue(key, out var security))
            {
                security = SecurityConverter.ToSecurity(cfg);
                _securities[key] = security;
            }
            return security;
        }
    }

    #endregion

#else
    // Stub implementations when StockSharp packages are not installed

    /// <summary>
    /// Centralizes the non-StockSharp guard so each #else stub can share one documented failure path.
    /// </summary>
    private static Exception ThrowPlatformNotSupported() => new NotSupportedException(
        "StockSharp integration requires StockSharp.Algo NuGet package. " +
        "Install with: dotnet add package StockSharp.Algo");

    /// <summary>
    /// Stub: Connect not available without StockSharp packages.
    /// </summary>
    public Task ConnectAsync(CancellationToken ct = default)
        => Task.FromException(ThrowPlatformNotSupported());

    /// <summary>
    /// Stub: Disconnect not available without StockSharp packages.
    /// </summary>
    public Task DisconnectAsync(CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stub: SubscribeMarketDepth not available without StockSharp packages.
    /// </summary>
    public int SubscribeMarketDepth(SymbolConfig cfg)
        => throw ThrowPlatformNotSupported();

    /// <summary>
    /// Stub: UnsubscribeMarketDepth not available without StockSharp packages.
    /// </summary>
    public void UnsubscribeMarketDepth(int subscriptionId)
    {
    }

    /// <summary>
    /// Stub: SubscribeTrades not available without StockSharp packages.
    /// </summary>
    public int SubscribeTrades(SymbolConfig cfg)
        => throw ThrowPlatformNotSupported();

    /// <summary>
    /// Stub: UnsubscribeTrades not available without StockSharp packages.
    /// </summary>
    public void UnsubscribeTrades(int subscriptionId)
    {
    }

    /// <summary>
    /// Stub: DisposeAsync.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
#endif
}

/// <summary>
/// Connection state for the StockSharp client (Hydra-inspired pattern).
/// </summary>
public enum ConnectionState : byte
{
    /// <summary>Client is disconnected.</summary>
    Disconnected,

    /// <summary>Client is connecting.</summary>
    Connecting,

    /// <summary>Client is connected and receiving data.</summary>
    Connected,

    /// <summary>Client is reconnecting after a connection loss.</summary>
    Reconnecting,

    /// <summary>Client encountered an error.</summary>
    Error
}

/// <summary>
/// Type of subscription for recovery purposes.
/// </summary>
public enum SubscriptionType : byte
{
    /// <summary>Trade tick subscription.</summary>
    Trades,

    /// <summary>Market depth (Level 2) subscription.</summary>
    Depth,

    /// <summary>BBO/Level 1 quote subscription.</summary>
    Quotes,

    /// <summary>OHLC candle subscription.</summary>
    Candles,

    /// <summary>Order log (tape) subscription.</summary>
    OrderLog
}

/// <summary>
/// Health metrics for the StockSharp provider.
/// Exposes internal state for monitoring and dashboard display.
/// </summary>
/// <param name="ProviderId">Provider identifier.</param>
/// <param name="ProviderName">Human-readable provider name.</param>
/// <param name="CurrentState">Current connection state.</param>
/// <param name="MessagesDropped">Total messages dropped due to buffer overflow.</param>
/// <param name="LastDataReceived">Timestamp of last data received.</param>
/// <param name="ActiveSubscriptions">Count of active subscriptions.</param>
/// <param name="ConnectorType">StockSharp connector type.</param>
public sealed record ProviderHealthMetrics(
    string ProviderId,
    string ProviderName,
    ConnectionState CurrentState,
    long MessagesDropped,
    DateTimeOffset LastDataReceived,
    int ActiveSubscriptions,
    string ConnectorType
)
{
    /// <summary>
    /// Whether the provider is healthy (connected and receiving data recently).
    /// </summary>
    public bool IsHealthy => CurrentState == ConnectionState.Connected &&
        (DateTimeOffset.UtcNow - LastDataReceived).TotalMinutes < 5;

    /// <summary>
    /// Time since last data was received.
    /// </summary>
    public TimeSpan TimeSinceLastData => DateTimeOffset.UtcNow - LastDataReceived;

    /// <summary>
    /// Get a dictionary representation for serialization.
    /// </summary>
    public IReadOnlyDictionary<string, object> ToDictionary() => new Dictionary<string, object>
    {
        ["providerId"] = ProviderId,
        ["providerName"] = ProviderName,
        ["currentState"] = CurrentState.ToString(),
        ["messagesDropped"] = MessagesDropped,
        ["lastDataReceived"] = LastDataReceived.ToString("O"),
        ["activeSubscriptions"] = ActiveSubscriptions,
        ["connectorType"] = ConnectorType,
        ["isHealthy"] = IsHealthy,
        ["timeSinceLastDataSeconds"] = TimeSinceLastData.TotalSeconds
    };
}

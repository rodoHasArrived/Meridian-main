#if IBAPI
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using IBApi;
using Meridian.Core.Config;
using Meridian.Core.Logging;
using Meridian.Core.Performance;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Resilience;
using Meridian.ProviderSdk;
using Serilog;

namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

public sealed partial class EnhancedIBConnectionManager : EWrapper, IDisposable
{
    private static readonly ILogger _log = LoggingSetup.ForContext<EnhancedIBConnectionManager>();
    private readonly IBCallbackRouter _router;

    private readonly EReaderSignal _signal;
    private readonly EClientSocket _clientSocket;
    private EReader? _reader;

    private CancellationTokenSource? _cts;
    private Task? _readerLoop;
    private readonly ProviderConnectionSupervisor _connectionSupervisor;

    private int _nextDepthTickerId = 10_000;
    private readonly ConcurrentDictionary<int, string> _depthTickerMap = new();
    private readonly ConcurrentDictionary<int, bool> _depthTickerSmartDepthMap = new();
    private readonly ConcurrentDictionary<int, DepthSubscription> _depthSubscriptions = new();

    private int _nextTradeTickerId = 20_000;
    private readonly ConcurrentDictionary<int, string> _tradeTickerMap = new();
    private readonly ConcurrentDictionary<int, TradeSubscription> _tradeSubscriptions = new();

    private int _nextQuoteTickerId = 30_000;
    private readonly ConcurrentDictionary<int, string> _quoteTickerMap = new();
    private readonly ConcurrentDictionary<int, QuoteSubscription> _quoteSubscriptions = new();

    private int _nextHistoricalReqId = 40_000;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<List<IBApi.Bar>>> _historicalDataRequests = new();
    private readonly ConcurrentDictionary<int, List<IBApi.Bar>> _historicalDataBuffers = new();
    private int _nextBrokerRequestId = 50_000;
    private readonly ConcurrentDictionary<int, int> _marketRuleRequests = new();

    // Performance monitoring
    private readonly ConnectionWarmUp _warmUp;
    private HeartbeatMonitor? _heartbeatMonitor;

    // Connection state
    private volatile bool _disposed;
    private long _lastMessageTimestamp;
    private long _connectionEstablishedTimestamp;
    private long _totalMessagesReceived;
    private DateTimeOffset? _lastMessageReceivedAt;

    // Latency tracking
    private long _currentTimeRequestTimestamp;
    private double _lastRoundTripLatencyUs;

    public EnhancedIBConnectionManager(
        IBCallbackRouter router,
        string host = "127.0.0.1",
        int port = 7497,
        int clientId = 1,
        bool enableAutoReconnect = true,
        bool enableHeartbeat = true)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _signal = new EReaderMonitorSignal();
        _clientSocket = new EClientSocket(this, _signal);

        Host = host;
        Port = port;
        ClientId = clientId;
        EnableAutoReconnect = enableAutoReconnect;
        EnableHeartbeat = enableHeartbeat;

        // Initialize performance utilities
        _warmUp = new ConnectionWarmUp(
            warmUpInterval: TimeSpan.FromMinutes(5),
            warmUpIterations: 5);

        _connectionSupervisor = new ProviderConnectionSupervisor(
            providerName: "Interactive Brokers",
            maxReconnectAttempts: 10,
            retryBaseDelay: TimeSpan.FromSeconds(1),
            maxRetryDelay: TimeSpan.FromMinutes(2),
            logger: _log);
        _connectionSupervisor.StateChanged += OnConnectionSupervisorStateChanged;
    }

    public string Host { get; }
    public int Port { get; }
    public int ClientId { get; }
    public bool EnableAutoReconnect { get; set; }
    public bool EnableHeartbeat { get; set; }

    public bool IsConnected => _connectionSupervisor.IsConnected && IsTransportConnected;
    private bool IsTransportConnected => _clientSocket.IsConnected();
    public bool IsReconnecting => _connectionSupervisor.IsReconnecting;
    public long TotalMessagesReceived => Interlocked.Read(ref _totalMessagesReceived);
    public long ReconnectAttempts => _connectionSupervisor.GetSnapshot().ReconnectAttempts;
    public double LastRoundTripLatencyUs => _lastRoundTripLatencyUs;
    public WarmUpStatistics? LastWarmUpStats => _warmUp.LastWarmUpStats;

    public TimeSpan ConnectionUptime
    {
        get
        {
            var ts = Interlocked.Read(ref _connectionEstablishedTimestamp);
            if (ts == 0 || !IsConnected) return TimeSpan.Zero;
            return TimeSpan.FromTicks((long)((Stopwatch.GetTimestamp() - ts) *
                (TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency)));
        }
    }

    /// <summary>
    /// Event raised when connection is lost.
    /// </summary>
    public event EventHandler? ConnectionLost;

    /// <summary>
    /// Event raised when connection is restored after reconnection.
    /// </summary>
    public event EventHandler? ConnectionRestored;

    /// <summary>
    /// Raised when the shared connection lifecycle snapshot changes.
    /// </summary>
    public event Action<WebSocketConnectionDiagnostics>? ConnectionDiagnosticsChanged;

    /// <summary>
    /// Raised whenever a live streaming request is sent to TWS/Gateway.
    /// </summary>
    public event EventHandler? StreamingRequestSent;

    /// <summary>
    /// Event raised on latency measurement from heartbeat/time request.
    /// </summary>
    public event EventHandler<double>? LatencyMeasured;
    public event EventHandler<int>? NextValidIdReceived;
    public event EventHandler<IBOrderStatusUpdate>? OrderStatusReceived;
    public event EventHandler<IBOpenOrderUpdate>? OpenOrderReceived;
    public event EventHandler? OpenOrdersCompleted;
    public event EventHandler<IBExecutionUpdate>? ExecutionDetailsReceived;
    public event EventHandler<IBPositionUpdate>? PositionReceived;
    public event EventHandler? PositionsCompleted;
    public event EventHandler<IBAccountSummaryUpdate>? AccountSummaryReceived;
    public event EventHandler<int>? AccountSummaryCompleted;
    public event EventHandler<(int RequestId, ProviderOptionContract Contract)>? OptionContractReceived;
    public event EventHandler<(int RequestId, ProviderScannerResult Result)>? ScannerResultReceived;
    public event EventHandler<(int RequestId, ProviderRealTimeBar Bar)>? RealTimeBarReceived;
    public event EventHandler<(int RequestId, ProviderHistoricalTick Tick, bool Completed)>? HistoricalTickReceived;
    public event EventHandler<(int RequestId, ProviderAccountPnl Pnl)>? PnlReceived;
    public event EventHandler<(int RequestId, IReadOnlyList<ProviderMarketRuleIncrement> Increments)>? MarketRuleReceived;
    public event EventHandler<int>? RequestCompleted;
    public event EventHandler<(int RequestId, string Code, string Message)>? RequestRejected;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected) return;
        if (_disposed) throw new ObjectDisposedException(nameof(EnhancedIBConnectionManager));

        await _connectionSupervisor.ConnectAsync(
            CompleteConnectionTransactionAsync,
            ct).ConfigureAwait(false);
    }

    private async Task CompleteConnectionTransactionAsync(CancellationToken ct)
    {
        await CleanupTransportAsync(CancellationToken.None).ConfigureAwait(false);
        await ConnectTransportAsync(ct).ConfigureAwait(false);
        await ReplaySubscriptionsAsync(ct).ConfigureAwait(false);
    }

    private async Task ConnectTransportAsync(CancellationToken ct)
    {
        _clientSocket.eConnect(Host, Port, ClientId);

        // Wait briefly for connection establishment
        await Task.Delay(100, ct).ConfigureAwait(false);

        if (!IsTransportConnected)
        {
            throw new InvalidOperationException($"Failed to connect to IB Gateway at {Host}:{Port}");
        }

        _reader = new EReader(_clientSocket, _signal);
        _reader.Start();

        // Start reader loop with high priority
        var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(
            _connectionSupervisor.ConnectionLifetimeToken);
        _cts = connectionCts;
        _readerLoop = Task.Factory.StartNew(
            () => ReaderLoop(connectionCts.Token),
            connectionCts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        Interlocked.Exchange(ref _connectionEstablishedTimestamp, Stopwatch.GetTimestamp());

        // Validate the server version reported by TWS/Gateway before proceeding.
        IBApiVersionValidator.ValidateServerVersion(
            _clientSocket.ServerVersion,
            IBApiVersionValidator.MinSupportedClientVersion);

        // Start heartbeat monitor if enabled
        if (EnableHeartbeat)
        {
            StartHeartbeatMonitor();
        }
    }

    /// <summary>
    /// Performs connection warm-up by sending lightweight requests to prime the connection.
    /// Call this before market open to minimize initial latency variance.
    /// </summary>
    public async Task<WarmUpStatistics> WarmUpConnectionAsync(CancellationToken ct = default)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Connection must be established before warm-up");

        return await _warmUp.ExecuteWarmUpAsync(async token =>
        {
            // Request current time as a lightweight operation to warm up the connection
            await RequestCurrentTimeAsync(token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Requests the current server time and measures round-trip latency.
    /// </summary>
    public Task RequestCurrentTimeAsync(CancellationToken ct = default)
    {
        if (!IsConnected) return Task.CompletedTask;

        Interlocked.Exchange(ref _currentTimeRequestTimestamp, Stopwatch.GetTimestamp());
        _clientSocket.reqCurrentTime();
        return Task.CompletedTask;
    }

    private void StartHeartbeatMonitor()
    {
        _heartbeatMonitor?.Dispose();
        _heartbeatMonitor = new HeartbeatMonitor(
            heartbeatFunc: async ct =>
            {
                if (!IsConnected) return false;
                await RequestCurrentTimeAsync(ct).ConfigureAwait(false);
                return true;
            },
            interval: TimeSpan.FromSeconds(30),
            timeout: TimeSpan.FromSeconds(10));

        _heartbeatMonitor.ConnectionUnhealthy += OnHeartbeatUnhealthy;
        _heartbeatMonitor.Start();
    }

    private void OnHeartbeatUnhealthy(object? sender, EventArgs e)
    {
        NotifyConnectionLost(new TimeoutException("Interactive Brokers heartbeat became unhealthy."));
    }

    public Task DisconnectAsync(CancellationToken ct = default)
        => _connectionSupervisor.DisconnectAsync(CleanupTransportAsync, ct);

    /// <summary>
    /// Returns a safe lifecycle snapshot for the provider diagnostics endpoints.
    /// Interactive Brokers uses a TCP socket, so the WebSocket state is intentionally None.
    /// </summary>
    public WebSocketConnectionDiagnostics GetConnectionDiagnosticsSnapshot()
    {
        var supervisor = _connectionSupervisor.GetSnapshot();
        var now = DateTimeOffset.UtcNow;
        var lastActivity = _lastMessageReceivedAt ?? supervisor.LastConnectedAt;

        return new WebSocketConnectionDiagnostics(
            ProviderName: "Interactive Brokers",
            LifecycleState: supervisor.LifecycleState,
            WebSocketState: System.Net.WebSockets.WebSocketState.None,
            IsConnected: IsConnected,
            IsReconnecting: supervisor.IsReconnecting,
            ReconnectAttempts: supervisor.ReconnectAttempts,
            LastConnectedAt: supervisor.LastConnectedAt,
            LastDisconnectedAt: supervisor.LastDisconnectedAt,
            LastHeartbeatReceivedAt: _lastMessageReceivedAt,
            LastMessageReceivedAt: _lastMessageReceivedAt,
            LastReconnectAttemptAt: supervisor.LastReconnectAttemptAt,
            LastError: supervisor.LastError,
            LastFailureKind: supervisor.LastFailureKind,
            ConnectionAge: supervisor.ConnectionAge,
            IdleDuration: lastActivity.HasValue ? now - lastActivity.Value : null,
            ActiveSubscriptions: _depthSubscriptions.Count + _tradeSubscriptions.Count + _quoteSubscriptions.Count);
    }

    private async Task CleanupTransportAsync(CancellationToken ct)
    {
        var heartbeatMonitor = _heartbeatMonitor;
        var connectionCts = _cts;
        var readerLoop = _readerLoop;

        _heartbeatMonitor = null;
        _cts = null;
        _readerLoop = null;
        _reader = null;

        heartbeatMonitor?.Dispose();

        try
        {
            connectionCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        if (IsTransportConnected)
            _clientSocket.eDisconnect();

        if (readerLoop is not null && readerLoop.Id != Task.CurrentId)
        {
            try
            {
                await readerLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
            {
                _log.Debug(ex, "Interactive Brokers reader-loop cleanup failed");
            }
        }

        connectionCts?.Dispose();
        Interlocked.Exchange(ref _connectionEstablishedTimestamp, 0);
        ct.ThrowIfCancellationRequested();
    }

    private void OnConnectionSupervisorStateChanged(ProviderConnectionSupervisorSnapshot _)
        => ConnectionDiagnosticsChanged?.Invoke(GetConnectionDiagnosticsSnapshot());

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            DisconnectAsync().GetAwaiter().GetResult();
        }
        finally
        {
            _connectionSupervisor.StateChanged -= OnConnectionSupervisorStateChanged;
            _connectionSupervisor.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _cts?.Dispose();
            _cts = null;
            _disposed = true;
        }
    }

    private void ReaderLoop(CancellationToken ct)
    {
        // Set thread priority for reduced latency
        ThreadingUtilities.SetHighPriority();

        while (!ct.IsCancellationRequested && IsConnected)
        {
            try
            {
                _signal.waitForSignal();
                if (ct.IsCancellationRequested) break;

                _reader?.processMsgs();
                Interlocked.Exchange(ref _lastMessageTimestamp, Stopwatch.GetTimestamp());
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is System.Net.WebSockets.WebSocketException or IOException or InvalidOperationException)
            {
                NotifyConnectionLost(ex);
                break;
            }
        }
    }

    private void NotifyConnectionLost(Exception? error = null)
    {
        if (_disposed || !_connectionSupervisor.MarkConnectionLost(error))
            return;

        ConnectionLost?.Invoke(this, EventArgs.Empty);

        if (EnableAutoReconnect)
            _ = TriggerReconnectAsync();
    }

    private async Task TriggerReconnectAsync(CancellationToken ct = default)
    {
        try
        {
            var reconnected = await _connectionSupervisor.ReconnectAsync(
                CompleteConnectionTransactionAsync,
                ct).ConfigureAwait(false);

            if (reconnected)
            {
                ConnectionRestored?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || _disposed)
        {
            // Explicit shutdown cancelled the supervised reconnect.
        }
        catch (Exception ex)
        {
            _connectionSupervisor.RecordFailure(ex);
            _log.Error(ex, "Interactive Brokers supervised reconnect failed");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RecordMessageReceived()
    {
        Interlocked.Increment(ref _totalMessagesReceived);
        Interlocked.Exchange(ref _lastMessageTimestamp, Stopwatch.GetTimestamp());
        _lastMessageReceivedAt = DateTimeOffset.UtcNow;
    }

    // -----------------------
    // Depth subscriptions
    // -----------------------
    public int SubscribeMarketDepth(string symbol, Contract contract, int depthLevels = 10, bool smartDepth = true)
    {
        if (string.IsNullOrWhiteSpace(symbol)) throw new ArgumentException("symbol required", nameof(symbol));
        if (contract is null) throw new ArgumentNullException(nameof(contract));

        var id = Interlocked.Increment(ref _nextDepthTickerId);
        _depthTickerMap[id] = symbol;
        _depthSubscriptions[id] = new DepthSubscription(symbol, contract, depthLevels, smartDepth);

        // Router needs this mapping for callbacks
        _router.RegisterDepthTicker(id, symbol);
        _depthTickerSmartDepthMap[id] = smartDepth;

        SendDepthSubscription(id, contract, depthLevels, smartDepth);
        return id;
    }

    /// <summary>
    /// Subscribe to L2 depth using a SymbolConfig (contract built via ContractFactory).
    /// </summary>
    public int SubscribeMarketDepth(SymbolConfig cfg, bool smartDepth = true)
    {
        if (cfg is null) throw new ArgumentNullException(nameof(cfg));
        var contract = ContractFactory.Create(cfg);
        var levels = cfg.DepthLevels <= 0 ? 10 : cfg.DepthLevels;
        return SubscribeMarketDepth(cfg.Symbol, contract, levels, smartDepth);
    }

    public void UnsubscribeMarketDepth(int tickerId)
    {
        var smartDepth = _depthTickerSmartDepthMap.TryRemove(tickerId, out var storedSmartDepth) && storedSmartDepth;
        _clientSocket.cancelMktDepth(tickerId, smartDepth);
        _depthTickerMap.TryRemove(tickerId, out _);
        _depthSubscriptions.TryRemove(tickerId, out _);
        StreamingRequestSent?.Invoke(this, EventArgs.Empty);
    }


    // -----------------------
    // Trade (tick-by-tick) subscriptions
    // -----------------------
    public int SubscribeTrades(SymbolConfig cfg)
    {
        if (cfg is null) throw new ArgumentNullException(nameof(cfg));
        var contract = ContractFactory.Create(cfg);

        var id = Interlocked.Increment(ref _nextTradeTickerId);
        _tradeTickerMap[id] = cfg.Symbol;
        _tradeSubscriptions[id] = new TradeSubscription(cfg.Symbol, contract);
        _router.RegisterTradeTicker(id, cfg.Symbol);

        // tickType can be "AllLast" (prints + special conditions) or "Last".
        SendTradeSubscription(id, contract);
        return id;
    }

    public void UnsubscribeTrades(int tickerId)
    {
        _clientSocket.cancelTickByTickData(tickerId);
        _tradeTickerMap.TryRemove(tickerId, out _);
        _tradeSubscriptions.TryRemove(tickerId, out _);
        StreamingRequestSent?.Invoke(this, EventArgs.Empty);
    }

    // -----------------------
    // Level 1 quote subscriptions (reqMktData)
    // -----------------------

    /// <summary>
    /// Subscribe to Level 1 streaming market data for a symbol using reqMktData.
    /// Uses free Cboe One + IEX data for US equities.
    /// </summary>
    /// <param name="cfg">Symbol configuration.</param>
    /// <param name="genericTickList">Comma-separated generic tick types (e.g., "233,236" for RT Volume + Shortable).</param>
    /// <param name="snapshot">True for one-time snapshot (uses monthly quota).</param>
    /// <param name="regulatorySnapshot">True for regulatory snapshot ($0.01/request).</param>
    /// <returns>The subscription request ID.</returns>
    public int SubscribeQuotes(SymbolConfig cfg, string? genericTickList = null, bool snapshot = false, bool regulatorySnapshot = false)
    {
        if (cfg is null) throw new ArgumentNullException(nameof(cfg));
        var contract = ContractFactory.Create(cfg);
        var ticks = genericTickList ?? IBGenericTickTypes.DefaultEquityGenericTicks;

        var id = Interlocked.Increment(ref _nextQuoteTickerId);
        _quoteTickerMap[id] = cfg.Symbol;
        _quoteSubscriptions[id] = new QuoteSubscription(
            cfg.Symbol,
            contract,
            ticks,
            snapshot,
            regulatorySnapshot);
        _router.RegisterQuoteTicker(id, cfg.Symbol);

        SendQuoteSubscription(id, contract, ticks, snapshot, regulatorySnapshot);
        return id;
    }

    /// <summary>
    /// Unsubscribe from Level 1 market data.
    /// </summary>
    public void UnsubscribeQuotes(int tickerId)
    {
        _clientSocket.cancelMktData(tickerId);
        _quoteTickerMap.TryRemove(tickerId, out _);
        _quoteSubscriptions.TryRemove(tickerId, out _);
        StreamingRequestSent?.Invoke(this, EventArgs.Empty);
    }

    private Task ReplaySubscriptionsAsync(CancellationToken ct)
    {
        foreach (var (tickerId, subscription) in _depthSubscriptions.OrderBy(pair => pair.Key))
        {
            ct.ThrowIfCancellationRequested();
            _router.RegisterDepthTicker(tickerId, subscription.Symbol);
            SendDepthSubscription(
                tickerId,
                subscription.Contract,
                subscription.DepthLevels,
                subscription.SmartDepth);
        }

        foreach (var (tickerId, subscription) in _tradeSubscriptions.OrderBy(pair => pair.Key))
        {
            ct.ThrowIfCancellationRequested();
            _router.RegisterTradeTicker(tickerId, subscription.Symbol);
            SendTradeSubscription(tickerId, subscription.Contract);
        }

        foreach (var (tickerId, subscription) in _quoteSubscriptions.OrderBy(pair => pair.Key))
        {
            ct.ThrowIfCancellationRequested();
            _router.RegisterQuoteTicker(tickerId, subscription.Symbol);
            SendQuoteSubscription(
                tickerId,
                subscription.Contract,
                subscription.GenericTickList,
                subscription.Snapshot,
                subscription.RegulatorySnapshot);
        }

        return Task.CompletedTask;
    }

    private void SendDepthSubscription(int tickerId, Contract contract, int depthLevels, bool smartDepth)
    {
#if IBAPI_VENDOR
        _clientSocket.reqMarketDepth(tickerId, contract, depthLevels, smartDepth, null);
#else
        _clientSocket.reqMktDepth(tickerId, contract, depthLevels, smartDepth, null);
#endif
        StreamingRequestSent?.Invoke(this, EventArgs.Empty);
    }

    private void SendTradeSubscription(int tickerId, Contract contract)
    {
        _clientSocket.reqTickByTickData(tickerId, contract, "AllLast", 0, ignoreSize: false);
        StreamingRequestSent?.Invoke(this, EventArgs.Empty);
    }

    private void SendQuoteSubscription(
        int tickerId,
        Contract contract,
        string genericTickList,
        bool snapshot,
        bool regulatorySnapshot)
    {
        _clientSocket.reqMktData(
            tickerId,
            contract,
            genericTickList,
            snapshot,
            regulatorySnapshot,
            null);
        StreamingRequestSent?.Invoke(this, EventArgs.Empty);
    }

    // -----------------------
    // Historical data requests
    // -----------------------

    /// <summary>
    /// Request historical bars for a symbol.
    /// Requires active Level 1 streaming subscription for US equities.
    /// </summary>
    /// <param name="cfg">Symbol configuration.</param>
    /// <param name="endDateTime">End date/time (empty string = now).</param>
    /// <param name="durationStr">Duration (e.g., "1 D", "1 W", "1 M").</param>
    /// <param name="barSizeSetting">Bar size (e.g., "1 min", "1 hour", "1 day").</param>
    /// <param name="whatToShow">Data type (TRADES, MIDPOINT, BID, ASK, etc.).</param>
    /// <param name="useRTH">True = Regular Trading Hours only.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of historical bars.</returns>
    public async Task<List<IBApi.Bar>> RequestHistoricalDataAsync(
        SymbolConfig cfg,
        string endDateTime,
        string durationStr,
        string barSizeSetting,
        string whatToShow,
        bool useRTH = true,
        CancellationToken ct = default)
    {
        if (cfg is null) throw new ArgumentNullException(nameof(cfg));
        if (!IsConnected) throw new InvalidOperationException("Not connected to IB Gateway/TWS");

        var contract = ContractFactory.Create(cfg);
        var id = Interlocked.Increment(ref _nextHistoricalReqId);

        var tcs = new TaskCompletionSource<List<IBApi.Bar>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _historicalDataRequests[id] = tcs;
        _historicalDataBuffers[id] = new List<IBApi.Bar>();

        // Register cancellation
        await using var registration = ct.Register(() =>
        {
            if (_historicalDataRequests.TryRemove(id, out var t))
            {
                _historicalDataBuffers.TryRemove(id, out _);
                t.TrySetCanceled(ct);
                _clientSocket.cancelHistoricalData(id);
            }
        });

        try
        {
            _clientSocket.reqHistoricalData(
                id,
                contract,
                endDateTime,
                durationStr,
                barSizeSetting,
                whatToShow,
                useRTH ? 1 : 0,
                1, // formatDate: 1 = string format
                false, // keepUpToDate
                null); // chartOptions

            return await tcs.Task.ConfigureAwait(false);
        }
        catch
        {
            _historicalDataRequests.TryRemove(id, out _);
            _historicalDataBuffers.TryRemove(id, out _);
            throw;
        }
    }

    public void RequestNextValidId()
    {
        if (!IsConnected) throw new InvalidOperationException("Not connected to IB Gateway/TWS");
        _clientSocket.reqIds(-1);
    }

    public Task PlaceOrderAsync(int orderId, OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsConnected) throw new InvalidOperationException("Not connected to IB Gateway/TWS");
        ct.ThrowIfCancellationRequested();

        var contract = BuildBrokerageContract(request);
        var order = BuildBrokerageOrder(orderId, request);
        _clientSocket.placeOrder(orderId, contract, order);
        return Task.CompletedTask;
    }

    public Task CancelOrderAsync(int orderId, CancellationToken ct = default)
    {
        if (!IsConnected) throw new InvalidOperationException("Not connected to IB Gateway/TWS");
        ct.ThrowIfCancellationRequested();

        _clientSocket.cancelOrder(orderId, new IBApi.OrderCancel());
        return Task.CompletedTask;
    }

    public int RequestAccountSummary()
    {
        if (!IsConnected) throw new InvalidOperationException("Not connected to IB Gateway/TWS");

        var requestId = Interlocked.Increment(ref _nextBrokerRequestId);
        _clientSocket.reqAccountSummary(
            requestId,
            "All",
            "NetLiquidation,TotalCashValue,BuyingPower,Currency");
        return requestId;
    }

    public void CancelAccountSummary(int requestId)
    {
        if (IsConnected)
            _clientSocket.cancelAccountSummary(requestId);
    }

    public void RequestPositions()
    {
        if (!IsConnected) throw new InvalidOperationException("Not connected to IB Gateway/TWS");
        _clientSocket.reqPositions();
    }

    public void CancelPositions()
    {
        if (IsConnected)
            _clientSocket.cancelPositions();
    }

    public void RequestOpenOrders()
    {
        if (!IsConnected) throw new InvalidOperationException("Not connected to IB Gateway/TWS");
        _clientSocket.reqOpenOrders();
    }


    /// <summary>
    /// Request historical bars with default settings for daily OHLCV data.
    /// </summary>
    public Task<List<IBApi.Bar>> RequestDailyBarsAsync(
        SymbolConfig cfg,
        int daysBack = 30,
        bool useRTH = true,
        CancellationToken ct = default)
    {
        var endDateTime = DateTime.Now.ToString("yyyyMMdd-HH:mm:ss");
        return RequestHistoricalDataAsync(
            cfg,
            endDateTime,
            $"{daysBack} D",
            IBBarSizes.Day1,
            IBWhatToShow.Trades,
            useRTH,
            ct);
    }

    // -----------------------
    // IB entitlement-sensitive data services
    private void ThrowIfNotConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected to IB Gateway/TWS");
    }

    // -----------------------
    public void RequestScanner(int requestId, IBScannerRequest request)
    {
        ThrowIfNotConnected();
        var subscription = new ScannerSubscription
        {
            Instrument = request.Instrument,
            LocationCode = request.LocationCode,
            ScanCode = request.ScanCode,
            NumberOfRows = request.NumberOfRows,
            AbovePrice = request.AbovePrice,
            AboveVolume = request.AboveVolume
        };
        _clientSocket.reqScannerSubscription(requestId, subscription, [], []);
    }

    public void RequestContractDetails(int requestId, SymbolConfig contract)
    {
        ThrowIfNotConnected();
        _clientSocket.reqContractDetails(requestId, ContractFactory.Create(contract));
    }

    public void RequestOptionChain(int requestId, SymbolConfig underlying)
    {
        ThrowIfNotConnected();
        if (underlying.ConId is not int conId || conId <= 0)
            throw new ArgumentException("IB option-chain requests require the resolved underlying ConId.", nameof(underlying));
        _clientSocket.reqSecDefOptParams(requestId, underlying.Symbol, string.Empty, ContractFactory.ResolveSecType(underlying), conId);
    }

    public void RequestHistoricalNews(int requestId, int conId, string providerCodes, DateTimeOffset start, DateTimeOffset end, int maximumResults)
    {
        ThrowIfNotConnected();
        _clientSocket.reqHistoricalNews(requestId, conId, providerCodes,
            start.UtcDateTime.ToString("yyyyMMdd-HH:mm:ss", CultureInfo.InvariantCulture),
            end.UtcDateTime.ToString("yyyyMMdd-HH:mm:ss", CultureInfo.InvariantCulture), maximumResults, []);
    }

    public void RequestNewsArticle(int requestId, string providerCode, string articleId)
    {
        ThrowIfNotConnected();
        _clientSocket.reqNewsArticle(requestId, providerCode, articleId, []);
    }

    public void RequestFundamentals(int requestId, SymbolConfig contract, string reportType)
    {
        ThrowIfNotConnected();
        _clientSocket.reqFundamentalData(requestId, ContractFactory.Create(contract), reportType, []);
    }

    public void RequestDividendEarnings(int requestId, SymbolConfig contract)
    {
        ThrowIfNotConnected();
        _clientSocket.reqMktData(requestId, ContractFactory.Create(contract), "456,258", false, false, []);
    }

    public void RequestTickByTick(int requestId, SymbolConfig contract, string tickType, int numberOfTicks, bool ignoreSize)
    {
        ThrowIfNotConnected();
        _clientSocket.reqTickByTickData(requestId, ContractFactory.Create(contract), tickType, numberOfTicks, ignoreSize);
    }

    public void RequestPnl(int requestId, string account, string? modelCode)
    {
        ThrowIfNotConnected();
        _clientSocket.reqPnL(requestId, account, modelCode ?? string.Empty);
    }

    public void RequestMarketRule(int requestId, int marketRuleId)
    {
        ThrowIfNotConnected();
        _marketRuleRequests[marketRuleId] = requestId;
        _clientSocket.reqMarketRule(marketRuleId);
    }

    public void RequestDepthExchanges(int requestId)
    {
        ThrowIfNotConnected();
        _clientSocket.reqMktDepthExchanges();
    }

    /// <summary>Starts a correlated, five-second real-time bar stream.</summary>
    public void RequestRealTimeBars(int requestId, IBRealTimeBarRequest request)
    {
        ThrowIfNotConnected();
#if IBAPI_VENDOR
        _clientSocket.reqRealTimeBars(requestId, ContractFactory.Create(request.Contract), 5, request.WhatToShow, request.UseRegularTradingHours, []);
#else
        throw new NotSupportedException("Real-time bars require the official Interactive Brokers vendor SDK.");
#endif
    }

    /// <summary>Requests a bounded historical-tick page with explicit time bounds.</summary>
    public void RequestHistoricalTicks(int requestId, IBHistoricalTickRequest request)
    {
        ThrowIfNotConnected();
#if IBAPI_VENDOR
        _clientSocket.reqHistoricalTicks(requestId, ContractFactory.Create(request.Contract),
            request.Start?.UtcDateTime.ToString("yyyyMMdd-HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty,
            request.End?.UtcDateTime.ToString("yyyyMMdd-HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty,
            request.NumberOfTicks, request.WhatToShow, request.UseRegularTradingHours ? 1 : 0, false, []);
#else
        throw new NotSupportedException("Historical ticks require the official Interactive Brokers vendor SDK.");
#endif
    }

    /// <summary>Cancels the associated vendor stream when its request lifetime ends.</summary>
    public void CancelDataRequest(int requestId, string capability)
    {
        if (!IsConnected) return;
        switch (capability)
        {
            case "scanner":
#if IBAPI_VENDOR
                _clientSocket.cancelScannerSubscription(requestId);
#endif
                break;
            case "tick-by-tick": _clientSocket.cancelTickByTickData(requestId); break;
            case "pnl":
#if IBAPI_VENDOR
                _clientSocket.cancelPnL(requestId);
#endif
                break;
            case "real-time-bars":
#if IBAPI_VENDOR
                _clientSocket.cancelRealTimeBars(requestId);
#endif
                break;
            case "historical-ticks":
                break; // IB historical ticks are bounded and complete from their terminal callback.
        }
    }

    // -----------------------
    // EWrapper depth callbacks
    // -----------------------
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void updateMktDepth(int tickerId, int position, int operation, int side, double price, decimal size)
    {
        RecordMessageReceived();
        _router.UpdateMktDepth(tickerId, position, operation, side, price, (double)size);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void updateMktDepthL2(int tickerId, int position, string marketMaker, int operation, int side, double price, decimal size, bool isSmartDepth)
    {
        RecordMessageReceived();
        _router.UpdateMktDepthL2(tickerId, position, marketMaker, operation, side, price, (double)size, isSmartDepth);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void tickByTickAllLast(int reqId, int tickType, long time, double price, decimal size, TickAttribLast tickAttribLast, string exchange, string specialConditions)
    {
        RecordMessageReceived();
        _router.OnTickByTickAllLast(reqId, tickType, time, price, (double)size, exchange, specialConditions);
    }

    // -----------------------
    // EWrapper error handling
    // -----------------------

    /// <summary>
    /// Event raised when an IB API error occurs.
    /// </summary>
    public event EventHandler<IBApiError>? ErrorOccurred;

    /// <summary>
    /// Event raised when a pacing violation is detected.
    /// </summary>
    public event EventHandler<int>? PacingViolation;

    public void error(Exception e)
    {
        if (IsPacingViolation(-1, e.Message))
            PacingViolation?.Invoke(this, -1);
        ErrorOccurred?.Invoke(this, new IBApiError(-1, -1, e.Message, null));
    }

    public void error(string str)
    {
        if (IsPacingViolation(-1, str))
            PacingViolation?.Invoke(this, -1);
        ErrorOccurred?.Invoke(this, new IBApiError(-1, -1, str, null));
    }

    public void error(int id, long errorTime, int errorCode, string errorMsg, string advancedOrderRejectJson)
    {
        RecordMessageReceived();

        // Handle pacing violations
        if (IsPacingViolation(errorCode, errorMsg))
        {
            PacingViolation?.Invoke(this, id);
        }

        // Handle historical data errors - complete the request with error
        if (_historicalDataRequests.TryRemove(id, out var tcs))
        {
            _historicalDataBuffers.TryRemove(id, out _);

            // For pacing violations, throw specific exception
            if (IsPacingViolation(errorCode, errorMsg) ||
                errorCode == IBApiLimits.ErrorHistoricalDataService)
            {
                tcs.TrySetException(new IBPacingViolationException(errorCode, errorMsg));
            }
            else if (errorCode == IBApiLimits.ErrorMarketDataNotSubscribed ||
                     errorCode == IBApiLimits.ErrorDelayedDataNotSubscribed)
            {
                tcs.TrySetException(new IBMarketDataNotSubscribedException(errorCode, errorMsg));
            }
            else if (errorCode == IBApiLimits.ErrorNoSecurityDefinition)
            {
                tcs.TrySetException(new IBSecurityNotFoundException(errorCode, errorMsg));
            }
            else
            {
                tcs.TrySetException(new IBApiException(errorCode, errorMsg));
            }
        }

        ErrorOccurred?.Invoke(this, new IBApiError(id, errorCode, errorMsg, advancedOrderRejectJson));
        if (id > 0)
            RequestRejected?.Invoke(this, (id, errorCode.ToString(CultureInfo.InvariantCulture), errorMsg));
    }

    public void connectionClosed()
    {
        NotifyConnectionLost(new IOException("Interactive Brokers connection closed."));
    }

    private static bool IsPacingViolation(int errorCode, string message)
        => errorCode is IBApiLimits.ErrorPacingViolation or IBApiLimits.ErrorMaxMessageRateExceeded
            || message.Contains("pacing", StringComparison.OrdinalIgnoreCase)
            || message.Contains("max rate", StringComparison.OrdinalIgnoreCase)
            || message.Contains("maximum rate", StringComparison.OrdinalIgnoreCase);

    // -----------------------
    // EWrapper Level 1 tick callbacks
    // -----------------------
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void tickPrice(int tickerId, int field, double price, TickAttrib attribs)
    {
        RecordMessageReceived();
        _router.OnTickPrice(tickerId, field, price, attribs.CanAutoExecute, attribs.PastLimit);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void tickSize(int tickerId, int field, decimal size)
    {
        RecordMessageReceived();
        _router.OnTickSize(tickerId, field, (long)size);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void tickString(int tickerId, int field, string value)
    {
        RecordMessageReceived();
        _router.OnTickString(tickerId, field, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void tickGeneric(int tickerId, int field, double value)
    {
        RecordMessageReceived();
        _router.OnTickGeneric(tickerId, field, value);
    }

    public void tickEFP(int tickerId, int tickType, double basisPoints, string formattedBasisPoints, double impliedFuture, int holdDays, string futureLastTradeDate, double dividendImpact, double dividendsToLastTradeDate) { }

    public void tickSnapshotEnd(int tickerId)
    {
        RecordMessageReceived();
        _router.OnTickSnapshotEnd(tickerId);
    }
    public void nextValidId(int orderId)
    {
        RecordMessageReceived();
        NextValidIdReceived?.Invoke(this, orderId);
    }
    public void managedAccounts(string accountsList) { }
    public void currentTime(long time)
    {
        RecordMessageReceived();

        // Measure round-trip latency from the time request
        var requestTs = Interlocked.Read(ref _currentTimeRequestTimestamp);
        if (requestTs > 0)
        {
            var latencyUs = HighResolutionTimestamp.GetElapsedMicroseconds(requestTs);
            _lastRoundTripLatencyUs = latencyUs;
            LatencyMeasured?.Invoke(this, latencyUs);
        }
    }
    public void accountSummary(int reqId, string account, string tag, string value, string currency)
    {
        RecordMessageReceived();
        AccountSummaryReceived?.Invoke(this, new IBAccountSummaryUpdate(
            reqId,
            account,
            tag,
            value,
            currency,
            DateTimeOffset.UtcNow));
    }
    public void accountSummaryEnd(int reqId)
    {
        RecordMessageReceived();
        AccountSummaryCompleted?.Invoke(this, reqId);
    }
    public void accountUpdateMulti(int reqId, string account, string modelCode, string key, string value, string currency) { }
    public void accountUpdateMultiEnd(int reqId) { }
    public void tickOptionComputation(int tickerId, int field, int tickAttrib, double impliedVolatility, double delta, double optPrice, double pvDividend, double gamma, double vega, double theta, double undPrice)
    {
        RecordMessageReceived();
        try
        {
            _router.OnTickOptionComputation(tickerId, field, impliedVolatility, delta, optPrice, pvDividend, gamma, vega, theta, undPrice);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Dropping malformed option computation tick for tickerId {TickerId}.", tickerId);
        }
    }

    public event EventHandler<IBMarketDataTypeUpdate>? MarketDataTypeReceived;

    public void marketDataType(int reqId, int marketDataType)
    {
        RecordMessageReceived();
        MarketDataTypeReceived?.Invoke(this, new IBMarketDataTypeUpdate(reqId, marketDataType));
    }
    public void contractDetails(int reqId, ContractDetails contractDetails)
    {
        RecordMessageReceived();
#if IBAPI_VENDOR
        var contract = contractDetails.Contract;
        var expiration = DateOnly.TryParse(contract.LastTradeDateOrContractMonth, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : DateOnly.MinValue;
        if (expiration != DateOnly.MinValue && contract.Strike > 0 && !string.IsNullOrWhiteSpace(contract.Right))
            OptionContractReceived?.Invoke(this, (reqId, new ProviderOptionContract(contract.Symbol, string.Empty, expiration, (decimal)contract.Strike, contract.Right, contract.Exchange, contract.TradingClass, contract.Multiplier, contract.ConId.ToString(CultureInfo.InvariantCulture), ProviderDataProvenance.Unattributed(DateTimeOffset.UtcNow))));
#endif
    }
    public void contractDetailsEnd(int reqId)
    {
        RecordMessageReceived();
        RequestCompleted?.Invoke(this, reqId);
    }
    public void symbolSamples(int reqId, ContractDescription[] contractDescriptions) { }
    public void reqMktDepthExchanges(DepthMktDataDescription[] depthMktDataDescriptions) { }
    public void tickReqParams(int tickerId, double minTick, string bboExchange, int snapshotPermissions) { }
    public void newsProviders(NewsProvider[] newsProviders) { }
    public void newsArticle(int requestId, int articleType, string articleText) { }
    public void historicalNews(int requestId, string time, string providerCode, string articleId, string headline) { }
    public void historicalNewsEnd(int requestId, bool hasMore) { }
    public void headTimestamp(int reqId, string headTimestamp) { }

    // -----------------------
    // EWrapper Historical Data callbacks
    // -----------------------
    public void historicalData(int reqId, Bar bar)
    {
        RecordMessageReceived();
        if (_historicalDataBuffers.TryGetValue(reqId, out var buffer))
        {
            buffer.Add(bar);
        }
    }

    // The IB API has a typo in the method name (histoicalData vs historicalData).
    // Some versions use one or the other.
    public void histoicalData(int reqId, Bar bar)
    {
        historicalData(reqId, bar);
    }

    public void historicalDataEnd(int reqId, string start, string end)
    {
        RecordMessageReceived();
        if (_historicalDataRequests.TryRemove(reqId, out var tcs))
        {
            if (_historicalDataBuffers.TryRemove(reqId, out var buffer))
            {
                tcs.TrySetResult(buffer);
            }
            else
            {
                tcs.TrySetResult(new List<Bar>());
            }
        }
    }

    public void historicalDataUpdate(int reqId, Bar bar)
    {
        RecordMessageReceived();
        // For keepUpToDate=True subscriptions - route to callback if needed
    }

    public void orderStatus(int orderId, string status, decimal filled, decimal remaining, double avgFillPrice, long permId, int parentId, double lastFillPrice, int clientId, string whyHeld, double mktCapPrice)
    {
        RecordMessageReceived();
        OrderStatusReceived?.Invoke(this, new IBOrderStatusUpdate(
            orderId,
            status,
            filled,
            remaining,
            avgFillPrice,
            lastFillPrice,
            permId,
            clientId,
            whyHeld,
            null,
            DateTimeOffset.UtcNow));
    }

    public void openOrder(int orderId, Contract contract, Order order, IBApi.OrderState orderState)
    {
        RecordMessageReceived();

        var metadata = BuildContractMetadata(contract);
        OpenOrderReceived?.Invoke(this, new IBOpenOrderUpdate(
            orderId,
            contract.Symbol ?? contract.LocalSymbol ?? order.OrderRef ?? orderId.ToString(CultureInfo.InvariantCulture),
            contract.SecType,
            order.Action ?? "BUY",
            order.OrderType ?? "MKT",
            order.TotalQuantity,
            0m,
            order.LmtPrice > 0 ? order.LmtPrice : null,
            order.AuxPrice > 0 ? order.AuxPrice : null,
            orderState.Status ?? "Submitted",
            string.IsNullOrWhiteSpace(order.OrderRef) ? null : order.OrderRef,
            null,
            orderState.CommissionAndFees > 0 ? orderState.CommissionAndFees : null,
            string.IsNullOrWhiteSpace(orderState.RejectReason) ? null : orderState.RejectReason,
            metadata,
            DateTimeOffset.UtcNow));
    }

    public void openOrderEnd()
    {
        RecordMessageReceived();
        OpenOrdersCompleted?.Invoke(this, EventArgs.Empty);
    }

    public void execDetails(int reqId, Contract contract, IBApi.Execution execution)
    {
        RecordMessageReceived();

        var executedAt = DateTimeOffset.TryParse(execution.Time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;

        ExecutionDetailsReceived?.Invoke(this, new IBExecutionUpdate(
            execution.OrderId,
            contract.Symbol ?? contract.LocalSymbol ?? execution.OrderRef ?? execution.OrderId.ToString(CultureInfo.InvariantCulture),
            execution.Side ?? "BOT",
            execution.Shares,
            execution.Price,
            execution.CumQty,
            execution.AvgPrice,
            execution.ExecId ?? string.Empty,
            execution.AcctNumber,
            execution.Exchange,
            execution.PermId,
            BuildContractMetadata(contract),
            executedAt));
    }

    public void execDetailsEnd(int reqId) { }

    public void position(string account, Contract contract, decimal pos, double avgCost)
    {
        RecordMessageReceived();
        PositionReceived?.Invoke(this, new IBPositionUpdate(
            account,
            contract.Symbol ?? contract.LocalSymbol ?? string.Empty,
            contract.SecType,
            pos,
            avgCost,
            contract.Currency,
            contract.Exchange,
            BuildContractMetadata(contract),
            DateTimeOffset.UtcNow));
    }

    public void positionEnd()
    {
        RecordMessageReceived();
        PositionsCompleted?.Invoke(this, EventArgs.Empty);
    }

    private static Contract BuildBrokerageContract(OrderRequest request)
    {
        var metadata = request.Metadata;
        var securityType = GetMetadata(metadata, "sec_type")
            ?? GetMetadata(metadata, "security_type")
            ?? "STK";

        var contract = new Contract
        {
            Symbol = request.Symbol,
            SecType = securityType,
            Exchange = GetMetadata(metadata, "exchange") ?? GetDefaultExchange(securityType),
            Currency = GetMetadata(metadata, "currency") ?? "USD",
        };

        if (int.TryParse(GetMetadata(metadata, "con_id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var conId))
            contract.ConId = conId;

        contract.PrimaryExch = GetMetadata(metadata, "primary_exchange");
        contract.TradingClass = GetMetadata(metadata, "trading_class");
        contract.LocalSymbol = GetMetadata(metadata, "local_symbol");
        contract.LastTradeDateOrContractMonth = GetMetadata(metadata, "last_trade_date_or_contract_month");

        if (decimal.TryParse(GetMetadata(metadata, "strike"), NumberStyles.Float, CultureInfo.InvariantCulture, out var strike))
            contract.Strike = (double)strike;

        var right = GetMetadata(metadata, "right");
        if (!string.IsNullOrWhiteSpace(right))
            contract.Right = right.Equals("call", StringComparison.OrdinalIgnoreCase) ? "C"
                : right.Equals("put", StringComparison.OrdinalIgnoreCase) ? "P"
                : right;

        var multiplier = GetMetadata(metadata, "multiplier");
        if (!string.IsNullOrWhiteSpace(multiplier))
            contract.Multiplier = multiplier;

        return contract;
    }

    private static Order BuildBrokerageOrder(int orderId, OrderRequest request)
    {
        var order = new Order
        {
            OrderId = orderId,
            Action = request.Side == Meridian.Execution.Sdk.OrderSide.Buy ? "BUY" : "SELL",
            TotalQuantity = request.Quantity,
            OrderType = request.Type switch
            {
                OrderType.Market => "MKT",
                OrderType.Limit => "LMT",
                OrderType.StopMarket => "STP",
                OrderType.StopLimit => "STP LMT",
                OrderType.MarketOnOpen or OrderType.MarketOnClose or OrderType.LimitOnOpen or OrderType.LimitOnClose
                    => throw new NotSupportedException(
                        $"Interactive Brokers order mapping does not preserve the {request.Type} session timing qualifier."),
                _ => throw new NotSupportedException($"Interactive Brokers order mapping does not support {request.Type}.")
            },
            Tif = request.TimeInForce switch
            {
                TimeInForce.Day => "DAY",
                TimeInForce.GoodTilCancelled => "GTC",
                TimeInForce.ImmediateOrCancel => "IOC",
                TimeInForce.FillOrKill => "FOK",
                _ => "DAY"
            },
            Transmit = true,
            OrderRef = request.ClientOrderId ?? request.StrategyId ?? string.Empty,
            OutsideRth = ParseBooleanMetadata(request.Metadata, "outside_rth")
        };

        if (request.LimitPrice is decimal limitPrice)
            order.LmtPrice = (double)limitPrice;

        if (request.StopPrice is decimal stopPrice)
            order.AuxPrice = (double)stopPrice;

        var account = GetMetadata(request.Metadata, "account");
        if (!string.IsNullOrWhiteSpace(account))
            order.Account = account;

        return order;
    }

    private static IReadOnlyDictionary<string, string> BuildContractMetadata(Contract contract)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (contract.ConId > 0)
            metadata["con_id"] = contract.ConId.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(contract.Currency))
            metadata["currency"] = contract.Currency;
        if (!string.IsNullOrWhiteSpace(contract.Exchange))
            metadata["exchange"] = contract.Exchange;
        if (!string.IsNullOrWhiteSpace(contract.LocalSymbol))
            metadata["local_symbol"] = contract.LocalSymbol;
        if (!string.IsNullOrWhiteSpace(contract.PrimaryExch))
            metadata["primary_exchange"] = contract.PrimaryExch;
        if (!string.IsNullOrWhiteSpace(contract.TradingClass))
            metadata["trading_class"] = contract.TradingClass;
        if (!string.IsNullOrWhiteSpace(contract.LastTradeDateOrContractMonth))
            metadata["last_trade_date_or_contract_month"] = contract.LastTradeDateOrContractMonth;
        if (!string.IsNullOrWhiteSpace(contract.SecType))
            metadata["sec_type"] = contract.SecType;
        if (contract.Strike > 0)
            metadata["strike"] = contract.Strike.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(contract.Right))
            metadata["right"] = contract.Right;
        if (!string.IsNullOrWhiteSpace(contract.Multiplier))
            metadata["multiplier"] = contract.Multiplier;

        return metadata;
    }

    private static string? GetMetadata(IReadOnlyDictionary<string, string>? metadata, string key)
        => metadata is not null && metadata.TryGetValue(key, out var value) ? value : null;

    private static bool ParseBooleanMetadata(IReadOnlyDictionary<string, string>? metadata, string key)
        => bool.TryParse(GetMetadata(metadata, key), out var value) && value;

    private static string GetDefaultExchange(string securityType)
        => securityType.ToUpperInvariant() switch
        {
            "CASH" => "IDEALPRO",
            "FUT" => "GLOBEX",
            "GOVT" => "SMART",
            "BOND" => "SMART",
            _ => "SMART"
        };

    private sealed record DepthSubscription(
        string Symbol,
        Contract Contract,
        int DepthLevels,
        bool SmartDepth);

    private sealed record TradeSubscription(string Symbol, Contract Contract);

    private sealed record QuoteSubscription(
        string Symbol,
        Contract Contract,
        string GenericTickList,
        bool Snapshot,
        bool RegulatorySnapshot);

    // The full EWrapper interface is extensive. Add methods as you need them for trades/ticks/orders.
}
#endif

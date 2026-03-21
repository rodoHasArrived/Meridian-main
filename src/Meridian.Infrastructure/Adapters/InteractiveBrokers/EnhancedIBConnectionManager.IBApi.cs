#if IBAPI
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using IBApi;
using Meridian.Application.Config;
using Meridian.Infrastructure.Performance;

namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

public sealed partial class EnhancedIBConnectionManager : EWrapper, IDisposable
{
    private readonly IBCallbackRouter _router;

    private readonly EReaderSignal _signal;
    private readonly EClientSocket _clientSocket;
    private EReader? _reader;

    private CancellationTokenSource _cts = new();
    private Task? _readerLoop;
    private Task? _reconnectTask;

    private int _nextDepthTickerId = 10_000;
    private readonly ConcurrentDictionary<int, string> _depthTickerMap = new();

    private int _nextTradeTickerId = 20_000;
    private readonly ConcurrentDictionary<int, string> _tradeTickerMap = new();

    private int _nextQuoteTickerId = 30_000;
    private readonly ConcurrentDictionary<int, string> _quoteTickerMap = new();

    private int _nextHistoricalReqId = 40_000;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<List<IBApi.Bar>>> _historicalDataRequests = new();
    private readonly ConcurrentDictionary<int, List<IBApi.Bar>> _historicalDataBuffers = new();

    // Performance monitoring
    private readonly ConnectionWarmUp _warmUp;
    private readonly ExponentialBackoffRetry _reconnectBackoff;
    private HeartbeatMonitor? _heartbeatMonitor;

    // Connection state
    private volatile bool _isReconnecting;
    private volatile bool _disposed;
    private long _lastMessageTimestamp;
    private long _connectionEstablishedTimestamp;
    private long _totalMessagesReceived;
    private long _reconnectAttempts;

    // Latency tracking
    private long _currentTimeRequestTimestamp;
    private volatile double _lastRoundTripLatencyUs;

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

        _reconnectBackoff = new ExponentialBackoffRetry(
            initialDelay: TimeSpan.FromSeconds(1),
            maxDelay: TimeSpan.FromMinutes(2),
            maxRetries: -1, // Unlimited retries
            multiplier: 2.0,
            jitterFactor: 0.1);
    }

    public string Host { get; }
    public int Port { get; }
    public int ClientId { get; }
    public bool EnableAutoReconnect { get; set; }
    public bool EnableHeartbeat { get; set; }

    public bool IsConnected => _clientSocket.IsConnected();
    public bool IsReconnecting => _isReconnecting;
    public long TotalMessagesReceived => Interlocked.Read(ref _totalMessagesReceived);
    public long ReconnectAttempts => Interlocked.Read(ref _reconnectAttempts);
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
    /// Event raised on latency measurement from heartbeat/time request.
    /// </summary>
    public event EventHandler<double>? LatencyMeasured;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected) return;
        if (_disposed) throw new ObjectDisposedException(nameof(EnhancedIBConnectionManager));

        await ConnectInternalAsync(ct).ConfigureAwait(false);
    }

    private async Task ConnectInternalAsync(CancellationToken ct)
    {
        _clientSocket.eConnect(Host, Port, ClientId);

        // Wait briefly for connection establishment
        await Task.Delay(100, ct).ConfigureAwait(false);

        if (!IsConnected)
        {
            throw new InvalidOperationException($"Failed to connect to IB Gateway at {Host}:{Port}");
        }

        _reader = new EReader(_clientSocket, _signal);
        _reader.Start();

        // Start reader loop with high priority
        _cts = new CancellationTokenSource();
        _readerLoop = Task.Factory.StartNew(
            () => ReaderLoop(_cts.Token),
            _cts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        Interlocked.Exchange(ref _connectionEstablishedTimestamp, Stopwatch.GetTimestamp());
        _reconnectBackoff.Reset();

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
        if (EnableAutoReconnect && !_isReconnecting)
        {
            _ = TriggerReconnectAsync();
        }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        try
        {
            _heartbeatMonitor?.Dispose();
            _heartbeatMonitor = null;

            _cts.Cancel();
            if (_readerLoop is not null)
            {
                try
                {
                    await _readerLoop.ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            _log.Debug(ex, "Error during Interactive Brokers disconnect cleanup");
        }

        if (IsConnected)
            _clientSocket.eDisconnect();

        Interlocked.Exchange(ref _connectionEstablishedTimestamp, 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _heartbeatMonitor?.Dispose();
        _cts.Cancel();

        if (IsConnected)
            _clientSocket.eDisconnect();

        _cts.Dispose();
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
                // Connection may have been lost
                if (EnableAutoReconnect && !_isReconnecting)
                {
                    _ = TriggerReconnectAsync();
                }
                break;
            }
        }
    }

    private async Task TriggerReconnectAsync(CancellationToken ct = default)
    {
        if (_isReconnecting || _disposed) return;
        _isReconnecting = true;

        ConnectionLost?.Invoke(this, EventArgs.Empty);

        try
        {
            // Stop existing reader
            _cts.Cancel();
            _heartbeatMonitor?.Dispose();
            _heartbeatMonitor = null;

            if (_readerLoop is not null)
            {
                try { await _readerLoop.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
                {
                    _log.Debug(ex, "Reader loop cleanup failed");
                }
            }

            if (IsConnected)
                _clientSocket.eDisconnect();

            // Attempt reconnection with exponential backoff
            while (!_disposed && _reconnectBackoff.CanRetry)
            {
                Interlocked.Increment(ref _reconnectAttempts);

                await _reconnectBackoff.WaitAsync().ConfigureAwait(false);

                try
                {
                    // Dispose the previous CTS before creating a new one to prevent leaks
                    _cts.Dispose();
                    _cts = new CancellationTokenSource();
                    await ConnectInternalAsync(_cts.Token).ConfigureAwait(false);

                    if (IsConnected)
                    {
                        _reconnectBackoff.Reset();
                        ConnectionRestored?.Invoke(this, EventArgs.Empty);
                        break;
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _log.Debug(ex, "Reconnection attempt {Attempt} failed", Interlocked.Read(ref _reconnectAttempts));
                }
            }
        }
        finally
        {
            _isReconnecting = false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RecordMessageReceived()
    {
        Interlocked.Increment(ref _totalMessagesReceived);
        Interlocked.Exchange(ref _lastMessageTimestamp, Stopwatch.GetTimestamp());
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

        // Router needs this mapping for callbacks
        _router.RegisterDepthTicker(id, symbol);

        _clientSocket.reqMktDepth(id, contract, depthLevels, smartDepth, null);
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
        _clientSocket.cancelMktDepth(tickerId);
        _depthTickerMap.TryRemove(tickerId, out _);
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
        _router.RegisterTradeTicker(id, cfg.Symbol);

        // tickType can be "AllLast" (prints + special conditions) or "Last".
        _clientSocket.reqTickByTickData(id, contract, "AllLast", 0, ignoreSize: false);
        return id;
    }

    public void UnsubscribeTrades(int tickerId)
    {
        _clientSocket.cancelTickByTickData(tickerId);
        _tradeTickerMap.TryRemove(tickerId, out _);
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

        var id = Interlocked.Increment(ref _nextQuoteTickerId);
        _quoteTickerMap[id] = cfg.Symbol;
        _router.RegisterQuoteTicker(id, cfg.Symbol);

        // Default to RT Volume and Shortable ticks for equities
        var ticks = genericTickList ?? IBGenericTickTypes.DefaultEquityGenericTicks;

        _clientSocket.reqMktData(id, contract, ticks, snapshot, regulatorySnapshot, null);
        return id;
    }

    /// <summary>
    /// Unsubscribe from Level 1 market data.
    /// </summary>
    public void UnsubscribeQuotes(int tickerId)
    {
        _clientSocket.cancelMktData(tickerId);
        _quoteTickerMap.TryRemove(tickerId, out _);
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
        ErrorOccurred?.Invoke(this, new IBApiError(-1, -1, e.Message, null));
    }

    public void error(string str)
    {
        ErrorOccurred?.Invoke(this, new IBApiError(-1, -1, str, null));
    }

    public void error(int id, int errorCode, string errorMsg, string advancedOrderRejectJson)
    {
        RecordMessageReceived();

        // Handle pacing violations
        if (errorCode == IBApiLimits.ErrorPacingViolation ||
            errorMsg.Contains("pacing", StringComparison.OrdinalIgnoreCase))
        {
            PacingViolation?.Invoke(this, id);
        }

        // Handle historical data errors - complete the request with error
        if (_historicalDataRequests.TryRemove(id, out var tcs))
        {
            _historicalDataBuffers.TryRemove(id, out _);

            // For pacing violations, throw specific exception
            if (errorCode == IBApiLimits.ErrorPacingViolation ||
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
    }

    public void connectionClosed()
    {
        ConnectionLost?.Invoke(this, EventArgs.Empty);
    }

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
    public void nextValidId(int orderId) { }
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
    public void accountSummary(int reqId, string account, string tag, string value, string currency) { }
    public void accountSummaryEnd(int reqId) { }
    public void accountUpdateMulti(int reqId, string account, string modelCode, string key, string value, string currency) { }
    public void accountUpdateMultiEnd(int reqId) { }
    public void marketDataType(int reqId, int marketDataType) { }
    public void contractDetails(int reqId, ContractDetails contractDetails) { }
    public void contractDetailsEnd(int reqId) { }
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

    // The full EWrapper interface is extensive. Add methods as you need them for trades/ticks/orders.
}
#endif

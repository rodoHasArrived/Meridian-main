using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Meridian.Core.Config;
using Meridian.Core.Logging;
using Meridian.Core.Monitoring;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Meridian.Infrastructure.Resilience;
using Meridian.Infrastructure.Shared;
using Meridian.Infrastructure.Utilities;
using Meridian.ProviderSdk;
using Serilog;
using DataSourceType = Meridian.Infrastructure.DataSources.DataSourceType;

namespace Meridian.Infrastructure.Adapters.NYSE;

/// <summary>
/// NYSE Direct Connection data source providing real-time and historical market data
/// directly from the New York Stock Exchange.
///
/// Features:
/// - Real-time trades and quotes via NYSE Integrated Feed
/// - Historical daily OHLCV bars via NYSE Historical Data API
/// - Level 2 market depth (Premium/Professional tiers)
/// - Trade conditions and participant IDs
/// - Pre-market and after-hours data
/// - Corporate actions (dividends, splits)
///
/// Requires NYSE Connect API credentials and appropriate data subscriptions.
/// </summary>
[DataSource(
    id: "nyse",
    displayName: "NYSE Direct",
    type: DataSourceType.Hybrid,
    category: DataSourceCategory.Exchange,
    Priority = 5,
    Description = "Direct connection to NYSE for real-time and historical US equity data")]
[ImplementsAdr("ADR-001", "NYSE streaming and historical data provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
public sealed class NYSEDataSource : DataSourceBase, IRealtimeDataSource, IHistoricalDataSource
{

    private readonly NYSEOptions _options;

    // Shared OAuth token acquisition/caching and authenticated REST client creation.
    private readonly NyseAccessTokenProvider _auth;

    // Historical / corporate-action fetch + parse logic (wrapped here by the resilience policies).
    private readonly NyseHistoricalDataProvider _historical;

    // WebSocket lifecycle managed by WebSocketConnectionManager (replaces _webSocket, _connectionCts, _receiveTask)
    private readonly WebSocketConnectionManager _wsManager;
    private readonly ProviderRateLimitTracker _streamingRateLimits;

    private readonly Subject<RealtimeTrade> _trades = new();
    private readonly Subject<RealtimeQuote> _quotes = new();
    private readonly Subject<RealtimeDepthUpdate> _depthUpdates = new();

    private readonly ConcurrentDictionary<int, SubscriptionInfo> _subscriptions = new();
    private readonly ConcurrentDictionary<string, int> _symbolToSubId = new();
    private int _nextSubscriptionId = 1;

    private static readonly HashSet<string> SupportedMarketsSet = new(StringComparer.OrdinalIgnoreCase) { "US" };
    private static readonly HashSet<AssetClass> SupportedAssetClassesSet = new()
    {
        AssetClass.Equity,
        AssetClass.ETF,
        AssetClass.Index
    };

    private static readonly string[] BarIntervalsArray = { "1Min", "5Min", "15Min", "30Min", "1Hour", "1Day" };



    public override string Id => "nyse";
    public override string DisplayName => "NYSE Direct";
    public override string Description => "Direct connection to NYSE for real-time and historical US equity data";



    public override DataSourceType Type => DataSourceType.Hybrid;
    public override DataSourceCategory Category => DataSourceCategory.Exchange;



    public override DataSourceCapabilities Capabilities =>
        DataSourceCapabilities.RealtimeTrades |
        DataSourceCapabilities.RealtimeQuotes |
        DataSourceCapabilities.RealtimeDepthL1 |
        (_options.EnableLevel2 ? DataSourceCapabilities.RealtimeDepthL2 : 0) |
        DataSourceCapabilities.HistoricalDailyBars |
        DataSourceCapabilities.HistoricalIntradayBars |
        DataSourceCapabilities.HistoricalAdjustedPrices |
        DataSourceCapabilities.HistoricalDividends |
        DataSourceCapabilities.HistoricalSplits |
        DataSourceCapabilities.SupportsBackfill |
        DataSourceCapabilities.SupportsStreaming |
        DataSourceCapabilities.SupportsWebSocket |
        DataSourceCapabilities.SupportsBatchRequests |
        DataSourceCapabilities.SupportsSymbolSearch |
        DataSourceCapabilities.SupportsMultiSubscription |
        DataSourceCapabilities.ExchangeTimestamps |
        DataSourceCapabilities.SequenceNumbers |
        DataSourceCapabilities.TradeConditions |
        DataSourceCapabilities.ParticipantIds |
        DataSourceCapabilities.ConsolidatedTape;

    public override DataSourceCapabilityInfo CapabilityInfo => new(
        Capabilities,
        MinHistoricalDate: new DateOnly(1990, 1, 1),
        MaxHistoricalLookback: TimeSpan.FromDays(365 * 35),
        MaxSymbolsPerSubscription: _options.MaxSubscriptions,
        MaxDepthLevels: _options.FeedTier >= NYSEFeedTier.Premium ? 10 : 1,
        MinBarResolution: TimeSpan.FromMinutes(1),
        SupportedBarIntervals: BarIntervalsArray,
        MaxRequestsPerMinute: 100,
        MaxRequestsPerHour: 5000,
        MaxRequestsPerDay: 50000
    );

    public override IReadOnlySet<string> SupportedMarkets => SupportedMarketsSet;
    public override IReadOnlySet<AssetClass> SupportedAssetClasses => SupportedAssetClassesSet;



    public NYSEDataSource(
        NYSEOptions options,
        IHttpClientFactory httpClientFactory,
        DataSourceOptions? sourceOptions = null,
        ILogger? logger = null)
        : base(sourceOptions ?? DataSourceOptions.Default, logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentNullException.ThrowIfNull(httpClientFactory);

        _auth = new NyseAccessTokenProvider(
            _options,
            httpClientFactory,
            logger ?? LoggingSetup.ForContext<NYSEDataSource>());

        _historical = new NyseHistoricalDataProvider(
            _auth,
            sourceId: "nyse",
            logger ?? LoggingSetup.ForContext<NYSEDataSource>());

        _wsManager = new WebSocketConnectionManager(
            providerName: "NYSE",
            config: WebSocketConnectionConfig.Resilient with
            {
                MaxReconnectAttempts = _options.MaxReconnectAttempts,
                RetryBaseDelay = TimeSpan.FromSeconds(_options.ReconnectDelaySeconds),
                MaxRetryDelay = TimeSpan.FromSeconds(60)
            },
            logger: logger ?? LoggingSetup.ForContext<NYSEDataSource>());

        _wsManager.ConnectionLost += OnWsConnectionLostAsync;

        _streamingRateLimits = new ProviderRateLimitTracker(
            logger ?? LoggingSetup.ForContext<NYSEDataSource>());
        _streamingRateLimits.RegisterProvider(
            Id,
            maxRequestsPerWindow: 100,
            window: TimeSpan.FromMinutes(1),
            minDelay: TimeSpan.Zero);
    }



    public override async Task<bool> ValidateCredentialsAsync(CancellationToken ct = default)
    {
        var apiKey = _options.ResolveApiKey();
        var apiSecret = _options.ResolveApiSecret();

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
        {
            Log.Warning("NYSE credentials not configured. Set NYSE_API_KEY and NYSE_API_SECRET environment variables.");
            return false;
        }

        try
        {
            // Try to obtain an access token to validate credentials
            await _auth.EnsureAuthenticatedAsync(ct).ConfigureAwait(false);
            return !string.IsNullOrEmpty(_auth.AccessToken);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "NYSE credential validation failed");
            return false;
        }
    }

    public override async Task<bool> TestConnectivityAsync(CancellationToken ct = default)
    {
        try
        {
            await _auth.EnsureAuthenticatedAsync(ct).ConfigureAwait(false);

            // Test REST API connectivity
            using var request = new HttpRequestMessage(HttpMethod.Get, "/markets/status");
            _auth.AddAuthHeader(request);

            using var httpClient = _auth.CreateHttpClient();
            using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "NYSE connectivity test failed");
            return false;
        }
    }

    protected override async ValueTask OnDisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);

        _wsManager.ConnectionLost -= OnWsConnectionLostAsync;
        await _wsManager.DisposeAsync().ConfigureAwait(false);

        _trades.OnCompleted();
        _trades.Dispose();
        _quotes.OnCompleted();
        _quotes.Dispose();
        _depthUpdates.OnCompleted();
        _depthUpdates.Dispose();

        _auth.Dispose();
        _streamingRateLimits.Dispose();
    }



    public bool IsConnected => _wsManager.IsConnected;

    /// <summary>
    /// Raised when the underlying WebSocket connection diagnostics change.
    /// </summary>
    public event Action<WebSocketConnectionDiagnostics>? ConnectionDiagnosticsChanged
    {
        add => _wsManager.DiagnosticsChanged += value;
        remove => _wsManager.DiagnosticsChanged -= value;
    }

    /// <summary>
    /// Returns the live connection diagnostics snapshot from the shared connection manager.
    /// </summary>
    public WebSocketConnectionDiagnostics GetConnectionDiagnosticsSnapshot()
        => _wsManager.GetDiagnosticsSnapshot();

    internal ProviderRateLimitDiagnosticSnapshot GetStreamingRateLimitDiagnosticsSnapshot()
    {
        var status = _streamingRateLimits.GetStatus(Id)
            ?? throw new InvalidOperationException("NYSE streaming rate-limit tracking is not initialized.");

        return new ProviderRateLimitDiagnosticSnapshot(
            ProviderId: Id,
            Surface: ProviderRateLimitSurfaces.Streaming,
            ObservedAt: status.ObservedAt,
            RequestsInWindow: status.RequestsInWindow,
            MaxRequestsPerWindow: status.MaxRequestsPerWindow,
            Window: status.Window,
            IsRateLimited: status.IsRateLimited,
            ResetAt: status.ResetAt,
            UsageRatio: status.UsageRatio,
            Reason: status.Reason);
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected)
        {
            Log.Debug("NYSE WebSocket already connected");
            return;
        }

        Log.Information("Connecting to NYSE WebSocket at {Url}", _options.EffectiveWebSocketUrl);

        try
        {
            await _wsManager.ConnectAsync(
                new Uri(_options.EffectiveWebSocketUrl),
                ws => ws.Options.SetRequestHeader("Authorization", $"Bearer {_auth.AccessToken}"),
                prepareConnection: _auth.EnsureAuthenticatedAsync,
                initializeConnection: async token =>
                {
                    _wsManager.StartReceiveLoop(msg =>
                    {
                        ProcessWebSocketMessage(msg);
                        return Task.CompletedTask;
                    });
                    await ResubscribeAllAsync(token, allowInitializingConnection: true).ConfigureAwait(false);
                },
                ct: ct).ConfigureAwait(false);

            Status = DataSourceStatus.Connected;
            Log.Information("Connected to NYSE WebSocket");
        }
        catch (Exception ex)
        {
            Status = DataSourceStatus.Disconnected;
            Log.Error(ex, "Failed to connect to NYSE WebSocket");
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        Log.Information("Disconnecting from NYSE WebSocket");

        try
        {
            await _wsManager.DisconnectAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error during NYSE WebSocket disconnect");
        }
        finally
        {
            Status = DataSourceStatus.Disconnected;
        }
    }



    public IObservable<RealtimeTrade> Trades => _trades.AsObservable();

    public int SubscribeTrades(SymbolConfig config)
    {
        var subId = GetOrCreateSubscription(config, SubscriptionType.Trades);

        if (IsConnected)
        {
            SendSubscriptionMessageAsync(config.Symbol, "trades", "subscribe")
                .ObserveException(Log, $"NYSE subscribe trades for {config.Symbol}");
        }

        return subId;
    }

    public void UnsubscribeTrades(int subscriptionId)
    {
        if (_subscriptions.TryRemove(subscriptionId, out var info))
        {
            _symbolToSubId.TryRemove(info.Symbol + "_trades", out _);

            if (IsConnected)
            {
                SendSubscriptionMessageAsync(info.Symbol, "trades", "unsubscribe")
                    .ObserveException(Log, $"NYSE unsubscribe trades for {info.Symbol}");
            }
        }
    }



    public IObservable<RealtimeQuote> Quotes => _quotes.AsObservable();

    public int SubscribeQuotes(SymbolConfig config)
    {
        var subId = GetOrCreateSubscription(config, SubscriptionType.Quotes);

        if (IsConnected)
        {
            SendSubscriptionMessageAsync(config.Symbol, "quotes", "subscribe")
                .ObserveException(Log, $"NYSE subscribe quotes for {config.Symbol}");
        }

        return subId;
    }

    public void UnsubscribeQuotes(int subscriptionId)
    {
        if (_subscriptions.TryRemove(subscriptionId, out var info))
        {
            _symbolToSubId.TryRemove(info.Symbol + "_quotes", out _);

            if (IsConnected)
            {
                SendSubscriptionMessageAsync(info.Symbol, "quotes", "unsubscribe")
                    .ObserveException(Log, $"NYSE unsubscribe quotes for {info.Symbol}");
            }
        }
    }



    public IObservable<RealtimeDepthUpdate> DepthUpdates => _depthUpdates.AsObservable();

    public int SubscribeMarketDepth(SymbolConfig config)
    {
        if (_options.FeedTier < NYSEFeedTier.Premium)
        {
            Log.Warning("NYSE Level 2 depth requires Premium or Professional feed tier");
        }

        var subId = GetOrCreateSubscription(config, SubscriptionType.Depth);

        if (IsConnected)
        {
            SendSubscriptionMessageAsync(config.Symbol, "depth", "subscribe")
                .ObserveException(Log, $"NYSE subscribe depth for {config.Symbol}");
        }

        return subId;
    }

    public void UnsubscribeMarketDepth(int subscriptionId)
    {
        if (_subscriptions.TryRemove(subscriptionId, out var info))
        {
            _symbolToSubId.TryRemove(info.Symbol + "_depth", out _);

            if (IsConnected)
            {
                SendSubscriptionMessageAsync(info.Symbol, "depth", "unsubscribe")
                    .ObserveException(Log, $"NYSE unsubscribe depth for {info.Symbol}");
            }
        }
    }



    public IReadOnlySet<int> ActiveSubscriptions =>
        new HashSet<int>(_subscriptions.Keys);

    public IReadOnlySet<string> SubscribedSymbols =>
        new HashSet<string>(_subscriptions.Values.Select(s => s.Symbol));

    public void UnsubscribeAll()
    {
        var allSubs = _subscriptions.Keys.ToList();
        foreach (var subId in allSubs)
        {
            if (_subscriptions.TryRemove(subId, out var info))
            {
                _symbolToSubId.TryRemove($"{info.Symbol}_{info.Type.ToString().ToLowerInvariant()}", out _);
            }
        }

        if (IsConnected)
        {
            SendUnsubscribeAllMessageAsync()
                .ObserveException(Log, "NYSE unsubscribe all");
        }
    }



    public bool SupportsIntraday => true;
    public IReadOnlyList<string> SupportedBarIntervals => BarIntervalsArray;
    public bool SupportsDividends => true;
    public bool SupportsSplits => true;

    public async Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
        string symbol,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken ct = default)
    {
        var adjusted = await GetAdjustedDailyBarsAsync(symbol, from, to, ct).ConfigureAwait(false);
        return adjusted.Select(b => b.ToHistoricalBar(preferAdjusted: false)).ToList();
    }

    public Task<IReadOnlyList<AdjustedHistoricalBar>> GetAdjustedDailyBarsAsync(
        string symbol,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken ct = default)
        => ExecuteWithPoliciesAsync(
            token => _historical.FetchAdjustedDailyBarsAsync(symbol, from, to, token),
            "GetAdjustedDailyBars",
            ct);

    public Task<IReadOnlyList<IntradayBar>> GetIntradayBarsAsync(
        string symbol,
        string interval,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
        => ExecuteWithPoliciesAsync(
            token => _historical.FetchIntradayBarsAsync(symbol, interval, from, to, token),
            "GetIntradayBars",
            ct);

    public Task<IReadOnlyList<DividendInfo>> GetDividendsAsync(
        string symbol,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken ct = default)
        => ExecuteWithPoliciesAsync(
            token => _historical.FetchDividendsAsync(symbol, from, to, token),
            "GetDividends",
            ct);

    public Task<IReadOnlyList<SplitInfo>> GetSplitsAsync(
        string symbol,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken ct = default)
        => ExecuteWithPoliciesAsync(
            token => _historical.FetchSplitsAsync(symbol, from, to, token),
            "GetSplits",
            ct);



    private async Task OnWsConnectionLostAsync()
    {
        Status = DataSourceStatus.Disconnected;
        MigrationDiagnostics.IncReconnectAttempt(Id);

        var success = await _wsManager.TryReconnectAsync(
            new Uri(_options.EffectiveWebSocketUrl),
            ws => ws.Options.SetRequestHeader("Authorization", $"Bearer {_auth.AccessToken}"),
            prepareConnection: _auth.EnsureAuthenticatedAsync,
            initializeConnection: async token =>
            {
                _wsManager.StartReceiveLoop(msg =>
                {
                    ProcessWebSocketMessage(msg);
                    return Task.CompletedTask;
                });
                await ResubscribeAllAsync(token, allowInitializingConnection: true).ConfigureAwait(false);
            },
            ct: CancellationToken.None).ConfigureAwait(false);

        if (success)
        {
            Status = DataSourceStatus.Connected;
            MigrationDiagnostics.IncReconnectSuccess(Id);
            return;
        }

        Status = DataSourceStatus.Unavailable;
        MigrationDiagnostics.IncReconnectFailure(Id);
        Log.Error(
            "NYSE failed to complete its connection transaction after {Max} attempts",
            _options.MaxReconnectAttempts);
    }

    /// <summary>
    /// Test entry point: injects a raw WebSocket JSON payload directly into the processing pipeline,
    /// bypassing the live WebSocket connection. Used by <c>NyseMessagePipelineTests</c>.
    /// </summary>
    public void ProcessTestMessage(string json) => ProcessWebSocketMessage(json);

    private void ProcessWebSocketMessage(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            var msgType = root.GetProperty("type").GetString();

            switch (msgType)
            {
                case "trade":
                    ProcessTradeMessage(root);
                    break;
                case "quote":
                    ProcessQuoteMessage(root);
                    break;
                case "depth":
                    ProcessDepthMessage(root);
                    break;
                case "heartbeat":
                    Log.Verbose("NYSE heartbeat received");
                    break;
                case "error":
                    var errorMsg = root.GetProperty("message").GetString();
                    if (IsRateLimitError(root, errorMsg))
                    {
                        _streamingRateLimits.RecordRateLimitHit(
                            Id,
                            TryGetRetryAfter(root));
                    }
                    Log.Error("NYSE WebSocket error: {Error}", errorMsg);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to process NYSE WebSocket message: {Message}", message);
        }
    }

    private void ProcessTradeMessage(JsonElement root)
    {
        var trade = new RealtimeTrade(
            Symbol: root.GetProperty("symbol").GetString() ?? "",
            Price: root.GetProperty("price").GetDecimal(),
            Size: root.GetProperty("size").GetInt64(),
            Timestamp: DateTimeOffset.Parse(root.GetProperty("timestamp").GetString() ?? ""),
            SourceId: Id,
            Exchange: root.TryGetProperty("exchange", out var ex) ? ex.GetString() : "NYSE",
            Conditions: root.TryGetProperty("conditions", out var cond) ? cond.GetString() : null,
            SequenceNumber: root.TryGetProperty("sequence", out var seq) ? seq.GetInt64() : null,
            Side: ParseAggressorSide(root.TryGetProperty("side", out var side) ? side.GetString() : null)
        );

        _trades.OnNext(trade);
    }

    private void ProcessQuoteMessage(JsonElement root)
    {
        var quote = new RealtimeQuote(
            Symbol: root.GetProperty("symbol").GetString() ?? "",
            BidPrice: root.GetProperty("bidPrice").GetDecimal(),
            BidSize: root.GetProperty("bidSize").GetInt64(),
            AskPrice: root.GetProperty("askPrice").GetDecimal(),
            AskSize: root.GetProperty("askSize").GetInt64(),
            Timestamp: DateTimeOffset.Parse(root.GetProperty("timestamp").GetString() ?? ""),
            SourceId: Id,
            BidExchange: root.TryGetProperty("bidExchange", out var bidEx) ? bidEx.GetString() : "NYSE",
            AskExchange: root.TryGetProperty("askExchange", out var askEx) ? askEx.GetString() : "NYSE",
            SequenceNumber: root.TryGetProperty("sequence", out var seq) ? seq.GetInt64() : null
        );

        _quotes.OnNext(quote);
    }

    private void ProcessDepthMessage(JsonElement root)
    {
        var update = new RealtimeDepthUpdate(
            Symbol: root.GetProperty("symbol").GetString() ?? "",
            Operation: ParseDepthOperation(root.GetProperty("operation").GetString()),
            Side: root.GetProperty("side").GetString()?.ToLowerInvariant() == "bid"
                ? OrderBookSide.Bid : OrderBookSide.Ask,
            Level: root.GetProperty("level").GetInt32(),
            Price: root.GetProperty("price").GetDecimal(),
            Size: root.GetProperty("size").GetInt64(),
            Timestamp: DateTimeOffset.Parse(root.GetProperty("timestamp").GetString() ?? ""),
            SourceId: Id,
            MarketMaker: root.TryGetProperty("marketMaker", out var mm) ? mm.GetString() : null,
            SequenceNumber: root.TryGetProperty("sequence", out var seq) ? seq.GetInt64() : null
        );

        _depthUpdates.OnNext(update);
    }

    private async Task SendSubscriptionMessageAsync(
        string symbol,
        string channel,
        string action,
        CancellationToken ct = default,
        bool allowInitializingConnection = false)
    {
        try
        {
            if (!(allowInitializingConnection ? _wsManager.IsTransportConnected : IsConnected))
                return;

            var message = JsonSerializer.Serialize(new
            {
                action,
                channel,
                symbol
            });

            await _wsManager.SendAsync(message, ct).ConfigureAwait(false);
            _streamingRateLimits.RecordRequest(Id);

            Log.Debug("NYSE {Action} {Channel} for {Symbol}", action, channel, symbol);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to send NYSE {Action} {Channel} for {Symbol}. " +
                "Subscription state may be inconsistent.", action, channel, symbol);

            if (allowInitializingConnection)
                throw;
        }
    }

    private async Task SendUnsubscribeAllMessageAsync(CancellationToken ct = default)
    {
        try
        {
            if (!IsConnected)
                return;

            var message = JsonSerializer.Serialize(new { action = "unsubscribe_all" });
            await _wsManager.SendAsync(message, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to send NYSE unsubscribe_all message");
        }
    }

    private async Task ResubscribeAllAsync(
        CancellationToken ct,
        bool allowInitializingConnection = false)
        => await ReplaySubscriptionIntentsAsync(
            (symbol, channel, token) => SendSubscriptionMessageAsync(
                symbol,
                channel,
                "subscribe",
                token,
                allowInitializingConnection),
            ct).ConfigureAwait(false);

    /// <summary>
    /// Replays the adapter's durable subscription intent. The injected sender keeps the
    /// replay transaction directly testable without opening a provider socket.
    /// </summary>
    internal async Task ReplaySubscriptionIntentsAsync(
        Func<string, string, CancellationToken, Task> sendAsync,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sendAsync);

        foreach (var (_, info) in _subscriptions)
        {
            ct.ThrowIfCancellationRequested();
            var channel = info.Type.ToString().ToLowerInvariant();
            await sendAsync(
                info.Symbol,
                channel,
                ct).ConfigureAwait(false);
        }
    }

    private static bool IsRateLimitError(JsonElement root, string? message)
    {
        if (root.TryGetProperty("code", out var code))
        {
            if (code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var numericCode) && numericCode == 429)
                return true;
            if (code.ValueKind == JsonValueKind.String &&
                string.Equals(code.GetString(), "429", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return message?.Contains("429", StringComparison.OrdinalIgnoreCase) == true
            || message?.Contains("rate limit", StringComparison.OrdinalIgnoreCase) == true
            || message?.Contains("too many requests", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static TimeSpan? TryGetRetryAfter(JsonElement root)
    {
        foreach (var propertyName in new[] { "retryAfterSeconds", "retry_after", "retryAfter" })
        {
            if (!root.TryGetProperty(propertyName, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var seconds) && seconds > 0)
                return TimeSpan.FromSeconds(seconds);
            if (value.ValueKind == JsonValueKind.String &&
                double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) &&
                seconds > 0)
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }

        return null;
    }



    private int GetOrCreateSubscription(SymbolConfig config, SubscriptionType type)
    {
        var key = $"{config.Symbol}_{type.ToString().ToLowerInvariant()}";

        if (_symbolToSubId.TryGetValue(key, out var existingId))
        {
            return existingId;
        }

        var subId = Interlocked.Increment(ref _nextSubscriptionId);
        var info = new SubscriptionInfo(config.Symbol, type, DateTimeOffset.UtcNow);

        _subscriptions[subId] = info;
        _symbolToSubId[key] = subId;

        return subId;
    }

    private static AggressorSide ParseAggressorSide(string? side) => side?.ToLowerInvariant() switch
    {
        "buy" => AggressorSide.Buy,
        "sell" => AggressorSide.Sell,
        _ => AggressorSide.Unknown
    };

    private static DepthOperation ParseDepthOperation(string? operation) => operation?.ToLowerInvariant() switch
    {
        "add" or "insert" => DepthOperation.Insert,
        "update" or "modify" => DepthOperation.Update,
        "delete" or "remove" => DepthOperation.Delete,
        _ => DepthOperation.Insert // Default to Insert for unrecognized operations
    };

    private enum SubscriptionType { Trades, Quotes, Depth }

    private sealed record SubscriptionInfo(string Symbol, SubscriptionType Type, DateTimeOffset CreatedAt);

}


internal sealed class NYSETokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

internal sealed class NYSEHistoricalBarsResponse
{
    [JsonPropertyName("bars")]
    public List<NYSEHistoricalBar>? Bars { get; set; }
}

internal sealed class NYSEHistoricalBar
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("open")]
    public decimal Open { get; set; }

    [JsonPropertyName("high")]
    public decimal High { get; set; }

    [JsonPropertyName("low")]
    public decimal Low { get; set; }

    [JsonPropertyName("close")]
    public decimal Close { get; set; }

    [JsonPropertyName("volume")]
    public long Volume { get; set; }

    [JsonPropertyName("adjustedOpen")]
    public decimal? AdjustedOpen { get; set; }

    [JsonPropertyName("adjustedHigh")]
    public decimal? AdjustedHigh { get; set; }

    [JsonPropertyName("adjustedLow")]
    public decimal? AdjustedLow { get; set; }

    [JsonPropertyName("adjustedClose")]
    public decimal? AdjustedClose { get; set; }

    [JsonPropertyName("adjustedVolume")]
    public long? AdjustedVolume { get; set; }

    [JsonPropertyName("splitFactor")]
    public decimal? SplitFactor { get; set; }

    [JsonPropertyName("dividendAmount")]
    public decimal? DividendAmount { get; set; }

    [JsonPropertyName("sequenceNumber")]
    public long? SequenceNumber { get; set; }
}

internal sealed class NYSEIntradayBarsResponse
{
    [JsonPropertyName("bars")]
    public List<NYSEIntradayBar>? Bars { get; set; }
}

internal sealed class NYSEIntradayBar
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";

    [JsonPropertyName("open")]
    public decimal Open { get; set; }

    [JsonPropertyName("high")]
    public decimal High { get; set; }

    [JsonPropertyName("low")]
    public decimal Low { get; set; }

    [JsonPropertyName("close")]
    public decimal Close { get; set; }

    [JsonPropertyName("volume")]
    public long Volume { get; set; }

    [JsonPropertyName("tradeCount")]
    public long? TradeCount { get; set; }

    [JsonPropertyName("vwap")]
    public decimal? Vwap { get; set; }
}

internal sealed class NYSEDividendsResponse
{
    [JsonPropertyName("dividends")]
    public List<NYSEDividend>? Dividends { get; set; }
}

internal sealed class NYSEDividend
{
    [JsonPropertyName("exDate")]
    public string ExDate { get; set; } = "";

    [JsonPropertyName("paymentDate")]
    public string? PaymentDate { get; set; }

    [JsonPropertyName("recordDate")]
    public string? RecordDate { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

internal sealed class NYSESplitsResponse
{
    [JsonPropertyName("splits")]
    public List<NYSESplit>? Splits { get; set; }
}

internal sealed class NYSESplit
{
    [JsonPropertyName("exDate")]
    public string ExDate { get; set; } = "";

    [JsonPropertyName("splitFrom")]
    public decimal SplitFrom { get; set; }

    [JsonPropertyName("splitTo")]
    public decimal SplitTo { get; set; }
}

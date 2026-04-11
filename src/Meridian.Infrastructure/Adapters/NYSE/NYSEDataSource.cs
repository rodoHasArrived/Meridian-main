using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Meridian.Application.Monitoring;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Meridian.Infrastructure.Resilience;
using Meridian.Infrastructure.Shared;
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
    private readonly IHttpClientFactory _httpClientFactory;

    // WebSocket lifecycle managed by WebSocketConnectionManager (replaces _webSocket, _connectionCts, _receiveTask)
    private readonly WebSocketConnectionManager _wsManager;

    // CancellationTokenSource cancelled on DisconnectAsync so reconnect delays honour shutdown signals
    private CancellationTokenSource _reconnectCts = new();

    private readonly Subject<RealtimeTrade> _trades = new();
    private readonly Subject<RealtimeQuote> _quotes = new();
    private readonly Subject<RealtimeDepthUpdate> _depthUpdates = new();

    private readonly ConcurrentDictionary<int, SubscriptionInfo> _subscriptions = new();
    private readonly ConcurrentDictionary<string, int> _symbolToSubId = new();
    private int _nextSubscriptionId = 1;

    private string? _accessToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _authLock = new(1, 1);

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
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));

        _wsManager = new WebSocketConnectionManager(
            providerName: "NYSE",
            config: WebSocketConnectionConfig.Resilient,
            logger: logger ?? LoggingSetup.ForContext<NYSEDataSource>());

        _wsManager.ConnectionLost += OnWsConnectionLostAsync;
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
            await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);
            return !string.IsNullOrEmpty(_accessToken);
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
            await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);

            // Test REST API connectivity
            using var request = new HttpRequestMessage(HttpMethod.Get, "/markets/status");
            AddAuthHeader(request);

            using var httpClient = CreateNyseHttpClient();
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

        _authLock.Dispose();
        _reconnectCts.Dispose();
    }



    public bool IsConnected => _wsManager.IsConnected;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected)
        {
            Log.Debug("NYSE WebSocket already connected");
            return;
        }

        await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);

        Log.Information("Connecting to NYSE WebSocket at {Url}", _options.EffectiveWebSocketUrl);

        try
        {
            await _wsManager.ConnectAsync(
                new Uri(_options.EffectiveWebSocketUrl),
                ws => ws.Options.SetRequestHeader("Authorization", $"Bearer {_accessToken}"),
                ct).ConfigureAwait(false);

            Status = DataSourceStatus.Connected;
            Log.Information("Connected to NYSE WebSocket");

            // Start receiving messages via the connection manager
            _wsManager.StartReceiveLoop(msg => { ProcessWebSocketMessage(msg); return Task.CompletedTask; }, ct);

            // Re-subscribe to any existing subscriptions
            await ResubscribeAllAsync(ct).ConfigureAwait(false);
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
        if (!IsConnected)
            return;

        Log.Information("Disconnecting from NYSE WebSocket");

        // Signal any in-progress reconnect delay to abort immediately
        _reconnectCts.Cancel();

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
            SendSubscriptionMessageAsync(config.Symbol, "trades", "subscribe", _reconnectCts.Token)
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
                SendSubscriptionMessageAsync(info.Symbol, "trades", "unsubscribe", _reconnectCts.Token)
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
            SendSubscriptionMessageAsync(config.Symbol, "quotes", "subscribe", _reconnectCts.Token)
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
                SendSubscriptionMessageAsync(info.Symbol, "quotes", "unsubscribe", _reconnectCts.Token)
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
            SendSubscriptionMessageAsync(config.Symbol, "depth", "subscribe", _reconnectCts.Token)
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
                SendSubscriptionMessageAsync(info.Symbol, "depth", "unsubscribe", _reconnectCts.Token)
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

    public async Task<IReadOnlyList<AdjustedHistoricalBar>> GetAdjustedDailyBarsAsync(
        string symbol,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken ct = default)
    {
        return await ExecuteWithPoliciesAsync(async token =>
        {
            await EnsureAuthenticatedAsync(token).ConfigureAwait(false);

            var fromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1));
            var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);

            var url = $"/historical/bars/{symbol}?from={fromDate:yyyy-MM-dd}&to={toDate:yyyy-MM-dd}&adjusted=true";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuthHeader(request);

            using var httpClient = CreateNyseHttpClient();
            using var response = await httpClient.SendAsync(request, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            var data = JsonSerializer.Deserialize<NYSEHistoricalBarsResponse>(json);

            if (data?.Bars == null || data.Bars.Count == 0)
            {
                Log.Information("No historical bars returned from NYSE for {Symbol}", symbol);
                return Array.Empty<AdjustedHistoricalBar>();
            }

            var bars = new List<AdjustedHistoricalBar>();

            foreach (var bar in data.Bars)
            {
                try
                {
                    bars.Add(new AdjustedHistoricalBar(
                        Symbol: symbol.ToUpperInvariant(),
                        SessionDate: DateOnly.Parse(bar.Date),
                        Open: bar.Open,
                        High: bar.High,
                        Low: bar.Low,
                        Close: bar.Close,
                        Volume: bar.Volume,
                        Source: Id,
                        SequenceNumber: bar.SequenceNumber ?? 0,
                        AdjustedOpen: bar.AdjustedOpen,
                        AdjustedHigh: bar.AdjustedHigh,
                        AdjustedLow: bar.AdjustedLow,
                        AdjustedClose: bar.AdjustedClose,
                        AdjustedVolume: bar.AdjustedVolume,
                        SplitFactor: bar.SplitFactor,
                        DividendAmount: bar.DividendAmount
                    ));
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to parse NYSE bar for {Symbol} on {Date}", symbol, bar.Date);
                }
            }

            Log.Information("Fetched {Count} historical bars from NYSE for {Symbol}", bars.Count, symbol);
            return bars.OrderBy(b => b.SessionDate).ToArray();
        }, "GetAdjustedDailyBars", ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IntradayBar>> GetIntradayBarsAsync(
        string symbol,
        string interval,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        return await ExecuteWithPoliciesAsync(async token =>
        {
            await EnsureAuthenticatedAsync(token).ConfigureAwait(false);

            var fromTime = from ?? DateTimeOffset.UtcNow.AddDays(-5);
            var toTime = to ?? DateTimeOffset.UtcNow;

            var url = $"/historical/intraday/{symbol}?from={fromTime:O}&to={toTime:O}&interval={interval}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuthHeader(request);

            using var httpClient = CreateNyseHttpClient();
            using var response = await httpClient.SendAsync(request, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            var data = JsonSerializer.Deserialize<NYSEIntradayBarsResponse>(json);

            if (data?.Bars == null || data.Bars.Count == 0)
            {
                return Array.Empty<IntradayBar>();
            }

            return data.Bars.Select(bar => new IntradayBar(
                Symbol: symbol.ToUpperInvariant(),
                Timestamp: DateTimeOffset.Parse(bar.Timestamp),
                Interval: interval,
                Open: bar.Open,
                High: bar.High,
                Low: bar.Low,
                Close: bar.Close,
                Volume: bar.Volume,
                Source: Id,
                TradeCount: bar.TradeCount,
                VWAP: bar.Vwap
            )).OrderBy(b => b.Timestamp).ToArray();
        }, "GetIntradayBars", ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DividendInfo>> GetDividendsAsync(
        string symbol,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken ct = default)
    {
        return await ExecuteWithPoliciesAsync(async token =>
        {
            await EnsureAuthenticatedAsync(token).ConfigureAwait(false);

            var fromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-5));
            var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);

            var url = $"/corporate-actions/dividends/{symbol}?from={fromDate:yyyy-MM-dd}&to={toDate:yyyy-MM-dd}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuthHeader(request);

            using var httpClient = CreateNyseHttpClient();
            using var response = await httpClient.SendAsync(request, token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("NYSE returned {Status} for dividends request for {Symbol}", response.StatusCode, symbol);
                return Array.Empty<DividendInfo>();
            }

            var json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            var data = JsonSerializer.Deserialize<NYSEDividendsResponse>(json);

            if (data?.Dividends == null || data.Dividends.Count == 0)
            {
                return Array.Empty<DividendInfo>();
            }

            return data.Dividends.Select(div => new DividendInfo(
                Symbol: symbol.ToUpperInvariant(),
                ExDate: DateOnly.Parse(div.ExDate),
                PaymentDate: !string.IsNullOrEmpty(div.PaymentDate) ? DateOnly.Parse(div.PaymentDate) : null,
                RecordDate: !string.IsNullOrEmpty(div.RecordDate) ? DateOnly.Parse(div.RecordDate) : null,
                Amount: div.Amount,
                Currency: div.Currency ?? "USD",
                Type: ParseDividendType(div.Type),
                Source: Id
            )).OrderBy(d => d.ExDate).ToArray();
        }, "GetDividends", ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SplitInfo>> GetSplitsAsync(
        string symbol,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken ct = default)
    {
        return await ExecuteWithPoliciesAsync(async token =>
        {
            await EnsureAuthenticatedAsync(token).ConfigureAwait(false);

            var fromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-10));
            var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);

            var url = $"/corporate-actions/splits/{symbol}?from={fromDate:yyyy-MM-dd}&to={toDate:yyyy-MM-dd}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuthHeader(request);

            using var httpClient = CreateNyseHttpClient();
            using var response = await httpClient.SendAsync(request, token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("NYSE returned {Status} for splits request for {Symbol}", response.StatusCode, symbol);
                return Array.Empty<SplitInfo>();
            }

            var json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            var data = JsonSerializer.Deserialize<NYSESplitsResponse>(json);

            if (data?.Splits == null || data.Splits.Count == 0)
            {
                return Array.Empty<SplitInfo>();
            }

            return data.Splits.Select(split => new SplitInfo(
                Symbol: symbol.ToUpperInvariant(),
                ExDate: DateOnly.Parse(split.ExDate),
                SplitFrom: split.SplitFrom,
                SplitTo: split.SplitTo,
                Source: Id
            )).OrderBy(s => s.ExDate).ToArray();
        }, "GetSplits", ct).ConfigureAwait(false);
    }



    private async Task EnsureAuthenticatedAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _tokenExpiry.AddMinutes(-5))
        {
            return;
        }

        await _authLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _tokenExpiry.AddMinutes(-5))
            {
                return;
            }

            var apiKey = _options.ResolveApiKey();
            var apiSecret = _options.ResolveApiSecret();

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
            {
                throw new InvalidOperationException("NYSE API credentials not configured");
            }

            // OAuth2 client credentials flow
            var authContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ResolveClientId() ?? apiKey,
                ["client_secret"] = apiSecret
            });

            using var authRequest = new HttpRequestMessage(HttpMethod.Post, "/oauth/token")
            {
                Content = authContent
            };

            using var httpClient = CreateNyseHttpClient();
            using var response = await httpClient.SendAsync(authRequest, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var tokenResponse = JsonSerializer.Deserialize<NYSETokenResponse>(json);

            _accessToken = tokenResponse?.AccessToken
                ?? throw new InvalidOperationException("Failed to obtain NYSE access token");
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

            Log.Debug("Obtained NYSE access token, expires at {Expiry}", _tokenExpiry);
        }
        finally
        {
            _authLock.Release();
        }
    }

    private void AddAuthHeader(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(_accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        }
    }

    /// <summary>
    /// Creates a named <see cref="HttpClient"/> via <see cref="IHttpClientFactory"/>, setting the
    /// dynamic <see cref="NYSEOptions.EffectiveBaseUrl"/> as the base address.  The shared handler
    /// pool (connection lifetime, retry pipeline) is managed entirely by the factory.
    /// </summary>
    private HttpClient CreateNyseHttpClient()
    {
        var client = _httpClientFactory.CreateClient(HttpClientNames.NYSE);
        // BaseAddress is dynamic per NYSEOptions; it must be applied to the lightweight
        // HttpClient wrapper.  The underlying HttpMessageHandler/connection pool is
        // owned by IHttpClientFactory and survives beyond any single HttpClient instance.
        client.BaseAddress = new Uri(_options.EffectiveBaseUrl);
        return client;
    }



    private async Task OnWsConnectionLostAsync()
    {
        Status = DataSourceStatus.Disconnected;

        // Create a fresh CTS for this reconnect session so ConnectAsync calls
        // during the loop do not cancel the token the loop itself is watching.
        _reconnectCts.Dispose();
        _reconnectCts = new CancellationTokenSource();
        var reconnectCt = _reconnectCts.Token;

        for (int attempt = 1; attempt <= _options.MaxReconnectAttempts; attempt++)
        {
            if (reconnectCt.IsCancellationRequested)
            {
                Log.Debug("NYSE reconnection loop cancelled by shutdown signal");
                return;
            }

            MigrationDiagnostics.IncReconnectAttempt("nyse");
            Log.Information("NYSE reconnection attempt {Attempt}/{Max}", attempt, _options.MaxReconnectAttempts);

            // Exponential backoff with ±15% jitter; cap at 60 s [P3]
            var jitter = Random.Shared.NextDouble() * 0.3 + 0.85;
            var delaySecs = Math.Min(_options.ReconnectDelaySeconds * Math.Pow(2, attempt - 1) * jitter, 60.0);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySecs), reconnectCt).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Log.Debug("NYSE reconnect delay cancelled");
                return;
            }

            // Check cancellation immediately before attempting the connect so that a
            // shutdown signal received during the backoff delay is still honoured.
            if (reconnectCt.IsCancellationRequested)
            {
                Log.Debug("NYSE reconnection loop cancelled by shutdown signal");
                return;
            }

            try
            {
                // Pass reconnectCt so that a shutdown signal also cancels the connect operation
                // itself, not just the backoff delay.  ConnectAsync no longer resets _reconnectCts
                // so reconnectCt remains valid for the duration of the connect call.
                await ConnectAsync(reconnectCt).ConfigureAwait(false);
                MigrationDiagnostics.IncReconnectSuccess("nyse");
                return;
            }
            catch (OperationCanceledException)
            {
                Log.Debug("NYSE reconnect cancelled during connect");
                return;
            }
            catch (Exception ex)
            {
                MigrationDiagnostics.IncReconnectFailure("nyse");
                Log.Warning(ex, "NYSE reconnection attempt {Attempt} failed", attempt);
            }
        }

        Log.Error("NYSE failed to reconnect after {Max} attempts", _options.MaxReconnectAttempts);
        Status = DataSourceStatus.Unavailable;
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

    private async Task SendSubscriptionMessageAsync(string symbol, string channel, string action, CancellationToken ct = default)
    {
        try
        {
            if (!IsConnected)
                return;

            var message = JsonSerializer.Serialize(new
            {
                action,
                channel,
                symbol
            });

            await _wsManager.SendAsync(message, ct).ConfigureAwait(false);

            Log.Debug("NYSE {Action} {Channel} for {Symbol}", action, channel, symbol);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to send NYSE {Action} {Channel} for {Symbol}. " +
                "Subscription state may be inconsistent.", action, channel, symbol);
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

    private async Task ResubscribeAllAsync(CancellationToken ct)
    {
        foreach (var (_, info) in _subscriptions)
        {
            var channel = info.Type.ToString().ToLowerInvariant();
            await SendSubscriptionMessageAsync(info.Symbol, channel, "subscribe", ct).ConfigureAwait(false);
        }
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

    private static DividendType ParseDividendType(string? type) => type?.ToLowerInvariant() switch
    {
        "special" => DividendType.Special,
        "return" => DividendType.Return,
        "liquidation" => DividendType.Liquidation,
        _ => DividendType.Regular
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


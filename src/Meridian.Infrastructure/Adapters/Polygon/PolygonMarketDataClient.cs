using System.Text;
using System.Text.Json;
using System.Threading;
using Meridian.Application.Exceptions;
using Meridian.Application.Logging;
using Meridian.Application.Monitoring;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Resilience;
using Meridian.Infrastructure.Shared;
using PolygonOptions = Meridian.Application.Config.PolygonOptions;

namespace Meridian.Infrastructure.Adapters.Polygon;

/// <summary>
/// Polygon.io market data adapter implementing the IMarketDataClient abstraction.
/// Supports full WebSocket streaming for trades, quotes, and aggregates.
///
/// Extends <see cref="WebSocketProviderBase"/>, which centralises connection lifecycle,
/// resilience (retry + circuit breaker), heartbeat monitoring, and automatic reconnection.
/// ~280 LOC of WebSocket boilerplate removed compared to the previous direct implementation.
///
/// Current support:
/// - Trades: YES (streams "T" messages and forwards to TradeDataCollector)
/// - Quotes: YES (streams "Q" messages and forwards to QuoteCollector)
/// - Aggregates: YES (streams "A" and "AM" messages for second/minute bars)
///
/// Polygon WebSocket Protocol:
/// - Endpoint: wss://socket.polygon.io/{feed} (stocks, options, forex, crypto)
/// - Auth: Send {"action":"auth","params":"{apiKey}"} after connect
/// - Subscribe: {"action":"subscribe","params":"T.AAPL,Q.AAPL"}
/// - Message types: T=trade, Q=quote, A=aggregate, AM=minute aggregate
/// </summary>
[DataSource("polygon", "Polygon.io", Infrastructure.DataSources.DataSourceType.Realtime, DataSourceCategory.Aggregator,
    Priority = 15, Description = "WebSocket streaming from Polygon.io for trades, quotes, and aggregates")]
[ImplementsAdr("ADR-001", "Polygon.io streaming data provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
public sealed class PolygonMarketDataClient : WebSocketProviderBase
{
    private readonly IMarketEventPublisher _publisher;
    private readonly TradeDataCollector _tradeCollector;
    private readonly QuoteCollector _quoteCollector;
    private readonly PolygonOptions _options;
    private long _messageSequence;

    /// <summary>
    /// Creates a new Polygon market data client.
    /// </summary>
    /// <param name="publisher">Event publisher for heartbeats and status.</param>
    /// <param name="tradeCollector">Collector for trade data.</param>
    /// <param name="quoteCollector">Collector for quote data.</param>
    /// <param name="options">Polygon configuration options. A valid API key is required to connect.</param>
    /// <param name="reconnectionMetrics">Unused; kept for backward-compatible constructor signature.</param>
    /// <exception cref="ArgumentNullException">If publisher, tradeCollector, or quoteCollector is null.</exception>
    public PolygonMarketDataClient(
        IMarketEventPublisher publisher,
        TradeDataCollector tradeCollector,
        QuoteCollector quoteCollector,
        PolygonOptions? options = null,
        IReconnectionMetrics? reconnectionMetrics = null)
        : base(
            providerName: "Polygon",
            config: WebSocketConnectionConfig.Default,
            subscriptionStartId: ProviderSubscriptionRanges.PolygonStart)
    {
        _ = reconnectionMetrics; // kept for backward-compatible constructor signature
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _tradeCollector = tradeCollector ?? throw new ArgumentNullException(nameof(tradeCollector));
        _quoteCollector = quoteCollector ?? throw new ArgumentNullException(nameof(quoteCollector));
        _options = options ?? new PolygonOptions();

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            ValidateApiKeyFormat(_options.ApiKey);
        }

        Log.Information(
            "Polygon client initialized (CredentialsConfigured: {CredentialsConfigured}, Feed: {Feed}, Trades: {Trades}, Quotes: {Quotes}, Aggregates: {Aggregates})",
            HasValidCredentials,
            _options.Feed,
            _options.SubscribeTrades,
            _options.SubscribeQuotes,
            _options.SubscribeAggregates);
    }

    /// <summary>
    /// Minimum length for a valid Polygon API key.
    /// Polygon API keys are typically 32 characters, but we accept 20+ for flexibility.
    /// </summary>
    private const int MinApiKeyLength = 20;

    /// <summary>
    /// Gets whether the client has a valid API key configured.
    /// </summary>
    public bool HasValidCredentials =>
        !string.IsNullOrWhiteSpace(_options.ApiKey) && _options.ApiKey.Length >= MinApiKeyLength;

    /// <inheritdoc/>
    public override bool IsEnabled => HasValidCredentials;

    /// <summary>
    /// Gets the current connection state.
    /// </summary>
    public bool IsConnected => Connected;

    /// <summary>
    /// Gets the configured feed type (stocks, options, forex, crypto).
    /// </summary>
    public string Feed => _options.Feed;

    /// <summary>
    /// Gets whether using delayed (15-minute) data.
    /// </summary>
    public bool UseDelayed => _options.UseDelayed;


    /// <inheritdoc/>
    public override string ProviderId => "polygon";

    /// <inheritdoc/>
    public override string ProviderDisplayName => "Polygon.io Streaming";

    /// <inheritdoc/>
    public override string ProviderDescription => "Real-time trades, quotes, and aggregates via Polygon.io WebSocket API";

    /// <inheritdoc/>
    public override int ProviderPriority => 15;

    /// <inheritdoc/>
    public override ProviderCapabilities ProviderCapabilities { get; } = ProviderCapabilities.Streaming(
        trades: true,
        quotes: true,
        depth: false) with
    {
        SupportedMarkets = new[] { "US" },
        MaxRequestsPerWindow = 5,
        RateLimitWindow = TimeSpan.FromMinutes(1),
        MinRequestDelay = TimeSpan.FromMilliseconds(12000)
    };

    /// <inheritdoc/>
    public ProviderCredentialField[] ProviderCredentialFields => new[]
    {
        new ProviderCredentialField("ApiKey", "POLYGON__APIKEY", "Polygon API Key", true)
    };

    /// <inheritdoc/>
    public string[] ProviderNotes => new[]
    {
        "Polygon provides comprehensive market data.",
        "Free tier has limited rate limits; paid plans offer more.",
        "Supports stocks, options, forex, and crypto."
    };

    /// <inheritdoc/>
    public string[] ProviderWarnings => new[]
    {
        "Free tier has 15-minute delayed data for most feeds."
    };



    /// <inheritdoc/>
    protected override Uri BuildWebSocketUri()
    {
        var endpoint = _options.UseDelayed
            ? $"wss://delayed.polygon.io/{_options.Feed}"
            : $"wss://socket.polygon.io/{_options.Feed}";

        Log.Information("Polygon WebSocket endpoint: {Endpoint} (Delayed: {UseDelayed})", endpoint, _options.UseDelayed);
        return new Uri(endpoint);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Polygon requires a two-step handshake before the receive loop starts:
    /// 1. Read the initial "connected" status message from the server.
    /// 2. Send the API key and read the "auth_success" response.
    /// Both reads use <see cref="WebSocketProviderBase.ReadOneMessageAsync"/> which reads
    /// directly from the socket before <see cref="WebSocketConnectionManager.StartReceiveLoop"/>
    /// takes ownership.
    /// </remarks>
    protected override async Task AuthenticateAsync(CancellationToken ct)
    {
        // Step 1 – read the initial connection confirmation from Polygon
        var connectedMsg = await ReadOneMessageAsync(ct).ConfigureAwait(false);
        if (connectedMsg != null)
        {
            Log.Debug("Received Polygon connection message: {Message}", connectedMsg);
            try
            {
                using var doc = JsonDocument.Parse(connectedMsg);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        if (elem.TryGetProperty("ev", out var evProp) &&
                            evProp.GetString() == "status" &&
                            elem.TryGetProperty("status", out var statusProp) &&
                            statusProp.GetString() == "connected")
                        {
                            Log.Debug("Polygon connection status confirmed");
                            break;
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                Log.Warning(ex, "Failed to parse Polygon connection message; proceeding with auth");
            }
        }

        // Step 2 – authenticate
        var authMessage = JsonSerializer.Serialize(new { action = "auth", @params = _options.ApiKey });
        await SendAsync(authMessage, ct).ConfigureAwait(false);
        Log.Debug("Sent Polygon authentication message, waiting for response");

        // Step 3 – read auth response
        var authResponse = await ReadOneMessageAsync(ct).ConfigureAwait(false);
        if (authResponse != null)
        {
            Log.Debug("Received Polygon auth response: {Response}", authResponse);
            try
            {
                using var doc = JsonDocument.Parse(authResponse);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        if (elem.TryGetProperty("ev", out var evProp) && evProp.GetString() == "status")
                        {
                            var status = elem.TryGetProperty("status", out var statusProp)
                                ? statusProp.GetString() : null;
                            var message = elem.TryGetProperty("message", out var msgProp)
                                ? msgProp.GetString() : null;

                            if (status == "auth_success")
                            {
                                Log.Information("Polygon authentication successful");
                                return;
                            }
                            else if (status == "auth_failed")
                            {
                                throw new ConnectionException(
                                    $"Polygon authentication failed: {message}",
                                    provider: "Polygon");
                            }
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                Log.Warning(ex, "Failed to parse Polygon auth response");
            }
        }

        throw new ConnectionException(
            "Did not receive valid authentication response from Polygon",
            provider: "Polygon");
    }

    /// <inheritdoc/>
    protected override Task HandleMessageAsync(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return Task.CompletedTask;
        RecordActivity();
        ProcessMessage(message);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    protected override async Task ResubscribeAsync(CancellationToken ct)
    {
        var tradeSyms = Subscriptions.GetSymbolsByKind("trades");
        var quoteSyms = Subscriptions.GetSymbolsByKind("quotes");
        var aggregateSyms = Subscriptions.GetSymbolsByKind("aggregates");

        if (tradeSyms.Length > 0)
        {
            var channels = string.Join(",", tradeSyms.Select(s => $"T.{s}"));
            await SendAsync(
                JsonSerializer.Serialize(new { action = "subscribe", @params = channels }), ct)
                .ConfigureAwait(false);
            Log.Information("Re-subscribed to {Count} trade channels", tradeSyms.Length);
        }

        if (quoteSyms.Length > 0)
        {
            var channels = string.Join(",", quoteSyms.Select(s => $"Q.{s}"));
            await SendAsync(
                JsonSerializer.Serialize(new { action = "subscribe", @params = channels }), ct)
                .ConfigureAwait(false);
            Log.Information("Re-subscribed to {Count} quote channels", quoteSyms.Length);
        }

        if (aggregateSyms.Length > 0)
        {
            var channels = string.Join(",",
                aggregateSyms.SelectMany(s => new[] { $"A.{s}", $"AM.{s}" }));
            await SendAsync(
                JsonSerializer.Serialize(new { action = "subscribe", @params = channels }), ct)
                .ConfigureAwait(false);
            Log.Information("Re-subscribed to {Count} aggregate channels", aggregateSyms.Length);
        }
    }



    /// <inheritdoc/>
    /// <remarks>
    /// Fails fast when Polygon credentials are missing so production code never falls back
    /// to synthetic data generation. Test-only stub behavior lives in the test project.
    /// </remarks>
    public override async Task ConnectAsync(CancellationToken ct = default)
    {
        EnsureCredentialsConfigured();

        await base.ConnectAsync(ct).ConfigureAwait(false);
        _publisher.TryPublish(MarketEvent.Heartbeat(DateTimeOffset.UtcNow, source: "Polygon"));
    }



    /// <inheritdoc/>
    public override int SubscribeMarketDepth(SymbolConfig cfg)
    {
        if (cfg is null)
            throw new ArgumentNullException(nameof(cfg));

        // Polygon provides quotes (BBO), not full L2 depth
        if (!_options.SubscribeQuotes)
        {
            Log.Debug("Quote subscription disabled in Polygon options, skipping depth for {Symbol}", cfg.Symbol);
            return -1;
        }

        var symbol = cfg.Symbol.Trim().ToUpperInvariant();
        var isNewSymbol = !Subscriptions.HasSubscription(symbol, "quotes");
        var id = Subscriptions.Subscribe(symbol, "quotes");
        if (id == -1)
            return -1;

        Log.Debug("Subscribed to Polygon quotes for {Symbol} (SubId: {SubId}, Connected: {Connected})",
            symbol, id, Connected);

        if (Connected && isNewSymbol)
        {
            SendSubscribeAsync($"Q.{symbol}")
                .ObserveException(Log, $"Polygon subscribe quotes for {symbol}");
        }

        return id;
    }

    /// <inheritdoc/>
    public override void UnsubscribeMarketDepth(int subscriptionId)
    {
        var subscription = Subscriptions.Unsubscribe(subscriptionId);
        if (subscription == null)
            return;

        if (!Subscriptions.HasSubscription(subscription.Symbol, "quotes"))
        {
            Log.Debug("Unsubscribed from Polygon quotes for {Symbol}", subscription.Symbol);
            if (Connected)
            {
                SendUnsubscribeAsync($"Q.{subscription.Symbol}")
                    .ObserveException(Log, $"Polygon unsubscribe quotes for {subscription.Symbol}");
            }
        }
    }

    /// <inheritdoc/>
    public override int SubscribeTrades(SymbolConfig cfg)
    {
        if (cfg is null)
            throw new ArgumentNullException(nameof(cfg));

        if (!_options.SubscribeTrades)
        {
            Log.Debug("Trade subscription disabled in Polygon options, skipping trades for {Symbol}", cfg.Symbol);
            return -1;
        }

        var symbol = cfg.Symbol.Trim().ToUpperInvariant();
        var isNewSymbol = !Subscriptions.HasSubscription(symbol, "trades");
        var id = Subscriptions.Subscribe(symbol, "trades");
        if (id == -1)
            return -1;

        Log.Debug("Subscribed to Polygon trades for {Symbol} (SubId: {SubId}, Connected: {Connected})",
            symbol, id, Connected);

        if (Connected && isNewSymbol)
        {
            SendSubscribeAsync($"T.{symbol}")
                .ObserveException(Log, $"Polygon subscribe trades for {symbol}");
        }

        return id;
    }

    /// <inheritdoc/>
    public override void UnsubscribeTrades(int subscriptionId)
    {
        var subscription = Subscriptions.Unsubscribe(subscriptionId);
        if (subscription == null)
            return;

        if (!Subscriptions.HasSubscription(subscription.Symbol, "trades"))
        {
            Log.Debug("Unsubscribed from Polygon trades for {Symbol}", subscription.Symbol);
            if (Connected)
            {
                SendUnsubscribeAsync($"T.{subscription.Symbol}")
                    .ObserveException(Log, $"Polygon unsubscribe trades for {subscription.Symbol}");
            }
        }
    }

    /// <summary>
    /// Subscribes to aggregate bars (second and minute) for the specified symbol.
    /// </summary>
    /// <param name="cfg">Symbol configuration.</param>
    /// <returns>Subscription ID, or -1 if not supported/not subscribed.</returns>
    public int SubscribeAggregates(SymbolConfig cfg)
    {
        if (cfg is null)
            throw new ArgumentNullException(nameof(cfg));

        if (!_options.SubscribeAggregates)
        {
            Log.Debug("Aggregate subscription disabled in Polygon options, skipping aggregates for {Symbol}", cfg.Symbol);
            return -1;
        }

        var symbol = cfg.Symbol.Trim().ToUpperInvariant();
        var isNewSymbol = !Subscriptions.HasSubscription(symbol, "aggregates");
        var id = Subscriptions.Subscribe(symbol, "aggregates");
        if (id == -1)
            return -1;

        Log.Debug("Subscribed to Polygon aggregates for {Symbol} (SubId: {SubId}, Connected: {Connected})",
            symbol, id, Connected);

        if (Connected && isNewSymbol)
        {
            SendSubscribeAsync($"A.{symbol},AM.{symbol}")
                .ObserveException(Log, $"Polygon subscribe aggregates for {symbol}");
        }

        return id;
    }

    /// <summary>
    /// Unsubscribes from aggregate bars for the specified subscription.
    /// </summary>
    public void UnsubscribeAggregates(int subscriptionId)
    {
        var subscription = Subscriptions.Unsubscribe(subscriptionId);
        if (subscription == null)
            return;

        if (!Subscriptions.HasSubscription(subscription.Symbol, "aggregates"))
        {
            Log.Debug("Unsubscribed from Polygon aggregates for {Symbol}", subscription.Symbol);
            if (Connected)
            {
                SendUnsubscribeAsync($"A.{subscription.Symbol},AM.{subscription.Symbol}")
                    .ObserveException(Log, $"Polygon unsubscribe aggregates for {subscription.Symbol}");
            }
        }
    }

    /// <summary>Gets the current subscription count.</summary>
    public int SubscriptionCount => Subscriptions.Count;

    /// <summary>Gets the list of currently subscribed trade symbols.</summary>
    public IReadOnlyList<string> SubscribedTradeSymbols => Subscriptions.GetSymbolsByKind("trades");

    /// <summary>Gets the list of currently subscribed quote symbols.</summary>
    public IReadOnlyList<string> SubscribedQuoteSymbols => Subscriptions.GetSymbolsByKind("quotes");

    /// <summary>Gets the list of currently subscribed aggregate symbols.</summary>
    public IReadOnlyList<string> SubscribedAggregateSymbols => Subscriptions.GetSymbolsByKind("aggregates");



    /// <summary>
    /// Ensures a usable Polygon API key is configured before attempting a live connection.
    /// </summary>
    /// <exception cref="ConfigurationException">Thrown when the API key is missing or too short.</exception>
    private void EnsureCredentialsConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new ConfigurationException(
                "Polygon API key is required for live market data. Set Polygon:ApiKey or POLYGON__APIKEY before connecting.",
                configPath: "Polygon:ApiKey",
                fieldName: "ApiKey");
        }

        if (_options.ApiKey.Length < MinApiKeyLength)
        {
            throw new ConfigurationException(
                $"Polygon API key must be at least {MinApiKeyLength} characters long.",
                configPath: "Polygon:ApiKey",
                fieldName: "ApiKey");
        }
    }

    /// <summary>
    /// Validates the API key format.
    /// </summary>
    private void ValidateApiKeyFormat(string apiKey)
    {
        if (apiKey.Length < 10)
        {
            Log.Warning("Polygon API key appears too short ({Length} chars). Expected ~32 characters.", apiKey.Length);
        }

        if (apiKey.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Polygon API key contains whitespace characters", nameof(apiKey));
        }

        Log.Debug("Polygon API key format validated (length: {Length})", apiKey.Length);
    }



    private async Task SendSubscribeAsync(string channel, CancellationToken ct = default)
    {
        try
        {
            await SendAsync(
                JsonSerializer.Serialize(new { action = "subscribe", @params = channel }),
                CancellationToken.None).ConfigureAwait(false);
            Log.Debug("Sent subscribe request for {Channel}", channel);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to send subscribe message for {Channel}", channel);
        }
    }

    private async Task SendUnsubscribeAsync(string channel, CancellationToken ct = default)
    {
        try
        {
            await SendAsync(
                JsonSerializer.Serialize(new { action = "unsubscribe", @params = channel }),
                CancellationToken.None).ConfigureAwait(false);
            Log.Debug("Sent unsubscribe request for {Channel}", channel);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to send unsubscribe message for {Channel}", channel);
        }
    }



    /// <summary>
    /// Processes an incoming WebSocket message (string form, called by the base receive loop).
    /// </summary>
    private void ProcessMessage(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                Log.Warning("Unexpected Polygon message format (not array), length: {Length}", message.Length);
                return;
            }

            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                if (!elem.TryGetProperty("ev", out var evProp))
                    continue;

                var eventType = evProp.GetString();
                switch (eventType)
                {
                    case "T":
                        ProcessTrade(elem);
                        break;
                    case "Q":
                        ProcessQuote(elem);
                        break;
                    case "A":
                        ProcessAggregate(elem, Domain.Models.AggregateTimeframe.Second);
                        break;
                    case "AM":
                        ProcessAggregate(elem, Domain.Models.AggregateTimeframe.Minute);
                        break;
                    case "status":
                        ProcessStatus(elem);
                        break;
                    default:
                        Log.Debug("Unhandled Polygon event type: {EventType}", eventType);
                        break;
                }
            }
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "Failed to parse Polygon message, length: {Length}", message.Length);
        }
    }

    /// <summary>
    /// Processes a trade message from Polygon.
    /// Trade format: { "ev":"T", "sym":"AAPL", "p":150.25, "s":100, "t":1234567890000, "c":[12,37], "i":"trade_id", "x":4, "z":1 }
    /// </summary>
    private void ProcessTrade(JsonElement elem)
    {
        try
        {
            var symbol = elem.TryGetProperty("sym", out var symProp) ? symProp.GetString() : null;
            if (string.IsNullOrEmpty(symbol))
                return;

            if (!Subscriptions.HasSubscription(symbol, "trades"))
                return;

            var price = elem.TryGetProperty("p", out var priceProp) ? priceProp.GetDecimal() : 0m;
            var size = elem.TryGetProperty("s", out var sizeProp) ? sizeProp.GetInt64() : 0L;
            var timestamp = elem.TryGetProperty("t", out var tsProp) ? tsProp.GetInt64() : 0L;
            var tradeId = elem.TryGetProperty("i", out var idProp) ? idProp.GetString() : null;
            var exchange = elem.TryGetProperty("x", out var xProp) ? xProp.GetInt32() : 0;

            if (price <= 0)
            {
                Log.Debug("Skipping Polygon trade for {Symbol} with invalid price {Price}", symbol, price);
                return;
            }

            if (size <= 0)
            {
                Log.Debug("Skipping Polygon trade for {Symbol} with invalid size {Size}", symbol, size);
                return;
            }

            var aggressor = AggressorSide.Unknown;
            string[]? rawConditions = null;
            if (elem.TryGetProperty("c", out var conditions) && conditions.ValueKind == JsonValueKind.Array)
            {
                var conditionArray = conditions.EnumerateArray().ToArray();
                var conditionCodes = conditionArray.Select(c => c.GetInt32());
                aggressor = MapConditionCodesToAggressor(conditionCodes);
                // Store as strings matching the keys used in condition-codes.json (e.g. "0", "12")
                rawConditions = conditionArray.Select(c => c.GetInt32().ToString()).ToArray();
            }

            var seq = Interlocked.Increment(ref _messageSequence);
            var trade = new MarketTradeUpdate(
                Timestamp: timestamp > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
                    : DateTimeOffset.UtcNow,
                Symbol: symbol,
                Price: price,
                Size: size,
                Aggressor: aggressor,
                SequenceNumber: seq,
                StreamId: tradeId ?? $"POLYGON_{seq}",
                Venue: MapExchangeCode(exchange),
                RawConditions: rawConditions);

            _tradeCollector.OnTrade(trade);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to process Polygon trade message");
        }
    }

    /// <summary>
    /// Processes a quote message from Polygon.
    /// Quote format: { "ev":"Q", "sym":"AAPL", "bp":150.20, "bs":100, "ap":150.25, "as":200, "t":1234567890000, "x":4 }
    /// </summary>
    private void ProcessQuote(JsonElement elem)
    {
        try
        {
            var symbol = elem.TryGetProperty("sym", out var symProp) ? symProp.GetString() : null;
            if (string.IsNullOrEmpty(symbol))
                return;

            if (!Subscriptions.HasSubscription(symbol, "quotes"))
                return;

            var bidPrice = elem.TryGetProperty("bp", out var bpProp) ? bpProp.GetDecimal() : 0m;
            var bidSize = elem.TryGetProperty("bs", out var bsProp) ? bsProp.GetInt64() : 0L;
            var askPrice = elem.TryGetProperty("ap", out var apProp) ? apProp.GetDecimal() : 0m;
            var askSize = elem.TryGetProperty("as", out var asProp) ? asProp.GetInt64() : 0L;
            var timestamp = elem.TryGetProperty("t", out var tsProp) ? tsProp.GetInt64() : 0L;
            var exchange = elem.TryGetProperty("x", out var xProp) ? xProp.GetInt32() : 0;

            if (bidPrice <= 0 && askPrice <= 0)
            {
                Log.Debug("Skipping Polygon quote for {Symbol} with no valid prices", symbol);
                return;
            }

            if (bidPrice > 0 && askPrice > 0 && askPrice < bidPrice)
            {
                Log.Debug("Skipping Polygon quote for {Symbol} with inverted spread: bid={Bid} ask={Ask}",
                    symbol, bidPrice, askPrice);
                return;
            }

            var ts = timestamp > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
                : DateTimeOffset.UtcNow;

            var quote = new MarketQuoteUpdate(
                Timestamp: ts,
                Symbol: symbol,
                BidPrice: bidPrice,
                BidSize: bidSize,
                AskPrice: askPrice,
                AskSize: askSize,
                SequenceNumber: null,
                StreamId: "POLYGON",
                Venue: MapExchangeCode(exchange));

            _quoteCollector.OnQuote(quote);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to process Polygon quote message");
        }
    }

    /// <summary>
    /// Processes an aggregate bar message from Polygon.
    /// Aggregate format: { "ev":"A/AM", "sym":"AAPL", "o":150.25, "h":150.50, "l":150.00, "c":150.40, "v":1000, "vw":150.30, "s":1234567890000, "e":1234567891000, "n":50 }
    /// </summary>
    private void ProcessAggregate(JsonElement elem, Domain.Models.AggregateTimeframe timeframe)
    {
        try
        {
            var symbol = elem.TryGetProperty("sym", out var symProp) ? symProp.GetString() : null;
            if (string.IsNullOrEmpty(symbol))
                return;

            if (!Subscriptions.HasSubscription(symbol, "aggregates"))
                return;

            var open = elem.TryGetProperty("o", out var oProp) ? oProp.GetDecimal() : 0m;
            var high = elem.TryGetProperty("h", out var hProp) ? hProp.GetDecimal() : 0m;
            var low = elem.TryGetProperty("l", out var lProp) ? lProp.GetDecimal() : 0m;
            var close = elem.TryGetProperty("c", out var cProp) ? cProp.GetDecimal() : 0m;
            var volume = elem.TryGetProperty("v", out var vProp) ? vProp.GetInt64() : 0L;

            if (open <= 0 || high <= 0 || low <= 0 || close <= 0)
            {
                Log.Debug("Skipping aggregate for {Symbol} with invalid OHLC data", symbol);
                return;
            }

            if (low > high || low > open || low > close || high < open || high < close)
            {
                Log.Warning("Skipping aggregate for {Symbol} with inconsistent OHLC: O={Open} H={High} L={Low} C={Close}",
                    symbol, open, high, low, close);
                return;
            }

            var vwap = elem.TryGetProperty("vw", out var vwProp) ? vwProp.GetDecimal() : 0m;
            var startTimestamp = elem.TryGetProperty("s", out var sProp) ? sProp.GetInt64() : 0L;
            var endTimestamp = elem.TryGetProperty("e", out var eProp) ? eProp.GetInt64() : 0L;
            var tradeCount = elem.TryGetProperty("n", out var nProp) ? nProp.GetInt32() : 0;

            var startTime = startTimestamp > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(startTimestamp)
                : DateTimeOffset.UtcNow;
            var endTime = endTimestamp > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(endTimestamp)
                : startTime.AddSeconds(timeframe == Domain.Models.AggregateTimeframe.Second ? 1 : 60);

            var seq = Interlocked.Increment(ref _messageSequence);
            var aggregateBar = new AggregateBar(
                Symbol: symbol,
                StartTime: startTime,
                EndTime: endTime,
                Open: open,
                High: high,
                Low: low,
                Close: close,
                Volume: volume,
                Vwap: vwap,
                TradeCount: tradeCount,
                Timeframe: timeframe,
                Source: "Polygon",
                SequenceNumber: seq);

            _publisher.TryPublish(MarketEvent.AggregateBar(endTime, symbol, aggregateBar, seq, "Polygon"));

            Log.Debug(
                "Processed {Timeframe} aggregate for {Symbol}: O={Open} H={High} L={Low} C={Close} V={Volume}",
                timeframe, symbol, open, high, low, close, volume);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to process Polygon aggregate message");
        }
    }

    private void ProcessStatus(JsonElement elem)
    {
        var status = elem.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;
        var message = elem.TryGetProperty("message", out var msgProp) ? msgProp.GetString() : null;

        if (status == "error" || status == "auth_failed")
        {
            Log.Error("Polygon server error: {Status} - {Message}", status, message);
            return;
        }

        if (status == "success" && message?.Contains("subscribed") == true)
        {
            Log.Information("Polygon subscription confirmed: {Message}", message);
            return;
        }

        Log.Debug("Polygon status: {Status} - {Message}", status, message);
    }



    /// <summary>
    /// Maps Polygon exchange codes to exchange names.
    /// </summary>
    private static string MapExchangeCode(int code) => code switch
    {
        1 => "NYSE",
        2 => "AMEX",
        3 => "ARCA",
        4 => "NASDAQ",
        5 => "NASDAQ_BX",
        6 => "NASDAQ_PSX",
        7 => "BATS_Y",
        8 => "BATS",
        9 => "IEX",
        10 => "EDGX",
        11 => "EDGA",
        12 => "CHX",
        13 => "NSX",
        14 => "FINRA_ADF",
        15 => "CBOE",
        16 => "MEMX",
        17 => "MIAX",
        19 => "LTSE",
        _ => $"EX_{code}"
    };

    /// <summary>
    /// Maps Polygon CTA/UTP trade condition codes to aggressor side.
    /// Reference: https://polygon.io/docs/stocks/get_v3_reference_conditions
    /// </summary>
    private static AggressorSide MapConditionCodesToAggressor(IEnumerable<int> conditionCodes)
    {
        foreach (var code in conditionCodes)
        {
            switch (code)
            {
                // Seller-initiated condition codes
                case 29: // Seller
                case 30: // Sold Last
                case 31: // Sold Last and Stopped Stock
                case 32: // Sold (Out of Sequence)
                case 33: // Sold (Out of Sequence) and Stopped Stock
                    return AggressorSide.Sell;
            }
        }
        return AggressorSide.Unknown;
    }



    /// <summary>
    /// Injects a raw WebSocket message for unit testing without a live connection.
    /// </summary>
    internal void ProcessTestMessage(string message) => ProcessMessage(message);

}

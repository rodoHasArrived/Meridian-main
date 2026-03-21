using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using Meridian.Application.Logging;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Resilience;
using Meridian.Infrastructure.Shared;
using Serilog;
using AlpacaOptions = Meridian.Application.Config.AlpacaOptions;

namespace Meridian.Infrastructure.Adapters.Alpaca;

/// <summary>
/// Alpaca Market Data client (WebSocket) that implements the IMarketDataClient abstraction.
///
/// Current support:
/// - Trades: YES (streams "t" messages and forwards to TradeDataCollector)
/// - Depth (L2): NO (Alpaca stock stream provides quotes/BBO, not full L2 updates; method returns -1)
///
/// Notes:
/// - Alpaca typically limits to 1 active stream connection per user per endpoint.
/// - Authentication is performed by sending an "auth" message immediately after connect.
/// </summary>
[DataSource("alpaca", "Alpaca Markets", Infrastructure.DataSources.DataSourceType.Realtime, DataSourceCategory.Broker,
    Priority = 10, Description = "WebSocket streaming from Alpaca Markets")]
[ImplementsAdr("ADR-001", "Alpaca streaming data provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
public sealed class AlpacaMarketDataClient : IMarketDataClient
{
    private readonly ILogger _log = LoggingSetup.ForContext<AlpacaMarketDataClient>();
    private readonly TradeDataCollector _tradeCollector;
    private readonly QuoteCollector _quoteCollector;
    private readonly AlpacaOptions _opt;

    // Connection management - uses centralized WebSocketConnectionManager
    private readonly WebSocketConnectionManager _connectionManager;
    private Uri? _wsUri;

    // Centralized subscription management with provider-specific ID range
    private readonly Infrastructure.Shared.SubscriptionManager _subscriptionManager = new(startingId: ProviderSubscriptionRanges.AlpacaStart);

    // Content-based trade deduplication: sliding window keyed on (symbol, price, size, timestamp).
    // Alpaca's WebSocket is known to re-deliver identical trade messages during reconnections and
    // under high load. Using a bounded HashSet avoids double-counting duplicates without the
    // overhead of the sequence-based PersistentDedupLedger (which won't catch trades that arrive
    // with the same content but different assigned sequence numbers).
    private readonly HashSet<(string symbol, decimal price, long size, DateTimeOffset ts)> _recentTrades = new();
    private readonly Queue<(string symbol, decimal price, long size, DateTimeOffset ts)> _recentTradeOrder = new();
    private const int MaxDedupWindowSize = 2048;

    // Cached serializer options to avoid allocations in hot path
    private static readonly JsonSerializerOptions s_serializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public AlpacaMarketDataClient(TradeDataCollector tradeCollector, QuoteCollector quoteCollector, AlpacaOptions opt)
    {
        _tradeCollector = tradeCollector ?? throw new ArgumentNullException(nameof(tradeCollector));
        _quoteCollector = quoteCollector ?? throw new ArgumentNullException(nameof(quoteCollector));
        _opt = opt ?? throw new ArgumentNullException(nameof(opt));
        if (string.IsNullOrWhiteSpace(_opt.KeyId) || string.IsNullOrWhiteSpace(_opt.SecretKey))
            throw new ArgumentException("Alpaca KeyId/SecretKey required.");

        // Use centralized connection manager with default resilience configuration
        _connectionManager = new WebSocketConnectionManager(
            providerName: "Alpaca",
            config: WebSocketConnectionConfig.Default,
            logger: _log);

        // Set up reconnection handler
        _connectionManager.ConnectionLost += OnConnectionLostAsync;
    }

    public bool IsEnabled => true;

    #region IProviderMetadata

    /// <inheritdoc/>
    public string ProviderId => "alpaca";

    /// <inheritdoc/>
    public string ProviderDisplayName => "Alpaca Markets Streaming";

    /// <inheritdoc/>
    public string ProviderDescription => "Real-time trades and quotes via Alpaca WebSocket API";

    /// <inheritdoc/>
    public int ProviderPriority => 10;

    /// <inheritdoc/>
    public ProviderCapabilities ProviderCapabilities { get; } = ProviderCapabilities.Streaming(
        trades: true,
        quotes: true,
        depth: false) with
    {
        SupportedMarkets = new[] { "US" },
        MaxRequestsPerWindow = 200,
        RateLimitWindow = TimeSpan.FromMinutes(1),
        MinRequestDelay = TimeSpan.FromMilliseconds(300)
    };

    /// <inheritdoc/>
    public ProviderCredentialField[] ProviderCredentialFields => new[]
    {
        new ProviderCredentialField("KeyId", "ALPACA__KEYID", "API Key ID", true),
        new ProviderCredentialField("SecretKey", "ALPACA__SECRETKEY", "API Secret Key", true)
    };

    /// <inheritdoc/>
    public string[] ProviderNotes => new[]
    {
        "Alpaca requires API credentials (free account available).",
        "Rate limit: 200 requests/minute.",
        "IEX feed is free; SIP feed requires subscription."
    };

    #endregion

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_connectionManager.IsConnected)
            return;

        var host = _opt.UseSandbox ? "stream.data.sandbox.alpaca.markets" : "stream.data.alpaca.markets";
        _wsUri = new Uri($"wss://{host}/v2/{_opt.Feed}");

        _log.Information("Connecting to Alpaca WebSocket at {Uri} (Sandbox: {UseSandbox})", _wsUri, _opt.UseSandbox);

        await _connectionManager.ConnectAsync(_wsUri, configureSocket: null, ct).ConfigureAwait(false);

        // Authenticate via message (must be within ~10 seconds of connection)
        var authMsg = JsonSerializer.Serialize(new { action = "auth", key = _opt.KeyId, secret = _opt.SecretKey });
        await _connectionManager.SendAsync(authMsg, ct).ConfigureAwait(false);
        _log.Debug("Authentication message sent to Alpaca");

        // Start receive loop with message handler
        _connectionManager.StartReceiveLoop(HandleMessageAsync, ct);
    }

    /// <summary>
    /// Handles automatic reconnection when connection is lost.
    /// Delegates to WebSocketConnectionManager for gated reconnection.
    /// </summary>
    private async Task OnConnectionLostAsync()
    {
        if (_wsUri == null)
            return;

        var success = await _connectionManager.TryReconnectAsync(
            _wsUri,
            configureSocket: null,
            onReconnected: async () =>
            {
                // Re-authenticate after reconnection
                var authMsg = JsonSerializer.Serialize(new { action = "auth", key = _opt.KeyId, secret = _opt.SecretKey });
                await _connectionManager.SendAsync(authMsg, CancellationToken.None).ConfigureAwait(false);

                // Restart receive loop
                _connectionManager.StartReceiveLoop(HandleMessageAsync, CancellationToken.None);

                // Resubscribe to all active subscriptions
                await TrySendSubscribeAsync();
                _log.Information("Successfully reconnected and resubscribed to Alpaca WebSocket");
            },
            ct: CancellationToken.None).ConfigureAwait(false);

        if (!success)
        {
            _log.Error("Failed to reconnect to Alpaca WebSocket. Manual intervention may be required.");
        }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await _connectionManager.DisconnectAsync(ct).ConfigureAwait(false);
    }

    public int SubscribeTrades(SymbolConfig cfg)
    {
        if (cfg is null)
            throw new ArgumentNullException(nameof(cfg));
        var id = _subscriptionManager.Subscribe(cfg.Symbol, "trades");
        if (id == -1)
            return -1;

        SendSubscribeWithLoggingAsync("SubscribeTrades", cfg.Symbol)
            .ObserveException(_log, $"Alpaca subscribe trades for {cfg.Symbol}");
        return id;
    }

    public void UnsubscribeTrades(int subscriptionId)
    {
        var subscription = _subscriptionManager.Unsubscribe(subscriptionId);
        if (subscription != null)
        {
            SendSubscribeWithLoggingAsync("UnsubscribeTrades", subscription.Symbol)
                .ObserveException(_log, $"Alpaca unsubscribe trades for {subscription.Symbol}");
        }
    }

    public int SubscribeMarketDepth(SymbolConfig cfg)
    {
        // Not supported for stocks: Alpaca provides quotes, not full L2 depth updates.
        // If you later add QuoteCollector -> L2Snapshot mapping, wire it here.
        if (!_opt.SubscribeQuotes)
            return -1;

        if (cfg is null)
            throw new ArgumentNullException(nameof(cfg));
        var id = _subscriptionManager.Subscribe(cfg.Symbol, "quotes");
        if (id == -1)
            return -1;

        SendSubscribeWithLoggingAsync("SubscribeMarketDepth", cfg.Symbol)
            .ObserveException(_log, $"Alpaca subscribe depth for {cfg.Symbol}");
        return id;
    }

    public void UnsubscribeMarketDepth(int subscriptionId)
    {
        var subscription = _subscriptionManager.Unsubscribe(subscriptionId);
        if (subscription != null)
        {
            SendSubscribeWithLoggingAsync("UnsubscribeMarketDepth", subscription.Symbol)
                .ObserveException(_log, $"Alpaca unsubscribe depth for {subscription.Symbol}");
        }
    }

    private async Task SendSubscribeWithLoggingAsync(string operation, string symbol, CancellationToken ct = default)
    {
        try
        {
            await TrySendSubscribeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Fire-and-forget subscription update failed during {Operation} for {Symbol}. " +
                "The subscription state may be inconsistent.", operation, symbol);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _connectionManager.ConnectionLost -= OnConnectionLostAsync;
        await _connectionManager.DisposeAsync().ConfigureAwait(false);
        _subscriptionManager.Dispose();
    }

    private async Task TrySendSubscribeAsync(CancellationToken ct = default)
    {
        try
        {
            if (!_connectionManager.IsConnected)
                return;

            var trades = _subscriptionManager.GetSymbolsByKind("trades");
            var quotes = _subscriptionManager.GetSymbolsByKind("quotes");

            var msg = new Dictionary<string, object?>
            {
                ["action"] = "subscribe",
                ["trades"] = trades.Length == 0 ? null : trades
            };

            if (_opt.SubscribeQuotes && quotes.Length > 0)
                msg["quotes"] = quotes;

            var json = JsonSerializer.Serialize(msg, s_serializerOptions);
            await _connectionManager.SendAsync(json, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to send subscription update to Alpaca WebSocket. " +
                "This may indicate a connection issue. Check network connectivity and Alpaca service status.");
        }
    }

    /// <summary>
    /// Handles incoming WebSocket messages. Called by WebSocketConnectionManager.
    /// </summary>
    private Task HandleMessageAsync(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Task.CompletedTask;

        // Alpaca sends arrays of objects: [{"T":"success",...}, {"T":"t",...}]
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                    HandleMessage(el);
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                HandleMessage(doc.RootElement);
            }
        }
        catch (JsonException ex)
        {
            _log.Warning(ex, "Failed to parse Alpaca WebSocket message. Raw JSON: {RawJson}. " +
                "This may indicate a protocol change or malformed message.",
                json.Length > 500 ? json[..500] + "..." : json);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Unexpected error processing Alpaca WebSocket message");
        }

        return Task.CompletedTask;
    }

    private void HandleMessage(JsonElement el)
    {
        // Trades: "T":"t" (per Alpaca docs)
        if (!el.TryGetProperty("T", out var tProp))
            return;
        var t = tProp.GetString();
        if (t == "t")
        {
            var sym = el.TryGetProperty("S", out var sProp) ? sProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(sym))
                return;

            var price = el.TryGetProperty("p", out var pProp) ? pProp.GetDecimal() : 0m;
            // Use GetInt64 to avoid truncation on large block trades (> int.MaxValue shares).
            var size = el.TryGetProperty("s", out var szProp) ? szProp.GetInt64() : 0L;
            var ts = el.TryGetProperty("t", out var tsProp) ? tsProp.GetString() : null;
            var venue = el.TryGetProperty("x", out var xProp) ? xProp.GetString() : null;
            var tradeId = el.TryGetProperty("i", out var iProp) ? iProp.GetInt64() : 0;

            // Reject events with unparseable timestamps rather than recording the wrong time.
            // Substituting UtcNow silently corrupts time-series integrity.
            if (!DateTimeOffset.TryParse(ts, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dto))
            {
                _log.Warning("Alpaca trade message for {Symbol} has unparseable timestamp {Timestamp}, skipping", sym, ts);
                return;
            }

            // Content-based deduplication: Alpaca's WebSocket is known to re-deliver identical
            // trade messages during reconnections. Deduplicate on (symbol, price, size, exchange
            // timestamp) using a bounded sliding window so we don't double-count phantom trades.
            var dedupKey = (sym!, price, size, dto);
            lock (_recentTrades)
            {
                if (!_recentTrades.Add(dedupKey))
                {
                    _log.Debug("Alpaca duplicate trade suppressed: {Symbol} @ {Price} x {Size} at {Timestamp}",
                        sym, price, size, dto);
                    return;
                }
                _recentTradeOrder.Enqueue(dedupKey);
                // Evict oldest entries when the window is full to bound memory usage.
                while (_recentTradeOrder.Count > MaxDedupWindowSize)
                    _recentTrades.Remove(_recentTradeOrder.Dequeue());
            }

            var update = new MarketTradeUpdate(
                Timestamp: dto,
                Symbol: sym!,
                Price: price,
                Size: size,
                Aggressor: AggressorSide.Unknown,
                SequenceNumber: tradeId <= 0 ? 0L : tradeId,
                StreamId: "ALPACA",
                Venue: venue ?? "ALPACA",
                RawConditions: el.TryGetProperty("c", out var cProp) && cProp.ValueKind == JsonValueKind.Array
                    ? cProp.EnumerateArray()
                            .Select(c => c.GetString())
                            .Where(c => c is not null)
                            .Select(c => c!)
                            .ToArray()
                    : null
            );

            _tradeCollector.OnTrade(update);
        }

        // Handle Alpaca quotes ("T":"q") - BBO updates
        // Alpaca quote fields: S=symbol, bp=bidPrice, bs=bidSize, ap=askPrice, as=askSize, t=timestamp
        if (t == "q")
        {
            var sym = el.TryGetProperty("S", out var sProp) ? sProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(sym))
                return;

            var bidPrice = el.TryGetProperty("bp", out var bpProp) ? bpProp.GetDecimal() : 0m;
            var bidSize = el.TryGetProperty("bs", out var bsProp) ? bsProp.GetInt64() : 0L;
            var askPrice = el.TryGetProperty("ap", out var apProp) ? apProp.GetDecimal() : 0m;
            var askSize = el.TryGetProperty("as", out var asProp) ? asProp.GetInt64() : 0L;
            var ts = el.TryGetProperty("t", out var tsProp) ? tsProp.GetString() : null;

            // Reject events with unparseable timestamps rather than recording the wrong time.
            if (!DateTimeOffset.TryParse(ts, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dto))
            {
                _log.Warning("Alpaca quote message for {Symbol} has unparseable timestamp {Timestamp}, skipping", sym, ts);
                return;
            }

            var quoteUpdate = new MarketQuoteUpdate(
                Timestamp: dto,
                Symbol: sym!,
                BidPrice: bidPrice,
                BidSize: bidSize,
                AskPrice: askPrice,
                AskSize: askSize,
                SequenceNumber: null,
                StreamId: "ALPACA",
                Venue: "ALPACA"
            );

            _quoteCollector.OnQuote(quoteUpdate);
        }
    }
}

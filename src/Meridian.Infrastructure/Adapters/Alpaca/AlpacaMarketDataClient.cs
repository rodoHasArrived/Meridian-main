using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Threading;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Resilience;
using Meridian.Infrastructure.Shared;
using AlpacaOptions = Meridian.Core.Config.AlpacaOptions;

namespace Meridian.Infrastructure.Adapters.Alpaca;

/// <summary>
/// Alpaca equities market-data stream adapter (WebSocket) that implements the IMarketDataClient abstraction.
/// Extends <see cref="WebSocketProviderBase"/>, which centralises connection lifecycle,
/// resilience (retry + circuit breaker), heartbeat monitoring, and automatic reconnection.
/// ~80 LOC of WebSocket boilerplate removed compared to the previous direct implementation.
///
/// Current support:
/// - Trades: YES (streams "t" messages and forwards to TradeDataCollector)
/// - Depth (L2): NO (Alpaca stock stream provides quotes/BBO, not full L2 updates; method returns -1)
///
/// Notes:
/// - Alpaca typically limits to 1 active stream connection per user per endpoint.
/// - Authentication is performed by sending an "auth" message immediately after connect.
///   The auth response arrives in the main receive loop (no separate handshake step).
/// </summary>
[DataSource("alpaca", "Alpaca Markets", Infrastructure.DataSources.DataSourceType.Realtime, DataSourceCategory.Broker,
    Priority = 10, Description = "WebSocket streaming from Alpaca Markets")]
[ImplementsAdr("ADR-001", "Alpaca streaming data provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
[RequiresCredential("ALPACA_KEY_ID",
    EnvironmentVariables = new[] { "ALPACA_KEY_ID", "ALPACA__KEYID" },
    DisplayName = "API Key ID",
    Description = "Alpaca API key ID from https://app.alpaca.markets/brokerage/papers")]
[RequiresCredential("ALPACA_SECRET_KEY",
    EnvironmentVariables = new[] { "ALPACA_SECRET_KEY", "ALPACA__SECRETKEY" },
    DisplayName = "API Secret Key",
    Description = "Alpaca API secret key from https://app.alpaca.markets/brokerage/papers")]
public class AlpacaMarketDataClient : WebSocketProviderBase, IAlpacaAssetStream
{
    private readonly TradeDataCollector _tradeCollector;
    private readonly QuoteCollector _quoteCollector;
    protected readonly AlpacaOptions Options;

    // Content-based trade deduplication: sliding window keyed on (symbol, price, size, timestamp).
    // Alpaca's WebSocket is known to re-deliver identical trade messages during reconnections and
    // under high load. Using a bounded HashSet avoids double-counting duplicates without the
    // overhead of the sequence-based PersistentDedupLedger (which won't catch trades that arrive
    // with the same content but different assigned sequence numbers).
    private readonly HashSet<(string symbol, decimal price, long size, DateTimeOffset ts)> _recentTrades = new();
    private readonly Queue<(string symbol, decimal price, long size, DateTimeOffset ts)> _recentTradeOrder = new();
    private const int MaxDedupWindowSize = 2048;

    public AlpacaMarketDataClient(TradeDataCollector tradeCollector, QuoteCollector quoteCollector, AlpacaOptions opt)
        : base(
            providerName: "Alpaca",
            config: WebSocketConnectionConfig.Default,
            subscriptionStartId: ProviderSubscriptionRanges.AlpacaStart)
    {
        _tradeCollector = tradeCollector ?? throw new ArgumentNullException(nameof(tradeCollector));
        _quoteCollector = quoteCollector ?? throw new ArgumentNullException(nameof(quoteCollector));
        Options = opt ?? throw new ArgumentNullException(nameof(opt));
        if (string.IsNullOrWhiteSpace(Options.KeyId) || string.IsNullOrWhiteSpace(Options.SecretKey))
            throw new ArgumentException("Alpaca KeyId/SecretKey required.");
    }


    /// <inheritdoc/>
    public override bool IsEnabled => true;

    /// <summary>The asset class served by this independent WebSocket adapter.</summary>
    public virtual MarketDataAssetClass AssetClass => MarketDataAssetClass.Equities;

    /// <summary>Instrument types this stream is permitted to publish.</summary>
    public virtual IReadOnlyList<Meridian.Contracts.Domain.Enums.InstrumentType> SupportedInstrumentTypes => [Meridian.Contracts.Domain.Enums.InstrumentType.Equity];

    /// <summary>Whether this stream has a configured entitlement usable for subscription.</summary>
    public virtual bool IsSubscriptionAvailable => IsConfiguredFeed(Options.EquitiesFeed);

    /// <inheritdoc/>
    public override string ProviderId => "alpaca";

    /// <inheritdoc/>
    public override string ProviderDisplayName => "Alpaca Markets Streaming";

    /// <inheritdoc/>
    public override string ProviderDescription => "Real-time trades and quotes via Alpaca WebSocket API";

    /// <inheritdoc/>
    public override int ProviderPriority => 10;

    /// <inheritdoc/>
    public override ProviderCapabilities ProviderCapabilities { get; } = ProviderCapabilities.Streaming(
        trades: true,
        quotes: true,
        depth: false) with
    {
        SupportedMarkets = new[] { "US" },
        MaxRequestsPerWindow = 200,
        RateLimitWindow = TimeSpan.FromMinutes(1),
        MinRequestDelay = TimeSpan.FromMilliseconds(300)
    };

    /// <summary>
    /// Returns diagnostics for every distinct Alpaca asset-class stream. Equities is backed by
    /// this live adapter; options, crypto, and news remain separately visible rather than being
    /// incorrectly reported as covered by the equities socket.
    /// </summary>
    public override WebSocketConnectionDiagnostics GetConnectionDiagnosticsSnapshot()
    {
        var snapshot = base.GetConnectionDiagnosticsSnapshot();
        return snapshot with { Streams = AlpacaStreamProfiles.Create(Options, snapshot, AssetClass) };
    }

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



    /// <inheritdoc/>
    protected override Uri BuildWebSocketUri()
    {
        var host = Options.UseSandbox ? "stream.data.sandbox.alpaca.markets" : "stream.data.alpaca.markets";
        var uri = new Uri($"wss://{host}/v2/{Options.Feed}");
        Log.Information("Connecting to Alpaca {AssetClass} WebSocket at {Uri} (Sandbox: {UseSandbox})", AssetClass, uri, Options.UseSandbox);
        return uri;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Alpaca does not use a pre-loop handshake. Authentication is performed by sending a
    /// single "auth" message; the auth success/failure response arrives in
    /// <see cref="HandleMessageAsync"/> as a status message (type "success" or "error").
    /// </remarks>
    protected override Task AuthenticateAsync(CancellationToken ct)
    {
        var authMsg = BuildAuthenticationMessage(Options);
        Log.Debug("Sending authentication message to Alpaca");
        return SendAsync(authMsg, ct);
    }

    /// <inheritdoc/>
    protected override Task HandleMessageAsync(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return Task.CompletedTask;

        RecordActivity();

        // Alpaca sends arrays of objects: [{"T":"success",...}, {"T":"t",...}]
        try
        {
            using var doc = JsonDocument.Parse(message);
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
            Log.Warning(ex, "Failed to parse Alpaca WebSocket message. Raw JSON: {RawJson}. " +
                "This may indicate a protocol change or malformed message.",
                message.Length > 500 ? message[..500] + "..." : message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error processing Alpaca WebSocket message");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    protected override async Task ResubscribeAsync(CancellationToken ct)
    {
        try
        {
            if (!Connected)
                return;

            var json = BuildSubscriptionPayload();
            await SendAsync(json, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to send subscription update to Alpaca WebSocket. " +
                "This may indicate a connection issue. Check network connectivity and Alpaca service status.");
        }
    }



    /// <inheritdoc/>
    public override int SubscribeTrades(SymbolConfig cfg)
    {
        if (!IsSubscriptionAvailable)
            return -1;
        if (cfg is null)
            throw new ArgumentNullException(nameof(cfg));
        var id = Subscriptions.Subscribe(cfg.Symbol, "trades");
        if (id == -1)
            return -1;

        ResubscribeAsync(CancellationToken.None)
            .ObserveException(Log, $"Alpaca subscribe trades for {cfg.Symbol}");
        return id;
    }

    /// <inheritdoc/>
    public override void UnsubscribeTrades(int subscriptionId)
    {
        var subscription = Subscriptions.Unsubscribe(subscriptionId);
        if (subscription != null)
        {
            ResubscribeAsync(CancellationToken.None)
                .ObserveException(Log, $"Alpaca unsubscribe trades for {subscription.Symbol}");
        }
    }

    /// <inheritdoc/>
    public override int SubscribeMarketDepth(SymbolConfig cfg)
    {
        if (!IsSubscriptionAvailable)
            return -1;
        // Not supported for stocks: Alpaca provides quotes, not full L2 depth updates.
        // If you later add QuoteCollector -> L2Snapshot mapping, wire it here.
        if (!Options.SubscribeQuotes)
            return -1;

        if (cfg is null)
            throw new ArgumentNullException(nameof(cfg));
        var id = Subscriptions.Subscribe(cfg.Symbol, "quotes");
        if (id == -1)
            return -1;

        ResubscribeAsync(CancellationToken.None)
            .ObserveException(Log, $"Alpaca subscribe depth for {cfg.Symbol}");
        return id;
    }

    /// <inheritdoc/>
    public override void UnsubscribeMarketDepth(int subscriptionId)
    {
        var subscription = Subscriptions.Unsubscribe(subscriptionId);
        if (subscription != null)
        {
            ResubscribeAsync(CancellationToken.None)
                .ObserveException(Log, $"Alpaca unsubscribe depth for {subscription.Symbol}");
        }
    }

    protected virtual string BuildSubscriptionPayload() => BuildSubscriptionMessage(
        Options.SubscribeQuotes, Subscriptions.GetSymbolsByKind("trades"), Subscriptions.GetSymbolsByKind("quotes"));

    protected int Subscribe(SymbolConfig cfg, string kind)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        if (!IsSubscriptionAvailable)
            return -1;
        var id = Subscriptions.Subscribe(cfg.Symbol, kind);
        if (id != -1)
            ResubscribeAsync(CancellationToken.None).ObserveException(Log, $"Alpaca subscribe {kind} for {cfg.Symbol}");
        return id;
    }

    protected void Unsubscribe(int subscriptionId)
    {
        if (Subscriptions.Unsubscribe(subscriptionId) is not null)
            ResubscribeAsync(CancellationToken.None).ObserveException(Log, "Alpaca unsubscribe");
    }

    protected static bool IsConfiguredFeed(string? feed) => !string.IsNullOrWhiteSpace(feed) && !string.Equals(feed, "none", StringComparison.OrdinalIgnoreCase) && !string.Equals(feed, "disabled", StringComparison.OrdinalIgnoreCase);

    internal static string BuildAuthenticationMessage(AlpacaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return BuildJsonMessage(writer =>
        {
            writer.WriteString("action", "auth");
            writer.WriteString("key", options.KeyId);
            writer.WriteString("secret", options.SecretKey);
        });
    }

    internal static string BuildSubscriptionMessage(
        bool subscribeQuotes,
        IReadOnlyList<string> trades,
        IReadOnlyList<string> quotes)
    {
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(quotes);

        return BuildJsonMessage(writer =>
        {
            writer.WriteString("action", "subscribe");

            if (trades.Count > 0)
            {
                writer.WritePropertyName("trades");
                writer.WriteStartArray();
                foreach (var trade in trades)
                    writer.WriteStringValue(trade);
                writer.WriteEndArray();
            }

            if (subscribeQuotes && quotes.Count > 0)
            {
                writer.WritePropertyName("quotes");
                writer.WriteStartArray();
                foreach (var quote in quotes)
                    writer.WriteStringValue(quote);
                writer.WriteEndArray();
            }
        });
    }

    internal static string BuildNewsSubscriptionMessage(IReadOnlyList<string> symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        return BuildJsonMessage(writer =>
        {
            writer.WriteString("action", "subscribe");
            if (symbols.Count > 0)
            { writer.WritePropertyName("news"); writer.WriteStartArray(); foreach (var symbol in symbols) writer.WriteStringValue(symbol); writer.WriteEndArray(); }
        });
    }

    private static string BuildJsonMessage(Action<Utf8JsonWriter> writePayload)
    {
        ArgumentNullException.ThrowIfNull(writePayload);

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writePayload(writer);
        writer.WriteEndObject();
        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }



    protected virtual void HandleMessage(JsonElement el)
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
                Log.Warning("Alpaca trade message for {Symbol} has unparseable timestamp {Timestamp}, skipping", sym, ts);
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
                    Log.Debug("Alpaca duplicate trade suppressed: {Symbol} @ {Price} x {Size} at {Timestamp}",
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

            var issues = ProviderDataQualityValidator.ValidateTrade(ProviderId, update);
            if (HasBlockingIssues(issues))
            {
                LogDataQualityRejection("trade", sym!, issues);
                return;
            }

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
                Log.Warning("Alpaca quote message for {Symbol} has unparseable timestamp {Timestamp}, skipping", sym, ts);
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

            var issues = ProviderDataQualityValidator.ValidateQuote(ProviderId, quoteUpdate);
            if (HasBlockingIssues(issues))
            {
                LogDataQualityRejection("quote", sym!, issues);
                return;
            }

            _quoteCollector.OnQuote(quoteUpdate);
        }
    }

    private static bool HasBlockingIssues(IReadOnlyList<ProviderDataQualityIssue> issues)
        => issues.Any(issue => issue.Severity == ProviderDataQualitySeverity.Error);

    private void LogDataQualityRejection(
        string messageType,
        string symbol,
        IReadOnlyList<ProviderDataQualityIssue> issues)
    {
        Log.Warning(
            "Rejected Alpaca {MessageType} for {Symbol} due to provider data quality issues: {Issues}",
            messageType,
            symbol,
            string.Join("; ", issues.Select(issue => $"{issue.FieldPath}: {issue.Message}")));
    }

}

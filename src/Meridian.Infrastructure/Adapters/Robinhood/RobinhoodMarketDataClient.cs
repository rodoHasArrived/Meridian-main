// ✅ ADR-001: IMarketDataClient contract for streaming data providers
// ✅ ADR-004: CancellationToken on all async methods
// ✅ ADR-005: Attribute-based provider discovery via [DataSource]
// ✅ ADR-010: HTTP client via IHttpClientFactory
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Core.Exceptions;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Meridian.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Adapters.Robinhood;

/// <summary>
/// Polling-based market data client using the Robinhood unofficial API.
/// Provides real-time BBO quotes for US equities via polling the quotes endpoint.
///
/// <para>
/// <b>Important:</b> Robinhood does not provide a public WebSocket API.
/// This client uses REST polling (default interval: 2 seconds) as the closest
/// available substitute for streaming quotes.
/// </para>
///
/// <para>
/// Authentication: set <c>ROBINHOOD_ACCESS_TOKEN</c> environment variable.
/// Trade data and market depth are not available via the Robinhood unofficial API.
/// </para>
/// </summary>
[DataSource("robinhood-live", "Robinhood Live Quotes", DataSourceType.Realtime, DataSourceCategory.Free,
    Priority = 35, Description = "Polling-based BBO quotes via Robinhood unofficial API (requires personal access token)")]
[ImplementsAdr("ADR-001", "Robinhood streaming market data client implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
[ImplementsAdr("ADR-010", "Uses IHttpClientFactory for HTTP connections")]
public sealed class RobinhoodMarketDataClient : PollingProviderBase, IMarketDataClient, IProviderConnectionDiagnosticsSource
{
    private const string QuotesEndpoint = "https://api.robinhood.com/marketdata/quotes/";
    private const string EnvAccessToken = "ROBINHOOD_ACCESS_TOKEN";
    private const int MaxSymbolsPerBatch = 50;
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly QuoteCollector _quoteCollector;
    private readonly ILogger<RobinhoodMarketDataClient> _logger;
    private readonly string? _accessToken;

    private readonly ConcurrentDictionary<int, string> _subscriptions = new();
    private int _nextSubId;
    private long _dataQualityRejections;

    public RobinhoodMarketDataClient(
        IHttpClientFactory httpClientFactory,
        QuoteCollector quoteCollector,
        ILogger<RobinhoodMarketDataClient> logger,
        string? accessToken = null)
        : base("Robinhood Live Quotes", logger, DefaultPollInterval)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _quoteCollector = quoteCollector ?? throw new ArgumentNullException(nameof(quoteCollector));
        _logger = logger;
        _accessToken = accessToken ?? Environment.GetEnvironmentVariable(EnvAccessToken);
    }

    // ── IProviderMetadata ─────────────────────────────────────────────────

    string IProviderMetadata.ProviderId => "robinhood-live";
    string IProviderMetadata.ProviderDisplayName => "Robinhood Live Quotes";
    string IProviderMetadata.ProviderDescription => "Polling-based BBO quotes via Robinhood unofficial API";
    int IProviderMetadata.ProviderPriority => 35;

    ProviderCapabilities IProviderMetadata.ProviderCapabilities => ProviderCapabilities.Streaming(
        trades: false,
        quotes: true,
        depth: false);

    ProviderCredentialField[] IProviderMetadata.ProviderCredentialFields =>
    [
        new ProviderCredentialField("AccessToken", EnvAccessToken, "Robinhood Access Token", Required: true)
    ];

    string[] IProviderMetadata.ProviderWarnings =>
    [
        "Uses the unofficial Robinhood API — no SLA, subject to change without notice.",
        "Quote data is provided via polling (2-second interval), not true streaming.",
        "Trade tick data and market depth are not available."
    ];

    // ── IMarketDataClient ─────────────────────────────────────────────────

    /// <inheritdoc />
    public override bool IsEnabled => !string.IsNullOrWhiteSpace(_accessToken);

    /// <summary>Error recorded when a connect is attempted without a token (see base class).</summary>
    protected override string NotEnabledError => "Robinhood access token is missing.";

    /// <summary>Exception thrown by the base <c>ConnectAsync</c> when no token is configured.</summary>
    protected override Exception CreateNotEnabledException()
        => new ConnectionException(
            "ROBINHOOD_ACCESS_TOKEN environment variable is not set. " +
            "Set it to your Robinhood personal access token before connecting.");

    /// <summary>Number of active quote subscriptions, surfaced in shared diagnostics.</summary>
    protected override int ActiveSubscriptionCount => _subscriptions.Count;

    /// <inheritdoc />
    public int SubscribeTrades(SymbolConfig cfg)
    {
        // Robinhood unofficial API does not expose tick-by-tick trades.
        _logger.LogDebug(
            "Robinhood does not support trade subscriptions; ignoring SubscribeTrades for {Symbol}", cfg.Symbol);
        return -1;
    }

    /// <inheritdoc />
    public void UnsubscribeTrades(int subscriptionId) { }

    /// <inheritdoc />
    public int SubscribeMarketDepth(SymbolConfig cfg)
    {
        // Robinhood unofficial API does not expose order book depth.
        _logger.LogDebug(
            "Robinhood does not support depth subscriptions; ignoring SubscribeMarketDepth for {Symbol}", cfg.Symbol);
        return -1;
    }

    /// <inheritdoc />
    public void UnsubscribeMarketDepth(int subscriptionId) { }

    /// <summary>Subscribe a symbol to receive polling-based BBO quote updates.</summary>
    public int SubscribeQuotes(SymbolConfig cfg)
    {
        ThrowIfDisposed();
        var id = Interlocked.Increment(ref _nextSubId);
        _subscriptions.TryAdd(id, cfg.Symbol.ToUpperInvariant());
        _logger.LogDebug("Robinhood subscribed quotes for {Symbol} (subId={SubId})", cfg.Symbol, id);
        return id;
    }

    /// <summary>Unsubscribe from polling-based BBO quote updates.</summary>
    public void UnsubscribeQuotes(int subscriptionId)
    {
        if (_subscriptions.TryRemove(subscriptionId, out var symbol))
            _logger.LogDebug("Robinhood unsubscribed quotes for {Symbol} (subId={SubId})", symbol, subscriptionId);
    }

    /// <summary>
    /// Returns a redacted diagnostics snapshot for health surfaces and tests.
    /// </summary>
    public RobinhoodMarketDataDiagnostics GetDiagnosticsSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        var lastActivity = LastMessageReceivedAt ?? LastSuccessfulApiCallAt ?? ConnectedAt;

        return new RobinhoodMarketDataDiagnostics(
            ProviderId: "robinhood-live",
            LifecycleState: LifecycleState,
            IsConnected: Connected,
            ActiveSubscriptionCount: _subscriptions.Count,
            LastConnectedAt: ConnectedAt,
            LastDisconnectedAt: DisconnectedAt,
            LastPollAttemptAt: LastPollAttemptAt,
            LastSuccessfulApiCallAt: LastSuccessfulApiCallAt,
            LastMessageReceivedAt: LastMessageReceivedAt,
            LastError: LastError,
            ConsecutivePollFailures: ConsecutivePollFailures,
            DataQualityRejections: Interlocked.Read(ref _dataQualityRejections),
            ConnectionAge: Connected && ConnectedAt is { } connectedAt ? now - connectedAt : null,
            IdleDuration: lastActivity is { } activityAt ? now - activityAt : null);
    }

    // ── Polling ────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs one poll cycle: batches the subscribed symbols (deduplicated) and polls the
    /// Robinhood quotes endpoint per batch. Returns <see langword="true"/> only if every batch
    /// succeeded, so the base class can drive degraded-state backoff.
    /// </summary>
    protected override async Task<bool> PollOnceAsync(CancellationToken ct)
    {
        var seenSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var batch = new List<string>(MaxSymbolsPerBatch);
        var pollSucceeded = true;

        foreach (var symbol in _subscriptions.Values)
        {
            ct.ThrowIfCancellationRequested();

            if (!seenSymbols.Add(symbol))
            {
                continue;
            }

            batch.Add(symbol);
            if (batch.Count < MaxSymbolsPerBatch)
            {
                continue;
            }

            pollSucceeded &= await PollBatchAsync(batch, ct).ConfigureAwait(false);
            batch.Clear();
        }

        if (batch.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            pollSucceeded &= await PollBatchAsync(batch, ct).ConfigureAwait(false);
        }

        return pollSucceeded;
    }

    private async Task<bool> PollBatchAsync(IReadOnlyList<string> symbols, CancellationToken ct)
    {
        try
        {
            RecordPollAttempt();
            using var client = CreateHttpClient();
            var symbolList = string.Join(",", symbols);
            var url = $"{QuotesEndpoint}?symbols={Uri.EscapeDataString(symbolList)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuthHeader(request);

            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                RecordTerminalPollFailure(new UnauthorizedAccessException(
                    "Provider returned Unauthorized while polling Robinhood quotes. Refresh or replace the stored access token."));
                _logger.LogWarning("Robinhood quote polling: 401 Unauthorized — access token may have expired");
                return false;
            }

            if (!response.IsSuccessStatusCode)
            {
                RecordError($"Provider returned HTTP {(int)response.StatusCode} while polling Robinhood quotes.");
                _logger.LogWarning(
                    "Robinhood quote polling: HTTP {StatusCode} for batch {Symbols}",
                    response.StatusCode, string.Join(",", symbols));
                return false;
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var result = await JsonSerializer.DeserializeAsync(
                stream, RobinhoodQuoteSerializerContext.Default.RobinhoodQuoteResponse, ct)
                .ConfigureAwait(false);

            if (result?.Results is null)
            {
                RecordSuccessfulApiCall();
                ClearError();
                return true;
            }

            var timestamp = DateTimeOffset.UtcNow;
            var publishedAny = false;
            foreach (var q in result.Results)
            {
                if (string.IsNullOrWhiteSpace(q.Symbol))
                    continue;

                if (!decimal.TryParse(q.BidPrice, System.Globalization.NumberStyles.AllowDecimalPoint,
                        System.Globalization.CultureInfo.InvariantCulture, out var bid))
                    bid = 0m;
                if (!decimal.TryParse(q.AskPrice, System.Globalization.NumberStyles.AllowDecimalPoint,
                        System.Globalization.CultureInfo.InvariantCulture, out var ask))
                    ask = 0m;

                var update = new MarketQuoteUpdate(
                    Timestamp: q.UpdatedAt ?? timestamp,
                    Symbol: q.Symbol,
                    BidPrice: bid,
                    BidSize: q.BidSize ?? 0L,
                    AskPrice: ask,
                    AskSize: q.AskSize ?? 0L,
                    StreamId: "ROBINHOOD");

                var issues = ProviderDataQualityValidator.ValidateQuote("robinhood-live", update);
                if (issues.Any(issue => issue.Severity == ProviderDataQualitySeverity.Error))
                {
                    Interlocked.Increment(ref _dataQualityRejections);
                    _logger.LogWarning(
                        "Rejected Robinhood quote for {Symbol} due to provider data quality issues: {Issues}",
                        q.Symbol,
                        string.Join("; ", issues.Select(issue => $"{issue.FieldPath}: {issue.Message}")));
                    continue;
                }

                _quoteCollector.OnQuote(update);
                publishedAny = true;
            }

            RecordSuccessfulApiCall();
            if (publishedAny)
                RecordMessageReceived();
            ClearError();
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            RecordError(ex.Message);
            _logger.LogError(ex, "Robinhood quote poll error for batch {Symbols}", string.Join(",", symbols));
            return false;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private HttpClient CreateHttpClient()
        => _httpClientFactory.CreateClient(HttpClientNames.RobinhoodMarketData);

    /// <summary>
    /// Applies the bearer token to the specific request rather than mutating the
    /// factory-shared <see cref="HttpClient.DefaultRequestHeaders"/>, which is unsafe if the
    /// client is ever pooled or cached. Mirrors <c>NYSEDataSource.AddAuthHeader</c>.
    /// </summary>
    private void AddAuthHeader(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(_accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        }
    }

    // ── JSON DTOs (ADR-014: source generators) ────────────────────────────

    internal sealed class RobinhoodQuoteResponse
    {
        [JsonPropertyName("results")]
        public RobinhoodQuote[]? Results { get; set; }
    }

    internal sealed class RobinhoodQuote
    {
        [JsonPropertyName("symbol")]
        public string? Symbol { get; set; }

        [JsonPropertyName("bid_price")]
        public string? BidPrice { get; set; }

        [JsonPropertyName("bid_size")]
        public long? BidSize { get; set; }

        [JsonPropertyName("ask_price")]
        public string? AskPrice { get; set; }

        [JsonPropertyName("ask_size")]
        public long? AskSize { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset? UpdatedAt { get; set; }
    }
}

/// <summary>
/// Source-generated JSON serializer context for Robinhood quote DTOs (ADR-014).
/// </summary>
[JsonSerializable(typeof(RobinhoodMarketDataClient.RobinhoodQuoteResponse))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
internal sealed partial class RobinhoodQuoteSerializerContext : JsonSerializerContext;

/// <summary>
/// Redacted provider diagnostics for the Robinhood polling market data adapter.
/// Excludes access tokens, request URLs, account IDs, and response payloads.
/// </summary>
public sealed record RobinhoodMarketDataDiagnostics(
    string ProviderId,
    ProviderConnectionLifecycleState LifecycleState,
    bool IsConnected,
    int ActiveSubscriptionCount,
    DateTimeOffset? LastConnectedAt,
    DateTimeOffset? LastDisconnectedAt,
    DateTimeOffset? LastPollAttemptAt,
    DateTimeOffset? LastSuccessfulApiCallAt,
    DateTimeOffset? LastMessageReceivedAt,
    string? LastError,
    int ConsecutivePollFailures,
    long DataQualityRejections,
    TimeSpan? ConnectionAge,
    TimeSpan? IdleDuration);

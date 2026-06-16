// ✅ ADR-001: IMarketDataClient contract for streaming data providers
// ✅ ADR-004: CancellationToken on all async methods
// ✅ ADR-005: Attribute-based provider discovery via [DataSource]
// ✅ ADR-010: HTTP client via IHttpClientFactory
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Exceptions;
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
public sealed class RobinhoodMarketDataClient : IMarketDataClient
{
    private const string QuotesEndpoint = "https://api.robinhood.com/marketdata/quotes/";
    private const string EnvAccessToken = "ROBINHOOD_ACCESS_TOKEN";
    private const int MaxSymbolsPerBatch = 50;
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly QuoteCollector _quoteCollector;
    private readonly ILogger<RobinhoodMarketDataClient> _logger;
    private readonly string? _accessToken;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    private readonly ConcurrentDictionary<int, string> _subscriptions = new();
    private int _nextSubId;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private volatile bool _connected;
    private bool _disposed;
    private volatile ProviderConnectionLifecycleState _lifecycleState = ProviderConnectionLifecycleState.Configured;
    private DateTimeOffset? _connectedAt;
    private DateTimeOffset? _disconnectedAt;
    private DateTimeOffset? _lastPollAttemptAt;
    private DateTimeOffset? _lastSuccessfulApiCallAt;
    private DateTimeOffset? _lastMessageReceivedAt;
    private string? _lastError;
    private int _consecutivePollFailures;
    private long _dataQualityRejections;

    public RobinhoodMarketDataClient(
        IHttpClientFactory httpClientFactory,
        QuoteCollector quoteCollector,
        ILogger<RobinhoodMarketDataClient> logger,
        string? accessToken = null)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _quoteCollector = quoteCollector ?? throw new ArgumentNullException(nameof(quoteCollector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_accessToken);

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(_accessToken))
        {
            SetLifecycleState(ProviderConnectionLifecycleState.NotConfigured);
            _lastError = "Robinhood access token is missing.";
            throw new ConnectionException(
                $"ROBINHOOD_ACCESS_TOKEN environment variable is not set. " +
                "Set it to your Robinhood personal access token before connecting.");
        }

        await _lifecycleLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_connected)
                return;

            SetLifecycleState(ProviderConnectionLifecycleState.Connecting);
            _pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _connected = true;
            _connectedAt = DateTimeOffset.UtcNow;
            _lastError = null;
            _consecutivePollFailures = 0;
            SetLifecycleState(ProviderConnectionLifecycleState.Connected);
            _pollTask = RunPollLoopAsync(_pollCts.Token);
            _logger.LogInformation("Robinhood market data client connected (polling interval {Interval})", DefaultPollInterval);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await _lifecycleLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_connected)
            {
                if (_lifecycleState is not ProviderConnectionLifecycleState.NotConfigured)
                    SetLifecycleState(ProviderConnectionLifecycleState.Disconnected);
                return;
            }

            SetLifecycleState(ProviderConnectionLifecycleState.Disconnecting);
            _connected = false;

            if (_pollCts is not null)
            {
                await _pollCts.CancelAsync().ConfigureAwait(false);
                if (_pollTask is not null)
                {
                    try
                    { await _pollTask.WaitAsync(ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                }
                _pollCts.Dispose();
                _pollCts = null;
            }

            _pollTask = null;
            _disconnectedAt = DateTimeOffset.UtcNow;
            SetLifecycleState(ProviderConnectionLifecycleState.Disconnected);
            _logger.LogInformation("Robinhood market data client disconnected");
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

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
        ObjectDisposedException.ThrowIf(_disposed, this);
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
        var lastActivity = _lastMessageReceivedAt ?? _lastSuccessfulApiCallAt ?? _connectedAt;

        return new RobinhoodMarketDataDiagnostics(
            ProviderId: "robinhood-live",
            LifecycleState: _lifecycleState,
            IsConnected: _connected,
            ActiveSubscriptionCount: _subscriptions.Count,
            LastConnectedAt: _connectedAt,
            LastDisconnectedAt: _disconnectedAt,
            LastPollAttemptAt: _lastPollAttemptAt,
            LastSuccessfulApiCallAt: _lastSuccessfulApiCallAt,
            LastMessageReceivedAt: _lastMessageReceivedAt,
            LastError: _lastError,
            ConsecutivePollFailures: _consecutivePollFailures,
            DataQualityRejections: Interlocked.Read(ref _dataQualityRejections),
            ConnectionAge: _connected && _connectedAt is not null ? now - _connectedAt.Value : null,
            IdleDuration: lastActivity is not null ? now - lastActivity.Value : null);
    }

    // ── Polling loop ──────────────────────────────────────────────────────

    private async Task RunPollLoopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Robinhood quote polling loop started");
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(DefaultPollInterval, ct).ConfigureAwait(false);

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

                if (pollSucceeded)
                {
                    _consecutivePollFailures = 0;
                    if (_lifecycleState is ProviderConnectionLifecycleState.Degraded)
                        SetLifecycleState(ProviderConnectionLifecycleState.Connected);
                }
                else
                {
                    _consecutivePollFailures++;
                    if (_lifecycleState is not ProviderConnectionLifecycleState.Failed)
                        SetLifecycleState(ProviderConnectionLifecycleState.Degraded);
                    var backoff = CalculatePollBackoff(_consecutivePollFailures);
                    _logger.LogWarning(
                        "Robinhood quote polling degraded after {Failures} consecutive failed poll cycles; backing off for {Delay}",
                        _consecutivePollFailures,
                        backoff);
                    await Task.Delay(backoff, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Robinhood quote polling loop failed unexpectedly");
        }
        finally
        {
            _logger.LogInformation("Robinhood quote polling loop stopped");
        }
    }

    private async Task<bool> PollBatchAsync(IReadOnlyList<string> symbols, CancellationToken ct)
    {
        try
        {
            _lastPollAttemptAt = DateTimeOffset.UtcNow;
            using var client = CreateHttpClient();
            var symbolList = string.Join(",", symbols);
            var url = $"{QuotesEndpoint}?symbols={Uri.EscapeDataString(symbolList)}";

            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _lastError = "Provider returned Unauthorized while polling Robinhood quotes. Refresh or replace the stored access token.";
                SetLifecycleState(ProviderConnectionLifecycleState.Failed);
                _logger.LogWarning("Robinhood quote polling: 401 Unauthorized — access token may have expired");
                return false;
            }

            if (!response.IsSuccessStatusCode)
            {
                _lastError = $"Provider returned HTTP {(int)response.StatusCode} while polling Robinhood quotes.";
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
                _lastSuccessfulApiCallAt = DateTimeOffset.UtcNow;
                _lastError = null;
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

            _lastSuccessfulApiCallAt = DateTimeOffset.UtcNow;
            if (publishedAny)
                _lastMessageReceivedAt = _lastSuccessfulApiCallAt;
            _lastError = null;
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _logger.LogError(ex, "Robinhood quote poll error for batch {Symbols}", string.Join(",", symbols));
            return false;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private HttpClient CreateHttpClient()
    {
        var client = _httpClientFactory.CreateClient(HttpClientNames.RobinhoodMarketData);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _accessToken);
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await DisconnectAsync().ConfigureAwait(false);
        _lifecycleLock.Dispose();
    }

    private static TimeSpan CalculatePollBackoff(int consecutiveFailures)
    {
        var attempt = Math.Clamp(consecutiveFailures, 1, 5);
        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
        return delay > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delay;
    }

    private void SetLifecycleState(ProviderConnectionLifecycleState state)
    {
        if (_lifecycleState == state)
            return;

        _lifecycleState = state;
        _logger.LogInformation("Robinhood market data lifecycle state changed to {LifecycleState}", state);
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

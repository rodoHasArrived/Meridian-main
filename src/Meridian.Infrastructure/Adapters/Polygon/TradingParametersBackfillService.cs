using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Adapters.Polygon;

/// <summary>
/// Backfills trading parameters (tick size, lot size, currency) from Polygon.io
/// into the Security Master for equities and ETFs.
/// Implements rate limiting and graceful error handling.
/// </summary>
[ImplementsAdr("ADR-001", "Provider abstraction for trading parameter data")]
[ImplementsAdr("ADR-004", "Async method with CancellationToken support")]
public sealed class TradingParametersBackfillService : ITradingParametersBackfillService
{
    private const string BaseUrl = "https://api.polygon.io/v3/reference/tickers";
    private const int MaxRequestsPerMinute = 5;
    private const int MinDelayMilliseconds = 200;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecurityMasterService _securityMasterService;
    private readonly ISecurityMasterQueryService _queryService;
    private readonly RateLimiter _rateLimiter;
    private readonly ILogger<TradingParametersBackfillService> _logger;
    private readonly string? _apiKey;

    public TradingParametersBackfillService(
        IHttpClientFactory httpClientFactory,
        ISecurityMasterService securityMasterService,
        ISecurityMasterQueryService queryService,
        ILogger<TradingParametersBackfillService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _securityMasterService = securityMasterService;
        _queryService = queryService;
        _logger = logger;

        _apiKey = Environment.GetEnvironmentVariable("POLYGON_API_KEY") ?? string.Empty;
        _rateLimiter = new RateLimiter(
            maxRequestsPerWindow: MaxRequestsPerMinute,
            window: TimeSpan.FromMinutes(1),
            minDelayBetweenRequests: TimeSpan.FromMilliseconds(MinDelayMilliseconds),
            log: Serilog.Log.ForContext<TradingParametersBackfillService>());
    }

    public async Task BackfillAllAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogWarning("Polygon API key not configured; cannot backfill trading parameters");
            return;
        }

        _logger.LogInformation("Starting backfill of trading parameters for all active securities");

        var searchRequest = new SecuritySearchRequest(Query: "", Take: 1000, ActiveOnly: true);
        IReadOnlyList<SecuritySummaryDto> results;

        try
        {
            results = await _queryService.SearchAsync(searchRequest, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search active securities");
            return;
        }

        if (results.Count == 0)
        {
            _logger.LogWarning("No active securities found for backfill");
            return;
        }

        _logger.LogInformation("Found {Count} active securities to backfill trading parameters for", results.Count);

        int successCount = 0;
        int failureCount = 0;

        foreach (var security in results)
        {
            if (ct.IsCancellationRequested)
            {
                _logger.LogInformation("Trading parameter backfill cancelled");
                break;
            }

            var primaryId = security.PrimaryIdentifier;
            if (string.IsNullOrEmpty(primaryId))
            {
                _logger.LogDebug("Skipping {SecurityId}: no primary identifier", security.SecurityId);
                continue;
            }

            try
            {
                await BackfillTickerAsync(primaryId, security.SecurityId, ct).ConfigureAwait(false);
                successCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to backfill trading parameters for {Ticker} ({SecurityId})",
                    primaryId, security.SecurityId);
                failureCount++;
            }
        }

        _logger.LogInformation(
            "Completed backfill of trading parameters: {SuccessCount} succeeded, {FailureCount} failed",
            successCount, failureCount);
    }

    public async Task BackfillTickerAsync(string ticker, Guid securityId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogWarning("Polygon API key not configured; cannot backfill trading parameters");
            return;
        }

        _logger.LogInformation("Backfilling trading parameters for {Ticker} (SecurityId={SecurityId})",
            ticker, securityId);

        try
        {
            await _rateLimiter.WaitForSlotAsync(ct).ConfigureAwait(false);

            var client = _httpClientFactory.CreateClient("Polygon");
            var url = $"{BaseUrl}/{ticker}?apiKey={Uri.EscapeDataString(_apiKey)}";

            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Polygon API returned status {StatusCode} for {Ticker}",
                    response.StatusCode, ticker);
                return;
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("results", out var resultsElement) ||
                resultsElement.ValueKind != JsonValueKind.Array ||
                resultsElement.GetArrayLength() == 0)
            {
                _logger.LogWarning("No results found for {Ticker}", ticker);
                return;
            }

            var tickerData = resultsElement[0];

            decimal? minTickSize = null;
            decimal? lotSize = null;
            string? currency = null;
            string? market = null;

            if (tickerData.TryGetProperty("min_tick_size", out var minTickSizeElement) &&
                minTickSizeElement.TryGetDecimal(out var minTickSizeValue))
            {
                minTickSize = minTickSizeValue;
            }

            if (tickerData.TryGetProperty("lot_size", out var lotSizeElement) &&
                lotSizeElement.TryGetInt32(out var lotSizeValue))
            {
                lotSize = lotSizeValue;
            }

            if (tickerData.TryGetProperty("currency_name", out var currencyElement) &&
                currencyElement.ValueKind == JsonValueKind.String)
            {
                currency = currencyElement.GetString();
            }

            if (tickerData.TryGetProperty("market", out var marketElement) &&
                marketElement.ValueKind == JsonValueKind.String)
            {
                market = marketElement.GetString();
            }

            // Build the trading parameters update
            var commonTermsJson = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                min_tick_size = minTickSize,
                lot_size = lotSize,
                market = market
            }));

            var detail = await _queryService.GetByIdAsync(securityId, ct).ConfigureAwait(false);
            if (detail is null)
            {
                _logger.LogWarning("Security {SecurityId} not found in Security Master", securityId);
                return;
            }

            var amendRequest = new AmendSecurityTermsRequest(
                SecurityId: securityId,
                ExpectedVersion: detail.Version,
                CommonTerms: commonTermsJson.RootElement,
                AssetSpecificTermsPatch: null,
                IdentifiersToAdd: [],
                IdentifiersToExpire: [],
                EffectiveFrom: DateTimeOffset.UtcNow,
                SourceSystem: "PolygonBackfill",
                UpdatedBy: "TradingParametersBackfillService",
                SourceRecordId: $"polygon:{ticker}",
                Reason: "Automated trading parameters backfill from Polygon.io");

            await _securityMasterService.AmendTermsAsync(amendRequest, ct).ConfigureAwait(false);

            _logger.LogInformation("Successfully backfilled trading parameters for {Ticker}: " +
                "TickSize={TickSize}, LotSize={LotSize}, Currency={Currency}",
                ticker, minTickSize, lotSize, currency ?? "N/A");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error backfilling trading parameters for {Ticker}", ticker);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Backfill cancelled for {Ticker}", ticker);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error backfilling trading parameters for {Ticker}", ticker);
        }
    }

    private sealed record PolygonTickerResponse
    {
        [JsonPropertyName("results")]
        public PolygonTickerData[]? Results { get; init; }
    }

    private sealed record PolygonTickerData
    {
        [JsonPropertyName("min_tick_size")]
        public decimal? MinTickSize { get; init; }

        [JsonPropertyName("lot_size")]
        public int? LotSize { get; init; }

        [JsonPropertyName("currency_name")]
        public string? CurrencyName { get; init; }

        [JsonPropertyName("market")]
        public string? Market { get; init; }
    }
}

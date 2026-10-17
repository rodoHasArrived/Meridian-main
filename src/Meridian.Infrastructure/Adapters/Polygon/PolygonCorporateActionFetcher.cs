using System.Globalization;
using System.Net;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Adapters.Polygon;

/// <summary>
/// Fetches corporate action data (dividends and stock splits) from Polygon.io REST API
/// and persists them to the Security Master event store.
/// </summary>
public interface IPolygonCorporateActionFetcher
{
    /// <summary>
    /// Fetch and persist all corporate-action pages for a security ticker. Failures throw with
    /// completion evidence; cancellation propagates with acknowledged persistence counts.
    /// </summary>
    Task<PolygonCorporateActionFetchResult> FetchAndPersistAsync(string ticker, Guid securityId, CancellationToken ct);

    /// <summary>
    /// Fetch and persist corporate actions for all active securities.
    /// </summary>
    Task FetchAndPersistAllAsync(CancellationToken ct);
}

/// <summary>
/// Background service that periodically fetches corporate action data from Polygon.io
/// and persists it to the Security Master.
/// Implements rate limiting and graceful error handling.
/// </summary>
[ImplementsAdr("ADR-001", "Corporate action data provider abstraction")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
public sealed class PolygonCorporateActionFetcher : IPolygonCorporateActionFetcher, IHostedService
{
    private const string BaseUrl = PolygonEndpoints.RestBase;
    private const int MaxResultsPerRequest = 100;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecurityMasterQueryService _queryService;
    private readonly ISecurityMasterCorporateActionCommandService _corporateActionCommandService;
    private readonly RateLimiter _rateLimiter;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PolygonCorporateActionFetcher> _logger;

    private string? _apiKey;
    private CancellationTokenSource? _backgroundCts;
    private Task? _backgroundTask;

    public PolygonCorporateActionFetcher(
        IHttpClientFactory httpClientFactory,
        ISecurityMasterQueryService queryService,
        ISecurityMasterCorporateActionCommandService corporateActionCommandService,
        RateLimiter rateLimiter,
        IConfiguration configuration,
        ILogger<PolygonCorporateActionFetcher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _queryService = queryService;
        _corporateActionCommandService = corporateActionCommandService;
        _rateLimiter = rateLimiter;
        _configuration = configuration;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _apiKey = ResolvePolygonApiKey(_configuration);

        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogInformation("Polygon API key not configured; corporate action fetcher will not start");
            return Task.CompletedTask;
        }

        _logger.LogInformation("Starting PolygonCorporateActionFetcher with API key configured");

        _backgroundCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _backgroundTask = Task.Run(
            async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), _backgroundCts.Token).ConfigureAwait(false);
                try
                {
                    await FetchAndPersistAllAsync(_backgroundCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_backgroundCts.Token.IsCancellationRequested)
                {
                    _logger.LogDebug("Corporate action fetch cancelled");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during scheduled corporate action fetch");
                }
            },
            _backgroundCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_backgroundCts != null)
        {
            _backgroundCts.Cancel();
        }

        if (_backgroundTask != null)
        {
            try
            {
                await _backgroundTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _backgroundCts?.Dispose();
        _logger.LogInformation("PolygonCorporateActionFetcher stopped");
    }

    public async Task<PolygonCorporateActionFetchResult> FetchAndPersistAsync(string ticker, Guid securityId, CancellationToken ct)
    {
        var progress = new FetchProgress(ticker, securityId);
        try
        {
            ct.ThrowIfCancellationRequested();
            _apiKey ??= ResolvePolygonApiKey(_configuration);
            if (string.IsNullOrWhiteSpace(_apiKey))
                throw new PolygonCorporateActionFetchException(PolygonCorporateActionFailureKind.NotConfigured, progress.Snapshot());

            await FetchEndpointAsync("dividends", progress, ct).ConfigureAwait(false);
            await FetchEndpointAsync("splits", progress, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return progress.Snapshot();
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            throw new PolygonCorporateActionFetchCanceledException(progress.Snapshot(), ex, ct);
        }
    }

    public async Task FetchAndPersistAllAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const int pageSize = 500;
        var seenSecurities = new HashSet<Guid>();
        var fetchedCount = 0;
        var skippedCount = 0;
        var skip = 0;
        while (true)
        {
            var request = new SecuritySearchRequest(Query: "", Take: pageSize, Skip: skip, ActiveOnly: true);
            var results = await _queryService.SearchAsync(request, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var newSecurities = 0;
            foreach (var security in results)
            {
                ct.ThrowIfCancellationRequested();
                if (!seenSecurities.Add(security.SecurityId))
                    continue;
                newSecurities++;
                if (string.IsNullOrWhiteSpace(security.PrimaryIdentifier))
                {
                    skippedCount++;
                    continue;
                }

                // Failures propagate: an interrupted universe scan cannot claim complete coverage.
                await FetchAndPersistAsync(security.PrimaryIdentifier, security.SecurityId, ct).ConfigureAwait(false);
                fetchedCount++;
            }

            if (results.Count < pageSize)
                break;
            if (newSecurities == 0)
                throw new InvalidDataException("Active security search repeated a corporate-action scan page.");
            skip += results.Count;
        }

        ct.ThrowIfCancellationRequested();
        _logger.LogInformation(
            "Completed corporate action scan: {FetchedCount} securities fetched, {SkippedCount} skipped without a primary identifier",
            fetchedCount, skippedCount);
    }

    private async Task FetchEndpointAsync(string endpoint, FetchProgress progress, CancellationToken ct)
    {
        var nextUrl = $"{BaseUrl}/v3/reference/{endpoint}?ticker={Uri.EscapeDataString(progress.Ticker)}&limit={MaxResultsPerRequest}";
        var visitedPages = new HashSet<string>(StringComparer.Ordinal);
        var seenActions = new HashSet<string>(StringComparer.Ordinal);
        using var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Meridian/1.0");
        var failureKind = PolygonCorporateActionFailureKind.InvalidResponse;
        TimeSpan? retryAfter = null;
        HttpStatusCode? statusCode = null;

        try
        {
            while (!string.IsNullOrWhiteSpace(nextUrl))
            {
                ct.ThrowIfCancellationRequested();
                failureKind = PolygonCorporateActionFailureKind.InvalidResponse;
                var pageUri = PolygonPagination.GetAuthenticatedPageUri(nextUrl, _apiKey!);
                if (!visitedPages.Add(PolygonPagination.GetPageIdentity(pageUri)))
                    throw new InvalidDataException("Polygon repeated a corporate-action pagination cursor.");

                failureKind = PolygonCorporateActionFailureKind.Http;
                // Pacing applies to each HTTP request, including empty pages and subsequent cursors.
                await _rateLimiter.WaitForSlotAsync(ct).ConfigureAwait(false);
                using var response = await client.GetAsync(pageUri, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    statusCode = response.StatusCode;
                    failureKind = response.StatusCode switch
                    {
                        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => PolygonCorporateActionFailureKind.Authentication,
                        HttpStatusCode.TooManyRequests => PolygonCorporateActionFailureKind.Throttled,
                        _ => PolygonCorporateActionFailureKind.Http
                    };
                    retryAfter = response.Headers.RetryAfter?.Delta;
                    if (retryAfter == null && response.Headers.RetryAfter?.Date is { } retryDate)
                        retryAfter = retryDate > DateTimeOffset.UtcNow ? retryDate - DateTimeOffset.UtcNow : TimeSpan.Zero;
                    response.EnsureSuccessStatusCode();
                }

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                failureKind = PolygonCorporateActionFailureKind.InvalidResponse;
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("status", out var status) &&
                    status.GetString() is { } statusText &&
                    !string.Equals(statusText, "OK", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Polygon returned an unsuccessful corporate-action response.");

                if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Polygon corporate-action response omitted its results array.");

                foreach (var item in results.EnumerateArray())
                {
                    ct.ThrowIfCancellationRequested();
                    failureKind = PolygonCorporateActionFailureKind.InvalidResponse;
                    var identity = item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                        ? id.GetString()! : item.GetRawText();
                    if (!seenActions.Add(identity))
                        continue;

                    CorporateActionDto dto;
                    var parsed = endpoint == "dividends"
                        ? ParseDividend(item, progress.SecurityId, out dto)
                        : ParseSplit(item, progress.SecurityId, out dto);
                    if (!parsed)
                        throw new InvalidDataException("Polygon returned an invalid corporate action.");

                    failureKind = PolygonCorporateActionFailureKind.Persistence;
                    await AppendCorporateActionAsync(progress.Ticker, progress.SecurityId, dto, ct).ConfigureAwait(false);
                    progress.RecordPersisted(endpoint);
                }

                failureKind = PolygonCorporateActionFailureKind.InvalidResponse;
                nextUrl = root.TryGetProperty("next_url", out var next) && next.ValueKind != JsonValueKind.Null
                    ? next.GetString() : null;
            }

            ct.ThrowIfCancellationRequested();
            progress.RecordComplete(endpoint);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (failureKind != PolygonCorporateActionFailureKind.Persistence && ex is HttpRequestException httpException)
            {
                statusCode ??= httpException.StatusCode;
                failureKind = statusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => PolygonCorporateActionFailureKind.Authentication,
                    HttpStatusCode.TooManyRequests => PolygonCorporateActionFailureKind.Throttled,
                    _ => PolygonCorporateActionFailureKind.Http
                };
            }
            throw new PolygonCorporateActionFetchException(failureKind, progress.Snapshot(), ex, statusCode, retryAfter);
        }
    }

    private sealed class FetchProgress(string ticker, Guid securityId)
    {
        public string Ticker { get; } = ticker;
        public Guid SecurityId { get; } = securityId;
        private bool _dividendsComplete;
        private bool _splitsComplete;
        private int _dividendsPersisted;
        private int _splitsPersisted;

        public void RecordPersisted(string endpoint)
        {
            if (endpoint == "dividends")
                _dividendsPersisted++;
            else
                _splitsPersisted++;
        }

        public void RecordComplete(string endpoint)
        {
            if (endpoint == "dividends")
                _dividendsComplete = true;
            else
                _splitsComplete = true;
        }

        public PolygonCorporateActionFetchResult Snapshot() => new(
            Ticker, SecurityId,
            new(_dividendsComplete, _dividendsPersisted),
            new(_splitsComplete, _splitsPersisted));
    }

    private Task AppendCorporateActionAsync(
        string ticker,
        Guid securityId,
        CorporateActionDto dto,
        CancellationToken ct)
        => _corporateActionCommandService.AppendAsync(
            new SecurityMasterCorporateActionAppendRequestDto(
                securityId,
                dto,
                SourceSystem: "Polygon",
                Actor: "polygon-corporate-action-fetcher",
                SourceRecordId: ticker,
                Reason: $"Provider corporate action backfill for {ticker}."),
            ct);

    private bool ParseDividend(JsonElement item, Guid securityId, out CorporateActionDto dto)
    {
        dto = default!;

        if (!item.TryGetProperty("cash_amount", out var cashElement) ||
            !TryGetDecimal(cashElement, out var cashAmount))
        {
            return false;
        }

        if (!item.TryGetProperty("ex_dividend_date", out var exDateElement) ||
            exDateElement.GetString() == null ||
            !DateOnly.TryParse(exDateElement.GetString()!, out var exDate))
        {
            return false;
        }

        var currency = item.TryGetProperty("currency", out var currencyElement)
            ? currencyElement.GetString()
            : null;

        var payDate = item.TryGetProperty("pay_date", out var payDateElement) &&
                      payDateElement.GetString() != null &&
                      DateOnly.TryParse(payDateElement.GetString()!, out var pDate)
            ? pDate
            : (DateOnly?)null;
        var recordDate = item.TryGetProperty("record_date", out var recordDateElement) &&
                         recordDateElement.GetString() != null &&
                         DateOnly.TryParse(recordDateElement.GetString()!, out var rDate)
            ? rDate
            : (DateOnly?)null;

        dto = new CorporateActionDto(
            CorpActId: Guid.NewGuid(),
            SecurityId: securityId,
            EventType: "Dividend",
            ExDate: exDate,
            PayDate: payDate,
            DividendPerShare: cashAmount,
            Currency: currency,
            SplitRatio: null,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null,
            RecordDate: recordDate);

        return true;
    }

    private bool ParseSplit(JsonElement item, Guid securityId, out CorporateActionDto dto)
    {
        dto = default!;

        if (!item.TryGetProperty("split_from", out var fromElement) ||
            !TryGetDecimal(fromElement, out var splitFrom) || splitFrom <= 0m ||
            !item.TryGetProperty("split_to", out var toElement) ||
            !TryGetDecimal(toElement, out var splitTo) || splitTo <= 0m)
        {
            return false;
        }

        if (!item.TryGetProperty("execution_date", out var exDateElement) ||
            exDateElement.GetString() == null ||
            !DateOnly.TryParse(exDateElement.GetString()!, out var exDate))
        {
            return false;
        }

        var splitRatio = splitTo / splitFrom;

        dto = new CorporateActionDto(
            CorpActId: Guid.NewGuid(),
            SecurityId: securityId,
            EventType: splitRatio < 1m ? "ReverseStockSplit" : "StockSplit",
            ExDate: exDate,
            PayDate: null,
            DividendPerShare: null,
            Currency: null,
            SplitRatio: splitRatio,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null);

        return true;
    }

    private static bool TryGetDecimal(JsonElement element, out decimal value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Number
            ? element.TryGetDecimal(out value)
            : element.ValueKind == JsonValueKind.String && decimal.TryParse(
                element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static string? ResolvePolygonApiKey(IConfiguration configuration)
    {
        var meridianKey = Environment.GetEnvironmentVariable("MERIDIAN_POLYGON_API_KEY");
        if (!string.IsNullOrWhiteSpace(meridianKey))
            return meridianKey;

        var polygonKey = Environment.GetEnvironmentVariable("POLYGON_API_KEY");
        if (!string.IsNullOrWhiteSpace(polygonKey))
            return polygonKey;

        var configuredKey = configuration["Polygon:ApiKey"];
        if (!string.IsNullOrWhiteSpace(configuredKey))
            return configuredKey;

        return configuration["Backfill:Providers:Polygon:ApiKey"];
    }
}

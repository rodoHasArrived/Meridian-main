using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Config;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Polygon;

/// <summary>
/// Fetches corporate action data (dividends and stock splits) from Polygon.io REST API
/// and persists them to the Security Master event store.
/// </summary>
public interface IPolygonCorporateActionFetcher
{
    /// <summary>
    /// Fetch and persist corporate actions for a specific security ticker.
    /// </summary>
    Task FetchAndPersistAsync(string ticker, Guid securityId, CancellationToken ct);

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
    private const string BaseUrl = "https://api.polygon.io";
    private const int MaxResultsPerRequest = 100;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecurityMasterQueryService _queryService;
    private readonly ISecurityMasterEventStore _eventStore;
    private readonly RateLimiter _rateLimiter;
    private readonly IOptions<AppConfig> _configOptions;
    private readonly ILogger<PolygonCorporateActionFetcher> _logger;

    private string? _apiKey;
    private CancellationTokenSource? _backgroundCts;
    private Task? _backgroundTask;

    public PolygonCorporateActionFetcher(
        IHttpClientFactory httpClientFactory,
        ISecurityMasterQueryService queryService,
        ISecurityMasterEventStore eventStore,
        RateLimiter rateLimiter,
        IOptions<AppConfig> configOptions,
        ILogger<PolygonCorporateActionFetcher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _queryService = queryService;
        _eventStore = eventStore;
        _rateLimiter = rateLimiter;
        _configOptions = configOptions;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken ct)
    {
        var config = _configOptions.Value;
        _apiKey = ResolvePolygonApiKey(config);

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

    public async Task FetchAndPersistAsync(string ticker, Guid securityId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogWarning("Cannot fetch corporate actions: Polygon API key not configured");
            return;
        }

        _logger.LogInformation("Fetching corporate actions for {Ticker} (SecurityId={SecurityId})", ticker, securityId);

        try
        {
            await FetchAndPersistDividendsAsync(ticker, securityId, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch dividends for {Ticker} from Polygon", ticker);
        }

        try
        {
            await FetchAndPersistSplitsAsync(ticker, securityId, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch splits for {Ticker} from Polygon", ticker);
        }
    }

    public async Task FetchAndPersistAllAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting fetch of corporate actions for all active securities");

        var searchRequest = new SecuritySearchRequest(Query: "", Take: 500, ActiveOnly: true);
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
            _logger.LogWarning("No active securities found");
            return;
        }

        _logger.LogInformation("Found {Count} active securities to fetch corporate actions for", results.Count);

        foreach (var security in results)
        {
            if (ct.IsCancellationRequested)
            {
                _logger.LogInformation("Corporate action fetch cancelled");
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
                await FetchAndPersistAsync(primaryId, security.SecurityId, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching corporate actions for {Ticker} (SecurityId={SecurityId})", primaryId, security.SecurityId);
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Delay cancelled");
                break;
            }
        }

        _logger.LogInformation("Completed fetch of corporate actions for all active securities");
    }

    private async Task FetchAndPersistDividendsAsync(string ticker, Guid securityId, CancellationToken ct)
    {
        var url = $"{BaseUrl}/v3/reference/dividends?ticker={Uri.EscapeDataString(ticker)}&limit={MaxResultsPerRequest}&apiKey={_apiKey}";

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("User-Agent", "Meridian/1.0");

        var response = await client.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        var root = doc.RootElement;
        if (!root.TryGetProperty("results", out var results))
        {
            _logger.LogDebug("No dividend results for {Ticker}", ticker);
            return;
        }

        var dividendCount = 0;
        foreach (var item in results.EnumerateArray())
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                if (!ParseDividend(item, securityId, out var dto))
                    continue;

                await _rateLimiter.WaitForSlotAsync(ct).ConfigureAwait(false);
                await _eventStore.AppendCorporateActionAsync(dto, ct).ConfigureAwait(false);
                dividendCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing dividend for {Ticker}", ticker);
            }
        }

        _logger.LogInformation("Persisted {Count} dividends for {Ticker}", dividendCount, ticker);
    }

    private async Task FetchAndPersistSplitsAsync(string ticker, Guid securityId, CancellationToken ct)
    {
        var url = $"{BaseUrl}/v3/reference/splits?ticker={Uri.EscapeDataString(ticker)}&limit={MaxResultsPerRequest}&apiKey={_apiKey}";

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("User-Agent", "Meridian/1.0");

        var response = await client.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        var root = doc.RootElement;
        if (!root.TryGetProperty("results", out var results))
        {
            _logger.LogDebug("No split results for {Ticker}", ticker);
            return;
        }

        var splitCount = 0;
        foreach (var item in results.EnumerateArray())
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                if (!ParseSplit(item, securityId, out var dto))
                    continue;

                await _rateLimiter.WaitForSlotAsync(ct).ConfigureAwait(false);
                await _eventStore.AppendCorporateActionAsync(dto, ct).ConfigureAwait(false);
                splitCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing split for {Ticker}", ticker);
            }
        }

        _logger.LogInformation("Persisted {Count} splits for {Ticker}", splitCount, ticker);
    }

    private bool ParseDividend(JsonElement item, Guid securityId, out CorporateActionDto dto)
    {
        dto = default!;

        if (!item.TryGetProperty("cash_amount", out var cashElement) ||
            cashElement.GetString() == null ||
            !decimal.TryParse(cashElement.GetString()!, out var cashAmount))
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
            RightsPerShare: null);

        return true;
    }

    private bool ParseSplit(JsonElement item, Guid securityId, out CorporateActionDto dto)
    {
        dto = default!;

        if (!item.TryGetProperty("split_from", out var fromElement) ||
            fromElement.GetString() == null ||
            !decimal.TryParse(fromElement.GetString()!, out var splitFrom) ||
            !item.TryGetProperty("split_to", out var toElement) ||
            toElement.GetString() == null ||
            !decimal.TryParse(toElement.GetString()!, out var splitTo))
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
            EventType: "StockSplit",
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

    private static string? ResolvePolygonApiKey(AppConfig config)
    {
        if (config.DataSources?.Sources != null)
        {
            var sources = config.DataSources.Sources;
            var polygonSource = sources.FirstOrDefault(s => s.Provider == DataSourceKind.Polygon);
            if (polygonSource?.Polygon?.ApiKey != null)
            {
                return polygonSource.Polygon.ApiKey;
            }
        }

        return Environment.GetEnvironmentVariable("POLYGON_API_KEY");
    }
}

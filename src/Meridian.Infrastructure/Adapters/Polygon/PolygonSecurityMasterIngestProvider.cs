using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Http;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Adapters.Polygon;

/// <summary>
/// Pages through the Polygon /v3/reference/tickers endpoint and yields
/// <see cref="CreateSecurityRequest"/> objects for bulk Security Master ingest.
/// Supports filtering by exchange (MIC code) and asset type.
/// </summary>
public sealed class PolygonSecurityMasterIngestProvider : IDisposable
{
    private const string BaseUrl = "https://api.polygon.io";
    private const int PageSize = 250;

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly RateLimiter _rateLimiter;
    private readonly ILogger<PolygonSecurityMasterIngestProvider> _logger;
    private readonly string? _apiKey;
    private readonly string? _encodedApiKey;
    private bool _disposed;

    public PolygonSecurityMasterIngestProvider(
        ILogger<PolygonSecurityMasterIngestProvider> logger,
        HttpClient? httpClient = null)
    {
        _logger = logger;
        _apiKey = Environment.GetEnvironmentVariable("POLYGON_API_KEY")
                 ?? Environment.GetEnvironmentVariable("POLYGON__APIKEY");
        _encodedApiKey = _apiKey is not null ? Uri.EscapeDataString(_apiKey) : null;

        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? HttpClientFactoryProvider.CreateClient(HttpClientNames.PolygonSymbolSearch);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Meridian/1.0");

        // Polygon free tier: 5 req/min; unlimited tiers support much higher rates.
        _rateLimiter = new RateLimiter(5, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(12), null);
    }

    /// <summary>
    /// Returns all active tickers for the given exchange / asset-type filter as
    /// <see cref="CreateSecurityRequest"/> objects ready for Security Master bulk import.
    /// </summary>
    /// <param name="exchange">
    /// Optional MIC code (e.g. "XNAS", "XNYS"). When null all exchanges are included.
    /// </param>
    /// <param name="assetType">
    /// Optional Polygon asset type code (e.g. "CS" for common stock, "ETF").
    /// When null all asset types are included.
    /// </param>
    /// <param name="progress">Optional progress callback invoked after each fetched page.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All matching tickers as create-security requests.</returns>
    public async Task<IReadOnlyList<CreateSecurityRequest>> FetchAllAsync(
        string? exchange = null,
        string? assetType = null,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_encodedApiKey))
        {
            _logger.LogWarning("Polygon API key not configured — skipping Security Master ingest");
            return Array.Empty<CreateSecurityRequest>();
        }

        var results = new List<CreateSecurityRequest>();
        var nextUrl = BuildInitialUrl(exchange, assetType);
        int pagesProcessed = 0;

        while (!string.IsNullOrEmpty(nextUrl))
        {
            ct.ThrowIfCancellationRequested();
            await _rateLimiter.WaitForSlotAsync(ct).ConfigureAwait(false);

            var page = await FetchPageAsync(nextUrl, ct).ConfigureAwait(false);
            if (page is null)
                break;

            foreach (var ticker in page.Results ?? Enumerable.Empty<PolygonTickerItem>())
            {
                var request = MapToCreateRequest(ticker);
                if (request is not null)
                    results.Add(request);
            }

            pagesProcessed++;
            progress?.Report(results.Count);

            _logger.LogDebug("Polygon ingest page {Page}: {Count} tickers fetched so far", pagesProcessed, results.Count);

            // Follow cursor-based pagination via next_url
            nextUrl = page.NextUrl is not null
                ? AppendApiKey(page.NextUrl)
                : null;
        }

        _logger.LogInformation(
            "Polygon Security Master ingest complete: {Count} securities fetched in {Pages} pages",
            results.Count, pagesProcessed);

        return results;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private string BuildInitialUrl(string? exchange, string? assetType)
    {
        var url = $"{BaseUrl}/v3/reference/tickers?active=true&limit={PageSize}&apiKey={_encodedApiKey}";
        if (!string.IsNullOrWhiteSpace(exchange))
            url += $"&exchange={Uri.EscapeDataString(exchange)}";
        if (!string.IsNullOrWhiteSpace(assetType))
            url += $"&type={Uri.EscapeDataString(assetType)}";
        return url;
    }

    private string AppendApiKey(string url)
    {
        // Polygon next_url includes all params except apiKey
        return url.Contains("apiKey=", StringComparison.OrdinalIgnoreCase)
            ? url
            : url + $"&apiKey={_encodedApiKey}";
    }

    private async Task<PolygonTickersPage?> FetchPageAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogWarning("Polygon ingest request returned {Status}: {Body}", response.StatusCode, body);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<PolygonTickersPage>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Polygon ingest page fetch failed");
            return null;
        }
    }

    private static CreateSecurityRequest? MapToCreateRequest(PolygonTickerItem ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker.Ticker))
            return null;

        var now = DateTimeOffset.UtcNow;
        var securityId = Guid.NewGuid();

        // Map Polygon type → canonical asset class
        var assetClass = MapAssetClass(ticker.Type);

        var identifiers = new List<SecurityIdentifierDto>
        {
            new(SecurityIdentifierKind.Ticker, ticker.Ticker, IsPrimary: true, ValidFrom: now, Provider: "polygon")
        };

        if (!string.IsNullOrWhiteSpace(ticker.CompositeFigi))
        {
            identifiers.Add(new(SecurityIdentifierKind.Figi, ticker.CompositeFigi, IsPrimary: false, ValidFrom: now, Provider: "polygon"));
        }

        // Build CommonTerms JSON with available fields
        var commonTerms = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["displayName"] = ticker.Name ?? ticker.Ticker,
            ["currency"] = ticker.CurrencyName ?? "USD",
            ["exchangeCode"] = ticker.PrimaryExchange,
            ["locale"] = ticker.Locale?.ToUpperInvariant()
        };

        var commonJson = JsonSerializer.SerializeToElement(commonTerms);
        var assetSpecificJson = JsonSerializer.SerializeToElement(new { polygonType = ticker.Type });

        return new CreateSecurityRequest(
            SecurityId: securityId,
            AssetClass: assetClass,
            CommonTerms: commonJson,
            AssetSpecificTerms: assetSpecificJson,
            Identifiers: identifiers,
            EffectiveFrom: now,
            SourceSystem: "polygon",
            UpdatedBy: "PolygonSecurityMasterIngestProvider",
            SourceRecordId: ticker.Ticker,
            Reason: "Bulk ingest from Polygon /v3/reference/tickers");
    }

    private static string MapAssetClass(string? type) => type switch
    {
        "CS" or "OS" => "Equity",
        "ETF" or "ETV" or "ETN" => "ETF",
        "PFD" => "PreferredEquity",
        "ADRC" => "ADR",
        "WARRANT" => "Warrant",
        "RIGHT" => "Right",
        "INDEX" => "Index",
        "FUND" => "Fund",
        _ => "Equity"
    };

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _rateLimiter.Dispose();
        if (_ownsHttpClient)
            _http.Dispose();
    }

    // ── Polygon response models ───────────────────────────────────────────────

    private sealed class PolygonTickersPage
    {
        [JsonPropertyName("results")]
        public List<PolygonTickerItem>? Results { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("next_url")]
        public string? NextUrl { get; set; }
    }

    private sealed class PolygonTickerItem
    {
        [JsonPropertyName("ticker")]
        public string? Ticker { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("locale")]
        public string? Locale { get; set; }

        [JsonPropertyName("primary_exchange")]
        public string? PrimaryExchange { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("active")]
        public bool Active { get; set; }

        [JsonPropertyName("currency_name")]
        public string? CurrencyName { get; set; }

        [JsonPropertyName("composite_figi")]
        public string? CompositeFigi { get; set; }
    }
}

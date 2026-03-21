using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Logging;
using Meridian.Application.Subscriptions.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Http;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Polygon;

/// <summary>
/// Symbol search provider using Polygon.io API.
/// Provides comprehensive ticker search with market data.
/// Free tier: 5 calls/minute for basic endpoints.
/// </summary>
public sealed class PolygonSymbolSearchProvider : IFilterableSymbolSearchProvider, IDisposable
{
    private const string BaseUrl = "https://api.polygon.io";

    private readonly HttpClient _http;
    private readonly RateLimiter _rateLimiter;
    private readonly ILogger _log;
    private readonly string? _apiKey;
    private bool _disposed;

    public string Name => "polygon";
    public string DisplayName => "Polygon.io";
    public int Priority => 15;

    public IReadOnlyList<string> SupportedAssetTypes => new[]
    {
        "CS", "ETF", "ETN", "ETV", "UNIT", "RIGHT", "SP", "WARRANT", "INDEX", "ADRC", "FUND", "OS", "PFD"
    };

    public IReadOnlyList<string> SupportedExchanges => new[]
    {
        "XNYS", "XNAS", "XASE", "ARCX", "BATS", "XCHI", "XCBO", "XPHL", "XBOS", "IEXG", "EDGA", "EDGX"
    };

    /// <summary>
    /// Human-readable asset type mapping.
    /// </summary>
    private static readonly Dictionary<string, string> AssetTypeDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CS"] = "Common Stock",
        ["ETF"] = "ETF",
        ["ETN"] = "ETN",
        ["ETV"] = "ETV",
        ["UNIT"] = "Unit",
        ["RIGHT"] = "Right",
        ["SP"] = "Structured Product",
        ["WARRANT"] = "Warrant",
        ["INDEX"] = "Index",
        ["ADRC"] = "ADR",
        ["FUND"] = "Fund",
        ["OS"] = "Ordinary Share",
        ["PFD"] = "Preferred Stock"
    };

    public PolygonSymbolSearchProvider(string? apiKey = null, HttpClient? httpClient = null, ILogger? log = null)
    {
        _log = log ?? LoggingSetup.ForContext<PolygonSymbolSearchProvider>();
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("POLYGON_API_KEY")
                         ?? Environment.GetEnvironmentVariable("POLYGON__APIKEY");

        // TD-10: Use HttpClientFactory instead of creating new HttpClient instances
        _http = httpClient ?? HttpClientFactoryProvider.CreateClient(HttpClientNames.PolygonSymbolSearch);
        _http.DefaultRequestHeaders.Add("User-Agent", "Meridian/1.0");

        // Free tier: 5/min, paid tiers are higher
        _rateLimiter = new RateLimiter(5, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(12), _log);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            _log.Debug("Polygon API key not configured");
            return false;
        }

        try
        {
            var results = await SearchAsync("AAPL", 1, ct).ConfigureAwait(false);
            return results.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    public Task<IReadOnlyList<SymbolSearchResult>> SearchAsync(
        string query,
        int limit = 10,
        CancellationToken ct = default)
    {
        return SearchAsync(query, limit, null, null, ct);
    }

    public async Task<IReadOnlyList<SymbolSearchResult>> SearchAsync(
        string query,
        int limit = 10,
        string? assetType = null,
        string? exchange = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<SymbolSearchResult>();

        if (string.IsNullOrEmpty(_apiKey))
            return Array.Empty<SymbolSearchResult>();

        await _rateLimiter.WaitForSlotAsync(ct).ConfigureAwait(false);

        // Build URL with filters
        var url = $"{BaseUrl}/v3/reference/tickers?search={Uri.EscapeDataString(query)}&active=true&limit={Math.Min(limit * 2, 100)}&apiKey={_apiKey}";

        if (!string.IsNullOrEmpty(assetType))
        {
            url += $"&type={Uri.EscapeDataString(assetType)}";
        }

        if (!string.IsNullOrEmpty(exchange))
        {
            url += $"&exchange={Uri.EscapeDataString(exchange)}";
        }

        try
        {
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _log.Warning("Polygon search returned {Status} for query {Query}: {Error}",
                    response.StatusCode, query, error);
                return Array.Empty<SymbolSearchResult>();
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var data = JsonSerializer.Deserialize<PolygonTickersResponse>(json);

            if (data?.Results is null || data.Results.Count == 0)
                return Array.Empty<SymbolSearchResult>();

            return data.Results
                .Where(r => !string.IsNullOrEmpty(r.Ticker))
                .Select((r, i) => new SymbolSearchResult(
                    Symbol: r.Ticker!,
                    Name: r.Name ?? r.Ticker!,
                    Exchange: r.PrimaryExchange,
                    AssetType: MapAssetType(r.Type),
                    Country: r.Locale?.ToUpperInvariant(),
                    Currency: r.CurrencyName,
                    Source: Name,
                    MatchScore: CalculateMatchScore(query, r.Ticker!, r.Name, i)
                ))
                .OrderByDescending(r => r.MatchScore)
                .Take(limit)
                .ToList();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Polygon search failed for query {Query}", query);
            return Array.Empty<SymbolSearchResult>();
        }
    }

    public async Task<SymbolDetails?> GetDetailsAsync(string symbol, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(symbol))
            return null;

        if (string.IsNullOrEmpty(_apiKey))
            return null;

        await _rateLimiter.WaitForSlotAsync(ct).ConfigureAwait(false);

        var normalizedSymbol = symbol.ToUpperInvariant();
        var url = $"{BaseUrl}/v3/reference/tickers/{normalizedSymbol}?apiKey={_apiKey}";

        try
        {
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _log.Debug("Polygon ticker details returned {Status} for {Symbol}", response.StatusCode, symbol);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var data = JsonSerializer.Deserialize<PolygonTickerDetailsResponse>(json);

            if (data?.Results is null)
                return null;

            var ticker = data.Results;

            return new SymbolDetails(
                Symbol: normalizedSymbol,
                Name: ticker.Name ?? normalizedSymbol,
                Description: ticker.Description,
                Exchange: ticker.PrimaryExchange,
                AssetType: MapAssetType(ticker.Type),
                Sector: ticker.SicDescription,
                Industry: null,
                Country: ticker.Locale?.ToUpperInvariant(),
                Currency: ticker.CurrencyName,
                MarketCap: ticker.MarketCap,
                AverageVolume: null,
                Week52High: ticker.Branding?.Week52High,
                Week52Low: ticker.Branding?.Week52Low,
                LastPrice: null,
                WebUrl: ticker.HomepageUrl,
                LogoUrl: ticker.Branding?.LogoUrl,
                IpoDate: ParseDate(ticker.ListDate),
                PaysDividend: null,
                DividendYield: null,
                PeRatio: null,
                SharesOutstanding: ticker.ShareClassSharesOutstanding ?? ticker.WeightedSharesOutstanding,
                Figi: ticker.CompositeFigi,
                CompositeFigi: ticker.CompositeFigi,
                Isin: null,
                Cusip: ticker.Cik,
                Source: Name,
                LastUpdated: DateTimeOffset.UtcNow
            );
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Polygon ticker details lookup failed for {Symbol}", symbol);
            return null;
        }
    }

    private static string? MapAssetType(string? type)
    {
        if (string.IsNullOrEmpty(type))
            return null;

        return AssetTypeDisplayNames.TryGetValue(type, out var displayName)
            ? displayName
            : type;
    }

    private static int CalculateMatchScore(string query, string symbol, string? name, int position)
        => SymbolSearchUtility.CalculateMatchScore(query, symbol, name, position);

    private static DateOnly? ParseDate(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr))
            return null;

        if (DateOnly.TryParse(dateStr, out var date))
            return date;

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _rateLimiter.Dispose();
        _http.Dispose();
    }

    #region Polygon API Models

    private sealed class PolygonTickersResponse
    {
        [JsonPropertyName("results")]
        public List<PolygonTicker>? Results { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("next_url")]
        public string? NextUrl { get; set; }
    }

    private sealed class PolygonTicker
    {
        [JsonPropertyName("ticker")]
        public string? Ticker { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("market")]
        public string? Market { get; set; }

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

        [JsonPropertyName("cik")]
        public string? Cik { get; set; }

        [JsonPropertyName("composite_figi")]
        public string? CompositeFigi { get; set; }

        [JsonPropertyName("share_class_figi")]
        public string? ShareClassFigi { get; set; }
    }

    private sealed class PolygonTickerDetailsResponse
    {
        [JsonPropertyName("results")]
        public PolygonTickerDetails? Results { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }

    private sealed class PolygonTickerDetails
    {
        [JsonPropertyName("ticker")]
        public string? Ticker { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("market")]
        public string? Market { get; set; }

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

        [JsonPropertyName("cik")]
        public string? Cik { get; set; }

        [JsonPropertyName("composite_figi")]
        public string? CompositeFigi { get; set; }

        [JsonPropertyName("share_class_figi")]
        public string? ShareClassFigi { get; set; }

        [JsonPropertyName("market_cap")]
        public decimal? MarketCap { get; set; }

        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("address")]
        public PolygonAddress? Address { get; set; }

        [JsonPropertyName("homepage_url")]
        public string? HomepageUrl { get; set; }

        [JsonPropertyName("total_employees")]
        public int? TotalEmployees { get; set; }

        [JsonPropertyName("list_date")]
        public string? ListDate { get; set; }

        [JsonPropertyName("sic_code")]
        public string? SicCode { get; set; }

        [JsonPropertyName("sic_description")]
        public string? SicDescription { get; set; }

        [JsonPropertyName("ticker_root")]
        public string? TickerRoot { get; set; }

        [JsonPropertyName("share_class_shares_outstanding")]
        public long? ShareClassSharesOutstanding { get; set; }

        [JsonPropertyName("weighted_shares_outstanding")]
        public long? WeightedSharesOutstanding { get; set; }

        [JsonPropertyName("branding")]
        public PolygonBranding? Branding { get; set; }
    }

    private sealed class PolygonAddress
    {
        [JsonPropertyName("address1")]
        public string? Address1 { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("postal_code")]
        public string? PostalCode { get; set; }
    }

    private sealed class PolygonBranding
    {
        [JsonPropertyName("logo_url")]
        public string? LogoUrl { get; set; }

        [JsonPropertyName("icon_url")]
        public string? IconUrl { get; set; }

        // These aren't in branding but added for convenience
        public decimal? Week52High { get; set; }
        public decimal? Week52Low { get; set; }
    }

    #endregion
}

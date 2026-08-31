using System.Text.Json.Serialization;
using Meridian.Core.Subscriptions.Models;
using Meridian.Contracts.Domain;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Http;
using Meridian.Infrastructure.Utilities;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Polygon;

/// <summary>
/// Symbol search provider using Polygon.io API.
/// Provides comprehensive ticker search with market data.
/// Free tier: 5 calls/minute for basic endpoints.
/// </summary>
public sealed class PolygonSymbolSearchProvider : BaseSymbolSearchProvider
{
    public override string Name => "polygon";
    public override string DisplayName => "Polygon.io";
    public override int Priority => 15;

    protected override string HttpClientName => HttpClientNames.PolygonSymbolSearch;
    protected override string BaseUrl => PolygonEndpoints.RestBase;
    protected override string ApiKeyEnvVar => "POLYGON_API_KEY";
    protected override IReadOnlyList<string> AlternateApiKeyEnvVars => new[] { "POLYGON__APIKEY" };

    // Free tier: 5/min, paid tiers are higher.
    protected override int MaxRequestsPerWindow => PolygonRateLimits.MaxRequestsPerWindowFree;
    protected override TimeSpan RateLimitWindow => PolygonRateLimits.Window;
    protected override TimeSpan MinRequestDelay => TimeSpan.FromSeconds(12);

    public override IReadOnlyList<string> SupportedAssetTypes => new[]
    {
        "CS", "ETF", "ETN", "ETV", "UNIT", "RIGHT", "SP", "WARRANT", "INDEX", "ADRC", "FUND", "OS", "PFD"
    };

    public override IReadOnlyList<string> SupportedExchanges => new[]
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
        : base(apiKey, httpClient, log)
    {
    }

    protected override string BuildSearchUrl(string query, string? assetType, string? exchange)
    {
        var url = $"{BaseUrl}/v3/reference/tickers?search={Uri.EscapeDataString(query)}&active=true&limit=100&apiKey={ApiKey}";

        if (!string.IsNullOrEmpty(assetType))
        {
            url += $"&type={Uri.EscapeDataString(assetType)}";
        }

        if (!string.IsNullOrEmpty(exchange))
        {
            url += $"&exchange={Uri.EscapeDataString(exchange)}";
        }

        return url;
    }

    protected override string BuildDetailsUrl(string symbol)
        => $"{BaseUrl}/v3/reference/tickers/{symbol}?apiKey={ApiKey}";

    protected override IEnumerable<SymbolSearchResult> DeserializeSearchResults(string json, string query)
    {
        var data = DeserializeJson<PolygonTickersResponse>(json);

        if (data?.Results is null || data.Results.Count == 0)
            return Enumerable.Empty<SymbolSearchResult>();

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
                MatchScore: CalculateMatchScore(query, r.Ticker!, r.Name, i)));
    }

    protected override Task<SymbolDetails?> DeserializeDetailsAsync(string json, string symbol, CancellationToken ct)
    {
        var data = DeserializeJson<PolygonTickerDetailsResponse>(json);

        if (data?.Results is null)
            return Task.FromResult<SymbolDetails?>(null);

        var ticker = data.Results;

        var details = new SymbolDetails(
            Symbol: symbol,
            Name: ticker.Name ?? symbol,
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
            LastUpdated: DateTimeOffset.UtcNow);

        return Task.FromResult<SymbolDetails?>(details);
    }

    /// <summary>
    /// Polygon filters asset type and exchange natively via query parameters in
    /// <see cref="BuildSearchUrl"/>, so no additional post-filtering is applied here.
    /// (The base filter would compare the raw filter codes against mapped display names.)
    /// </summary>
    protected override IEnumerable<SymbolSearchResult> ApplyFilters(
        IEnumerable<SymbolSearchResult> results,
        string? assetType,
        string? exchange)
        => results;

    private static string? MapAssetType(string? type)
    {
        if (string.IsNullOrEmpty(type))
            return null;

        return AssetTypeDisplayNames.TryGetValue(type, out var displayName)
            ? displayName
            : type;
    }

    private static DateOnly? ParseDate(string? dateStr)
        => ProviderDateParsing.ParseProviderDateOrNull(dateStr);


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

}

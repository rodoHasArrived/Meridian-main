using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Subscriptions.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Finnhub;

/// <summary>
/// Refactored symbol search provider using Finnhub API, now extending BaseSymbolSearchProvider.
/// Provides symbol search and company profiles with generous free tier (60 calls/min).
/// </summary>
/// <remarks>
/// This refactored version eliminates ~200 lines of boilerplate code by leveraging
/// the BaseSymbolSearchProvider abstract class for common functionality.
/// </remarks>
[DataSource("finnhub-symbols", "Finnhub (Symbol Search)", DataSourceType.Historical, DataSourceCategory.Free,
    Priority = 10, Description = "Symbol search and lookup via Finnhub API")]
[ImplementsAdr("ADR-001", "Finnhub symbol search provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
public sealed class FinnhubSymbolSearchProviderRefactored : BaseSymbolSearchProvider
{
    public override string Name => "finnhub";
    public override string DisplayName => "Finnhub";
    public override int Priority => 10;

    protected override string HttpClientName => HttpClientNames.FinnhubSymbolSearch;
    protected override string BaseUrl => "https://finnhub.io/api/v1";
    protected override string ApiKeyEnvVar => "FINNHUB_API_KEY";

    protected override int MaxRequestsPerWindow => 60;
    protected override TimeSpan RateLimitWindow => TimeSpan.FromMinutes(1);
    protected override TimeSpan MinRequestDelay => TimeSpan.FromSeconds(1);

    public override IReadOnlyList<string> SupportedAssetTypes => new[]
    {
        "Common Stock", "ADR", "ETF", "ETN", "Unit", "Warrant", "Right",
        "REIT", "Closed-end Fund", "Preferred Stock", "Trust"
    };

    public override IReadOnlyList<string> SupportedExchanges => new[]
    {
        "US", "OTC", "NASDAQ", "NYSE", "NYSE ARCA", "BATS", "NYSE AMERICAN", "CBOE",
        "LSE", "TSX", "FRA", "XETRA", "ASX", "NSE", "BSE", "SGX", "HKEX", "TSE"
    };

    public FinnhubSymbolSearchProviderRefactored(
        string? apiKey = null,
        HttpClient? httpClient = null,
        ILogger? log = null)
        : base(apiKey, httpClient, log)
    {
    }

    protected override void ConfigureHttpClientHeaders()
    {
        base.ConfigureHttpClientHeaders();

        if (!string.IsNullOrEmpty(ApiKey))
        {
            Http.DefaultRequestHeaders.Add("X-Finnhub-Token", ApiKey);
        }
    }

    protected override string BuildSearchUrl(string query, string? assetType, string? exchange)
    {
        // Finnhub doesn't support filtering in the API, so we ignore assetType and exchange here
        // and filter in ApplyFilters
        return $"{BaseUrl}/search?q={Uri.EscapeDataString(query)}&token={ApiKey}";
    }

    protected override string BuildDetailsUrl(string symbol)
    {
        return $"{BaseUrl}/stock/profile2?symbol={symbol}&token={ApiKey}";
    }

    protected override IEnumerable<SymbolSearchResult> DeserializeSearchResults(string json, string query)
    {
        var data = DeserializeJson<FinnhubSearchResponse>(json);

        if (data?.Result is null || data.Count == 0)
            return Enumerable.Empty<SymbolSearchResult>();

        return data.Result
            .Where(r => !string.IsNullOrEmpty(r.Symbol))
            .Select((r, i) => new SymbolSearchResult(
                Symbol: r.Symbol!,
                Name: r.Description ?? r.Symbol!,
                Exchange: MapDisplayExchange(r.DisplaySymbol),
                AssetType: r.Type,
                Country: null,
                Currency: null,
                Source: Name,
                MatchScore: CalculateMatchScore(query, r.Symbol!, r.Description, i)
            ));
    }

    protected override async Task<SymbolDetails?> DeserializeDetailsAsync(string json, string symbol, CancellationToken ct)
    {
        var profile = DeserializeJson<FinnhubCompanyProfile>(json);

        if (profile is null || string.IsNullOrEmpty(profile.Name))
            return null;

        // Optionally fetch quote for last price and volume
        var quote = await GetQuoteAsync(symbol, ct).ConfigureAwait(false);

        return new SymbolDetails(
            Symbol: symbol,
            Name: profile.Name ?? symbol,
            Description: null,
            Exchange: profile.Exchange,
            AssetType: "Stock",
            Sector: null,
            Industry: profile.Industry,
            Country: profile.Country,
            Currency: profile.Currency,
            MarketCap: profile.MarketCap.HasValue ? profile.MarketCap.Value * 1_000_000m : null,
            AverageVolume: null,
            Week52High: quote?.Week52High,
            Week52Low: quote?.Week52Low,
            LastPrice: quote?.CurrentPrice,
            WebUrl: profile.WebUrl,
            LogoUrl: profile.LogoUrl,
            IpoDate: ParseIpoDate(profile.IpoDate),
            PaysDividend: null,
            DividendYield: null,
            PeRatio: null,
            SharesOutstanding: profile.SharesOutstanding.HasValue
                ? (long)(profile.SharesOutstanding.Value * 1_000_000m)
                : null,
            Figi: null,
            CompositeFigi: null,
            Isin: null,
            Cusip: null,
            Source: Name,
            LastUpdated: DateTimeOffset.UtcNow
        );
    }

    private async Task<FinnhubQuote?> GetQuoteAsync(string symbol, CancellationToken ct)
    {
        try
        {
            await RateLimiter.WaitForSlotAsync(ct).ConfigureAwait(false);

            var url = $"{BaseUrl}/quote?symbol={symbol}&token={ApiKey}";
            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return DeserializeJson<FinnhubQuote>(json);
        }
        catch
        {
            return null;
        }
    }

    private static string? MapDisplayExchange(string? displaySymbol)
    {
        if (string.IsNullOrEmpty(displaySymbol))
            return null;

        var parts = displaySymbol.Split('.');
        return parts.Length > 1 ? parts[^1] : "US";
    }

    private static DateOnly? ParseIpoDate(string? ipoDate)
    {
        if (string.IsNullOrEmpty(ipoDate))
            return null;

        return DateOnly.TryParse(ipoDate, out var date) ? date : null;
    }


    private sealed class FinnhubSearchResponse
    {
        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("result")]
        public List<FinnhubSearchResult>? Result { get; set; }
    }

    private sealed class FinnhubSearchResult
    {
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("displaySymbol")]
        public string? DisplaySymbol { get; set; }

        [JsonPropertyName("symbol")]
        public string? Symbol { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }

    private sealed class FinnhubCompanyProfile
    {
        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("exchange")]
        public string? Exchange { get; set; }

        [JsonPropertyName("finnhubIndustry")]
        public string? Industry { get; set; }

        [JsonPropertyName("ipo")]
        public string? IpoDate { get; set; }

        [JsonPropertyName("logo")]
        public string? LogoUrl { get; set; }

        [JsonPropertyName("marketCapitalization")]
        public decimal? MarketCap { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("shareOutstanding")]
        public decimal? SharesOutstanding { get; set; }

        [JsonPropertyName("weburl")]
        public string? WebUrl { get; set; }
    }

    private sealed class FinnhubQuote
    {
        [JsonPropertyName("c")]
        public decimal? CurrentPrice { get; set; }

        [JsonPropertyName("h")]
        public decimal? HighPrice { get; set; }

        [JsonPropertyName("l")]
        public decimal? LowPrice { get; set; }

        [JsonPropertyName("o")]
        public decimal? OpenPrice { get; set; }

        [JsonPropertyName("pc")]
        public decimal? PreviousClose { get; set; }

        public decimal? Week52High { get; set; }
        public decimal? Week52Low { get; set; }
    }

}

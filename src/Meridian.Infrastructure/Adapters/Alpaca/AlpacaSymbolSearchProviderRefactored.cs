using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Subscriptions.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.Http;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Alpaca;

/// <summary>
/// Refactored symbol search provider using Alpaca Markets API, now extending BaseSymbolSearchProvider.
/// Provides asset search for US equities with trading status information.
/// </summary>
/// <remarks>
/// This refactored version eliminates ~150 lines of boilerplate code by leveraging
/// the BaseSymbolSearchProvider abstract class for common functionality.
///
/// Key differences from original:
/// - Uses base class for HTTP client, rate limiting, and disposal
/// - Cleaner separation between API-specific and common logic
/// - Consistent error handling via base class
/// </remarks>
[ImplementsAdr("ADR-001", "Alpaca symbol search provider implementation")]
public sealed class AlpacaSymbolSearchProviderRefactored : BaseSymbolSearchProvider
{
    private readonly string? _secretKey;

    public override string Name => "alpaca";
    public override string DisplayName => "Alpaca Markets";
    public override int Priority => 5;

    protected override string HttpClientName => HttpClientNames.AlpacaSymbolSearch;
    protected override string BaseUrl => "https://api.alpaca.markets/v2";
    protected override string ApiKeyEnvVar => "ALPACA_KEY_ID";
    protected override IReadOnlyList<string> AlternateApiKeyEnvVars => new[] { "ALPACA__KEYID" };

    protected override int MaxRequestsPerWindow => 200;
    protected override TimeSpan RateLimitWindow => TimeSpan.FromMinutes(1);
    protected override TimeSpan MinRequestDelay => TimeSpan.FromMilliseconds(300);

    public override IReadOnlyList<string> SupportedAssetTypes => new[] { "us_equity", "crypto" };
    public override IReadOnlyList<string> SupportedExchanges => new[] { "NASDAQ", "NYSE", "ARCA", "AMEX", "BATS" };

    public AlpacaSymbolSearchProviderRefactored(
        string? keyId = null,
        string? secretKey = null,
        HttpClient? httpClient = null,
        ILogger? log = null)
        : base(keyId, httpClient, log)
    {
        _secretKey = secretKey
            ?? Environment.GetEnvironmentVariable("ALPACA_SECRET_KEY")
            ?? Environment.GetEnvironmentVariable("ALPACA__SECRETKEY");
    }

    protected override bool HasValidCredentials()
    {
        return !string.IsNullOrEmpty(ApiKey) && !string.IsNullOrEmpty(_secretKey);
    }

    protected override void ConfigureHttpClientHeaders()
    {
        if (HasValidCredentials())
        {
            Http.DefaultRequestHeaders.Add("APCA-API-KEY-ID", ApiKey);
            Http.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", _secretKey);
        }
        Http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    protected override string BuildSearchUrl(string query, string? assetType, string? exchange)
    {
        var url = $"{BaseUrl}/assets?status=active";

        if (!string.IsNullOrEmpty(assetType))
        {
            var assetClass = assetType.ToLowerInvariant() switch
            {
                "us_equity" or "stock" or "equity" => "us_equity",
                "crypto" => "crypto",
                _ => "us_equity"
            };
            url += $"&asset_class={assetClass}";
        }
        else
        {
            url += "&asset_class=us_equity";
        }

        if (!string.IsNullOrEmpty(exchange))
        {
            url += $"&exchange={Uri.EscapeDataString(exchange)}";
        }

        return url;
    }

    protected override string BuildDetailsUrl(string symbol)
    {
        return $"{BaseUrl}/assets/{symbol}";
    }

    protected override IEnumerable<SymbolSearchResult> DeserializeSearchResults(string json, string query)
    {
        var assets = DeserializeJson<List<AlpacaAsset>>(json);

        if (assets is null || assets.Count == 0)
            return Enumerable.Empty<SymbolSearchResult>();

        var queryUpper = query.ToUpperInvariant();

        return assets
            .Where(a => !string.IsNullOrEmpty(a.Symbol) && a.Tradable)
            .Where(a => a.Symbol!.ToUpperInvariant().Contains(queryUpper) ||
                       (a.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) == true))
            .Select((a, i) => new SymbolSearchResult(
                Symbol: a.Symbol!,
                Name: a.Name ?? a.Symbol!,
                Exchange: a.Exchange,
                AssetType: MapAssetClass(a.AssetClass),
                Country: "US",
                Currency: "USD",
                Source: Name,
                MatchScore: CalculateMatchScore(query, a.Symbol!, a.Name, i)
            ));
    }

    protected override Task<SymbolDetails?> DeserializeDetailsAsync(string json, string symbol, CancellationToken ct)
    {
        var asset = DeserializeJson<AlpacaAsset>(json);

        if (asset is null)
            return Task.FromResult<SymbolDetails?>(null);

        var details = new SymbolDetails(
            Symbol: symbol,
            Name: asset.Name ?? symbol,
            Description: null,
            Exchange: asset.Exchange,
            AssetType: MapAssetClass(asset.AssetClass),
            Sector: null,
            Industry: null,
            Country: "US",
            Currency: "USD",
            MarketCap: null,
            AverageVolume: null,
            Week52High: null,
            Week52Low: null,
            LastPrice: null,
            WebUrl: null,
            LogoUrl: null,
            IpoDate: null,
            PaysDividend: null,
            DividendYield: null,
            PeRatio: null,
            SharesOutstanding: null,
            Figi: null,
            CompositeFigi: null,
            Isin: null,
            Cusip: null,
            Source: Name,
            LastUpdated: DateTimeOffset.UtcNow
        );

        return Task.FromResult<SymbolDetails?>(details);
    }

    /// <summary>
    /// Alpaca supports filtering natively via URL parameters, so we skip client-side filtering.
    /// </summary>
    protected override IEnumerable<SymbolSearchResult> ApplyFilters(
        IEnumerable<SymbolSearchResult> results,
        string? assetType,
        string? exchange)
    {
        // Alpaca supports filtering in the API, so we don't need to filter client-side
        return results;
    }

    private static string? MapAssetClass(string? assetClass)
    {
        return assetClass?.ToLowerInvariant() switch
        {
            "us_equity" => "Stock",
            "crypto" => "Crypto",
            _ => assetClass
        };
    }

    #region Alpaca API Models

    private sealed class AlpacaAsset
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("class")]
        public string? AssetClass { get; set; }

        [JsonPropertyName("exchange")]
        public string? Exchange { get; set; }

        [JsonPropertyName("symbol")]
        public string? Symbol { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("tradable")]
        public bool Tradable { get; set; }

        [JsonPropertyName("marginable")]
        public bool Marginable { get; set; }

        [JsonPropertyName("shortable")]
        public bool Shortable { get; set; }

        [JsonPropertyName("easy_to_borrow")]
        public bool EasyToBorrow { get; set; }

        [JsonPropertyName("fractionable")]
        public bool Fractionable { get; set; }

        [JsonPropertyName("maintenance_margin_requirement")]
        public decimal? MaintenanceMarginRequirement { get; set; }

        [JsonPropertyName("min_order_size")]
        public decimal? MinOrderSize { get; set; }

        [JsonPropertyName("min_trade_increment")]
        public decimal? MinTradeIncrement { get; set; }

        [JsonPropertyName("price_increment")]
        public decimal? PriceIncrement { get; set; }
    }

    #endregion
}

using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Core.Subscriptions.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.Http;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Alpaca;

/// <summary>
/// Symbol search provider using the Alpaca Markets API.
/// Provides asset search for US equities with trading status information.
/// </summary>
/// <remarks>
/// <para>
/// Extends <see cref="BaseSymbolSearchProvider"/> for HTTP client management,
/// rate limiting, disposal, and consistent error handling.
/// </para>
/// <para>
/// Assets are served by the Trading API, so requests go to the paper or live Trading API host
/// that matches the credentials' trading environment. A paper key pair is rejected by the live
/// host, which previously left every paper-credentialed search and detail lookup empty.
/// </para>
/// </remarks>
[ImplementsAdr("ADR-001", "Alpaca symbol search provider implementation")]
public sealed class AlpacaSymbolSearchProvider : BaseSymbolSearchProvider
{
    private readonly string? _secretKey;
    private readonly string _tradingApiBaseUrl;

    public override string Name => "alpaca";
    public override string DisplayName => "Alpaca Markets";
    public override int Priority => 5;

    protected override string HttpClientName => HttpClientNames.AlpacaSymbolSearch;
    protected override string BaseUrl => _tradingApiBaseUrl;
    protected override string ApiKeyEnvVar => "ALPACA_KEY_ID";
    protected override IReadOnlyList<string> AlternateApiKeyEnvVars => new[] { "ALPACA__KEYID" };

    protected override int MaxRequestsPerWindow => 200;
    protected override TimeSpan RateLimitWindow => TimeSpan.FromMinutes(1);
    protected override TimeSpan MinRequestDelay => TimeSpan.FromMilliseconds(300);

    public override IReadOnlyList<string> SupportedAssetTypes => new[] { "us_equity", "crypto", "us_option", "fixed_income" };
    public override IReadOnlyList<string> SupportedExchanges => new[] { "NASDAQ", "NYSE", "ARCA", "AMEX", "BATS" };

    /// <param name="keyId">Alpaca API key ID (falls back to the environment).</param>
    /// <param name="secretKey">Alpaca API secret key (falls back to the environment).</param>
    /// <param name="httpClient">Optional pre-built HTTP client.</param>
    /// <param name="log">Optional logger.</param>
    /// <param name="useSandbox">
    /// <see langword="true"/> for the paper Trading API, <see langword="false"/> for live. When
    /// omitted, the environment selected by <c>ALPACA_TRADING_ENVIRONMENT</c> is used (paper
    /// unless it says live), matching the brokerage gateway.
    /// </param>
    public AlpacaSymbolSearchProvider(
        string? keyId = null,
        string? secretKey = null,
        HttpClient? httpClient = null,
        ILogger? log = null,
        bool? useSandbox = null)
        : base(keyId, httpClient, log)
    {
        _tradingApiBaseUrl = $"{AlpacaTradingApi.ResolveBaseUrl(useSandbox)}/v2";
        _secretKey = secretKey
            ?? Environment.GetEnvironmentVariable("ALPACA_SECRET_KEY")
            ?? Environment.GetEnvironmentVariable("ALPACA__SECRETKEY");
        // The base constructor runs before this provider's second credential exists.
        ConfigureHttpClientHeaders();
    }

    protected override bool HasValidCredentials()
    {
        return !string.IsNullOrEmpty(ApiKey) && !string.IsNullOrEmpty(_secretKey);
    }

    protected override void ConfigureHttpClientHeaders()
    {
        Http.DefaultRequestHeaders.Remove("APCA-API-KEY-ID");
        Http.DefaultRequestHeaders.Remove("APCA-API-SECRET-KEY");
        if (HasValidCredentials())
        {
            Http.DefaultRequestHeaders.Add("APCA-API-KEY-ID", ApiKey);
            Http.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", _secretKey);
        }
        if (!Http.DefaultRequestHeaders.Accept.Any(header => header.MediaType == "application/json"))
            Http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    protected override string BuildSearchUrl(string query, string? assetType, string? exchange)
    {
        var url = $"{BaseUrl}/assets?status=active";

        if (!string.IsNullOrWhiteSpace(assetType))
        {
            var assetClass = assetType.ToLowerInvariant() switch
            {
                "us_equity" or "stock" or "equity" => "us_equity",
                "crypto" => "crypto",
                "us_option" or "option" or "options" => "us_option",
                "fixed_income" or "bond" or "bonds" => "fixed_income",
                _ => throw new ArgumentOutOfRangeException(nameof(assetType), assetType, "Unsupported Alpaca asset class.")
            };
            url += $"&asset_class={assetClass}";
        }
        else
        {
            // The endpoint has no server-side query or pagination parameter. Keep the
            // unfiltered path bounded by its standard equity asset class.
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
        )
        {
            TradingEligibility = new SymbolTradingEligibility(asset.Tradable, asset.Marginable, asset.Shortable,
                asset.EasyToBorrow, asset.Fractionable, asset.MaintenanceMarginRequirement, asset.MinOrderSize,
                asset.MinTradeIncrement, asset.PriceIncrement, Name)
        };

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
            "us_option" => "Option",
            "fixed_income" => "FixedIncome",
            _ => assetClass
        };
    }


    /// <remarks>
    /// The Trading API reports the crypto order increments as JSON strings and the maintenance
    /// margin as a JSON number; both forms are accepted so one string-encoded field cannot
    /// discard the whole asset list.
    /// </remarks>
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
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal? MaintenanceMarginRequirement { get; set; }

        [JsonPropertyName("min_order_size")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal? MinOrderSize { get; set; }

        [JsonPropertyName("min_trade_increment")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal? MinTradeIncrement { get; set; }

        [JsonPropertyName("price_increment")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal? PriceIncrement { get; set; }
    }

}

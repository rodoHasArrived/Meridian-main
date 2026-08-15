using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Meridian.Contracts.Text;
using Meridian.Core.Subscriptions.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Tiingo;

/// <summary>
/// Symbol search provider using Tiingo's utilities search endpoint.
/// </summary>
[DataSource("tiingo-symbols", "Tiingo (Symbol Search)", DataSourceType.Reference, DataSourceCategory.Free,
    Priority = 15, Description = "Symbol search via Tiingo utilities API")]
[ImplementsAdr("ADR-001", "Tiingo symbol search provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
[RequiresCredential("TIINGO_API_TOKEN",
    EnvironmentVariables = new[] { "TIINGO_API_TOKEN", "TIINGO__TOKEN" },
    DisplayName = "API Token",
    Description = "Tiingo API token from https://www.tiingo.com/account/api/token")]
public sealed class TiingoSymbolSearchProvider : BaseSymbolSearchProvider
{
    public override string Name => "tiingo";
    public override string DisplayName => "Tiingo";
    public override int Priority => 15;

    protected override string HttpClientName => HttpClientNames.TiingoSymbolSearch;
    protected override string BaseUrl => "https://api.tiingo.com/tiingo/utilities/search";
    protected override string ApiKeyEnvVar => "TIINGO_API_TOKEN";
    protected override IReadOnlyList<string> AlternateApiKeyEnvVars => new[]
    {
        "TIINGO__TOKEN",
        "TIINGO_TOKEN",
        "MDC_TIINGO_TOKEN"
    };

    protected override int MaxRequestsPerWindow => 50;
    protected override TimeSpan RateLimitWindow => TimeSpan.FromHours(1);
    protected override TimeSpan MinRequestDelay => TimeSpan.FromMilliseconds(1500);

    public override IReadOnlyList<string> SupportedAssetTypes => new[]
    {
        "Stock",
        "ETF",
        "Mutual Fund",
        "Index",
        "Fund"
    };

    public override IReadOnlyList<string> SupportedExchanges => new[]
    {
        "NASDAQ",
        "NYSE",
        "NYSE ARCA",
        "NYSE MKT",
        "OTC",
        "LSE",
        "TSX",
        "TSXV",
        "ASX",
        "HKEX",
        "XETRA"
    };

    public TiingoSymbolSearchProvider(
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
            Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", ApiKey);
        }
    }

    protected override string BuildSearchUrl(string query, string? assetType, string? exchange)
        => $"{BaseUrl}?query={Uri.EscapeDataString(query)}&token={Uri.EscapeDataString(ApiKey ?? string.Empty)}";

    protected override string BuildDetailsUrl(string symbol)
        => BuildSearchUrl(symbol, assetType: null, exchange: null);

    protected override IEnumerable<SymbolSearchResult> DeserializeSearchResults(string json, string query)
    {
        if (IsErrorBody(json))
        {
            return Enumerable.Empty<SymbolSearchResult>();
        }

        var data = DeserializeJson<List<TiingoSearchMatch>>(json);
        if (data is null || data.Count == 0)
        {
            return Enumerable.Empty<SymbolSearchResult>();
        }

        return data
            .Where(static match => match.IsActive != false && !string.IsNullOrWhiteSpace(match.Ticker))
            .Select((match, index) =>
            {
                var symbol = match.Ticker!.Trim().ToUpperInvariant();
                var name = string.IsNullOrWhiteSpace(match.Name) ? symbol : match.Name!.Trim();

                return new SymbolSearchResult(
                    Symbol: symbol,
                    Name: name,
                    Exchange: FirstNonBlank(match.ExchangeCode, match.Exchange, match.ExchangeName),
                    AssetType: TextPrimitives.NormalizeOptional(match.AssetType),
                    Country: TextPrimitives.NormalizeOptional(match.CountryCode),
                    Currency: TextPrimitives.NormalizeOptional(match.Currency),
                    Source: Name,
                    MatchScore: CalculateMatchScore(query, symbol, name, index));
            });
    }

    protected override Task<SymbolDetails?> DeserializeDetailsAsync(string json, string symbol, CancellationToken ct)
    {
        var matches = DeserializeSearchResults(json, symbol).ToList();
        var match = matches.FirstOrDefault(r => r.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)) ??
            matches.FirstOrDefault();

        if (match is null)
        {
            return Task.FromResult<SymbolDetails?>(null);
        }

        var details = new SymbolDetails(
            Symbol: match.Symbol,
            Name: match.Name,
            Description: null,
            Exchange: match.Exchange,
            AssetType: match.AssetType,
            Sector: null,
            Industry: null,
            Country: match.Country,
            Currency: match.Currency,
            Source: Name,
            LastUpdated: DateTimeOffset.UtcNow);

        return Task.FromResult<SymbolDetails?>(details);
    }

    private static bool IsErrorBody(string json)
    {
        var trimmed = json.TrimStart();
        return trimmed.StartsWith('{') &&
            (trimmed.Contains("error", StringComparison.OrdinalIgnoreCase) ||
             trimmed.Contains("message", StringComparison.OrdinalIgnoreCase) ||
             trimmed.Contains("detail", StringComparison.OrdinalIgnoreCase));
    }

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private sealed class TiingoSearchMatch
    {
        [JsonPropertyName("ticker")]
        public string? Ticker { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("assetType")]
        public string? AssetType { get; init; }

        [JsonPropertyName("exchangeCode")]
        public string? ExchangeCode { get; init; }

        [JsonPropertyName("exchange")]
        public string? Exchange { get; init; }

        [JsonPropertyName("exchangeName")]
        public string? ExchangeName { get; init; }

        [JsonPropertyName("countryCode")]
        public string? CountryCode { get; init; }

        [JsonPropertyName("currency")]
        public string? Currency { get; init; }

        [JsonPropertyName("isActive")]
        public bool? IsActive { get; init; }
    }
}

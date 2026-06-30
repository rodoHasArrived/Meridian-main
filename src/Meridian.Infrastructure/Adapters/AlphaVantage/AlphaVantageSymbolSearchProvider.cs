using System.Globalization;
using System.Text.Json.Serialization;
using Meridian.Core.Subscriptions.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Serilog;

namespace Meridian.Infrastructure.Adapters.AlphaVantage;

/// <summary>
/// Symbol search provider using the Alpha Vantage SYMBOL_SEARCH endpoint.
/// </summary>
[DataSource("alphavantage-symbols", "Alpha Vantage (Symbol Search)", DataSourceType.Reference, DataSourceCategory.Free,
    Priority = 25, Description = "Keyword symbol search via Alpha Vantage free-tier API")]
[ImplementsAdr("ADR-001", "Alpha Vantage symbol search provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
[RequiresCredential("ALPHA_VANTAGE_API_KEY",
    EnvironmentVariables = new[] { "ALPHA_VANTAGE_API_KEY", "ALPHAVANTAGE__APIKEY" },
    DisplayName = "API Key",
    Description = "Alpha Vantage API key from https://www.alphavantage.co/support/#api-key")]
public sealed class AlphaVantageSymbolSearchProvider : BaseSymbolSearchProvider
{
    public override string Name => "alphavantage";
    public override string DisplayName => "Alpha Vantage";
    public override int Priority => 25;

    protected override string HttpClientName => HttpClientNames.AlphaVantageSymbolSearch;
    protected override string BaseUrl => "https://www.alphavantage.co/query";
    protected override string ApiKeyEnvVar => "ALPHA_VANTAGE_API_KEY";
    protected override IReadOnlyList<string> AlternateApiKeyEnvVars => new[]
    {
        "ALPHAVANTAGE__APIKEY",
        "ALPHAVANTAGE_API_KEY",
        "MDC_ALPHA_VANTAGE_API_KEY"
    };

    protected override int MaxRequestsPerWindow => 5;
    protected override TimeSpan RateLimitWindow => TimeSpan.FromMinutes(1);
    protected override TimeSpan MinRequestDelay => TimeSpan.FromSeconds(12);

    public override IReadOnlyList<string> SupportedAssetTypes => new[]
    {
        "Equity",
        "ETF",
        "Mutual Fund"
    };

    public override IReadOnlyList<string> SupportedExchanges => new[]
    {
        "United States",
        "Canada",
        "United Kingdom",
        "Europe",
        "India"
    };

    public AlphaVantageSymbolSearchProvider(
        string? apiKey = null,
        HttpClient? httpClient = null,
        ILogger? log = null)
        : base(apiKey, httpClient, log)
    {
    }

    protected override string BuildSearchUrl(string query, string? assetType, string? exchange)
        => $"{BaseUrl}?function=SYMBOL_SEARCH&keywords={Uri.EscapeDataString(query)}&apikey={Uri.EscapeDataString(ApiKey ?? string.Empty)}";

    protected override string BuildDetailsUrl(string symbol)
        => BuildSearchUrl(symbol, assetType: null, exchange: null);

    protected override IEnumerable<SymbolSearchResult> DeserializeSearchResults(string json, string query)
    {
        if (IsErrorOrRateLimitBody(json))
        {
            return Enumerable.Empty<SymbolSearchResult>();
        }

        var data = DeserializeJson<AlphaVantageSearchResponse>(json);
        if (data?.BestMatches is null || data.BestMatches.Count == 0)
        {
            return Enumerable.Empty<SymbolSearchResult>();
        }

        return data.BestMatches
            .Where(static match => !string.IsNullOrWhiteSpace(match.Symbol))
            .Select((match, index) => new SymbolSearchResult(
                Symbol: match.Symbol!.Trim().ToUpperInvariant(),
                Name: string.IsNullOrWhiteSpace(match.Name) ? match.Symbol!.Trim().ToUpperInvariant() : match.Name!.Trim(),
                Exchange: string.IsNullOrWhiteSpace(match.Region) ? null : match.Region!.Trim(),
                AssetType: string.IsNullOrWhiteSpace(match.Type) ? null : match.Type!.Trim(),
                Country: MapCountry(match.Region),
                Currency: string.IsNullOrWhiteSpace(match.Currency) ? null : match.Currency!.Trim(),
                Source: Name,
                MatchScore: Math.Max(CalculateMatchScore(query, match.Symbol!.Trim(), match.Name, index), ParseProviderScore(match.MatchScore))));
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

    private static bool IsErrorOrRateLimitBody(string json)
        => json.Contains("\"Note\"", StringComparison.Ordinal) ||
            json.Contains("\"Error Message\"", StringComparison.Ordinal) ||
            json.Contains("Thank you for using Alpha Vantage", StringComparison.Ordinal);

    private static int ParseProviderScore(string? score)
    {
        if (!decimal.TryParse(score, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return 0;
        }

        return (int)Math.Clamp(decimal.Round(parsed * 100m, MidpointRounding.AwayFromZero), 0m, 100m);
    }

    private static string? MapCountry(string? region)
    {
        if (string.IsNullOrWhiteSpace(region))
        {
            return null;
        }

        return region.Trim().ToUpperInvariant() switch
        {
            "UNITED STATES" => "US",
            "UNITED KINGDOM" => "GB",
            "CANADA" => "CA",
            "INDIA" => "IN",
            _ => null
        };
    }

    private sealed class AlphaVantageSearchResponse
    {
        [JsonPropertyName("bestMatches")]
        public List<AlphaVantageSearchMatch>? BestMatches { get; init; }
    }

    private sealed class AlphaVantageSearchMatch
    {
        [JsonPropertyName("1. symbol")]
        public string? Symbol { get; init; }

        [JsonPropertyName("2. name")]
        public string? Name { get; init; }

        [JsonPropertyName("3. type")]
        public string? Type { get; init; }

        [JsonPropertyName("4. region")]
        public string? Region { get; init; }

        [JsonPropertyName("8. currency")]
        public string? Currency { get; init; }

        [JsonPropertyName("9. matchScore")]
        public string? MatchScore { get; init; }
    }
}

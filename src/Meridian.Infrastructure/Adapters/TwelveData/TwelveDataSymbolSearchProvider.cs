using System.Text.Json.Serialization;
using Meridian.Contracts.Text;
using Meridian.Core.Subscriptions.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Serilog;

namespace Meridian.Infrastructure.Adapters.TwelveData;

/// <summary>
/// Symbol search provider using the Twelve Data /symbol_search discovery endpoint.
/// </summary>
[DataSource("twelvedata-symbols", "Twelve Data (Symbol Search)", DataSourceType.Reference, DataSourceCategory.Free,
    Priority = 22, Description = "Symbol search via Twelve Data discovery API")]
[ImplementsAdr("ADR-001", "Twelve Data symbol search provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
[RequiresCredential("TWELVEDATA_API_KEY",
    EnvironmentVariables = new[] { "TWELVEDATA_API_KEY", "TWELVEDATA__APIKEY" },
    DisplayName = "API Key",
    Description = "Twelve Data API key from https://twelvedata.com/account/api-keys")]
public sealed class TwelveDataSymbolSearchProvider : BaseSymbolSearchProvider
{
    public override string Name => "twelvedata";
    public override string DisplayName => "Twelve Data";
    public override int Priority => 22;

    protected override string HttpClientName => HttpClientNames.TwelveDataSymbolSearch;
    protected override string BaseUrl => "https://api.twelvedata.com/symbol_search";
    protected override string ApiKeyEnvVar => "TWELVEDATA_API_KEY";
    protected override IReadOnlyList<string> AlternateApiKeyEnvVars => new[]
    {
        "TWELVEDATA__APIKEY",
        "TWELVEDATA_APIKEY",
        "MDC_TWELVEDATA_API_KEY"
    };

    protected override int MaxRequestsPerWindow => 8;
    protected override TimeSpan RateLimitWindow => TimeSpan.FromMinutes(1);
    protected override TimeSpan MinRequestDelay => TimeSpan.FromMilliseconds(7500);

    public override IReadOnlyList<string> SupportedAssetTypes => new[]
    {
        "Common Stock",
        "ETF",
        "Index",
        "REIT",
        "American Depositary Receipt",
        "Digital Currency",
        "Physical Currency",
        "Mutual Fund"
    };

    public override IReadOnlyList<string> SupportedExchanges => new[]
    {
        "NASDAQ",
        "NYSE",
        "NYSE ARCA",
        "OTC",
        "LSE",
        "TSX",
        "TSXV",
        "XETRA",
        "HKEX",
        "ASX",
        "NSE"
    };

    public TwelveDataSymbolSearchProvider(
        string? apiKey = null,
        HttpClient? httpClient = null,
        ILogger? log = null)
        : base(apiKey, httpClient, log)
    {
    }

    protected override string BuildSearchUrl(string query, string? assetType, string? exchange)
        => $"{BaseUrl}?symbol={Uri.EscapeDataString(query)}&outputsize=120&apikey={Uri.EscapeDataString(ApiKey ?? string.Empty)}";

    protected override string BuildDetailsUrl(string symbol)
        => BuildSearchUrl(symbol, assetType: null, exchange: null);

    protected override IEnumerable<SymbolSearchResult> DeserializeSearchResults(string json, string query)
    {
        var data = DeserializeJson<TwelveDataSearchResponse>(json);
        if (data?.Data is null || IsErrorStatus(data.Status))
        {
            return Enumerable.Empty<SymbolSearchResult>();
        }

        return data.Data
            .Where(static match => !string.IsNullOrWhiteSpace(match.Symbol))
            .Select((match, index) =>
            {
                var symbol = match.Symbol!.Trim().ToUpperInvariant();
                var name = string.IsNullOrWhiteSpace(match.InstrumentName) ? symbol : match.InstrumentName!.Trim();
                var exchange = FirstNonBlank(match.Exchange, match.MicCode);

                return new SymbolSearchResult(
                    Symbol: symbol,
                    Name: name,
                    Exchange: exchange,
                    AssetType: TextPrimitives.NormalizeOptional(match.InstrumentType),
                    Country: MapCountry(match.Country),
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

    private static bool IsErrorStatus(string? status)
        => status is not null && !status.Equals("ok", StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string? MapCountry(string? country)
    {
        var normalized = TextPrimitives.NormalizeOptional(country);
        if (normalized is null)
        {
            return null;
        }

        return normalized.ToUpperInvariant() switch
        {
            "UNITED STATES" => "US",
            "UNITED KINGDOM" => "GB",
            "CANADA" => "CA",
            "AUSTRALIA" => "AU",
            "GERMANY" => "DE",
            "HONG KONG" => "HK",
            "INDIA" => "IN",
            _ => normalized
        };
    }

    private sealed class TwelveDataSearchResponse
    {
        [JsonPropertyName("data")]
        public List<TwelveDataSearchMatch>? Data { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }
    }

    private sealed class TwelveDataSearchMatch
    {
        [JsonPropertyName("symbol")]
        public string? Symbol { get; init; }

        [JsonPropertyName("instrument_name")]
        public string? InstrumentName { get; init; }

        [JsonPropertyName("exchange")]
        public string? Exchange { get; init; }

        [JsonPropertyName("mic_code")]
        public string? MicCode { get; init; }

        [JsonPropertyName("instrument_type")]
        public string? InstrumentType { get; init; }

        [JsonPropertyName("country")]
        public string? Country { get; init; }

        [JsonPropertyName("currency")]
        public string? Currency { get; init; }
    }
}

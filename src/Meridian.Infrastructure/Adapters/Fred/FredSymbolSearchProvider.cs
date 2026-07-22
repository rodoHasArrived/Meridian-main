using System.Text.Json.Serialization;
using Meridian.Core.Subscriptions.Models;
using Meridian.Contracts.Domain;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Fred;

/// <summary>
/// Symbol search provider for FRED economic series discovery.
/// </summary>
[DataSource("fred-symbols", "FRED Economic Data (Series Search)", DataSourceType.Reference, DataSourceCategory.Free,
    Priority = 28, Description = "Economic series search via the FRED series/search API")]
[ImplementsAdr("ADR-001", "FRED symbol search provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
[RequiresCredential("FRED_API_KEY",
    EnvironmentVariables = new[] { "FRED_API_KEY", "FRED__APIKEY" },
    DisplayName = "API Key",
    Description = "FRED API key from https://fred.stlouisfed.org/docs/api/api_key.html")]
public sealed class FredSymbolSearchProvider : BaseSymbolSearchProvider
{
    public override string Name => "fred";
    public override string DisplayName => "FRED Economic Data";
    public override int Priority => 28;

    protected override string HttpClientName => HttpClientNames.FredSymbolSearch;
    protected override string BaseUrl => "https://api.stlouisfed.org/fred/series/search";
    protected override string ApiKeyEnvVar => "FRED_API_KEY";
    protected override IReadOnlyList<string> AlternateApiKeyEnvVars => new[]
    {
        "FRED__APIKEY",
        "MDC_FRED_API_KEY"
    };

    protected override int MaxRequestsPerWindow => 120;
    protected override TimeSpan RateLimitWindow => TimeSpan.FromMinutes(1);
    protected override TimeSpan MinRequestDelay => TimeSpan.FromMilliseconds(500);

    public override IReadOnlyList<string> SupportedAssetTypes => new[]
    {
        "Economic Series"
    };

    public override IReadOnlyList<string> SupportedExchanges => new[]
    {
        "FRED"
    };

    public FredSymbolSearchProvider(
        string? apiKey = null,
        HttpClient? httpClient = null,
        ILogger? log = null)
        : base(apiKey, httpClient, log)
    {
    }

    protected override string BuildSearchUrl(string query, string? assetType, string? exchange)
        => $"{BaseUrl}?search_text={Uri.EscapeDataString(query)}&api_key={Uri.EscapeDataString(ApiKey ?? string.Empty)}&file_type=json&order_by=search_rank&sort_order=asc&limit=100";

    protected override string BuildDetailsUrl(string symbol)
        => BuildSearchUrl(symbol, assetType: null, exchange: null);

    protected override IEnumerable<SymbolSearchResult> DeserializeSearchResults(string json, string query)
    {
        if (IsErrorBody(json))
        {
            return Enumerable.Empty<SymbolSearchResult>();
        }

        var data = DeserializeJson<FredSeriesSearchResponse>(json);
        if (data?.Series is null || data.Series.Count == 0)
        {
            return Enumerable.Empty<SymbolSearchResult>();
        }

        return data.Series
            .Where(static series => !string.IsNullOrWhiteSpace(series.Id))
            .Select((series, index) =>
            {
                var id = series.Id!.Trim().ToUpperInvariant();
                var title = string.IsNullOrWhiteSpace(series.Title) ? id : series.Title!.Trim();
                var matchScore = Math.Max(
                    CalculateMatchScore(query, id, title, index),
                    NormalizePopularity(series.Popularity));

                return new SymbolSearchResult(
                    Symbol: id,
                    Name: title,
                    Exchange: "FRED",
                    AssetType: "Economic Series",
                    Country: "US",
                    Currency: null,
                    Source: Name,
                    MatchScore: matchScore);
            });
    }

    protected override Task<SymbolDetails?> DeserializeDetailsAsync(string json, string symbol, CancellationToken ct)
    {
        var matches = DeserializeJson<FredSeriesSearchResponse>(json)?.Series ?? [];
        var match = matches.FirstOrDefault(series => string.Equals(series.Id, symbol, StringComparison.OrdinalIgnoreCase)) ??
            matches.FirstOrDefault();

        if (match is null || string.IsNullOrWhiteSpace(match.Id))
        {
            return Task.FromResult<SymbolDetails?>(null);
        }

        var id = match.Id.Trim().ToUpperInvariant();
        var details = new SymbolDetails(
            Symbol: id,
            Name: string.IsNullOrWhiteSpace(match.Title) ? id : match.Title!.Trim(),
            Description: string.IsNullOrWhiteSpace(match.Notes) ? null : match.Notes!.Trim(),
            Exchange: "FRED",
            AssetType: "Economic Series",
            Sector: null,
            Industry: string.IsNullOrWhiteSpace(match.FrequencyShort) ? match.Frequency : match.FrequencyShort,
            Country: "US",
            Currency: null,
            Source: Name,
            LastUpdated: DateTimeOffset.UtcNow);

        return Task.FromResult<SymbolDetails?>(details);
    }

    private static bool IsErrorBody(string json)
        => json.Contains("\"error_code\"", StringComparison.OrdinalIgnoreCase) ||
            json.Contains("\"error_message\"", StringComparison.OrdinalIgnoreCase);

    private static int NormalizePopularity(int? popularity)
        => popularity.HasValue ? Math.Clamp(popularity.Value, 0, 100) : 0;

    private sealed class FredSeriesSearchResponse
    {
        [JsonPropertyName("seriess")]
        public List<FredSeries>? Series { get; init; }
    }

    private sealed class FredSeries
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("frequency")]
        public string? Frequency { get; init; }

        [JsonPropertyName("frequency_short")]
        public string? FrequencyShort { get; init; }

        [JsonPropertyName("popularity")]
        public int? Popularity { get; init; }

        [JsonPropertyName("notes")]
        public string? Notes { get; init; }
    }
}

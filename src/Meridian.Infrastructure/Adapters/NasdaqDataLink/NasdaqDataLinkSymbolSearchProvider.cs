using System.Text.Json.Serialization;
using System.Text.Json;
using Meridian.Contracts.Domain;
using Meridian.Core.Subscriptions.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Serilog;

namespace Meridian.Infrastructure.Adapters.NasdaqDataLink;

/// <summary>
/// Symbol/reference search provider for Nasdaq Data Link dataset discovery.
/// </summary>
[DataSource("nasdaq-symbols", "Nasdaq Data Link Dataset Search", DataSourceType.Reference, DataSourceCategory.Aggregator,
    Priority = 30, Description = "Dataset-code discovery via Nasdaq Data Link datasets search")]
[ImplementsAdr("ADR-001", "Nasdaq Data Link symbol search provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
[RequiresCredential("NASDAQ_DATA_LINK_API_KEY",
    EnvironmentVariables = new[] { "NASDAQ_DATA_LINK_API_KEY", "NASDAQ__APIKEY" },
    DisplayName = "API Key",
    Description = "Nasdaq Data Link API key from https://data.nasdaq.com/account/profile")]
public sealed class NasdaqDataLinkSymbolSearchProvider : BaseSymbolSearchProvider
{
    public override string Name => "nasdaq";
    public override string DisplayName => "Nasdaq Data Link";
    public override int Priority => 30;

    protected override string HttpClientName => HttpClientNames.NasdaqDataLinkHistorical;
    protected override string BaseUrl => "https://data.nasdaq.com/api/v3/datasets.json";
    protected override string ApiKeyEnvVar => "NASDAQ_DATA_LINK_API_KEY";
    protected override IReadOnlyList<string> AlternateApiKeyEnvVars => new[]
    {
        "NASDAQ__APIKEY",
        "NASDAQ_API_KEY",
        "MDC_NASDAQ_DATA_LINK_API_KEY"
    };

    protected override int MaxRequestsPerWindow => 50;
    protected override TimeSpan RateLimitWindow => TimeSpan.FromDays(1);
    protected override TimeSpan MinRequestDelay => TimeSpan.FromMilliseconds(100);

    public override IReadOnlyList<string> SupportedAssetTypes => new[]
    {
        "Dataset"
    };

    public override IReadOnlyList<string> SupportedExchanges => new[]
    {
        "EOD",
        "WIKI",
        "QDL",
        "NDAQ"
    };

    public NasdaqDataLinkSymbolSearchProvider(
        string? apiKey = null,
        HttpClient? httpClient = null,
        ILogger? log = null)
        : base(apiKey, httpClient, log)
    {
    }

    protected override string BuildSearchUrl(string query, string? assetType, string? exchange)
    {
        var queryParams = new List<string>
        {
            $"query={Uri.EscapeDataString(query)}",
            "per_page=100",
            "page=1",
            $"api_key={Uri.EscapeDataString(ApiKey ?? string.Empty)}"
        };

        if (!string.IsNullOrWhiteSpace(exchange))
        {
            queryParams.Add($"database_code={Uri.EscapeDataString(exchange.Trim().ToUpperInvariant())}");
        }

        return $"{BaseUrl}?{string.Join("&", queryParams)}";
    }

    protected override string BuildDetailsUrl(string symbol)
    {
        var query = symbol.Contains('/', StringComparison.Ordinal)
            ? symbol[(symbol.IndexOf('/') + 1)..]
            : symbol;
        return BuildSearchUrl(query, assetType: null, exchange: null);
    }

    protected override IEnumerable<SymbolSearchResult> DeserializeSearchResults(string json, string query)
    {
        if (IsErrorBody(json))
        {
            return Enumerable.Empty<SymbolSearchResult>();
        }

        var response = DeserializeJson<NasdaqDataLinkDatasetSearchResponse>(json);
        if (response?.Datasets is null || response.Datasets.Count == 0)
        {
            return Enumerable.Empty<SymbolSearchResult>();
        }

        return response.Datasets
            .Where(static dataset =>
                !string.IsNullOrWhiteSpace(dataset.DatasetCode) &&
                !string.IsNullOrWhiteSpace(dataset.DatabaseCode))
            .Select((dataset, index) =>
            {
                var datasetCode = dataset.DatasetCode!.Trim().ToUpperInvariant();
                var databaseCode = dataset.DatabaseCode!.Trim().ToUpperInvariant();
                var symbol = $"{databaseCode}/{datasetCode}";
                var name = string.IsNullOrWhiteSpace(dataset.Name) ? symbol : dataset.Name!.Trim();

                return new SymbolSearchResult(
                    Symbol: symbol,
                    Name: name,
                    Exchange: databaseCode,
                    AssetType: "Dataset",
                    Country: null,
                    Currency: null,
                    Source: Name,
                    MatchScore: CalculateMatchScore(query, symbol, name, index));
            });
    }

    protected override Task<SymbolDetails?> DeserializeDetailsAsync(string json, string symbol, CancellationToken ct)
    {
        var datasets = DeserializeJson<NasdaqDataLinkDatasetSearchResponse>(json)?.Datasets ?? [];
        var normalizedSymbol = symbol.Trim().ToUpperInvariant();

        var match = datasets.FirstOrDefault(dataset =>
            IsDatasetMatch(dataset, normalizedSymbol)) ?? datasets.FirstOrDefault();

        if (match is null ||
            string.IsNullOrWhiteSpace(match.DatasetCode) ||
            string.IsNullOrWhiteSpace(match.DatabaseCode))
        {
            return Task.FromResult<SymbolDetails?>(null);
        }

        var datasetCode = match.DatasetCode.Trim().ToUpperInvariant();
        var databaseCode = match.DatabaseCode.Trim().ToUpperInvariant();
        var fullCode = $"{databaseCode}/{datasetCode}";
        var name = string.IsNullOrWhiteSpace(match.Name) ? fullCode : match.Name!.Trim();
        var details = new SymbolDetails(
            Symbol: fullCode,
            Name: name,
            Description: string.IsNullOrWhiteSpace(match.Description) ? null : match.Description!.Trim(),
            Exchange: databaseCode,
            AssetType: "Dataset",
            Sector: null,
            Industry: string.IsNullOrWhiteSpace(match.Frequency) ? null : match.Frequency!.Trim(),
            Country: null,
            Currency: null,
            Source: Name,
            LastUpdated: DateTimeOffset.UtcNow);

        return Task.FromResult<SymbolDetails?>(details);
    }

    private static bool IsDatasetMatch(NasdaqDataLinkDataset dataset, string normalizedSymbol)
    {
        if (string.IsNullOrWhiteSpace(dataset.DatasetCode) || string.IsNullOrWhiteSpace(dataset.DatabaseCode))
        {
            return false;
        }

        var datasetCode = dataset.DatasetCode.Trim().ToUpperInvariant();
        var databaseCode = dataset.DatabaseCode.Trim().ToUpperInvariant();
        return string.Equals(datasetCode, normalizedSymbol, StringComparison.OrdinalIgnoreCase) ||
            string.Equals($"{databaseCode}/{datasetCode}", normalizedSymbol, StringComparison.OrdinalIgnoreCase);
    }

    protected override bool IsQuotaExceededResponse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("quandl_error", out var error) &&
                error.ValueKind == JsonValueKind.Object &&
                error.TryGetProperty("code", out var code) &&
                code.GetString() is { } value &&
                value.StartsWith("QELx", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return base.IsQuotaExceededResponse(json);
    }

    private static bool IsErrorBody(string json)
        => json.Contains("\"quandl_error\"", StringComparison.OrdinalIgnoreCase) ||
            json.Contains("\"error_code\"", StringComparison.OrdinalIgnoreCase) ||
            json.Contains("\"error_message\"", StringComparison.OrdinalIgnoreCase);

    private sealed class NasdaqDataLinkDatasetSearchResponse
    {
        [JsonPropertyName("datasets")]
        public List<NasdaqDataLinkDataset>? Datasets { get; init; }
    }

    private sealed class NasdaqDataLinkDataset
    {
        [JsonPropertyName("dataset_code")]
        public string? DatasetCode { get; init; }

        [JsonPropertyName("database_code")]
        public string? DatabaseCode { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("frequency")]
        public string? Frequency { get; init; }
    }
}

// ✅ ADR-001: ISymbolSearchProvider contract via BaseSymbolSearchProvider
// ✅ ADR-004: CancellationToken on all async methods
// ✅ ADR-005: Attribute-based provider discovery
// ✅ ADR-014: Source-generated JSON via RobinhoodJsonContext

using System.Text.Json;
using Meridian.Application.Subscriptions.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Robinhood;

/// <summary>
/// Symbol search provider backed by Robinhood's public instruments API.
/// No authentication required.
/// </summary>
/// <remarks>
/// Instruments search endpoint:
/// GET https://api.robinhood.com/instruments/?query={symbol}
/// </remarks>
[DataSource("robinhood-symbols", "Robinhood (Symbol Search)", DataSourceType.Historical, DataSourceCategory.Free,
    Priority = 25, Description = "Symbol search via Robinhood's public instruments API")]
[ImplementsAdr("ADR-001", "Robinhood symbol search provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
public sealed class RobinhoodSymbolSearchProvider : BaseSymbolSearchProvider
{
    public override string Name => "robinhood";
    public override string DisplayName => "Robinhood";
    public override int Priority => 25;

    protected override string HttpClientName => HttpClientNames.RobinhoodSymbolSearch;
    protected override string BaseUrl => "https://api.robinhood.com";
    protected override string ApiKeyEnvVar => string.Empty; // No API key needed

    protected override int MaxRequestsPerWindow => 10;
    protected override TimeSpan RateLimitWindow => TimeSpan.FromMinutes(1);
    protected override TimeSpan MinRequestDelay => TimeSpan.FromSeconds(1);

    public override IReadOnlyList<string> SupportedAssetTypes => new[]
    {
        "stock", "etp", "adr"
    };

    public override IReadOnlyList<string> SupportedExchanges => new[]
    {
        "NASDAQ", "NYSE", "NYSE ARCA", "NYSE AMERICAN", "BATS", "OTC"
    };

    // No API key required — override to always return true
    protected override bool HasValidCredentials() => true;

    public RobinhoodSymbolSearchProvider(HttpClient? httpClient = null, ILogger? log = null)
        : base(apiKey: null, httpClient, log)
    {
    }

    protected override string BuildSearchUrl(string query, string? assetType, string? exchange)
    {
        return $"{BaseUrl}/instruments/?query={Uri.EscapeDataString(query)}";
    }

    protected override string BuildDetailsUrl(string symbol)
    {
        return $"{BaseUrl}/instruments/?symbol={Uri.EscapeDataString(symbol)}";
    }

    protected override IEnumerable<SymbolSearchResult> DeserializeSearchResults(string json, string query)
    {
        RobinhoodInstrumentsResponse? data;
        try
        {
            data = JsonSerializer.Deserialize(json, RobinhoodJsonContext.Default.RobinhoodInstrumentsResponse);
        }
        catch (JsonException ex)
        {
            Log.Warning("Failed to parse Robinhood instruments response: {Error}", ex.Message);
            return Enumerable.Empty<SymbolSearchResult>();
        }

        if (data?.Results is null || data.Results.Length == 0)
            return Enumerable.Empty<SymbolSearchResult>();

        return data.Results
            .Where(r => !string.IsNullOrEmpty(r.Symbol))
            .Select((r, i) => new SymbolSearchResult(
                Symbol: r.Symbol!,
                Name: r.Name ?? r.Symbol!,
                Exchange: MapExchange(r.Market),
                AssetType: r.Type,
                Country: r.Country ?? "US",
                Currency: "USD",
                Source: Name,
                MatchScore: CalculateMatchScore(query, r.Symbol!, r.Name, i)
            ));
    }

    protected override Task<SymbolDetails?> DeserializeDetailsAsync(
        string json, string symbol, CancellationToken ct)
    {
        RobinhoodInstrumentsResponse? data;
        try
        {
            data = JsonSerializer.Deserialize(json, RobinhoodJsonContext.Default.RobinhoodInstrumentsResponse);
        }
        catch (JsonException ex)
        {
            Log.Warning("Failed to parse Robinhood instrument details for {Symbol}: {Error}", symbol, ex.Message);
            return Task.FromResult<SymbolDetails?>(null);
        }

        var instrument = data?.Results?.FirstOrDefault(r =>
            string.Equals(r.Symbol, symbol, StringComparison.OrdinalIgnoreCase));

        if (instrument is null)
            return Task.FromResult<SymbolDetails?>(null);

        return Task.FromResult<SymbolDetails?>(new SymbolDetails(
            Symbol: instrument.Symbol ?? symbol,
            Name: instrument.Name ?? symbol,
            Description: null,
            Exchange: MapExchange(instrument.Market),
            AssetType: instrument.Type,
            Sector: null,
            Industry: null,
            Country: instrument.Country ?? "US",
            Currency: "USD",
            MarketCap: null,
            AverageVolume: null,
            Week52High: null,
            Week52Low: null,
            LastPrice: null,
            WebUrl: instrument.Url,
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
        ));
    }

    private static string? MapExchange(string? marketUrl)
    {
        if (string.IsNullOrEmpty(marketUrl))
            return null;

        // Market URLs look like: https://api.robinhood.com/markets/XNAS/
        var parts = marketUrl.TrimEnd('/').Split('/');
        var mic = parts[^1];

        return mic switch
        {
            "XNAS" => "NASDAQ",
            "XNYS" => "NYSE",
            "ARCX" => "NYSE ARCA",
            "XASE" => "NYSE AMERICAN",
            "BATS" => "BATS",
            _ => mic
        };
    }
}

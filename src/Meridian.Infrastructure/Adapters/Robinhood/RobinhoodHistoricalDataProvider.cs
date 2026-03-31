// ✅ ADR-001: IHistoricalDataProvider contract via BaseHistoricalDataProvider
// ✅ ADR-004: CancellationToken on all async methods
// ✅ ADR-005: Attribute-based provider discovery
// ✅ Rate limiting via WaitForRateLimitSlotAsync

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Robinhood;

/// <summary>
/// Pulls free end-of-day historical bars from Robinhood's public historicals API.
/// No authentication required for the public endpoints.
/// </summary>
/// <remarks>
/// Robinhood historicals endpoint:
/// GET https://api.robinhood.com/quotes/historicals/{symbol}/?interval=day&amp;span=year
/// Returns up to 1 year of daily OHLCV data per request.
/// For multi-year history, multiple span requests are issued (year, 5year).
/// </remarks>
[DataSource("robinhood", "Robinhood (free EOD)", DataSourceType.Historical, DataSourceCategory.Free,
    Priority = 25, Description = "Free end-of-day OHLCV data from Robinhood's public API")]
[ImplementsAdr("ADR-001", "Robinhood historical data provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
public sealed class RobinhoodHistoricalDataProvider : BaseHistoricalDataProvider
{
    private const string BaseUrl = "https://api.robinhood.com";

    public override string Name => "robinhood";
    public override string DisplayName => "Robinhood (free EOD)";
    public override string Description => "Free daily OHLCV from Robinhood's public API (US equities).";
    protected override string HttpClientName => HttpClientNames.RobinhoodHistorical;

    public override int Priority => 25;

    // Robinhood public API: conservative defaults — 10 requests/minute
    public override int MaxRequestsPerWindow => 10;
    public override TimeSpan RateLimitWindow => TimeSpan.FromMinutes(1);

    public RobinhoodHistoricalDataProvider(HttpClient? httpClient = null, ILogger? log = null)
        : base(httpClient, log)
    {
    }

    protected override string NormalizeSymbol(string symbol) =>
        symbol.ToUpperInvariant();

    public override async Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
        string symbol,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidateSymbol(symbol);

        var normalized = NormalizeSymbol(symbol);
        var allBars = new List<HistoricalBar>();

        // Robinhood only returns up to 1 year per span. We fetch "5year" to cover more history.
        // Then filter to the requested date range.
        var spans = new[] { "5year", "year" };

        foreach (var span in spans)
        {
            await WaitForRateLimitSlotAsync(ct).ConfigureAwait(false);

            var url = $"{BaseUrl}/quotes/historicals/{normalized}/?interval=day&span={span}";
            Log.Information("Requesting Robinhood history for {Symbol} span={Span}", symbol, span);

            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                Log.Warning("Robinhood HTTP {StatusCode} for {Symbol}: {Body}", (int)response.StatusCode, symbol, body);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    break;

                // For 5year first, fall through to year span on errors
                continue;
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var parsed = ParseResponse(json, symbol, from, to);
            allBars.AddRange(parsed);

            // If 5year returned data, no need to fetch year span too
            if (allBars.Count > 0)
                break;
        }

        // Deduplicate and sort
        var result = allBars
            .GroupBy(b => b.SessionDate)
            .Select(g => g.First())
            .OrderBy(b => b.SessionDate)
            .ToArray();

        Log.Information("Fetched {Count} bars for {Symbol} from Robinhood", result.Length, symbol);
        return result;
    }

    private List<HistoricalBar> ParseResponse(string json, string symbol, DateOnly? from, DateOnly? to)
    {
        var bars = new List<HistoricalBar>();

        RobinhoodHistoricalsResponse? data;
        try
        {
            data = JsonSerializer.Deserialize(json, RobinhoodJsonContext.Default.RobinhoodHistoricalsResponse);
        }
        catch (JsonException ex)
        {
            Log.Warning("Failed to parse Robinhood response for {Symbol}: {Error}", symbol, ex.Message);
            return bars;
        }

        if (data?.Results is null || data.Results.Length == 0)
            return bars;

        foreach (var item in data.Results)
        {
            if (string.IsNullOrEmpty(item.BeginsAt))
                continue;

            if (!DateTimeOffset.TryParse(item.BeginsAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var ts))
                continue;

            var date = DateOnly.FromDateTime(ts.UtcDateTime);

            if (from is not null && date < from.Value)
                continue;
            if (to is not null && date > to.Value)
                continue;

            if (!decimal.TryParse(item.OpenPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var open))
                continue;
            if (!decimal.TryParse(item.ClosePrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var close))
                continue;
            if (!decimal.TryParse(item.HighPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var high))
                continue;
            if (!decimal.TryParse(item.LowPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var low))
                continue;
            if (!long.TryParse(item.Volume, NumberStyles.Any, CultureInfo.InvariantCulture, out var volume))
                continue;

            if (!IsValidOhlc(open, high, low, close))
                continue;

            bars.Add(new HistoricalBar(
                Symbol: symbol.ToUpperInvariant(),
                SessionDate: date,
                Open: open,
                High: high,
                Low: low,
                Close: close,
                Volume: volume,
                Source: Name,
                SequenceNumber: date.DayNumber));
        }

        return bars;
    }
}

/// <summary>
/// Robinhood historicals API response DTO.
/// </summary>
public sealed class RobinhoodHistoricalsResponse
{
    [JsonPropertyName("results")]
    public RobinhoodHistoricalItem[]? Results { get; set; }

    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    [JsonPropertyName("interval")]
    public string? Interval { get; set; }

    [JsonPropertyName("span")]
    public string? Span { get; set; }
}

/// <summary>
/// Single daily bar entry in the Robinhood historicals response.
/// </summary>
public sealed class RobinhoodHistoricalItem
{
    [JsonPropertyName("begins_at")]
    public string? BeginsAt { get; set; }

    [JsonPropertyName("open_price")]
    public string? OpenPrice { get; set; }

    [JsonPropertyName("close_price")]
    public string? ClosePrice { get; set; }

    [JsonPropertyName("high_price")]
    public string? HighPrice { get; set; }

    [JsonPropertyName("low_price")]
    public string? LowPrice { get; set; }

    [JsonPropertyName("volume")]
    public string? Volume { get; set; }

    [JsonPropertyName("session")]
    public string? Session { get; set; }

    [JsonPropertyName("interpolated")]
    public bool Interpolated { get; set; }
}

/// <summary>
/// Robinhood instruments search API response DTO.
/// </summary>
public sealed class RobinhoodInstrumentsResponse
{
    [JsonPropertyName("results")]
    public RobinhoodInstrument[]? Results { get; set; }

    [JsonPropertyName("next")]
    public string? Next { get; set; }
}

/// <summary>
/// Single instrument entry in the Robinhood instruments response.
/// </summary>
public sealed class RobinhoodInstrument
{
    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("market")]
    public string? Market { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("tradeable")]
    public bool Tradeable { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }
}

/// <summary>
/// ADR-014 compliant JSON source-generation context for Robinhood DTOs.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(RobinhoodHistoricalsResponse))]
[JsonSerializable(typeof(RobinhoodHistoricalItem))]
[JsonSerializable(typeof(RobinhoodInstrumentsResponse))]
[JsonSerializable(typeof(RobinhoodInstrument))]
public partial class RobinhoodJsonContext : JsonSerializerContext
{
}

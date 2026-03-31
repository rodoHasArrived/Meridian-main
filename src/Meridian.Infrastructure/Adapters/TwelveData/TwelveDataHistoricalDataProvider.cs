// ✅ ADR-001: IHistoricalDataProvider contract
// ✅ ADR-004: CancellationToken on all async methods
// ✅ Rate limiting via ExecuteGetAndReadAsync → WaitForRateLimitSlotAsync
// ✅ ThrowIfDisposed() + ValidateSymbol() at entry
// ✅ Private inner model classes with [JsonPropertyName]
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

namespace Meridian.Infrastructure.Adapters.TwelveData;

/// <summary>
/// Historical data provider using the Twelve Data API (https://api.twelvedata.com).
/// Provides OHLCV bars for US equities, ETFs, international stocks, forex, and crypto.
/// Free tier: 800 requests/day, 8 requests/minute.
/// Extends BaseHistoricalDataProvider for common functionality.
/// </summary>
[DataSource("twelvedata", "Twelve Data", DataSourceType.Historical, DataSourceCategory.Premium,
    Priority = 22, Description = "Historical OHLCV via Twelve Data API (free/paid tiers)")]
[ImplementsAdr("ADR-001", "TwelveData historical data provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
public sealed class TwelveDataHistoricalDataProvider : BaseHistoricalDataProvider
{
    private const string BaseUrl = "https://api.twelvedata.com/time_series";

    private readonly string? _apiKey;


    public override string Name => "twelvedata";
    public override string DisplayName => "Twelve Data";
    public override string Description => "OHLCV data for US and international equities, ETFs, forex, and crypto via Twelve Data API.";
    protected override string HttpClientName => HttpClientNames.TwelveDataHistorical;



    public override int Priority => 22;
    public override TimeSpan RateLimitDelay => TimeSpan.FromMilliseconds(7500); // 8 req/min
    public override int MaxRequestsPerWindow => 8;
    public override TimeSpan RateLimitWindow => TimeSpan.FromMinutes(1);

    public override HistoricalDataCapabilities Capabilities { get; } =
        HistoricalDataCapabilities.BarsOnly.WithMarkets("US", "UK", "EU", "CA", "AU");


    public TwelveDataHistoricalDataProvider(string? apiKey = null, HttpClient? httpClient = null, ILogger? log = null)
        : base(httpClient, log)
    {
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("TWELVEDATA_API_KEY");
    }

    public override Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            Log.Warning("Twelve Data API key not configured. Set TWELVEDATA_API_KEY environment variable or configure in settings.");
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    public override async Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
        string symbol,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidateSymbol(symbol);

        if (string.IsNullOrEmpty(_apiKey))
        {
            Log.Warning("Twelve Data API key not configured; skipping request for {Symbol}", symbol);
            return Array.Empty<HistoricalBar>();
        }

        var startDate = from?.ToString("yyyy-MM-dd") ?? "2000-01-01";
        var endDate = to?.ToString("yyyy-MM-dd") ?? DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        var url = $"{BaseUrl}?symbol={Uri.EscapeDataString(symbol)}&interval=1day" +
                  $"&start_date={startDate}&end_date={endDate}" +
                  $"&outputsize=5000&format=JSON&apikey={_apiKey}";

        Log.Information("Requesting Twelve Data history for {Symbol} ({StartDate} to {EndDate})", symbol, startDate, endDate);

        var json = await ExecuteGetAndReadAsync(url, symbol, "daily bars", ct).ConfigureAwait(false);

        if (string.IsNullOrEmpty(json))
        {
            Log.Warning("No data returned from Twelve Data for {Symbol}", symbol);
            return Array.Empty<HistoricalBar>();
        }

        var bars = ParseJsonResponse(json, symbol, from, to);

        Log.Information("Fetched {Count} bars for {Symbol} from Twelve Data", bars.Count, symbol);
        return bars.OrderBy(b => b.SessionDate).ToArray();
    }

    private List<HistoricalBar> ParseJsonResponse(string json, string symbol, DateOnly? from, DateOnly? to)
    {
        TwelveDataResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<TwelveDataResponse>(json);
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "Failed to parse Twelve Data response for {Symbol}", symbol);
            return new List<HistoricalBar>();
        }

        if (response is null || response.Status != "ok" || response.Values is null || response.Values.Count == 0)
        {
            Log.Warning("Twelve Data returned no usable data for {Symbol} (status={Status})", symbol, response?.Status ?? "null");
            return new List<HistoricalBar>();
        }

        var bars = new List<HistoricalBar>(response.Values.Count);

        foreach (var value in response.Values)
        {
            if (string.IsNullOrEmpty(value.Datetime))
                continue;

            if (!DateOnly.TryParseExact(value.Datetime, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sessionDate))
                continue;

            if (from.HasValue && sessionDate < from.Value)
                continue;
            if (to.HasValue && sessionDate > to.Value)
                continue;

            if (!decimal.TryParse(value.Open, NumberStyles.Any, CultureInfo.InvariantCulture, out var open))
                continue;
            if (!decimal.TryParse(value.High, NumberStyles.Any, CultureInfo.InvariantCulture, out var high))
                continue;
            if (!decimal.TryParse(value.Low, NumberStyles.Any, CultureInfo.InvariantCulture, out var low))
                continue;
            if (!decimal.TryParse(value.Close, NumberStyles.Any, CultureInfo.InvariantCulture, out var close))
                continue;

            if (!IsValidOhlc(open, high, low, close))
                continue;

            if (low > high)
                continue;

            long volume = 0;
            if (!string.IsNullOrEmpty(value.Volume))
                long.TryParse(value.Volume, NumberStyles.Any, CultureInfo.InvariantCulture, out volume);

            bars.Add(new HistoricalBar(
                Symbol: symbol.ToUpperInvariant(),
                SessionDate: sessionDate,
                Open: open,
                High: high,
                Low: low,
                Close: close,
                Volume: volume,
                Source: Name,
                SequenceNumber: sessionDate.DayNumber));
        }

        return bars;
    }


    private sealed class TwelveDataResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("values")]
        public List<TwelveDataBar>? Values { get; set; }
    }

    private sealed class TwelveDataBar
    {
        [JsonPropertyName("datetime")]
        public string? Datetime { get; set; }

        [JsonPropertyName("open")]
        public string? Open { get; set; }

        [JsonPropertyName("high")]
        public string? High { get; set; }

        [JsonPropertyName("low")]
        public string? Low { get; set; }

        [JsonPropertyName("close")]
        public string? Close { get; set; }

        [JsonPropertyName("volume")]
        public string? Volume { get; set; }
    }

}

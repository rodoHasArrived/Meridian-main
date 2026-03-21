using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Meridian.Infrastructure.Utilities;
using Serilog;

namespace Meridian.Infrastructure.Adapters.AlphaVantage;

/// <summary>
/// Historical data provider using Alpha Vantage API (free tier with API key).
/// Unique capability: Intraday historical data (1, 5, 15, 30, 60 min intervals).
/// Coverage: US equities, global indices, forex, crypto.
/// Free tier: 25 requests/day (severely limited), 5 calls/minute.
/// Extends BaseHistoricalDataProvider for common functionality.
/// </summary>
[DataSource("alphavantage", "Alpha Vantage (free tier)", DataSourceType.Historical, DataSourceCategory.Free,
    Priority = 25, Description = "Free tier historical OHLCV data via Alpha Vantage API")]
[ImplementsAdr("ADR-001", "Alpha Vantage historical data provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
public sealed class AlphaVantageHistoricalDataProvider : BaseHistoricalDataProvider
{
    private const string ApiBaseUrl = "https://www.alphavantage.co/query";

    private readonly string? _apiKey;

    public override string Name => "alphavantage";
    public override string DisplayName => "Alpha Vantage (free tier)";
    public override string Description => "US equities with unique intraday historical data support. Limited free tier (25 req/day).";
    protected override string HttpClientName => HttpClientNames.AlphaVantageHistorical;

    public override int Priority => 25; // Lower priority due to very limited free tier
    public override TimeSpan RateLimitDelay => TimeSpan.FromSeconds(12); // 5 requests/minute = 12 seconds between requests
    public override int MaxRequestsPerWindow => 5;
    public override TimeSpan RateLimitWindow => TimeSpan.FromMinutes(1);

    /// <summary>
    /// Alpha Vantage supports adjusted bars with intraday historical data (key differentiator).
    /// </summary>
    public override HistoricalDataCapabilities Capabilities { get; } = HistoricalDataCapabilities.BarsOnly with
    {
        Intraday = true
    };

    /// <summary>
    /// Supported intraday intervals.
    /// </summary>
    public static IReadOnlyList<string> SupportedIntervals => new[] { "1min", "5min", "15min", "30min", "60min" };

    public AlphaVantageHistoricalDataProvider(string? apiKey = null, HttpClient? httpClient = null, ILogger? log = null)
        : base(httpClient, log)
    {
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("ALPHA_VANTAGE_API_KEY");

        // Add custom User-Agent
        if (!Http.DefaultRequestHeaders.Contains("User-Agent"))
        {
            Http.DefaultRequestHeaders.Add("User-Agent", "Meridian/1.0");
        }
    }

    public override async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            Log.Warning("Alpha Vantage API key not configured. Set ALPHA_VANTAGE_API_KEY environment variable or configure in settings.");
            return false;
        }

        try
        {
            // Quick health check with quote endpoint
            var url = $"{ApiBaseUrl}?function=GLOBAL_QUOTE&symbol=AAPL&apikey={_apiKey}";
            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return false;

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            // Check for rate limit error message in response
            return !json.Contains("Note") && !json.Contains("Thank you for using Alpha Vantage");
        }
        catch
        {
            return false;
        }
    }

    public override async Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
        string symbol,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        var adjustedBars = await GetAdjustedDailyBarsAsync(symbol, from, to, ct).ConfigureAwait(false);
        return adjustedBars.Select(b => b.ToHistoricalBar(preferAdjusted: true)).ToList();
    }

    public override async Task<IReadOnlyList<AdjustedHistoricalBar>> GetAdjustedDailyBarsAsync(
        string symbol,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidateSymbol(symbol);
        ValidateApiKey();

        await WaitForRateLimitSlotAsync(ct).ConfigureAwait(false);

        var normalizedSymbol = NormalizeSymbol(symbol);
        var url = $"{ApiBaseUrl}?function=TIME_SERIES_DAILY_ADJUSTED&symbol={normalizedSymbol}&outputsize=full&apikey={_apiKey}";

        Log.Information("Requesting Alpha Vantage daily adjusted history for {Symbol}", symbol);

        try
        {
            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);

            var httpResult = await ResponseHandler.HandleResponseAsync(response, symbol, "daily bars", ct: ct).ConfigureAwait(false);
            if (httpResult.IsNotFound)
            {
                Log.Warning("Alpha Vantage: Symbol {Symbol} not found", symbol);
                return Array.Empty<AdjustedHistoricalBar>();
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            // Alpha Vantage returns 200 OK with error/rate limit messages in body
            if (IsRateLimitResponse(json))
            {
                Log.Warning("Alpha Vantage rate limit hit for {Symbol}. Message in response.", symbol);
                throw new HttpRequestException($"Alpha Vantage rate limit exceeded for {symbol}. Please wait before retrying.");
            }

            if (json.Contains("\"Error Message\""))
            {
                Log.Warning("Alpha Vantage error for {Symbol}: Invalid symbol or other error", symbol);
                return Array.Empty<AdjustedHistoricalBar>();
            }

            var data = DeserializeResponse<AlphaVantageDailyAdjustedResponse>(json, symbol);

            if (data?.TimeSeries is null || data.TimeSeries.Count == 0)
            {
                Log.Warning("No data returned from Alpha Vantage for {Symbol}", symbol);
                return Array.Empty<AdjustedHistoricalBar>();
            }

            var bars = ParseDailyResponse(data.TimeSeries, symbol, from, to);

            Log.Information("Fetched {Count} bars for {Symbol} from Alpha Vantage", bars.Count, symbol);
            return bars.OrderBy(b => b.SessionDate).ToList();
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "Failed to parse Alpha Vantage response for {Symbol}", symbol);
            throw new InvalidOperationException($"Failed to parse Alpha Vantage data for {symbol}", ex);
        }
    }

    /// <summary>
    /// Get intraday historical bars. This is the unique capability of Alpha Vantage.
    /// </summary>
    /// <remarks>
    /// Free tier only returns 1-2 months of intraday data.
    /// </remarks>
    public async Task<IReadOnlyList<IntradayBar>> GetIntradayBarsAsync(
        string symbol,
        string interval,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidateSymbol(symbol);
        ValidateApiKey();

        // Validate interval
        var normalizedInterval = NormalizeInterval(interval);
        if (!SupportedIntervals.Contains(normalizedInterval))
            throw new ArgumentException($"Unsupported interval: {interval}. Supported: {string.Join(", ", SupportedIntervals)}", nameof(interval));

        await WaitForRateLimitSlotAsync(ct).ConfigureAwait(false);

        var normalizedSymbol = NormalizeSymbol(symbol);
        var url = $"{ApiBaseUrl}?function=TIME_SERIES_INTRADAY&symbol={normalizedSymbol}&interval={normalizedInterval}&outputsize=full&adjusted=true&extended_hours=true&apikey={_apiKey}";

        Log.Information("Requesting Alpha Vantage {Interval} intraday data for {Symbol}", normalizedInterval, symbol);

        try
        {
            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);

            var httpResult = await ResponseHandler.HandleResponseAsync(response, symbol, "intraday bars", ct: ct).ConfigureAwait(false);
            if (httpResult.IsNotFound)
            {
                return Array.Empty<IntradayBar>();
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            // Alpha Vantage returns 200 OK with error/rate limit messages in body
            if (IsRateLimitResponse(json))
            {
                throw new HttpRequestException($"Alpha Vantage rate limit exceeded for {symbol}");
            }

            if (json.Contains("\"Error Message\""))
            {
                return Array.Empty<IntradayBar>();
            }

            var bars = ParseIntradayResponse(json, normalizedInterval, symbol, from, to);

            Log.Information("Fetched {Count} {Interval} bars for {Symbol} from Alpha Vantage",
                bars.Count, normalizedInterval, symbol);
            return bars.OrderBy(b => b.Timestamp).ToList();
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "Failed to parse Alpha Vantage intraday response for {Symbol}", symbol);
            throw new InvalidOperationException($"Failed to parse Alpha Vantage intraday data for {symbol}", ex);
        }
    }

    private void ValidateApiKey()
    {
        if (string.IsNullOrEmpty(_apiKey))
            throw new InvalidOperationException("Alpha Vantage API key is required. Set ALPHA_VANTAGE_API_KEY environment variable.");
    }

    private static bool IsRateLimitResponse(string json)
    {
        return json.Contains("\"Note\"") || json.Contains("Thank you for using Alpha Vantage");
    }

    private List<AdjustedHistoricalBar> ParseDailyResponse(
        Dictionary<string, AlphaVantageDailyPrice> timeSeries,
        string symbol,
        DateOnly? from,
        DateOnly? to)
    {
        var bars = new List<AdjustedHistoricalBar>();

        foreach (var kvp in timeSeries)
        {
            if (!DateOnly.TryParse(kvp.Key, out var sessionDate))
                continue;

            // Skip if outside requested range
            if (from.HasValue && sessionDate < from.Value)
                continue;
            if (to.HasValue && sessionDate > to.Value)
                continue;

            var price = kvp.Value;

            // Parse values
            if (!TryParseDecimal(price.Open, out var open))
                continue;
            if (!TryParseDecimal(price.High, out var high))
                continue;
            if (!TryParseDecimal(price.Low, out var low))
                continue;
            if (!TryParseDecimal(price.Close, out var close))
                continue;
            TryParseLong(price.Volume, out var volume);
            TryParseDecimal(price.AdjustedClose, out var adjClose);
            TryParseDecimal(price.DividendAmount, out var dividend);
            TryParseDecimal(price.SplitCoefficient, out var splitCoeff);

            // Validate OHLC using base class helper
            if (!IsValidOhlc(open, high, low, close))
                continue;

            // Calculate adjustment factor
            decimal? splitFactor = null;
            if (adjClose > 0 && close > 0)
            {
                var factor = adjClose / close;
                if (Math.Abs(factor - 1m) > 0.0001m)
                {
                    splitFactor = factor;
                }
            }

            // Also use split coefficient if provided
            if (splitCoeff != 1m && splitCoeff > 0)
            {
                splitFactor = splitCoeff;
            }

            bars.Add(new AdjustedHistoricalBar(
                Symbol: symbol.ToUpperInvariant(),
                SessionDate: sessionDate,
                Open: open,
                High: high,
                Low: low,
                Close: close,
                Volume: volume,
                Source: Name,
                SequenceNumber: sessionDate.DayNumber,
                AdjustedOpen: splitFactor.HasValue ? open * splitFactor.Value : null,
                AdjustedHigh: splitFactor.HasValue ? high * splitFactor.Value : null,
                AdjustedLow: splitFactor.HasValue ? low * splitFactor.Value : null,
                AdjustedClose: adjClose > 0 ? adjClose : null,
                AdjustedVolume: null,
                SplitFactor: splitCoeff != 1m && splitCoeff > 0 ? splitCoeff : splitFactor,
                DividendAmount: dividend > 0 ? dividend : null
            ));
        }

        return bars;
    }

    private List<IntradayBar> ParseIntradayResponse(
        string json,
        string normalizedInterval,
        string symbol,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        var timeSeriesKey = $"Time Series ({normalizedInterval})";
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty(timeSeriesKey, out var timeSeries))
        {
            Log.Warning("No intraday data returned from Alpha Vantage for {Symbol}", symbol);
            return new List<IntradayBar>();
        }

        var bars = new List<IntradayBar>();

        foreach (var prop in timeSeries.EnumerateObject())
        {
            if (!DateTimeOffset.TryParse(prop.Name, out var timestamp))
                continue;

            // Skip if outside requested range
            if (from.HasValue && timestamp < from.Value)
                continue;
            if (to.HasValue && timestamp > to.Value)
                continue;

            var price = prop.Value;

            if (!TryParseDecimalFromJson(price, "1. open", out var open))
                continue;
            if (!TryParseDecimalFromJson(price, "2. high", out var high))
                continue;
            if (!TryParseDecimalFromJson(price, "3. low", out var low))
                continue;
            if (!TryParseDecimalFromJson(price, "4. close", out var close))
                continue;
            TryParseLongFromJson(price, "5. volume", out var volume);

            if (!IsValidOhlc(open, high, low, close))
                continue;

            bars.Add(new IntradayBar(
                Symbol: symbol.ToUpperInvariant(),
                Timestamp: timestamp,
                Interval: normalizedInterval,
                Open: open,
                High: high,
                Low: low,
                Close: close,
                Volume: volume,
                Source: Name
            ));
        }

        return bars;
    }

    private static string NormalizeInterval(string interval)
    {
        return interval.ToLowerInvariant().Replace(" ", "") switch
        {
            "1m" or "1min" or "1minute" => "1min",
            "5m" or "5min" or "5minute" => "5min",
            "15m" or "15min" or "15minute" => "15min",
            "30m" or "30min" or "30minute" => "30min",
            "60m" or "60min" or "1h" or "1hour" => "60min",
            _ => interval.ToLowerInvariant()
        };
    }

    private static bool TryParseDecimal(string? value, out decimal result)
    {
        result = 0m;
        if (string.IsNullOrEmpty(value))
            return false;
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryParseLong(string? value, out long result)
    {
        result = 0;
        if (string.IsNullOrEmpty(value))
            return false;
        return long.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryParseDecimalFromJson(JsonElement element, string propertyName, out decimal result)
    {
        result = 0m;
        if (!element.TryGetProperty(propertyName, out var prop))
            return false;
        return decimal.TryParse(prop.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryParseLongFromJson(JsonElement element, string propertyName, out long result)
    {
        result = 0;
        if (!element.TryGetProperty(propertyName, out var prop))
            return false;
        return long.TryParse(prop.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out result);
    }

    #region Alpha Vantage API Models

    private sealed class AlphaVantageDailyAdjustedResponse
    {
        [JsonPropertyName("Meta Data")]
        public AlphaVantageMetaData? MetaData { get; set; }

        [JsonPropertyName("Time Series (Daily)")]
        public Dictionary<string, AlphaVantageDailyPrice>? TimeSeries { get; set; }
    }

    private sealed class AlphaVantageMetaData
    {
        [JsonPropertyName("1. Information")]
        public string? Information { get; set; }

        [JsonPropertyName("2. Symbol")]
        public string? Symbol { get; set; }

        [JsonPropertyName("3. Last Refreshed")]
        public string? LastRefreshed { get; set; }

        [JsonPropertyName("4. Output Size")]
        public string? OutputSize { get; set; }

        [JsonPropertyName("5. Time Zone")]
        public string? TimeZone { get; set; }
    }

    private sealed class AlphaVantageDailyPrice
    {
        [JsonPropertyName("1. open")]
        public string? Open { get; set; }

        [JsonPropertyName("2. high")]
        public string? High { get; set; }

        [JsonPropertyName("3. low")]
        public string? Low { get; set; }

        [JsonPropertyName("4. close")]
        public string? Close { get; set; }

        [JsonPropertyName("5. adjusted close")]
        public string? AdjustedClose { get; set; }

        [JsonPropertyName("6. volume")]
        public string? Volume { get; set; }

        [JsonPropertyName("7. dividend amount")]
        public string? DividendAmount { get; set; }

        [JsonPropertyName("8. split coefficient")]
        public string? SplitCoefficient { get; set; }
    }

    #endregion
}

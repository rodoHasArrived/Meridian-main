using System.Globalization;
using System.Net.Http.Headers;
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

namespace Meridian.Infrastructure.Adapters.Finnhub;

/// <summary>
/// Historical data provider using Finnhub API (free tier with API key).
/// Generous free tier: 60 API calls/minute.
/// Coverage: 60,000+ global securities with company fundamentals.
/// Best for: Earnings data, fundamentals, news, and high-frequency backfill operations.
/// Extends BaseHistoricalDataProvider for common functionality including:
/// - HTTP resilience (retry, circuit breaker)
/// - Rate limit tracking with IRateLimitAwareProvider
/// - Centralized error handling
/// </summary>
[DataSource("finnhub", "Finnhub (free tier)", DataSourceType.Historical, DataSourceCategory.Free,
    Priority = 18, Description = "Historical OHLCV data via Finnhub free tier API")]
[ImplementsAdr("ADR-001", "Finnhub historical data provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
[RequiresCredential("FINNHUB_API_KEY",
    EnvironmentVariables = new[] { "FINNHUB_API_KEY", "FINNHUB__APIKEY" },
    DisplayName = "API Key",
    Description = "Finnhub API key from https://finnhub.io/dashboard")]
public sealed class FinnhubHistoricalDataProvider : BaseHistoricalDataProvider
{
    private const string BaseUrl = "https://finnhub.io/api/v1";

    private readonly string? _apiKey;

    #region Abstract Property Implementations

    public override string Name => "finnhub";
    public override string DisplayName => "Finnhub (free tier)";
    public override string Description => "Global equities with generous 60 calls/min free tier. Includes fundamentals, earnings, and news.";
    protected override string HttpClientName => HttpClientNames.FinnhubHistorical;

    #endregion

    #region Virtual Property Overrides

    public override int Priority => 18;
    public override TimeSpan RateLimitDelay => TimeSpan.FromSeconds(1); // 60 requests/minute = 1 second between requests
    public override int MaxRequestsPerWindow => 60;
    public override TimeSpan RateLimitWindow => TimeSpan.FromMinutes(1);

    /// <summary>
    /// Finnhub supports intraday bars and corporate actions (raw prices, not adjusted).
    /// Wide market coverage: US, UK, DE, CA, AU, HK, JP, CN.
    /// </summary>
    public override HistoricalDataCapabilities Capabilities { get; } = new()
    {
        Intraday = true,
        Dividends = true,
        Splits = true,
        SupportedMarkets = ["US", "UK", "DE", "CA", "AU", "HK", "JP", "CN"]
    };

    #endregion

    /// <summary>
    /// Supported candle resolutions.
    /// </summary>
    public static IReadOnlyList<string> SupportedResolutions => ["1", "5", "15", "30", "60", "D", "W", "M"];

    public FinnhubHistoricalDataProvider(string? apiKey = null, HttpClient? httpClient = null, ILogger? log = null)
        : base(httpClient, log)
    {
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("FINNHUB_API_KEY");

        Http.DefaultRequestHeaders.Add("User-Agent", "Meridian/1.0");
        Http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrEmpty(_apiKey))
        {
            Http.DefaultRequestHeaders.Add("X-Finnhub-Token", _apiKey);
        }
    }

    public override async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            Log.Warning("Finnhub API key not configured. Set FINNHUB_API_KEY environment variable or configure in settings.");
            return false;
        }

        try
        {
            // Quick health check with quote endpoint
            var url = $"{BaseUrl}/quote?symbol=AAPL&token={_apiKey}";
            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public override async Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var adjustedBars = await GetAdjustedDailyBarsAsync(symbol, from, to, ct).ConfigureAwait(false);
        return adjustedBars.Select(b => b.ToHistoricalBar(preferAdjusted: false)).ToList();
    }

    public override async Task<IReadOnlyList<AdjustedHistoricalBar>> GetAdjustedDailyBarsAsync(string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidateSymbol(symbol);

        if (string.IsNullOrEmpty(_apiKey))
            throw new InvalidOperationException("Finnhub API key is required. Set FINNHUB_API_KEY environment variable.");

        await WaitForRateLimitSlotAsync(ct).ConfigureAwait(false);

        var normalizedSymbol = NormalizeSymbol(symbol);

        // Convert dates to Unix timestamps
        var fromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1));
        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var fromUnix = new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var toUnix = new DateTimeOffset(toDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero).ToUnixTimeSeconds();

        // Use stock/candle endpoint with D (daily) resolution
        var url = $"{BaseUrl}/stock/candle?symbol={normalizedSymbol}&resolution=D&from={fromUnix}&to={toUnix}&token={_apiKey}";

        Log.Information("Requesting Finnhub daily history for {Symbol} ({From} to {To})", symbol, fromDate, toDate);

        try
        {
            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                HandleHttpResponse(response, symbol, "candles");
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var data = DeserializeResponse<FinnhubCandleResponse>(json, symbol);

            if (data?.Status == "no_data" || data?.Close is null || data.Close.Length == 0)
            {
                Log.Warning("No data returned from Finnhub for {Symbol}", symbol);
                return Array.Empty<AdjustedHistoricalBar>();
            }

            var bars = new List<AdjustedHistoricalBar>();
            var timestamps = data.Timestamp ?? [];

            for (int i = 0; i < timestamps.Length; i++)
            {
                var date = DateTimeOffset.FromUnixTimeSeconds(timestamps[i]).Date;
                var sessionDate = DateOnly.FromDateTime(date);

                // Skip if outside requested range
                if (from.HasValue && sessionDate < from.Value)
                    continue;
                if (to.HasValue && sessionDate > to.Value)
                    continue;

                var open = GetDecimalValue(data.Open, i);
                var high = GetDecimalValue(data.High, i);
                var low = GetDecimalValue(data.Low, i);
                var close = GetDecimalValue(data.Close, i);
                var volume = GetLongValue(data.Volume, i);

                // Validate OHLC using base class helper
                if (!ValidateOhlc(open, high, low, close, symbol, sessionDate))
                    continue;

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
                    // Finnhub doesn't provide adjusted prices directly
                    AdjustedOpen: null,
                    AdjustedHigh: null,
                    AdjustedLow: null,
                    AdjustedClose: null,
                    AdjustedVolume: null,
                    SplitFactor: null,
                    DividendAmount: null
                ));
            }

            Log.Information("Fetched {Count} bars for {Symbol} from Finnhub", bars.Count, symbol);
            return bars.OrderBy(b => b.SessionDate).ToList();
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "Failed to parse Finnhub response for {Symbol}", symbol);
            throw new InvalidOperationException($"Failed to parse Finnhub data for {symbol}", ex);
        }
    }

    /// <summary>
    /// Get intraday bars for a symbol.
    /// </summary>
    public async Task<IReadOnlyList<IntradayBar>> GetIntradayBarsAsync(
        string symbol,
        string resolution,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidateSymbol(symbol);

        if (string.IsNullOrEmpty(_apiKey))
            throw new InvalidOperationException("Finnhub API key is required.");

        await WaitForRateLimitSlotAsync(ct).ConfigureAwait(false);

        var normalizedSymbol = NormalizeSymbol(symbol);
        var normalizedResolution = NormalizeResolution(resolution);

        // Convert dates to Unix timestamps
        var fromUnix = (from ?? DateTimeOffset.UtcNow.AddDays(-30)).ToUnixTimeSeconds();
        var toUnix = (to ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();

        var url = $"{BaseUrl}/stock/candle?symbol={normalizedSymbol}&resolution={normalizedResolution}&from={fromUnix}&to={toUnix}&token={_apiKey}";

        Log.Information("Requesting Finnhub {Resolution} bars for {Symbol}", normalizedResolution, symbol);

        try
        {
            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                HandleHttpResponse(response, symbol, "candles");
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var data = DeserializeResponse<FinnhubCandleResponse>(json, symbol);

            if (data?.Status == "no_data" || data?.Close is null || data.Close.Length == 0)
            {
                return Array.Empty<IntradayBar>();
            }

            var bars = new List<IntradayBar>();
            var timestamps = data.Timestamp ?? [];

            for (int i = 0; i < timestamps.Length; i++)
            {
                var timestamp = DateTimeOffset.FromUnixTimeSeconds(timestamps[i]);
                var sessionDate = DateOnly.FromDateTime(timestamp.Date);

                // Skip if outside requested range
                if (from.HasValue && timestamp < from.Value)
                    continue;
                if (to.HasValue && timestamp > to.Value)
                    continue;

                var open = GetDecimalValue(data.Open, i);
                var high = GetDecimalValue(data.High, i);
                var low = GetDecimalValue(data.Low, i);
                var close = GetDecimalValue(data.Close, i);
                var volume = GetLongValue(data.Volume, i);

                if (!ValidateOhlc(open, high, low, close, symbol, sessionDate))
                    continue;

                bars.Add(new IntradayBar(
                    Symbol: symbol.ToUpperInvariant(),
                    Timestamp: timestamp,
                    Interval: ResolutionToInterval(normalizedResolution),
                    Open: open,
                    High: high,
                    Low: low,
                    Close: close,
                    Volume: volume,
                    Source: Name
                ));
            }

            Log.Information("Fetched {Count} {Resolution} bars for {Symbol} from Finnhub",
                bars.Count, normalizedResolution, symbol);
            return bars.OrderBy(b => b.Timestamp).ToList();
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "Failed to parse Finnhub intraday response for {Symbol}", symbol);
            throw new InvalidOperationException($"Failed to parse Finnhub intraday data for {symbol}", ex);
        }
    }

    /// <summary>
    /// Get basic earnings data for a symbol.
    /// </summary>
    public async Task<IReadOnlyList<FinnhubEarning>> GetEarningsAsync(string symbol, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(_apiKey))
            throw new InvalidOperationException("Finnhub API key is required.");

        await WaitForRateLimitSlotAsync(ct).ConfigureAwait(false);

        var normalizedSymbol = NormalizeSymbol(symbol);
        var url = $"{BaseUrl}/stock/earnings?symbol={normalizedSymbol}&token={_apiKey}";

        try
        {
            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<FinnhubEarning>();
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var data = DeserializeResponse<List<FinnhubEarning>>(json, symbol);

            return data ?? [];
        }
        catch
        {
            return Array.Empty<FinnhubEarning>();
        }
    }

    /// <summary>
    /// Get company profile data.
    /// </summary>
    public async Task<FinnhubCompanyProfile?> GetCompanyProfileAsync(string symbol, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(_apiKey))
            throw new InvalidOperationException("Finnhub API key is required.");

        await WaitForRateLimitSlotAsync(ct).ConfigureAwait(false);

        var normalizedSymbol = NormalizeSymbol(symbol);
        var url = $"{BaseUrl}/stock/profile2?symbol={normalizedSymbol}&token={_apiKey}";

        try
        {
            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return DeserializeResponse<FinnhubCompanyProfile>(json, symbol);
        }
        catch
        {
            return null;
        }
    }

    #region Helper Methods

    protected override string NormalizeSymbol(string symbol)
    {
        // Use centralized symbol normalization utility
        return SymbolNormalization.Normalize(symbol);
    }

    private static string NormalizeResolution(string resolution)
    {
        return resolution.ToUpperInvariant() switch
        {
            "1MIN" or "1M" or "1" => "1",
            "5MIN" or "5M" or "5" => "5",
            "15MIN" or "15M" or "15" => "15",
            "30MIN" or "30M" or "30" => "30",
            "60MIN" or "60M" or "1H" or "60" => "60",
            "DAILY" or "1D" or "D" => "D",
            "WEEKLY" or "1W" or "W" => "W",
            "MONTHLY" or "1MO" or "M" => "M",
            _ => resolution
        };
    }

    private static string ResolutionToInterval(string resolution)
    {
        return resolution switch
        {
            "1" => "1min",
            "5" => "5min",
            "15" => "15min",
            "30" => "30min",
            "60" => "1hour",
            "D" => "1day",
            "W" => "1week",
            "M" => "1month",
            _ => resolution
        };
    }

    private static decimal GetDecimalValue(decimal[]? array, int index)
    {
        if (array is null || index >= array.Length)
            return 0m;
        return array[index];
    }

    private static long GetLongValue(decimal[]? array, int index)
    {
        if (array is null || index >= array.Length)
            return 0;
        return (long)array[index];
    }

    #endregion

    #region Finnhub API Models

    private sealed class FinnhubCandleResponse
    {
        [JsonPropertyName("c")]
        public decimal[]? Close { get; set; }

        [JsonPropertyName("h")]
        public decimal[]? High { get; set; }

        [JsonPropertyName("l")]
        public decimal[]? Low { get; set; }

        [JsonPropertyName("o")]
        public decimal[]? Open { get; set; }

        [JsonPropertyName("s")]
        public string? Status { get; set; }

        [JsonPropertyName("t")]
        public long[]? Timestamp { get; set; }

        [JsonPropertyName("v")]
        public decimal[]? Volume { get; set; }
    }

    #endregion
}

#region Finnhub Data Types

/// <summary>
/// Finnhub earnings data.
/// </summary>
public sealed record FinnhubEarning
{
    [JsonPropertyName("actual")]
    public decimal? Actual { get; init; }

    [JsonPropertyName("estimate")]
    public decimal? Estimate { get; init; }

    [JsonPropertyName("period")]
    public string? Period { get; init; }

    [JsonPropertyName("quarter")]
    public int? Quarter { get; init; }

    [JsonPropertyName("surprise")]
    public decimal? Surprise { get; init; }

    [JsonPropertyName("surprisePercent")]
    public decimal? SurprisePercent { get; init; }

    [JsonPropertyName("symbol")]
    public string? Symbol { get; init; }

    [JsonPropertyName("year")]
    public int? Year { get; init; }
}

/// <summary>
/// Finnhub company profile data.
/// </summary>
public sealed record FinnhubCompanyProfile
{
    [JsonPropertyName("country")]
    public string? Country { get; init; }

    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    [JsonPropertyName("exchange")]
    public string? Exchange { get; init; }

    [JsonPropertyName("finnhubIndustry")]
    public string? Industry { get; init; }

    [JsonPropertyName("ipo")]
    public string? IpoDate { get; init; }

    [JsonPropertyName("logo")]
    public string? LogoUrl { get; init; }

    [JsonPropertyName("marketCapitalization")]
    public decimal? MarketCap { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("phone")]
    public string? Phone { get; init; }

    [JsonPropertyName("shareOutstanding")]
    public decimal? SharesOutstanding { get; init; }

    [JsonPropertyName("ticker")]
    public string? Ticker { get; init; }

    [JsonPropertyName("weburl")]
    public string? WebUrl { get; init; }
}

#endregion

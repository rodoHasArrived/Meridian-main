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

namespace Meridian.Infrastructure.Adapters.Polygon;

/// <summary>
/// Historical data provider using Polygon.io REST API (free tier with API key).
/// Provides high-quality OHLCV aggregates with trades, quotes, and reference data.
/// Coverage: US equities, options, forex, crypto.
/// Free tier: 5 API calls/minute, delayed data, 2 years history.
/// Extends BaseHistoricalDataProvider for common functionality including:
/// - HTTP resilience (retry, circuit breaker)
/// - Rate limit tracking with IRateLimitAwareProvider
/// - Centralized error handling
/// </summary>
[DataSource("polygon", "Polygon.io", DataSourceType.Historical, DataSourceCategory.Aggregator,
    Priority = 12, Description = "High-quality OHLCV aggregates for US equities, options, forex, and crypto")]
[ImplementsAdr("ADR-001", "Polygon.io historical data provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
[RequiresCredential("POLYGON_API_KEY",
    EnvironmentVariables = new[] { "POLYGON_API_KEY", "POLYGON__APIKEY" },
    DisplayName = "API Key",
    Description = "Polygon.io API key from https://polygon.io/dashboard/api-keys")]
public sealed class PolygonHistoricalDataProvider : BaseHistoricalDataProvider
{
    private const string BaseUrl = "https://api.polygon.io";

    private readonly string? _apiKey;


    public override string Name => "polygon";
    public override string DisplayName => "Polygon.io (free tier)";
    public override string Description => "High-quality OHLCV aggregates for US equities with 2-year history on free tier.";
    protected override string HttpClientName => HttpClientNames.PolygonHistorical;



    public override int Priority => 12;
    public override TimeSpan RateLimitDelay => TimeSpan.FromSeconds(12); // 5 requests/minute = 12 seconds between requests
    public override int MaxRequestsPerWindow => 5;
    public override TimeSpan RateLimitWindow => TimeSpan.FromMinutes(1);

    /// <summary>
    /// Polygon supports adjusted bars with intraday aggregates and corporate actions.
    /// </summary>
    public override HistoricalDataCapabilities Capabilities { get; } = HistoricalDataCapabilities.BarsOnly with
    {
        Intraday = true
    };


    public PolygonHistoricalDataProvider(string? apiKey = null, HttpClient? httpClient = null, ILogger? log = null)
        : base(httpClient, log)
    {
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("POLYGON_API_KEY");

        Http.DefaultRequestHeaders.Add("User-Agent", "Meridian/1.0");
        Http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public override async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            Log.Warning("Polygon API key not configured. Set POLYGON_API_KEY environment variable or configure in settings.");
            return false;
        }

        try
        {
            await WaitForRateLimitSlotAsync(ct).ConfigureAwait(false);

            // Quick health check with ticker details endpoint
            var url = $"{BaseUrl}/v3/reference/tickers/AAPL?apiKey={_apiKey}";
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
        return adjustedBars.Select(b => b.ToHistoricalBar(preferAdjusted: true)).ToList();
    }

    public override async Task<IReadOnlyList<AdjustedHistoricalBar>> GetAdjustedDailyBarsAsync(string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidateSymbol(symbol);

        if (string.IsNullOrEmpty(_apiKey))
            throw new InvalidOperationException("Polygon API key is required. Set POLYGON_API_KEY environment variable.");

        await WaitForRateLimitSlotAsync(ct).ConfigureAwait(false);

        var normalizedSymbol = NormalizeSymbol(symbol);

        // Build URL with date range
        // Free tier limited to 2 years
        var startDate = from?.ToString("yyyy-MM-dd") ?? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-2)).ToString("yyyy-MM-dd");
        var endDate = to?.ToString("yyyy-MM-dd") ?? DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        // Use aggregates/range endpoint with adjusted=true for split/dividend adjusted data
        var url = $"{BaseUrl}/v2/aggs/ticker/{normalizedSymbol}/range/1/day/{startDate}/{endDate}?adjusted=true&sort=asc&limit=50000&apiKey={_apiKey}";

        Log.Information("Requesting Polygon history for {Symbol} ({StartDate} to {EndDate})", symbol, startDate, endDate);

        try
        {
            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                HandleHttpResponse(response, symbol, "bars");
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var data = DeserializeResponse<PolygonAggregatesResponse>(json, symbol);

            if (data?.Results is null || data.Results.Count == 0)
            {
                Log.Warning("No data returned from Polygon for {Symbol} (resultsCount: {Count})",
                    symbol, data?.ResultsCount ?? 0);
                return Array.Empty<AdjustedHistoricalBar>();
            }

            var bars = new List<AdjustedHistoricalBar>();

            foreach (var result in data.Results)
            {
                // Polygon timestamp is in milliseconds
                var date = DateTimeOffset.FromUnixTimeMilliseconds(result.Timestamp).Date;
                var sessionDate = DateOnly.FromDateTime(date);

                // Skip if outside requested range
                if (from.HasValue && sessionDate < from.Value)
                    continue;
                if (to.HasValue && sessionDate > to.Value)
                    continue;

                // Validate OHLC using base class helper
                if (!ValidateOhlc(result.Open, result.High, result.Low, result.Close, symbol, sessionDate))
                    continue;

                bars.Add(new AdjustedHistoricalBar(
                    Symbol: symbol.ToUpperInvariant(),
                    SessionDate: sessionDate,
                    Open: result.Open,
                    High: result.High,
                    Low: result.Low,
                    Close: result.Close,
                    Volume: (long)(result.Volume ?? 0),
                    Source: Name,
                    SequenceNumber: sessionDate.DayNumber,
                    // Polygon returns adjusted prices by default when adjusted=true
                    AdjustedOpen: result.Open,
                    AdjustedHigh: result.High,
                    AdjustedLow: result.Low,
                    AdjustedClose: result.Close,
                    AdjustedVolume: (long)(result.Volume ?? 0),
                    SplitFactor: null,
                    DividendAmount: null
                ));
            }

            Log.Information("Fetched {Count} bars for {Symbol} from Polygon", bars.Count, symbol);
            return bars.OrderBy(b => b.SessionDate).ToList();
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "Failed to parse Polygon response for {Symbol}", symbol);
            throw new InvalidOperationException($"Failed to parse Polygon data for {symbol}", ex);
        }
    }

    /// <summary>
    /// Get intraday bars for a symbol. Polygon supports various intervals.
    /// </summary>
    public async Task<IReadOnlyList<AdjustedHistoricalBar>> GetIntradayBarsAsync(
        string symbol,
        string interval,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidateSymbol(symbol);

        if (string.IsNullOrEmpty(_apiKey))
            throw new InvalidOperationException("Polygon API key is required.");

        await WaitForRateLimitSlotAsync(ct).ConfigureAwait(false);

        var normalizedSymbol = NormalizeSymbol(symbol);

        // Parse interval (e.g., "1min", "5min", "15min", "1hour")
        var (multiplier, timespan) = ParseInterval(interval);

        var startDate = from?.ToString("yyyy-MM-dd") ?? DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1)).ToString("yyyy-MM-dd");
        var endDate = to?.ToString("yyyy-MM-dd") ?? DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        var url = $"{BaseUrl}/v2/aggs/ticker/{normalizedSymbol}/range/{multiplier}/{timespan}/{startDate}/{endDate}?adjusted=true&sort=asc&limit=50000&apiKey={_apiKey}";

        Log.Information("Requesting Polygon {Interval} bars for {Symbol} ({StartDate} to {EndDate})",
            interval, symbol, startDate, endDate);

        try
        {
            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                HandleHttpResponse(response, symbol, "bars");
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var data = DeserializeResponse<PolygonAggregatesResponse>(json, symbol);

            if (data?.Results is null || data.Results.Count == 0)
            {
                return Array.Empty<AdjustedHistoricalBar>();
            }

            var bars = new List<AdjustedHistoricalBar>();

            foreach (var result in data.Results)
            {
                var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(result.Timestamp);
                var sessionDate = DateOnly.FromDateTime(timestamp.Date);

                if (!ValidateOhlc(result.Open, result.High, result.Low, result.Close, symbol, sessionDate))
                    continue;

                bars.Add(new AdjustedHistoricalBar(
                    Symbol: symbol.ToUpperInvariant(),
                    SessionDate: sessionDate,
                    Open: result.Open,
                    High: result.High,
                    Low: result.Low,
                    Close: result.Close,
                    Volume: (long)(result.Volume ?? 0),
                    Source: Name,
                    SequenceNumber: result.Timestamp,
                    AdjustedOpen: result.Open,
                    AdjustedHigh: result.High,
                    AdjustedLow: result.Low,
                    AdjustedClose: result.Close,
                    AdjustedVolume: (long)(result.Volume ?? 0)
                ));
            }

            Log.Information("Fetched {Count} {Interval} bars for {Symbol} from Polygon",
                bars.Count, interval, symbol);
            return bars;
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "Failed to parse Polygon intraday response for {Symbol}", symbol);
            throw new InvalidOperationException($"Failed to parse Polygon intraday data for {symbol}", ex);
        }
    }

    /// <summary>
    /// Get stock splits for a symbol.
    /// </summary>
    public async Task<IReadOnlyList<SplitInfo>> GetSplitsAsync(string symbol, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(_apiKey))
            throw new InvalidOperationException("Polygon API key is required.");

        await WaitForRateLimitSlotAsync(ct).ConfigureAwait(false);

        var normalizedSymbol = NormalizeSymbol(symbol);
        var url = $"{BaseUrl}/v3/reference/splits?ticker={normalizedSymbol}&apiKey={_apiKey}";

        try
        {
            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<SplitInfo>();
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var data = DeserializeResponse<PolygonSplitsResponse>(json, symbol);

            if (data?.Results is null)
                return Array.Empty<SplitInfo>();

            return data.Results.Select(s => new SplitInfo(
                Symbol: symbol.ToUpperInvariant(),
                ExDate: DateOnly.ParseExact(s.ExecutionDate ?? s.ExDate ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture),
                SplitFrom: s.SplitFrom ?? 1,
                SplitTo: s.SplitTo ?? 1,
                Source: Name
            )).ToList();
        }
        catch
        {
            return Array.Empty<SplitInfo>();
        }
    }

    /// <summary>
    /// Get dividends for a symbol.
    /// </summary>
    public async Task<IReadOnlyList<DividendInfo>> GetDividendsAsync(string symbol, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(_apiKey))
            throw new InvalidOperationException("Polygon API key is required.");

        await WaitForRateLimitSlotAsync(ct).ConfigureAwait(false);

        var normalizedSymbol = NormalizeSymbol(symbol);
        var url = $"{BaseUrl}/v3/reference/dividends?ticker={normalizedSymbol}&apiKey={_apiKey}";

        try
        {
            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<DividendInfo>();
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var data = DeserializeResponse<PolygonDividendsResponse>(json, symbol);

            if (data?.Results is null)
                return Array.Empty<DividendInfo>();

            return data.Results.Select(d => new DividendInfo(
                Symbol: symbol.ToUpperInvariant(),
                ExDate: DateOnly.ParseExact(d.ExDividendDate ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture),
                PaymentDate: !string.IsNullOrEmpty(d.PayDate) ? DateOnly.ParseExact(d.PayDate, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
                RecordDate: !string.IsNullOrEmpty(d.RecordDate) ? DateOnly.ParseExact(d.RecordDate, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
                Amount: d.CashAmount ?? 0,
                Currency: d.Currency ?? "USD",
                Type: ParseDividendType(d.DividendType),
                Source: Name
            )).ToList();
        }
        catch
        {
            return Array.Empty<DividendInfo>();
        }
    }


    protected override string NormalizeSymbol(string symbol)
    {
        // Use centralized symbol normalization utility
        return SymbolNormalization.Normalize(symbol);
    }

    private static (int multiplier, string timespan) ParseInterval(string interval)
    {
        var normalized = interval.ToLowerInvariant().Replace(" ", "");

        return normalized switch
        {
            "1min" or "1m" or "minute" => (1, "minute"),
            "5min" or "5m" => (5, "minute"),
            "15min" or "15m" => (15, "minute"),
            "30min" or "30m" => (30, "minute"),
            "1hour" or "1h" or "hour" => (1, "hour"),
            "4hour" or "4h" => (4, "hour"),
            "1day" or "1d" or "day" or "daily" => (1, "day"),
            "1week" or "1w" or "week" => (1, "week"),
            "1month" or "1mo" or "month" => (1, "month"),
            _ => (1, "day")
        };
    }

    private static DividendType ParseDividendType(string? type)
    {
        return type?.ToLowerInvariant() switch
        {
            "cd" or "regular" => DividendType.Regular,
            "sc" or "special" => DividendType.Special,
            "lt" or "return" => DividendType.Return,
            "lq" or "liquidation" => DividendType.Liquidation,
            _ => DividendType.Regular
        };
    }



    private sealed class PolygonAggregatesResponse
    {
        [JsonPropertyName("ticker")]
        public string? Ticker { get; set; }

        [JsonPropertyName("queryCount")]
        public int QueryCount { get; set; }

        [JsonPropertyName("resultsCount")]
        public int ResultsCount { get; set; }

        [JsonPropertyName("adjusted")]
        public bool Adjusted { get; set; }

        [JsonPropertyName("results")]
        public List<PolygonAggregate>? Results { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("request_id")]
        public string? RequestId { get; set; }

        [JsonPropertyName("next_url")]
        public string? NextUrl { get; set; }
    }

    private sealed class PolygonAggregate
    {
        [JsonPropertyName("o")]
        public decimal Open { get; set; }

        [JsonPropertyName("h")]
        public decimal High { get; set; }

        [JsonPropertyName("l")]
        public decimal Low { get; set; }

        [JsonPropertyName("c")]
        public decimal Close { get; set; }

        [JsonPropertyName("v")]
        public decimal? Volume { get; set; }

        [JsonPropertyName("vw")]
        public decimal? VWAP { get; set; }

        [JsonPropertyName("t")]
        public long Timestamp { get; set; }

        [JsonPropertyName("n")]
        public int? NumberOfTrades { get; set; }
    }

    private sealed class PolygonSplitsResponse
    {
        [JsonPropertyName("results")]
        public List<PolygonSplit>? Results { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }

    private sealed class PolygonSplit
    {
        [JsonPropertyName("execution_date")]
        public string? ExecutionDate { get; set; }

        [JsonPropertyName("ex_date")]
        public string? ExDate { get; set; }

        [JsonPropertyName("split_from")]
        public decimal? SplitFrom { get; set; }

        [JsonPropertyName("split_to")]
        public decimal? SplitTo { get; set; }

        [JsonPropertyName("ticker")]
        public string? Ticker { get; set; }
    }

    private sealed class PolygonDividendsResponse
    {
        [JsonPropertyName("results")]
        public List<PolygonDividend>? Results { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }

    private sealed class PolygonDividend
    {
        [JsonPropertyName("cash_amount")]
        public decimal? CashAmount { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("declaration_date")]
        public string? DeclarationDate { get; set; }

        [JsonPropertyName("dividend_type")]
        public string? DividendType { get; set; }

        [JsonPropertyName("ex_dividend_date")]
        public string? ExDividendDate { get; set; }

        [JsonPropertyName("frequency")]
        public int? Frequency { get; set; }

        [JsonPropertyName("pay_date")]
        public string? PayDate { get; set; }

        [JsonPropertyName("record_date")]
        public string? RecordDate { get; set; }

        [JsonPropertyName("ticker")]
        public string? Ticker { get; set; }
    }

}

// ✅ ADR-001: IHistoricalDataProvider contract
// ✅ ADR-004: CancellationToken on all async methods
// ✅ Rate limiting via ExecuteGetAndReadAsync → WaitForRateLimitSlotAsync
// ✅ ThrowIfDisposed() + ValidateSymbol() at entry
// ✅ Private inner model classes with [JsonPropertyName]
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwelveDataQuotaConfig = Meridian.Core.Config.TwelveDataQuotaConfig;
using Meridian.Core.Exceptions;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Meridian.Infrastructure.Resilience;
using Meridian.Infrastructure.Utilities;
using Serilog;

namespace Meridian.Infrastructure.Adapters.TwelveData;

/// <summary>
/// Historical data provider using the Twelve Data API (https://api.twelvedata.com).
/// Provides OHLCV bars for US equities, ETFs, international stocks, forex, and crypto.
/// Basic profile: 800 credits/day, 8 credits/minute; daily history costs one credit per request.
/// Extends BaseHistoricalDataProvider for common functionality.
/// </summary>
[DataSource("twelvedata", "Twelve Data", DataSourceType.Historical, DataSourceCategory.Premium,
    Priority = 22, Description = "Historical OHLCV via Twelve Data API (free/paid tiers)")]
[ImplementsAdr("ADR-001", "TwelveData historical data provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
[RequiresCredential("TWELVEDATA_API_KEY",
    EnvironmentVariables = new[] { "TWELVEDATA_API_KEY", "TWELVEDATA__APIKEY" },
    DisplayName = "API Key",
    Description = "Twelve Data API key from https://twelvedata.com/account/api-keys")]
public sealed partial class TwelveDataHistoricalDataProvider : BaseHistoricalDataProvider
{
    private const string BaseUrl = "https://api.twelvedata.com/time_series";
    private const int MaximumDataPoints = 5000;

    private readonly string? _apiKey;
    private readonly RateLimiterRegistry _rateLimiterRegistry;
    private readonly TimeProvider _timeProvider;
    private readonly RequestBudgetConfiguration? _quotaBudget;

    protected override RateLimiter RequestRateLimiter => string.IsNullOrWhiteSpace(_apiKey)
        ? RateLimiter
        : _rateLimiterRegistry.GetOrCreate(Name, _apiKey, "api-requests", _quotaBudget!);



    public override string Name => "twelvedata";
    public override string DisplayName => "Twelve Data";
    public override string Description => "OHLCV data for US and international equities, ETFs, forex, and crypto via Twelve Data API.";
    protected override string HttpClientName => HttpClientNames.TwelveDataHistorical;



    public override int Priority => RuntimeOptions?.Priority ?? Meridian.Core.Config.BackfillProviderCatalog.TwelveDataPriority;
    public override TimeSpan RateLimitDelay => _quotaBudget?.MinimumDelay ?? TimeSpan.FromMilliseconds(7500);
    public override int MaxRequestsPerWindow => _quotaBudget?.Maximum ?? 8;
    public override TimeSpan RateLimitWindow => TimeSpan.FromMinutes(1);

    public override HistoricalDataCapabilities Capabilities { get; } =
        HistoricalDataCapabilities.BarsOnly.WithMarkets("US", "UK", "EU", "CA", "AU");


    public TwelveDataHistoricalDataProvider(string? apiKey = null, HttpClient? httpClient = null, ILogger? log = null, RateLimiterRegistry? rateLimiterRegistry = null,
        TwelveDataQuotaConfig? quotaConfig = null,
        BackfillProviderRuntimeOptions? runtimeOptions = null)
        : base(httpClient, log, timeProvider: rateLimiterRegistry?.TimeProvider, runtimeOptions: runtimeOptions)
    {
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("TWELVEDATA_API_KEY");
        _rateLimiterRegistry = rateLimiterRegistry ?? RateLimiterRegistry.Shared;
        _timeProvider = _rateLimiterRegistry.TimeProvider;
        _quotaBudget = ProviderQuotaBudgets.TwelveData(quotaConfig, runtimeOptions);
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
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(_apiKey))
        {
            Log.Warning("Twelve Data API key not configured; skipping request for {Symbol}", symbol);
            return Array.Empty<HistoricalBar>();
        }

        var startDate = from ?? new DateOnly(2000, 1, 1);
        var endDate = to ?? DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        if (startDate > endDate)
            throw new ArgumentException("The start date must not follow the end date.", nameof(from));

        // At most one daily bar exists per calendar date, including seven-day markets.
        // Bounding by calendar days avoids relying on a short page to prove completeness.
        // https://support.twelvedata.com/en/articles/5214728-getting-historical-data
        // Keep results private until every date window has been read successfully.
        var bars = new Dictionary<DateOnly, HistoricalBar>();
        var windowStart = startDate;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var windowEnd = DateOnly.FromDayNumber(Math.Min(
                windowStart.DayNumber + MaximumDataPoints - 1, endDate.DayNumber));
            var url = $"{BaseUrl}?symbol={Uri.EscapeDataString(symbol)}&interval=1day" +
                      $"&start_date={windowStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
                      $"&end_date={Uri.EscapeDataString(windowEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " 23:59:59")}" +
                      $"&outputsize={MaximumDataPoints}&format=JSON&apikey={Uri.EscapeDataString(_apiKey)}";

            Log.Information("Requesting bounded Twelve Data historical window");
            var json = await ExecuteGetAndReadAsync(url, symbol, "daily bars", ct).ConfigureAwait(false);
            // The shared HTTP helper returns null only for HTTP 404. Treat it like the
            // body's unknown-symbol outcome, never as a successfully read date window.
            if (json is null)
            {
                if (bars.Count > 0)
                    throw InvalidResponse(symbol, "symbol disappeared during historical retrieval");
                return Array.Empty<HistoricalBar>();
            }

            var page = ParseJsonResponse(json, symbol, windowStart, windowEnd);
            foreach (var bar in page.Bars)
            {
                ct.ThrowIfCancellationRequested();
                if (bars.TryGetValue(bar.SessionDate, out var existing) && existing != bar)
                    throw InvalidResponse(symbol, "conflicting daily bars for the same session");
                bars[bar.SessionDate] = bar;
            }

            // An unknown symbol applies to the whole request, unlike an empty date window.
            if (page.UnknownSymbol)
            {
                if (bars.Count > 0)
                    throw InvalidResponse(symbol, "symbol disappeared during historical retrieval");
                return Array.Empty<HistoricalBar>();
            }

            if (windowEnd == endDate)
                break;

            // Overlap the boundary to accommodate the provider's date-boundary behavior.
            // The next window always advances by MaximumDataPoints - 1 calendar days.
            windowStart = windowEnd;
        }

        ct.ThrowIfCancellationRequested();
        Log.Information("Fetched {Count} bars for {Symbol} from Twelve Data", bars.Count, symbol);
        return bars.Values.OrderBy(b => b.SessionDate).ToArray();
    }

    protected override Task<HttpHandleResult> HandleHttpResponseAsync(
        HttpResponseMessage response, string symbol, string dataType, CancellationToken ct)
    {
        if (CorporateActionFetchResult.IsPartialResponse(response))
            throw InvalidResponse(symbol, "incomplete HTTP response");
        return base.HandleHttpResponseAsync(response, symbol, dataType, ct);
    }

    private HistoricalPage ParseJsonResponse(string json, string symbol, DateOnly from, DateOnly to)
    {
        TwelveDataResponse? response;
        try
        {
            response = JsonSerializer.Deserialize(json, TwelveDataHistoricalJsonContext.Default.TwelveDataResponse);
        }
        catch (JsonException)
        {
            Log.Error("Failed to parse Twelve Data historical response");
            throw InvalidResponse(symbol, "malformed JSON");
        }

        if (response is null)
            throw InvalidResponse(symbol, "missing response envelope");

        if (string.Equals(response.Status, "error", StringComparison.OrdinalIgnoreCase))
        {
            if (response.Code is 401 or 403)
                throw new ConnectionException("Twelve Data rejected the historical request credentials or entitlement.", provider: Name);

            if (response.Code == 429)
            {
                RecordRateLimitHit(RateLimitWindow);
                RaiseRateLimitHit(GetRateLimitInfo());
                throw new RateLimitException("Twelve Data historical request exceeded its quota.",
                    provider: Name, symbol: symbol, retryAfter: RateLimitWindow);
            }

            // Other 400 errors (invalid parameters, unsupported intervals, etc.) are failures.
            // Do not put provider-supplied messages in exceptions: they may echo credentials.
            if (response.Code is 400 or 404 && response.Message is { } message)
            {
                if (message.Contains("symbol", StringComparison.OrdinalIgnoreCase)
                    && message.Contains("not found", StringComparison.OrdinalIgnoreCase))
                    return new HistoricalPage([], UnknownSymbol: true);
                if (message.StartsWith("No data is available", StringComparison.OrdinalIgnoreCase))
                    return new HistoricalPage([], UnknownSymbol: false);
            }

            throw InvalidResponse(symbol, $"provider error code {response.Code?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
        }

        if (!string.Equals(response.Status, "ok", StringComparison.OrdinalIgnoreCase) || response.Values is null)
            throw InvalidResponse(symbol, "missing success status or values");
        if (response.Values.Count > MaximumDataPoints)
            throw InvalidResponse(symbol, "daily window exceeded the response limit");

        var bars = new List<HistoricalBar>(response.Values.Count);
        var sessions = new HashSet<DateOnly>();

        foreach (var value in response.Values)
        {
            if (value is null)
                throw InvalidResponse(symbol, "null daily bar");
            if (string.IsNullOrEmpty(value.Datetime))
                throw InvalidResponse(symbol, "missing daily session date");

            if (!ProviderDateParsing.TryParseProviderDate(value.Datetime, "yyyy-MM-dd", out var sessionDate))
                throw InvalidResponse(symbol, "invalid daily session date");
            if (!sessions.Add(sessionDate))
                throw InvalidResponse(symbol, "duplicate daily session date");

            if (sessionDate < from)
                continue;
            if (sessionDate > to)
                continue;

            if (!decimal.TryParse(value.Open, NumberStyles.Float, CultureInfo.InvariantCulture, out var open))
                throw InvalidResponse(symbol, "invalid daily price");
            if (!decimal.TryParse(value.High, NumberStyles.Float, CultureInfo.InvariantCulture, out var high))
                throw InvalidResponse(symbol, "invalid daily price");
            if (!decimal.TryParse(value.Low, NumberStyles.Float, CultureInfo.InvariantCulture, out var low))
                throw InvalidResponse(symbol, "invalid daily price");
            if (!decimal.TryParse(value.Close, NumberStyles.Float, CultureInfo.InvariantCulture, out var close))
                throw InvalidResponse(symbol, "invalid daily price");

            if (!IsValidOhlc(open, high, low, close) || low > high || open < low || open > high || close < low || close > high)
                throw InvalidResponse(symbol, "inconsistent daily prices");

            long volume = 0;
            if (!string.IsNullOrEmpty(value.Volume)
                && (!long.TryParse(value.Volume, NumberStyles.Integer, CultureInfo.InvariantCulture, out volume) || volume < 0))
                throw InvalidResponse(symbol, "invalid daily volume");

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

        if (response.Values.Count > 0 && bars.Count == 0)
            throw InvalidResponse(symbol, "response contained no usable daily bars");
        return new HistoricalPage(bars, UnknownSymbol: false);
    }

    private DataProviderException InvalidResponse(string symbol, string reason)
        => new($"Twelve Data historical request failed: {reason}.", provider: Name, symbol: symbol);

    private sealed record HistoricalPage(List<HistoricalBar> Bars, bool UnknownSymbol);

    private sealed class TwelveDataResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("values")]
        public List<TwelveDataBar>? Values { get; set; }

        [JsonPropertyName("code")]
        public int? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
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

    [JsonSourceGenerationOptions(WriteIndented = false)]
    [JsonSerializable(typeof(TwelveDataResponse))]
    private sealed partial class TwelveDataHistoricalJsonContext : JsonSerializerContext;

}

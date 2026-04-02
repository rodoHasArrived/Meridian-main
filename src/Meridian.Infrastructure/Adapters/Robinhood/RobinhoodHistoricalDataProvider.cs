// ✅ ADR-001: IHistoricalDataProvider contract via BaseHistoricalDataProvider
// ✅ ADR-004: CancellationToken on all async methods
// ✅ ADR-005: Attribute-based provider discovery via [DataSource]
// ✅ ADR-010: HTTP client via IHttpClientFactory (HttpClientFactoryProvider)
// ✅ Rate limiting via WaitForRateLimitSlotAsync (inherited from BaseHistoricalDataProvider)
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Exceptions;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Robinhood;

/// <summary>
/// Historical data provider using the Robinhood unofficial API.
/// Provides daily OHLCV bars for US equities via Bearer-token authentication.
///
/// <para>
/// <b>Important:</b> Robinhood does not publish an official public API.
/// This provider targets the same endpoints used by the Robinhood mobile application.
/// Users must supply a personal access token via the <c>ROBINHOOD_ACCESS_TOKEN</c>
/// environment variable. Tokens can be obtained through the Robinhood OAuth2 login
/// flow or from a valid browser/app session.
/// </para>
///
/// <para>
/// Rate limit: the endpoint is undocumented; the provider defaults to 100 req/hour
/// to avoid triggering abuse-detection heuristics. Adjust <c>RateLimitPerHour</c>
/// in config if needed.
/// </para>
///
/// Coverage: US equities and ETFs traded on Robinhood's platform.
/// </summary>
[DataSource("robinhood", "Robinhood (unofficial)", DataSourceType.Historical, DataSourceCategory.Free,
    Priority = 35, Description = "Daily OHLCV bars via Robinhood unofficial API (requires personal access token)")]
[ImplementsAdr("ADR-001", "Robinhood historical data provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
public sealed class RobinhoodHistoricalDataProvider : BaseHistoricalDataProvider
{
    private const string BaseUrl = "https://api.robinhood.com/marketdata/historicals/";
    private const string EnvAccessToken = "ROBINHOOD_ACCESS_TOKEN";

    private readonly string? _accessToken;
    private readonly int _priority;

    #region Abstract property implementations

    public override string Name => "robinhood";
    public override string DisplayName => "Robinhood (unofficial)";
    public override string Description => "Daily OHLCV bars for US equities via the Robinhood unofficial API.";
    protected override string HttpClientName => HttpClientNames.RobinhoodHistorical;

    #endregion

    #region Rate-limit overrides

    public override int Priority => _priority;
    public override TimeSpan RateLimitDelay => TimeSpan.Zero; // Use the hourly window-based limit.
    public override int MaxRequestsPerWindow => 100; // 100 requests/hour for the undocumented endpoint.
    public override TimeSpan RateLimitWindow => TimeSpan.FromHours(1);

    #endregion

    /// <summary>
    /// Capabilities: daily bars only, US equities.
    /// </summary>
    public override HistoricalDataCapabilities Capabilities { get; } =
        HistoricalDataCapabilities.BarsOnly.WithMarkets("US");

    /// <summary>
    /// Creates a new Robinhood historical data provider.
    /// </summary>
    /// <param name="accessToken">Bearer access token (falls back to ROBINHOOD_ACCESS_TOKEN env var).</param>
    /// <param name="priority">Priority in fallback chain (lower = tried first, default: 35).</param>
    /// <param name="httpClient">Optional HTTP client instance.</param>
    /// <param name="log">Optional logger instance.</param>
    public RobinhoodHistoricalDataProvider(
        string? accessToken = null,
        int priority = 35,
        HttpClient? httpClient = null,
        ILogger? log = null)
        : base(httpClient, log)
    {
        _priority = priority;
        _accessToken = accessToken ?? Environment.GetEnvironmentVariable(EnvAccessToken);

        if (!string.IsNullOrEmpty(_accessToken))
        {
            Http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _accessToken);
        }

        Http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public override async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_accessToken))
        {
            Log.Warning("Robinhood access token not configured (set {Env})", EnvAccessToken);
            return false;
        }

        try
        {
            var url = $"{BaseUrl}?symbols=AAPL&interval=day&span=week&bounds=regular";
            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Robinhood availability check failed");
            return false;
        }
    }

    public override async Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
        string symbol,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidateSymbol(symbol);

        if (string.IsNullOrEmpty(_accessToken))
            throw new ConnectionException(
                "Robinhood access token is required. Set the ROBINHOOD_ACCESS_TOKEN environment variable.",
                provider: Name);

        var normalizedSymbol = NormalizeSymbol(symbol);

        // Robinhood uses span-based queries rather than explicit date ranges.
        // We request the maximum span (5year) and filter client-side.
        var span = SelectSpan(from, to);
        var url = BuildUrl(normalizedSymbol, span);

        Log.Information("Requesting Robinhood history for {Symbol} (span={Span})", symbol, span);

        // ExecuteGetAndReadAsync internally calls WaitForRateLimitSlotAsync; do not call it separately.
        var json = await ExecuteGetAndReadAsync(url, symbol, "bars", ct).ConfigureAwait(false);

        if (string.IsNullOrEmpty(json))
        {
            Log.Warning("No data returned from Robinhood for {Symbol}", symbol);
            return [];
        }

        var response = JsonSerializer.Deserialize(json, RobinhoodHistoricalSerializerContext.Default.RobinhoodHistoricalsResponse);
        if (response?.Results is null || response.Results.Count == 0)
        {
            Log.Warning("Empty results from Robinhood for {Symbol}", symbol);
            return [];
        }

        var symbolResult = response.Results.FirstOrDefault(r =>
            string.Equals(r.Symbol, normalizedSymbol, StringComparison.OrdinalIgnoreCase));

        if (symbolResult?.Historicals is null || symbolResult.Historicals.Count == 0)
        {
            Log.Warning("No historicals for {Symbol} in Robinhood response", symbol);
            return [];
        }

        var bars = new List<HistoricalBar>();

        foreach (var item in symbolResult.Historicals)
        {
            if (item.BeginsAt is null)
                continue;

            if (!DateTimeOffset.TryParse(item.BeginsAt, out var timestamp))
                continue;

            var sessionDate = DateOnly.FromDateTime(timestamp.UtcDateTime);

            if (from.HasValue && sessionDate < from.Value)
                continue;
            if (to.HasValue && sessionDate > to.Value)
                continue;

            if (!decimal.TryParse(item.OpenPrice, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var open) ||
                !decimal.TryParse(item.ClosePrice, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var close) ||
                !decimal.TryParse(item.HighPrice, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var high) ||
                !decimal.TryParse(item.LowPrice, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var low))
                continue;

            if (!ValidateOhlc(open, high, low, close, symbol, sessionDate))
                continue;

            bars.Add(new HistoricalBar(
                Symbol: symbol.ToUpperInvariant(),
                SessionDate: sessionDate,
                Open: open,
                High: high,
                Low: low,
                Close: close,
                Volume: item.Volume,
                Source: Name,
                SequenceNumber: sessionDate.DayNumber
            ));
        }

        var result = bars.OrderBy(b => b.SessionDate).ToList();
        Log.Information("Fetched {Count} bars for {Symbol} from Robinhood", result.Count, symbol);
        return result;
    }

    private static string SelectSpan(DateOnly? from, DateOnly? to)
    {
        if (from is null)
            return "5year";

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var reference = to ?? today;
        var daysBack = (reference.DayNumber - from.Value.DayNumber);

        return daysBack switch
        {
            <= 7 => "week",
            <= 31 => "month",
            <= 91 => "3month",
            <= 365 => "year",
            _ => "5year"
        };
    }

    private static string BuildUrl(string symbol, string span)
    {
        return $"{BaseUrl}?symbols={Uri.EscapeDataString(symbol)}&interval=day&span={span}&bounds=regular";
    }

    protected override string NormalizeSymbol(string symbol)
    {
        return symbol.ToUpperInvariant().Trim();
    }

    #region Robinhood API response models (internal — referenced by ADR-014 source-generated context)

    internal sealed class RobinhoodHistoricalsResponse
    {
        [JsonPropertyName("results")]
        public List<RobinhoodSymbolResult>? Results { get; set; }
    }

    internal sealed class RobinhoodSymbolResult
    {
        [JsonPropertyName("symbol")]
        public string? Symbol { get; set; }

        [JsonPropertyName("historicals")]
        public List<RobinhoodBar>? Historicals { get; set; }

        [JsonPropertyName("span")]
        public string? Span { get; set; }

        [JsonPropertyName("interval")]
        public string? Interval { get; set; }

        [JsonPropertyName("bounds")]
        public string? Bounds { get; set; }
    }

    internal sealed class RobinhoodBar
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
        public long Volume { get; set; }

        [JsonPropertyName("session")]
        public string? Session { get; set; }

        [JsonPropertyName("interpolated")]
        public bool Interpolated { get; set; }
    }

    #endregion
}

/// <summary>
/// Source-generated JSON serializer context for Robinhood historical API DTOs (ADR-014).
/// </summary>
[JsonSerializable(typeof(RobinhoodHistoricalDataProvider.RobinhoodHistoricalsResponse))]
[JsonSerializable(typeof(RobinhoodHistoricalDataProvider.RobinhoodSymbolResult))]
[JsonSerializable(typeof(RobinhoodHistoricalDataProvider.RobinhoodBar))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
internal sealed partial class RobinhoodHistoricalSerializerContext : JsonSerializerContext;

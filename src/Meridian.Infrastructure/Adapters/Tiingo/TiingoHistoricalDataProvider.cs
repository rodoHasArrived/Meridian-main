using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Exceptions;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Meridian.Infrastructure.Utilities;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Tiingo;

/// <summary>
/// Historical data provider using Tiingo API (free tier with API key).
/// Provides excellent dividend-adjusted OHLCV data with full adjustment history.
/// Coverage: 65,000+ US/international equities, ETFs, mutual funds.
/// Free tier: 1,000 requests/day, 50 requests/hour.
/// Extends BaseHistoricalDataProvider for common functionality.
/// </summary>
[DataSource("tiingo", "Tiingo (free tier)", DataSourceType.Historical, DataSourceCategory.Free,
    Priority = 15, Description = "Dividend-adjusted historical OHLCV data via Tiingo API")]
[ImplementsAdr("ADR-001", "Tiingo historical data provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
[RequiresCredential("TIINGO_API_TOKEN",
    EnvironmentVariables = new[] { "TIINGO_API_TOKEN", "TIINGO__TOKEN" },
    DisplayName = "API Token",
    Description = "Tiingo API token from https://www.tiingo.com/account/api/token")]
public sealed class TiingoHistoricalDataProvider : BaseHistoricalDataProvider
{
    private const string BaseUrl = "https://api.tiingo.com/tiingo/daily";

    private readonly string? _apiToken;


    public override string Name => "tiingo";
    public override string DisplayName => "Tiingo (free tier)";
    public override string Description => "High-quality dividend-adjusted OHLCV for US/international equities with corporate actions.";
    protected override string HttpClientName => HttpClientNames.TiingoHistorical;



    public override int Priority => 15;
    public override TimeSpan RateLimitDelay => TimeSpan.FromSeconds(1.5); // 50 requests/hour = ~72 seconds between requests
    public override int MaxRequestsPerWindow => 50;
    public override TimeSpan RateLimitWindow => TimeSpan.FromHours(1);

    /// <summary>
    /// Tiingo supports adjusted bars with corporate actions for multiple markets.
    /// </summary>
    public override HistoricalDataCapabilities Capabilities { get; } =
        HistoricalDataCapabilities.BarsOnly.WithMarkets("US", "UK", "DE", "CA", "AU");


    public TiingoHistoricalDataProvider(string? apiToken = null, HttpClient? httpClient = null, ILogger? log = null)
        : base(httpClient, log)
    {
        _apiToken = apiToken ?? Environment.GetEnvironmentVariable("TIINGO_API_TOKEN");

        // Content-Type is a content header, not a request header - it's set per-request on the Content object
        // No need to set it globally since each request will set it on its StringContent

        if (!string.IsNullOrEmpty(_apiToken))
        {
            Http.DefaultRequestHeaders.Add("Authorization", $"Token {_apiToken}");
        }
    }

    /// <summary>
    /// Tiingo uses uppercase tickers with dots replaced by dashes.
    /// </summary>
    protected override string NormalizeSymbol(string symbol)
    {
        return SymbolNormalization.NormalizeForTiingo(symbol);
    }

    public override async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiToken))
        {
            Log.Warning("Tiingo API token not configured. Set TIINGO_API_TOKEN environment variable or configure in settings.");
            return false;
        }

        try
        {
            // Quick health check with metadata endpoint
            var url = $"{BaseUrl}/AAPL?token={_apiToken}";
            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
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

        if (string.IsNullOrEmpty(_apiToken))
            throw new InvalidOperationException("Tiingo API token is required. Set TIINGO_API_TOKEN environment variable.");

        await WaitForRateLimitSlotAsync(ct).ConfigureAwait(false);

        var normalizedSymbol = NormalizeSymbol(symbol);

        // Build URL with date range
        var startDate = from?.ToString("yyyy-MM-dd") ?? "2000-01-01";
        var endDate = to?.ToString("yyyy-MM-dd") ?? DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        var url = $"{BaseUrl}/{normalizedSymbol}/prices?startDate={startDate}&endDate={endDate}&token={_apiToken}";

        Log.Information("Requesting Tiingo history for {Symbol} ({StartDate} to {EndDate})", symbol, startDate, endDate);

        try
        {
            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                Log.Warning("Tiingo returned {Status} for {Symbol}: {Error}",
                    response.StatusCode, symbol, error);

                if ((int)response.StatusCode == 429)
                {
                    var retryAfter = HttpResponseHandler.ExtractRetryAfter(response) ?? TimeSpan.FromMinutes(1);
                    RecordRateLimitHit(retryAfter);
                    throw new RateLimitException(
                        $"Tiingo rate limit exceeded (429) for {symbol}",
                        provider: Name,
                        symbol: symbol,
                        retryAfter: retryAfter);
                }

                throw new InvalidOperationException($"Tiingo returned {(int)response.StatusCode} for symbol {symbol}");
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var prices = DeserializeResponse<List<TiingoPriceData>>(json, symbol);

            if (prices is null || prices.Count == 0)
            {
                Log.Warning("No data returned from Tiingo for {Symbol}", symbol);
                return Array.Empty<AdjustedHistoricalBar>();
            }

            var bars = new List<AdjustedHistoricalBar>();

            foreach (var price in prices)
            {
                if (price.Date is null)
                    continue;

                var sessionDate = DateOnly.ParseExact(price.Date[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture);

                // Skip if outside requested range
                if (from.HasValue && sessionDate < from.Value)
                    continue;
                if (to.HasValue && sessionDate > to.Value)
                    continue;

                // Validate OHLC using base class helper
                if (!ValidateOhlc(price.Open, price.High, price.Low, price.Close, symbol, sessionDate))
                    continue;

                // Calculate split factor from adjusted/raw close ratio
                decimal? splitFactor = null;
                if (price.AdjClose.HasValue && price.Close > 0)
                {
                    var factor = price.AdjClose.Value / price.Close;
                    if (Math.Abs(factor - 1m) > 0.0001m)
                    {
                        splitFactor = factor;
                    }
                }

                // Tiingo provides divCash directly
                decimal? dividendAmount = price.DivCash.HasValue && price.DivCash.Value > 0
                    ? price.DivCash.Value
                    : null;

                bars.Add(new AdjustedHistoricalBar(
                    Symbol: symbol.ToUpperInvariant(),
                    SessionDate: sessionDate,
                    Open: price.Open,
                    High: price.High,
                    Low: price.Low,
                    Close: price.Close,
                    Volume: (long)(price.Volume ?? 0),
                    Source: Name,
                    SequenceNumber: sessionDate.DayNumber,
                    AdjustedOpen: price.AdjOpen,
                    AdjustedHigh: price.AdjHigh,
                    AdjustedLow: price.AdjLow,
                    AdjustedClose: price.AdjClose,
                    AdjustedVolume: price.AdjVolume.HasValue ? (long)price.AdjVolume.Value : null,
                    SplitFactor: price.SplitFactor.HasValue && price.SplitFactor.Value != 1.0m
                        ? price.SplitFactor.Value
                        : splitFactor,
                    DividendAmount: dividendAmount
                ));
            }

            Log.Information("Fetched {Count} bars for {Symbol} from Tiingo", bars.Count, symbol);
            return bars.OrderBy(b => b.SessionDate).ToList();
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "Failed to parse Tiingo response for {Symbol}", symbol);
            throw new InvalidOperationException($"Failed to parse Tiingo data for {symbol}", ex);
        }
    }


    private sealed class TiingoPriceData
    {
        [JsonPropertyName("date")]
        public string? Date { get; set; }

        [JsonPropertyName("open")]
        public decimal Open { get; set; }

        [JsonPropertyName("high")]
        public decimal High { get; set; }

        [JsonPropertyName("low")]
        public decimal Low { get; set; }

        [JsonPropertyName("close")]
        public decimal Close { get; set; }

        [JsonPropertyName("volume")]
        public decimal? Volume { get; set; }

        [JsonPropertyName("adjOpen")]
        public decimal? AdjOpen { get; set; }

        [JsonPropertyName("adjHigh")]
        public decimal? AdjHigh { get; set; }

        [JsonPropertyName("adjLow")]
        public decimal? AdjLow { get; set; }

        [JsonPropertyName("adjClose")]
        public decimal? AdjClose { get; set; }

        [JsonPropertyName("adjVolume")]
        public decimal? AdjVolume { get; set; }

        [JsonPropertyName("divCash")]
        public decimal? DivCash { get; set; }

        [JsonPropertyName("splitFactor")]
        public decimal? SplitFactor { get; set; }
    }

}

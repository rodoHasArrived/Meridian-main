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

namespace Meridian.Infrastructure.Adapters.YahooFinance;

/// <summary>
/// Historical data provider using Yahoo Finance (free, unofficial API).
/// Provides daily OHLCV with adjusted close prices.
/// Coverage: 50,000+ global equities, ETFs, indices, crypto.
/// Extends BaseHistoricalDataProvider for common functionality.
/// </summary>
[DataSource("yahoo", "Yahoo Finance (free)", DataSourceType.Historical, DataSourceCategory.Free,
    Priority = 22, Description = "Free daily OHLCV via Yahoo Finance unofficial API")]
[ImplementsAdr("ADR-001", "Yahoo Finance historical data provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
public sealed class YahooFinanceHistoricalDataProvider : BaseHistoricalDataProvider
{
    private const string ApiBaseUrl = "https://query1.finance.yahoo.com/v8/finance/chart";

    public override string Name => "yahoo";
    public override string DisplayName => "Yahoo Finance (free)";
    public override string Description => "Free daily OHLCV with adjusted prices for global equities, ETFs, indices.";
    protected override string HttpClientName => HttpClientNames.YahooFinanceHistorical;

    // Keep Yahoo as a resilience fallback; prefer API-backed providers first.
    public override int Priority => 22;
    public override TimeSpan RateLimitDelay => TimeSpan.FromSeconds(0.5);
    public override int MaxRequestsPerWindow => 2000;
    public override TimeSpan RateLimitWindow => TimeSpan.FromHours(1);

    /// <summary>
    /// Yahoo Finance supports adjusted bars with corporate actions for global markets.
    /// </summary>
    public override HistoricalDataCapabilities Capabilities { get; } =
        HistoricalDataCapabilities.BarsOnly.WithMarkets("US", "UK", "DE", "JP", "CA", "AU", "HK", "SG");

    public YahooFinanceHistoricalDataProvider(HttpClient? httpClient = null, ILogger? log = null)
        : base(httpClient, log)
    {
        // Yahoo Finance requires a browser-like User-Agent
        if (!Http.DefaultRequestHeaders.Contains("User-Agent"))
        {
            Http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        }
    }

    /// <summary>
    /// Yahoo Finance uses standard tickers for US stocks.
    /// International symbols append exchange suffix (e.g., .L for London, .T for Tokyo)
    /// </summary>
    protected override string NormalizeSymbol(string symbol)
    {
        return SymbolNormalization.NormalizeForYahoo(symbol);
    }

    public override async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            // Quick health check with a known symbol
            using var response = await Http.GetAsync($"{ApiBaseUrl}/AAPL?range=1d&interval=1d", ct).ConfigureAwait(false);
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

        await WaitForRateLimitSlotAsync(ct).ConfigureAwait(false);

        var normalizedSymbol = NormalizeSymbol(symbol);
        var url = BuildRequestUrl(normalizedSymbol, from, to);

        Log.Information("Requesting Yahoo Finance history for {Symbol} ({Url})", symbol, url);

        try
        {
            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);

            var httpResult = await ResponseHandler.HandleResponseAsync(response, symbol, "daily bars", ct: ct).ConfigureAwait(false);

            // Throw on HTTP errors (non-success status codes)
            if (!httpResult.IsSuccess)
            {
                var errorMsg = httpResult.IsNotFound
                    ? $"Symbol {symbol} not found (404)"
                    : $"HTTP error {httpResult.StatusCode}: {httpResult.ReasonPhrase}";

                Log.Warning("Yahoo Finance HTTP error for {Symbol}: {Error}", symbol, errorMsg);
                throw new InvalidOperationException($"Failed to fetch Yahoo Finance data for {symbol}: {errorMsg}");
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var data = DeserializeResponse<YahooChartResponse>(json, symbol);

            var chartResult = data?.Chart?.Result?.FirstOrDefault();
            if (chartResult?.Indicators?.Quote?.FirstOrDefault() is null)
            {
                Log.Warning("No data returned from Yahoo Finance for {Symbol}", symbol);
                return Array.Empty<AdjustedHistoricalBar>();
            }

            var bars = ParseChartResult(chartResult, symbol, from, to);

            Log.Information("Fetched {Count} bars for {Symbol} from Yahoo Finance", bars.Count, symbol);
            return bars.OrderBy(b => b.SessionDate).ToList();
        }
        catch (DataProviderException ex)
        {
            // Convert to InvalidOperationException to match test expectations
            throw new InvalidOperationException($"Failed to parse Yahoo Finance data for {symbol}", ex);
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "Failed to parse Yahoo Finance response for {Symbol}", symbol);
            throw new InvalidOperationException($"Failed to parse Yahoo Finance data for {symbol}", ex);
        }
    }

    private static string BuildRequestUrl(string normalizedSymbol, DateOnly? from, DateOnly? to)
    {
        var period1 = from.HasValue
            ? new DateTimeOffset(from.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds()
            : new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

        var period2 = to.HasValue
            ? new DateTimeOffset(to.Value.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero).ToUnixTimeSeconds()
            : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        return $"{ApiBaseUrl}/{normalizedSymbol}?period1={period1}&period2={period2}&interval=1d&events=div,splits";
    }

    private List<AdjustedHistoricalBar> ParseChartResult(YahooChartResult chartResult, string symbol, DateOnly? from, DateOnly? to)
    {
        var timestamps = chartResult.Timestamp ?? Array.Empty<long>();
        if (chartResult.Indicators?.Quote is not { Count: > 0 })
            throw new DataProviderException("Yahoo Finance response missing indicator data");
        var quote = chartResult.Indicators.Quote[0];
        var adjClose = chartResult.Indicators.AdjClose?.FirstOrDefault()?.AdjClose;
        var events = chartResult.Events;

        var bars = new List<AdjustedHistoricalBar>();

        for (int i = 0; i < timestamps.Length; i++)
        {
            var date = DateTimeOffset.FromUnixTimeSeconds(timestamps[i]).Date;
            var sessionDate = DateOnly.FromDateTime(date);

            // Skip if outside requested range
            if (from.HasValue && sessionDate < from.Value)
                continue;
            if (to.HasValue && sessionDate > to.Value)
                continue;

            var open = GetDecimalValue(quote.Open, i);
            var high = GetDecimalValue(quote.High, i);
            var low = GetDecimalValue(quote.Low, i);
            var close = GetDecimalValue(quote.Close, i);
            var volume = GetLongValue(quote.Volume, i);

            if (open is null || high is null || low is null || close is null)
                continue;

            // Validate OHLC using base class helper
            if (!IsValidOhlc(open.Value, high.Value, low.Value, close.Value))
                continue;

            var adjCloseValue = adjClose is not null && i < adjClose.Length
                ? GetDecimalValue(adjClose, i)
                : null;

            // Calculate adjustment factor from adjusted close
            decimal? splitFactor = null;
            decimal? dividendAmount = null;

            if (adjCloseValue.HasValue && close.Value > 0)
            {
                var factor = adjCloseValue.Value / close.Value;
                if (Math.Abs(factor - 1m) > 0.0001m)
                {
                    splitFactor = factor;
                }
            }

            // Check for dividend/split events on this date
            var dateKey = timestamps[i].ToString();
            if (events?.Dividends?.TryGetValue(dateKey, out var dividend) == true)
            {
                dividendAmount = dividend.Amount;
            }

            bars.Add(new AdjustedHistoricalBar(
                Symbol: symbol.ToUpperInvariant(),
                SessionDate: sessionDate,
                Open: open.Value,
                High: high.Value,
                Low: low.Value,
                Close: close.Value,
                Volume: volume ?? 0,
                Source: Name,
                SequenceNumber: sessionDate.DayNumber,
                AdjustedOpen: adjCloseValue.HasValue ? open.Value * (adjCloseValue.Value / close.Value) : null,
                AdjustedHigh: adjCloseValue.HasValue ? high.Value * (adjCloseValue.Value / close.Value) : null,
                AdjustedLow: adjCloseValue.HasValue ? low.Value * (adjCloseValue.Value / close.Value) : null,
                AdjustedClose: adjCloseValue,
                AdjustedVolume: null,
                SplitFactor: splitFactor,
                DividendAmount: dividendAmount
            ));
        }

        return bars;
    }

    private static decimal? GetDecimalValue(decimal?[]? array, int index)
    {
        if (array is null || index >= array.Length || array[index] is null)
            return null;
        return array[index]!.Value;
    }

    private static long? GetLongValue(long?[]? array, int index)
    {
        if (array is null || index >= array.Length)
            return null;
        return array[index];
    }


    private sealed class YahooChartResponse
    {
        [JsonPropertyName("chart")]
        public YahooChart? Chart { get; set; }
    }

    private sealed class YahooChart
    {
        [JsonPropertyName("result")]
        public List<YahooChartResult>? Result { get; set; }

        [JsonPropertyName("error")]
        public object? Error { get; set; }
    }

    private sealed class YahooChartResult
    {
        [JsonPropertyName("meta")]
        public YahooMeta? Meta { get; set; }

        [JsonPropertyName("timestamp")]
        public long[]? Timestamp { get; set; }

        [JsonPropertyName("events")]
        public YahooEvents? Events { get; set; }

        [JsonPropertyName("indicators")]
        public YahooIndicators? Indicators { get; set; }
    }

    private sealed class YahooMeta
    {
        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("symbol")]
        public string? Symbol { get; set; }

        [JsonPropertyName("exchangeName")]
        public string? ExchangeName { get; set; }

        [JsonPropertyName("instrumentType")]
        public string? InstrumentType { get; set; }

        [JsonPropertyName("regularMarketPrice")]
        public decimal? RegularMarketPrice { get; set; }
    }

    private sealed class YahooEvents
    {
        [JsonPropertyName("dividends")]
        public Dictionary<string, YahooDividend>? Dividends { get; set; }

        [JsonPropertyName("splits")]
        public Dictionary<string, YahooSplit>? Splits { get; set; }
    }

    private sealed class YahooDividend
    {
        [JsonPropertyName("amount")]
        public decimal? Amount { get; set; }

        [JsonPropertyName("date")]
        public long? Date { get; set; }
    }

    private sealed class YahooSplit
    {
        [JsonPropertyName("numerator")]
        public decimal? Numerator { get; set; }

        [JsonPropertyName("denominator")]
        public decimal? Denominator { get; set; }

        [JsonPropertyName("splitRatio")]
        public string? SplitRatio { get; set; }
    }

    private sealed class YahooIndicators
    {
        [JsonPropertyName("quote")]
        public List<YahooQuote>? Quote { get; set; }

        [JsonPropertyName("adjclose")]
        public List<YahooAdjClose>? AdjClose { get; set; }
    }

    private sealed class YahooQuote
    {
        [JsonPropertyName("open")]
        public decimal?[]? Open { get; set; }

        [JsonPropertyName("high")]
        public decimal?[]? High { get; set; }

        [JsonPropertyName("low")]
        public decimal?[]? Low { get; set; }

        [JsonPropertyName("close")]
        public decimal?[]? Close { get; set; }

        [JsonPropertyName("volume")]
        public long?[]? Volume { get; set; }
    }

    private sealed class YahooAdjClose
    {
        [JsonPropertyName("adjclose")]
        public decimal?[]? AdjClose { get; set; }
    }

}

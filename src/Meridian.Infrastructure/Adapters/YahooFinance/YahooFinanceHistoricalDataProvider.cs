using System.Net;
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
using DomainAggregateTimeframe = Meridian.Domain.Models.AggregateTimeframe;

namespace Meridian.Infrastructure.Adapters.YahooFinance;

[DataSource("yahoo", "Yahoo Finance (free)", DataSourceType.Historical, DataSourceCategory.Free,
    Priority = 22, Description = "Free daily and intraday OHLCV via Yahoo Finance unofficial API")]
[ImplementsAdr("ADR-001", "Yahoo Finance historical data provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
public sealed class YahooFinanceHistoricalDataProvider : BaseHistoricalDataProvider, IHistoricalAggregateBarProvider
{
    private const string ApiBaseUrl = "https://query1.finance.yahoo.com/v8/finance/chart";

    private static readonly IReadOnlyList<DataGranularity> IntradayGranularities =
    [
        DataGranularity.Minute1,
        DataGranularity.Minute5,
        DataGranularity.Minute15,
        DataGranularity.Minute30,
        DataGranularity.Hour1,
        DataGranularity.Hour4
    ];

    public override string Name => "yahoo";
    public override string DisplayName => "Yahoo Finance (free)";
    public override string Description => "Free daily and intraday OHLCV with adjusted prices for global equities, ETFs, and indices.";
    protected override string HttpClientName => HttpClientNames.YahooFinanceHistorical;
    public override int Priority => 22;
    public override TimeSpan RateLimitDelay => TimeSpan.FromSeconds(0.5);
    public override int MaxRequestsPerWindow => 2000;
    public override TimeSpan RateLimitWindow => TimeSpan.FromHours(1);

    public override HistoricalDataCapabilities Capabilities { get; } =
        HistoricalDataCapabilities.BarsOnly with
        {
            Intraday = true,
            SupportedMarkets = ["US", "UK", "DE", "JP", "CA", "AU", "HK", "SG"]
        };

    public IReadOnlyList<DataGranularity> SupportedGranularities => IntradayGranularities;

    public YahooFinanceHistoricalDataProvider(HttpClient? httpClient = null, ILogger? log = null)
        : base(httpClient, log)
    {
        if (!Http.DefaultRequestHeaders.Contains("User-Agent"))
            Http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

    protected override string NormalizeSymbol(string symbol) => SymbolNormalization.NormalizeForYahoo(symbol);

    public override async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
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
        var url = BuildRequestUrl(normalizedSymbol, from, to, "1d", includePrePost: false, includeEvents: true);

        using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
        var httpResult = await ResponseHandler.HandleResponseAsync(response, symbol, "daily bars", ct).ConfigureAwait(false);
        if (!httpResult.IsSuccess)
        {
            var errorMsg = httpResult.IsNotFound
                ? $"Symbol {symbol} not found (404)"
                : $"HTTP error {httpResult.StatusCode}: {httpResult.ReasonPhrase}";
            throw new InvalidOperationException($"Failed to fetch Yahoo Finance data for {symbol}: {errorMsg}");
        }

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        try
        {
            var data = DeserializeResponse<YahooChartResponse>(json, symbol);
            var chartResult = data?.Chart?.Result?.FirstOrDefault();
            if (chartResult?.Indicators?.Quote?.FirstOrDefault() is null)
                return Array.Empty<AdjustedHistoricalBar>();

            return ParseChartResult(chartResult, symbol, from, to).OrderBy(b => b.SessionDate).ToList();
        }
        catch (DataProviderException ex)
        {
            throw new InvalidOperationException($"Failed to parse Yahoo Finance data for {symbol}: {ex.Message}", ex);
        }
    }

    public async Task<IReadOnlyList<AggregateBar>> GetAggregateBarsAsync(
        string symbol,
        DataGranularity granularity,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidateSymbol(symbol);

        if (!SupportedGranularities.Contains(granularity))
        {
            throw new InvalidOperationException(
                $"Yahoo Finance does not support {granularity.ToDisplayName()} intraday backfill.");
        }

        var normalizedSymbol = NormalizeSymbol(symbol);
        var sourceGranularity = granularity == DataGranularity.Hour4 ? DataGranularity.Hour1 : granularity;
        var directBars = await FetchIntradayBarsAsync(symbol, normalizedSymbol, sourceGranularity, from, to, ct).ConfigureAwait(false);

        return granularity == DataGranularity.Hour4
            ? RollUpFourHourBars(directBars)
            : directBars;
    }

    private static string BuildRequestUrl(string normalizedSymbol, DateOnly? from, DateOnly? to, string interval, bool includePrePost, bool includeEvents)
    {
        var period1 = from.HasValue
            ? new DateTimeOffset(from.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds()
            : new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var period2 = to.HasValue
            ? new DateTimeOffset(to.Value.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero).ToUnixTimeSeconds()
            : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var url =
            $"{ApiBaseUrl}/{Uri.EscapeDataString(normalizedSymbol)}?period1={period1}&period2={period2}&interval={interval}&includePrePost={includePrePost.ToString().ToLowerInvariant()}";
        return includeEvents ? $"{url}&events=div,splits" : url;
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

        for (var i = 0; i < timestamps.Length; i++)
        {
            var sessionDate = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(timestamps[i]).Date);
            if (from.HasValue && sessionDate < from.Value)
                continue;
            if (to.HasValue && sessionDate > to.Value)
                continue;

            var open = GetDecimalValue(quote.Open, i);
            var high = GetDecimalValue(quote.High, i);
            var low = GetDecimalValue(quote.Low, i);
            var close = GetDecimalValue(quote.Close, i);
            var volume = GetLongValue(quote.Volume, i);
            if (open is null || high is null || low is null || close is null || !IsValidOhlc(open.Value, high.Value, low.Value, close.Value))
                continue;

            var adjCloseValue = adjClose is not null && i < adjClose.Length ? GetDecimalValue(adjClose, i) : null;
            decimal? splitFactor = null;
            if (adjCloseValue.HasValue && close.Value > 0)
            {
                var factor = adjCloseValue.Value / close.Value;
                if (Math.Abs(factor - 1m) > 0.0001m)
                    splitFactor = factor;
            }

            decimal? dividendAmount = null;
            if (events?.Dividends?.TryGetValue(timestamps[i].ToString(), out var dividend) == true)
                dividendAmount = dividend.Amount;

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
                DividendAmount: dividendAmount));
        }

        return bars;
    }

    private async Task<IReadOnlyList<AggregateBar>> FetchIntradayBarsAsync(
        string symbol,
        string normalizedSymbol,
        DataGranularity granularity,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct)
    {
        var spec = GetIntradaySpec(granularity);
        var effectiveFrom = from ?? new DateOnly(2000, 1, 1);
        var effectiveTo = to ?? DateOnly.FromDateTime(DateTime.UtcNow);

        if (effectiveTo < effectiveFrom)
            return Array.Empty<AggregateBar>();

        var bars = new List<AggregateBar>();
        var seenStartTimes = new HashSet<DateTimeOffset>();
        var chunkStart = effectiveFrom;

        while (chunkStart <= effectiveTo)
        {
            var chunkEnd = chunkStart.AddDays(spec.MaxDaysPerRequest - 1);
            if (chunkEnd > effectiveTo)
                chunkEnd = effectiveTo;

            var chunkBars = await FetchIntradayChunkAsync(symbol, normalizedSymbol, granularity, spec, chunkStart, chunkEnd, ct).ConfigureAwait(false);
            foreach (var bar in chunkBars.OrderBy(b => b.StartTime))
            {
                if (seenStartTimes.Add(bar.StartTime))
                    bars.Add(bar);
            }

            chunkStart = chunkEnd.AddDays(1);
        }

        return bars.OrderBy(b => b.StartTime).ToList();
    }

    private async Task<IReadOnlyList<AggregateBar>> FetchIntradayChunkAsync(
        string symbol,
        string normalizedSymbol,
        DataGranularity granularity,
        YahooIntervalSpec spec,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        await WaitForRateLimitSlotAsync(ct).ConfigureAwait(false);

        var url = BuildRequestUrl(normalizedSymbol, from, to, spec.Interval, includePrePost: false, includeEvents: false);
        using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
        var httpResult = await ResponseHandler.HandleResponseAsync(response, symbol, $"{granularity.ToDisplayName()} intraday bars", ct).ConfigureAwait(false);

        if (!httpResult.IsSuccess)
        {
            if (httpResult.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                var vendorMessage = TryExtractYahooErrorDescription(httpResult.ErrorContent, out var description)
                    ? description
                    : httpResult.ReasonPhrase;

                Log.Warning(
                    "Yahoo Finance rejected {Granularity} intraday chunk for {Symbol} [{From}..{To}]: {Message}",
                    granularity.ToDisplayName(),
                    symbol,
                    from,
                    to,
                    vendorMessage);

                throw new InvalidOperationException(
                    $"Yahoo Finance rejected {granularity.ToDisplayName()} intraday request for {symbol} [{from}..{to}]: {vendorMessage}");
            }

            var errorMsg = httpResult.IsNotFound
                ? $"Symbol {symbol} not found (404)"
                : $"HTTP error {httpResult.StatusCode}: {httpResult.ReasonPhrase}";

            throw new InvalidOperationException($"Failed to fetch Yahoo Finance data for {symbol}: {errorMsg}");
        }

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        try
        {
            var data = DeserializeResponse<YahooChartResponse>(json, symbol);
            var chartResult = data?.Chart?.Result?.FirstOrDefault();

            if (chartResult is null)
            {
                if (TryExtractYahooErrorDescription(json, out var vendorMessage))
                {
                    throw new InvalidOperationException(
                        $"Yahoo Finance returned an intraday error for {symbol} [{from}..{to}]: {vendorMessage}");
                }

                return Array.Empty<AggregateBar>();
            }

            if (chartResult.Indicators?.Quote?.FirstOrDefault() is null)
                return Array.Empty<AggregateBar>();

            return ParseAggregateChartResult(chartResult, symbol, granularity, from, to);
        }
        catch (DataProviderException ex)
        {
            throw new InvalidOperationException(
                $"Failed to parse Yahoo Finance {granularity.ToDisplayName()} intraday data for {symbol}: {ex.Message}",
                ex);
        }
    }

    private List<AggregateBar> ParseAggregateChartResult(
        YahooChartResult chartResult,
        string symbol,
        DataGranularity granularity,
        DateOnly? from,
        DateOnly? to)
    {
        var timestamps = chartResult.Timestamp ?? Array.Empty<long>();
        if (chartResult.Indicators?.Quote is not { Count: > 0 })
            throw new DataProviderException("Yahoo Finance response missing indicator data");

        var quote = chartResult.Indicators.Quote[0];
        var timeframe = GetIntradaySpec(granularity).Timeframe;
        var interval = granularity.ToTimeSpan();
        var sessionWindows = BuildSessionWindows(chartResult.Meta);
        var bars = new List<AggregateBar>();

        for (var i = 0; i < timestamps.Length; i++)
        {
            var startTime = DateTimeOffset.FromUnixTimeSeconds(timestamps[i]);
            var sessionDate = GetExchangeLocalDate(startTime, chartResult.Meta);

            if (from.HasValue && sessionDate < from.Value)
                continue;
            if (to.HasValue && sessionDate > to.Value)
                continue;

            var open = GetDecimalValue(quote.Open, i);
            var high = GetDecimalValue(quote.High, i);
            var low = GetDecimalValue(quote.Low, i);
            var close = GetDecimalValue(quote.Close, i);
            var volume = GetLongValue(quote.Volume, i);

            if (open is null || high is null || low is null || close is null || !IsValidOhlc(open.Value, high.Value, low.Value, close.Value))
                continue;

            var sessionWindow = FindSessionWindow(startTime, sessionWindows, chartResult.Meta);
            var endTime = ResolveBarEndTime(startTime, interval, sessionWindow);
            if (endTime <= startTime)
                continue;

            bars.Add(new AggregateBar(
                Symbol: symbol.ToUpperInvariant(),
                StartTime: startTime,
                EndTime: endTime,
                Open: open.Value,
                High: high.Value,
                Low: low.Value,
                Close: close.Value,
                Volume: volume ?? 0,
                Vwap: 0m,
                TradeCount: 0,
                Timeframe: timeframe,
                Source: Name,
                SequenceNumber: startTime.ToUnixTimeSeconds()));
        }

        return bars;
    }

    private IReadOnlyList<AggregateBar> RollUpFourHourBars(IReadOnlyList<AggregateBar> hourlyBars)
    {
        if (hourlyBars.Count == 0)
            return Array.Empty<AggregateBar>();

        var orderedBars = hourlyBars.OrderBy(b => b.StartTime).ToList();
        var rolledBars = new List<AggregateBar>();
        var sessionBars = new List<AggregateBar>();

        foreach (var bar in orderedBars)
        {
            if (sessionBars.Count > 0 && bar.StartTime - sessionBars[^1].StartTime > TimeSpan.FromMinutes(90))
            {
                FlushFourHourSession(sessionBars, rolledBars);
                sessionBars.Clear();
            }

            sessionBars.Add(bar);
        }

        FlushFourHourSession(sessionBars, rolledBars);
        return rolledBars;
    }

    private void FlushFourHourSession(List<AggregateBar> sessionBars, List<AggregateBar> rolledBars)
    {
        if (sessionBars.Count == 0)
            return;

        var anchor = sessionBars[0].StartTime;
        var bucketBars = new List<AggregateBar>();
        var bucketIndex = -1;

        foreach (var bar in sessionBars)
        {
            var currentBucketIndex = (int)Math.Floor((bar.StartTime - anchor).TotalHours / 4d);
            if (currentBucketIndex != bucketIndex && bucketBars.Count > 0)
            {
                rolledBars.Add(BuildRolledBar(bucketBars));
                bucketBars.Clear();
            }

            bucketIndex = currentBucketIndex;
            bucketBars.Add(bar);
        }

        if (bucketBars.Count > 0)
            rolledBars.Add(BuildRolledBar(bucketBars));
    }

    private AggregateBar BuildRolledBar(IReadOnlyList<AggregateBar> sourceBars)
    {
        var volume = sourceBars.Sum(static bar => bar.Volume);
        var tradeCount = sourceBars.Sum(static bar => bar.TradeCount);
        var weightedPrice = sourceBars.Sum(bar => bar.Volume > 0 && bar.Vwap > 0m ? bar.Vwap * bar.Volume : 0m);
        var weightedVolume = sourceBars.Sum(static bar => bar.Volume > 0 && bar.Vwap > 0m ? bar.Volume : 0L);
        var vwap = weightedVolume > 0 ? weightedPrice / weightedVolume : 0m;

        return new AggregateBar(
            Symbol: sourceBars[0].Symbol,
            StartTime: sourceBars[0].StartTime,
            EndTime: sourceBars[^1].EndTime,
            Open: sourceBars[0].Open,
            High: sourceBars.Max(static bar => bar.High),
            Low: sourceBars.Min(static bar => bar.Low),
            Close: sourceBars[^1].Close,
            Volume: volume,
            Vwap: vwap,
            TradeCount: tradeCount,
            Timeframe: DomainAggregateTimeframe.Hour,
            Source: Name,
            SequenceNumber: sourceBars[0].StartTime.ToUnixTimeSeconds());
    }

    private static decimal? GetDecimalValue(decimal?[]? array, int index)
        => array is null || index >= array.Length || array[index] is null ? null : array[index]!.Value;

    private static long? GetLongValue(long?[]? array, int index)
        => array is null || index >= array.Length ? null : array[index];

    private static YahooIntervalSpec GetIntradaySpec(DataGranularity granularity)
        => granularity switch
        {
            DataGranularity.Minute1 => new YahooIntervalSpec("1m", 8, DomainAggregateTimeframe.Minute),
            DataGranularity.Minute5 => new YahooIntervalSpec("5m", 60, DomainAggregateTimeframe.Minute),
            DataGranularity.Minute15 => new YahooIntervalSpec("15m", 60, DomainAggregateTimeframe.Minute),
            DataGranularity.Minute30 => new YahooIntervalSpec("30m", 60, DomainAggregateTimeframe.Minute),
            DataGranularity.Hour1 or DataGranularity.Hour4 => new YahooIntervalSpec("60m", 730, DomainAggregateTimeframe.Hour),
            _ => throw new InvalidOperationException($"Yahoo Finance does not support {granularity.ToDisplayName()} intraday bars.")
        };

    private static Dictionary<DateOnly, List<SessionWindow>> BuildSessionWindows(YahooMeta? meta)
    {
        var windows = new Dictionary<DateOnly, List<SessionWindow>>();
        if (meta is null)
            return windows;

        if (meta.TradingPeriods.ValueKind == JsonValueKind.Array)
        {
            foreach (var container in meta.TradingPeriods.EnumerateArray())
            {
                if (container.ValueKind == JsonValueKind.Array)
                {
                    foreach (var period in container.EnumerateArray())
                        TryAddSessionWindow(windows, period, meta.GmtOffset);
                }
                else
                {
                    TryAddSessionWindow(windows, container, meta.GmtOffset);
                }
            }
        }

        if (meta.CurrentTradingPeriod.ValueKind == JsonValueKind.Object &&
            meta.CurrentTradingPeriod.TryGetProperty("regular", out var regularPeriod))
        {
            TryAddSessionWindow(windows, regularPeriod, meta.GmtOffset);
        }

        return windows;
    }

    private static void TryAddSessionWindow(
        IDictionary<DateOnly, List<SessionWindow>> sessionWindows,
        JsonElement period,
        long? defaultOffsetSeconds)
    {
        if (period.ValueKind != JsonValueKind.Object)
            return;

        if (!period.TryGetProperty("start", out var startProperty) || !startProperty.TryGetInt64(out var startUnix))
            return;

        if (!period.TryGetProperty("end", out var endProperty) || !endProperty.TryGetInt64(out var endUnix))
            return;

        var offsetSeconds = defaultOffsetSeconds;
        if (period.TryGetProperty("gmtoffset", out var offsetProperty) && offsetProperty.TryGetInt64(out var vendorOffset))
            offsetSeconds = vendorOffset;

        var start = DateTimeOffset.FromUnixTimeSeconds(startUnix);
        var end = DateTimeOffset.FromUnixTimeSeconds(endUnix);
        if (end <= start)
            return;

        var localDate = GetExchangeLocalDate(start, offsetSeconds);
        if (!sessionWindows.TryGetValue(localDate, out var dayWindows))
        {
            dayWindows = new List<SessionWindow>();
            sessionWindows[localDate] = dayWindows;
        }

        if (dayWindows.Any(existing => existing.Start == start && existing.End == end))
            return;

        dayWindows.Add(new SessionWindow(start, end));
    }

    private static SessionWindow? FindSessionWindow(
        DateTimeOffset startTime,
        IReadOnlyDictionary<DateOnly, List<SessionWindow>> sessionWindows,
        YahooMeta? meta)
    {
        var localDate = GetExchangeLocalDate(startTime, meta);
        if (!sessionWindows.TryGetValue(localDate, out var dayWindows))
            return null;

        foreach (var window in dayWindows)
        {
            if (startTime >= window.Start && startTime < window.End)
                return window;
        }

        return null;
    }

    private static DateOnly GetExchangeLocalDate(DateTimeOffset timestamp, YahooMeta? meta)
        => GetExchangeLocalDate(timestamp, meta?.GmtOffset);

    private static DateOnly GetExchangeLocalDate(DateTimeOffset timestamp, long? offsetSeconds)
    {
        if (!offsetSeconds.HasValue)
            return DateOnly.FromDateTime(timestamp.UtcDateTime);

        var localTime = timestamp.ToOffset(TimeSpan.FromSeconds(offsetSeconds.Value));
        return DateOnly.FromDateTime(localTime.DateTime);
    }

    private static DateTimeOffset ResolveBarEndTime(DateTimeOffset startTime, TimeSpan interval, SessionWindow? sessionWindow)
    {
        var defaultEnd = startTime + interval;
        if (!sessionWindow.HasValue)
            return defaultEnd;

        return defaultEnd > sessionWindow.Value.End
            ? sessionWindow.Value.End
            : defaultEnd;
    }

    private static bool TryExtractYahooErrorDescription(string? content, out string description)
    {
        description = string.Empty;
        if (string.IsNullOrWhiteSpace(content))
            return false;

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("chart", out var chart) &&
                chart.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.Object &&
                error.TryGetProperty("description", out var chartDescription) &&
                chartDescription.ValueKind == JsonValueKind.String)
            {
                description = chartDescription.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(description);
            }

            return TryExtractDescriptionRecursive(document.RootElement, out description);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryExtractDescriptionRecursive(JsonElement element, out string description)
    {
        description = string.Empty;

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("description") && property.Value.ValueKind == JsonValueKind.String)
                    {
                        description = property.Value.GetString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(description))
                            return true;
                    }

                    if (TryExtractDescriptionRecursive(property.Value, out description))
                        return true;
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (TryExtractDescriptionRecursive(item, out description))
                        return true;
                }

                break;
        }

        return false;
    }

    private readonly record struct YahooIntervalSpec(string Interval, int MaxDaysPerRequest, DomainAggregateTimeframe Timeframe);

    private readonly record struct SessionWindow(DateTimeOffset Start, DateTimeOffset End);

    private sealed class YahooChartResponse
    {
        [JsonPropertyName("chart")]
        public YahooChart? Chart { get; set; }
    }

    private sealed class YahooChart
    {
        [JsonPropertyName("result")]
        public List<YahooChartResult>? Result { get; set; }
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
        [JsonPropertyName("gmtoffset")]
        public long? GmtOffset { get; set; }

        [JsonPropertyName("tradingPeriods")]
        public JsonElement TradingPeriods { get; set; }

        [JsonPropertyName("currentTradingPeriod")]
        public JsonElement CurrentTradingPeriod { get; set; }
    }

    private sealed class YahooEvents
    {
        [JsonPropertyName("dividends")]
        public Dictionary<string, YahooDividend>? Dividends { get; set; }
    }

    private sealed class YahooDividend
    {
        [JsonPropertyName("amount")]
        public decimal? Amount { get; set; }
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

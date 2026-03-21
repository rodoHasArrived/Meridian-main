using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.Http;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Alpaca;

/// <summary>
/// Historical data provider using Alpaca Markets Data API v2.
/// Provides daily and intraday OHLCV bars with split/dividend adjustments,
/// as well as tick-level quotes (NBBO), trades, and auction data.
/// Coverage: US equities, ETFs.
/// Extends BaseHistoricalDataProvider for common functionality including:
/// - HTTP resilience (retry, circuit breaker)
/// - Rate limit tracking with IRateLimitAwareProvider
/// - Centralized error handling
/// </summary>
[ImplementsAdr("ADR-001", "Alpaca historical data provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
public sealed class AlpacaHistoricalDataProvider : BaseHistoricalDataProvider
{
    private const string BaseUrl = "https://data.alpaca.markets/v2/stocks";
    private const string EnvKeyId = "ALPACA_KEY_ID";
    private const string EnvSecretKey = "ALPACA_SECRET_KEY";

    private readonly string? _keyId;
    private readonly string? _secretKey;
    private readonly string _feed;
    private readonly string _adjustment;
    private readonly int _priority;
    private readonly int _maxRequestsPerWindow;

    #region Abstract Property Implementations

    public override string Name => "alpaca";
    public override string DisplayName => "Alpaca Markets";
    public override string Description => "Daily and intraday OHLCV bars with split/dividend adjustments for US equities.";
    protected override string HttpClientName => HttpClientNames.AlpacaHistorical;

    #endregion

    #region Virtual Property Overrides

    public override int Priority => _priority;
    public override TimeSpan RateLimitDelay => TimeSpan.FromMilliseconds(300);
    public override int MaxRequestsPerWindow => _maxRequestsPerWindow;
    public override TimeSpan RateLimitWindow => TimeSpan.FromMinutes(1);

    /// <summary>
    /// Alpaca supports all data types: adjusted bars, intraday, quotes, trades, and auctions.
    /// </summary>
    public override HistoricalDataCapabilities Capabilities { get; } = HistoricalDataCapabilities.FullFeatured;

    #endregion

    /// <summary>
    /// Creates a new Alpaca historical data provider.
    /// </summary>
    /// <param name="keyId">API Key ID (falls back to ALPACA_KEY_ID env var).</param>
    /// <param name="secretKey">API Secret Key (falls back to ALPACA_SECRET_KEY env var).</param>
    /// <param name="feed">Data feed: "iex" (free), "sip" (paid), or "delayed_sip" (free, 15-min delay).</param>
    /// <param name="adjustment">Price adjustment: "raw", "split", "dividend", or "all".</param>
    /// <param name="priority">Priority in fallback chain (lower = tried first).</param>
    /// <param name="rateLimitPerMinute">Maximum requests per minute.</param>
    /// <param name="httpClient">Optional HTTP client instance.</param>
    /// <param name="log">Optional logger instance.</param>
    public AlpacaHistoricalDataProvider(
        string? keyId = null,
        string? secretKey = null,
        string feed = "iex",
        string adjustment = "all",
        int priority = 5,
        int rateLimitPerMinute = 200,
        HttpClient? httpClient = null,
        ILogger? log = null)
        : base(httpClient, log)
    {
        _keyId = keyId ?? Environment.GetEnvironmentVariable(EnvKeyId);
        _secretKey = secretKey ?? Environment.GetEnvironmentVariable(EnvSecretKey);
        _feed = ValidateFeed(feed);
        _adjustment = ValidateAdjustment(adjustment);
        _priority = priority;
        _maxRequestsPerWindow = rateLimitPerMinute;

        ConfigureHttpClient();
    }

    private void ConfigureHttpClient()
    {
        if (!string.IsNullOrEmpty(_keyId) && !string.IsNullOrEmpty(_secretKey))
        {
            Http.DefaultRequestHeaders.Add("APCA-API-KEY-ID", _keyId);
            Http.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", _secretKey);
        }
        Http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static string ValidateFeed(string feed)
    {
        return feed.ToLowerInvariant() switch
        {
            "iex" or "sip" or "delayed_sip" => feed.ToLowerInvariant(),
            _ => throw new ArgumentException($"Invalid feed '{feed}'. Must be 'iex', 'sip', or 'delayed_sip'.", nameof(feed))
        };
    }

    private static string ValidateAdjustment(string adjustment)
    {
        return adjustment.ToLowerInvariant() switch
        {
            "raw" or "split" or "dividend" or "all" => adjustment.ToLowerInvariant(),
            _ => throw new ArgumentException($"Invalid adjustment '{adjustment}'. Must be 'raw', 'split', 'dividend', or 'all'.", nameof(adjustment))
        };
    }

    public override async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_keyId) || string.IsNullOrEmpty(_secretKey))
        {
            Log.Warning("Alpaca API credentials not configured");
            return false;
        }

        try
        {
            // Quick health check with a known symbol
            var url = $"{BaseUrl}/AAPL/bars?timeframe=1Day&start=2024-01-02&end=2024-01-03&limit=1&feed={_feed}";
            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Alpaca availability check failed");
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

        if (string.IsNullOrEmpty(_keyId) || string.IsNullOrEmpty(_secretKey))
            throw new InvalidOperationException("Alpaca API credentials are required. Set ALPACA_KEY_ID and ALPACA_SECRET_KEY environment variables or provide them in configuration.");

        var normalizedSymbol = NormalizeSymbol(symbol);
        var allBars = new List<AdjustedHistoricalBar>();
        string? nextPageToken = null;

        do
        {
            await WaitForRateLimitSlotAsync(ct).ConfigureAwait(false);

            var url = BuildUrl(normalizedSymbol, from, to, nextPageToken);
            Log.Information("Requesting Alpaca history for {Symbol} ({Url})", symbol, url);

            try
            {
                using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
                HandleHttpResponse(response, symbol, "bars");

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var data = DeserializeResponse<AlpacaBarsResponse>(json, symbol);

                if (data?.Bars is null || data.Bars.Count == 0)
                {
                    if (allBars.Count == 0)
                    {
                        Log.Warning("No data returned from Alpaca for {Symbol}", symbol);
                    }
                    break;
                }

                foreach (var bar in data.Bars)
                {
                    if (bar.Timestamp is null)
                        continue;

                    var sessionDate = DateOnly.FromDateTime(bar.Timestamp.Value.UtcDateTime);

                    // Skip if outside requested range
                    if (from.HasValue && sessionDate < from.Value)
                        continue;
                    if (to.HasValue && sessionDate > to.Value)
                        continue;

                    // Validate OHLC using base class helper
                    if (!ValidateOhlc(bar.Open, bar.High, bar.Low, bar.Close, symbol, sessionDate))
                        continue;

                    // Alpaca returns adjusted prices when adjustment is specified
                    var isAdjusted = _adjustment != "raw";

                    allBars.Add(new AdjustedHistoricalBar(
                        Symbol: symbol.ToUpperInvariant(),
                        SessionDate: sessionDate,
                        Open: bar.Open,
                        High: bar.High,
                        Low: bar.Low,
                        Close: bar.Close,
                        Volume: bar.Volume,
                        Source: Name,
                        SequenceNumber: sessionDate.DayNumber,
                        AdjustedOpen: isAdjusted ? bar.Open : null,
                        AdjustedHigh: isAdjusted ? bar.High : null,
                        AdjustedLow: isAdjusted ? bar.Low : null,
                        AdjustedClose: isAdjusted ? bar.Close : null,
                        AdjustedVolume: isAdjusted ? bar.Volume : null,
                        SplitFactor: null,
                        DividendAmount: null
                    ));
                }

                nextPageToken = data.NextPageToken;
            }
            catch (JsonException ex)
            {
                Log.Error(ex, "Failed to parse Alpaca response for {Symbol}", symbol);
                throw new InvalidOperationException($"Failed to parse Alpaca data for {symbol}", ex);
            }

        } while (!string.IsNullOrEmpty(nextPageToken));

        Log.Information("Fetched {Count} bars for {Symbol} from Alpaca", allBars.Count, symbol);
        return allBars.OrderBy(b => b.SessionDate).ToList();
    }

    #region Historical Quotes (NBBO)

    /// <summary>
    /// Fetch historical NBBO quotes for a single symbol.
    /// </summary>
    public async Task<HistoricalQuotesResult> GetHistoricalQuotesAsync(
        string symbol,
        DateTimeOffset start,
        DateTimeOffset end,
        int? limit = null,
        CancellationToken ct = default)
    {
        return await GetHistoricalQuotesAsync(new[] { symbol }, start, end, limit, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetch historical NBBO quotes for multiple symbols.
    /// </summary>
    public async Task<HistoricalQuotesResult> GetHistoricalQuotesAsync(
        IEnumerable<string> symbols,
        DateTimeOffset start,
        DateTimeOffset end,
        int? limit = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidateCredentials();

        var symbolList = symbols.Select(NormalizeSymbol).ToList();
        ValidateSymbols(symbolList);

        var allQuotes = new List<HistoricalQuote>();
        string? nextPageToken = null;
        long sequenceNumber = 0;

        do
        {
            await WaitForRateLimitSlotAsync(ct).ConfigureAwait(false);

            var url = BuildQuotesUrl(symbolList, start, end, limit, nextPageToken);
            Log.Information("Requesting Alpaca quotes for {Symbols} ({Url})", string.Join(",", symbolList), url);

            try
            {
                using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
                HandleHttpResponse(response, symbolList.First(), "quotes");

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var data = DeserializeResponse<AlpacaQuotesResponse>(json, symbolList.First());

                if (data?.Quotes is null || data.Quotes.Count == 0)
                    break;

                foreach (var kvp in data.Quotes)
                {
                    var sym = kvp.Key;
                    foreach (var quote in kvp.Value)
                    {
                        if (quote.Timestamp is null)
                            continue;

                        allQuotes.Add(new HistoricalQuote(
                            Symbol: sym.ToUpperInvariant(),
                            Timestamp: quote.Timestamp.Value,
                            AskExchange: quote.AskExchange ?? "",
                            AskPrice: quote.AskPrice,
                            AskSize: quote.AskSize,
                            BidExchange: quote.BidExchange ?? "",
                            BidPrice: quote.BidPrice,
                            BidSize: quote.BidSize,
                            Conditions: quote.Conditions?.ToArray(),
                            Tape: quote.Tape,
                            Source: Name,
                            SequenceNumber: sequenceNumber++
                        ));
                    }
                }

                nextPageToken = data.NextPageToken;
            }
            catch (JsonException ex)
            {
                Log.Error(ex, "Failed to parse Alpaca quotes response for {Symbols}", string.Join(",", symbolList));
                throw new InvalidOperationException($"Failed to parse Alpaca quotes data", ex);
            }

        } while (!string.IsNullOrEmpty(nextPageToken));

        Log.Information("Fetched {Count} quotes for {Symbols} from Alpaca", allQuotes.Count, string.Join(",", symbolList));
        return new HistoricalQuotesResult(allQuotes.OrderBy(q => q.Timestamp).ToList(), null, allQuotes.Count);
    }

    private string BuildQuotesUrl(IReadOnlyList<string> symbols, DateTimeOffset start, DateTimeOffset end, int? limit, string? pageToken)
    {
        var symbolsParam = string.Join(",", symbols);
        var startStr = start.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var endStr = end.ToString("yyyy-MM-ddTHH:mm:ssZ");

        string url;
        if (symbols.Count == 1)
        {
            url = $"{BaseUrl}/{symbols[0]}/quotes?start={startStr}&end={endStr}&feed={_feed}";
        }
        else
        {
            url = $"{BaseUrl}/quotes?symbols={Uri.EscapeDataString(symbolsParam)}&start={startStr}&end={endStr}&feed={_feed}";
        }

        if (limit.HasValue)
            url += $"&limit={limit.Value}";

        if (!string.IsNullOrEmpty(pageToken))
            url += $"&page_token={Uri.EscapeDataString(pageToken)}";

        return url;
    }

    #endregion

    #region Historical Trades

    /// <summary>
    /// Fetch historical trades for a single symbol.
    /// </summary>
    public async Task<HistoricalTradesResult> GetHistoricalTradesAsync(
        string symbol,
        DateTimeOffset start,
        DateTimeOffset end,
        int? limit = null,
        CancellationToken ct = default)
    {
        return await GetHistoricalTradesAsync(new[] { symbol }, start, end, limit, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetch historical trades for multiple symbols.
    /// </summary>
    public async Task<HistoricalTradesResult> GetHistoricalTradesAsync(
        IEnumerable<string> symbols,
        DateTimeOffset start,
        DateTimeOffset end,
        int? limit = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidateCredentials();

        var symbolList = symbols.Select(NormalizeSymbol).ToList();
        ValidateSymbols(symbolList);

        var allTrades = new List<HistoricalTrade>();
        string? nextPageToken = null;
        long sequenceNumber = 0;

        do
        {
            await WaitForRateLimitSlotAsync(ct).ConfigureAwait(false);

            var url = BuildTradesUrl(symbolList, start, end, limit, nextPageToken);
            Log.Information("Requesting Alpaca trades for {Symbols} ({Url})", string.Join(",", symbolList), url);

            try
            {
                using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
                HandleHttpResponse(response, symbolList.First(), "trades");

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var data = DeserializeResponse<AlpacaTradesResponse>(json, symbolList.First());

                if (data?.Trades is null || data.Trades.Count == 0)
                    break;

                foreach (var kvp in data.Trades)
                {
                    var sym = kvp.Key;
                    foreach (var trade in kvp.Value)
                    {
                        if (trade.Timestamp is null)
                            continue;

                        allTrades.Add(new HistoricalTrade(
                            Symbol: sym.ToUpperInvariant(),
                            Timestamp: trade.Timestamp.Value,
                            Exchange: trade.Exchange ?? "",
                            Price: trade.Price,
                            Size: trade.Size,
                            TradeId: trade.TradeId ?? sequenceNumber.ToString(),
                            Conditions: trade.Conditions?.ToArray(),
                            Tape: trade.Tape,
                            Source: Name,
                            SequenceNumber: sequenceNumber++
                        ));
                    }
                }

                nextPageToken = data.NextPageToken;
            }
            catch (JsonException ex)
            {
                Log.Error(ex, "Failed to parse Alpaca trades response for {Symbols}", string.Join(",", symbolList));
                throw new InvalidOperationException($"Failed to parse Alpaca trades data", ex);
            }

        } while (!string.IsNullOrEmpty(nextPageToken));

        Log.Information("Fetched {Count} trades for {Symbols} from Alpaca", allTrades.Count, string.Join(",", symbolList));
        return new HistoricalTradesResult(allTrades.OrderBy(t => t.Timestamp).ToList(), null, allTrades.Count);
    }

    private string BuildTradesUrl(IReadOnlyList<string> symbols, DateTimeOffset start, DateTimeOffset end, int? limit, string? pageToken)
    {
        var symbolsParam = string.Join(",", symbols);
        var startStr = start.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var endStr = end.ToString("yyyy-MM-ddTHH:mm:ssZ");

        string url;
        if (symbols.Count == 1)
        {
            url = $"{BaseUrl}/{symbols[0]}/trades?start={startStr}&end={endStr}&feed={_feed}";
        }
        else
        {
            url = $"{BaseUrl}/trades?symbols={Uri.EscapeDataString(symbolsParam)}&start={startStr}&end={endStr}&feed={_feed}";
        }

        if (limit.HasValue)
            url += $"&limit={limit.Value}";

        if (!string.IsNullOrEmpty(pageToken))
            url += $"&page_token={Uri.EscapeDataString(pageToken)}";

        return url;
    }

    #endregion

    #region Historical Auctions

    /// <summary>
    /// Fetch historical auction data for a single symbol.
    /// </summary>
    public async Task<HistoricalAuctionsResult> GetHistoricalAuctionsAsync(
        string symbol,
        DateOnly start,
        DateOnly end,
        CancellationToken ct = default)
    {
        return await GetHistoricalAuctionsAsync(new[] { symbol }, start, end, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetch historical auction data for multiple symbols.
    /// </summary>
    public async Task<HistoricalAuctionsResult> GetHistoricalAuctionsAsync(
        IEnumerable<string> symbols,
        DateOnly start,
        DateOnly end,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidateCredentials();

        var symbolList = symbols.Select(NormalizeSymbol).ToList();
        ValidateSymbols(symbolList);

        var allAuctions = new List<HistoricalAuction>();
        string? nextPageToken = null;

        do
        {
            await WaitForRateLimitSlotAsync(ct).ConfigureAwait(false);

            var url = BuildAuctionsUrl(symbolList, start, end, nextPageToken);
            Log.Information("Requesting Alpaca auctions for {Symbols} ({Url})", string.Join(",", symbolList), url);

            try
            {
                using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
                HandleHttpResponse(response, symbolList.First(), "auctions");

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var data = DeserializeResponse<AlpacaAuctionsResponse>(json, symbolList.First());

                if (data?.Auctions is null || data.Auctions.Count == 0)
                    break;

                foreach (var kvp in data.Auctions)
                {
                    var sym = kvp.Key;
                    foreach (var auctionDay in kvp.Value)
                    {
                        if (auctionDay.Date is null)
                            continue;

                        var sessionDate = DateOnly.FromDateTime(auctionDay.Date.Value.UtcDateTime);

                        // Skip if outside requested range
                        if (sessionDate < start || sessionDate > end)
                            continue;

                        var openingAuctions = (auctionDay.OpeningAuctions ?? Array.Empty<AlpacaAuctionPrice>())
                            .Where(a => a.Timestamp.HasValue && a.Price > 0)
                            .Select(a => new AuctionPrice(
                                Timestamp: a.Timestamp!.Value,
                                Price: a.Price,
                                Size: a.Size,
                                Exchange: a.Exchange,
                                Condition: a.Condition
                            ))
                            .ToList();

                        var closingAuctions = (auctionDay.ClosingAuctions ?? Array.Empty<AlpacaAuctionPrice>())
                            .Where(a => a.Timestamp.HasValue && a.Price > 0)
                            .Select(a => new AuctionPrice(
                                Timestamp: a.Timestamp!.Value,
                                Price: a.Price,
                                Size: a.Size,
                                Exchange: a.Exchange,
                                Condition: a.Condition
                            ))
                            .ToList();

                        allAuctions.Add(new HistoricalAuction(
                            Symbol: sym.ToUpperInvariant(),
                            SessionDate: sessionDate,
                            OpeningAuctions: openingAuctions,
                            ClosingAuctions: closingAuctions,
                            Source: Name,
                            SequenceNumber: sessionDate.DayNumber
                        ));
                    }
                }

                nextPageToken = data.NextPageToken;
            }
            catch (JsonException ex)
            {
                Log.Error(ex, "Failed to parse Alpaca auctions response for {Symbols}", string.Join(",", symbolList));
                throw new InvalidOperationException($"Failed to parse Alpaca auctions data", ex);
            }

        } while (!string.IsNullOrEmpty(nextPageToken));

        Log.Information("Fetched {Count} auction days for {Symbols} from Alpaca", allAuctions.Count, string.Join(",", symbolList));
        return new HistoricalAuctionsResult(allAuctions.OrderBy(a => a.SessionDate).ToList(), null, allAuctions.Count);
    }

    private string BuildAuctionsUrl(IReadOnlyList<string> symbols, DateOnly start, DateOnly end, string? pageToken)
    {
        var symbolsParam = string.Join(",", symbols);
        var startStr = start.ToString("yyyy-MM-dd");
        var endStr = end.AddDays(1).ToString("yyyy-MM-dd");

        string url;
        if (symbols.Count == 1)
        {
            url = $"{BaseUrl}/{symbols[0]}/auctions?start={startStr}&end={endStr}&feed={_feed}";
        }
        else
        {
            url = $"{BaseUrl}/auctions?symbols={Uri.EscapeDataString(symbolsParam)}&start={startStr}&end={endStr}&feed={_feed}";
        }

        if (!string.IsNullOrEmpty(pageToken))
            url += $"&page_token={Uri.EscapeDataString(pageToken)}";

        return url;
    }

    #endregion

    #region Helper Methods

    private void ValidateCredentials()
    {
        if (string.IsNullOrEmpty(_keyId) || string.IsNullOrEmpty(_secretKey))
            throw new InvalidOperationException("Alpaca API credentials are required. Set ALPACA_KEY_ID and ALPACA_SECRET_KEY environment variables or provide them in configuration.");
    }

    private string BuildUrl(string symbol, DateOnly? from, DateOnly? to, string? pageToken)
    {
        var startDate = from?.ToString("yyyy-MM-dd") ?? "2000-01-01";
        var endDate = to?.AddDays(1).ToString("yyyy-MM-dd") ?? DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd");

        var url = $"{BaseUrl}/{symbol}/bars?timeframe=1Day&start={startDate}&end={endDate}&limit=10000&feed={_feed}&adjustment={_adjustment}";

        if (!string.IsNullOrEmpty(pageToken))
        {
            url += $"&page_token={Uri.EscapeDataString(pageToken)}";
        }

        return url;
    }

    /// <summary>
    /// Alpaca uses standard ticker symbols in uppercase.
    /// </summary>
    protected override string NormalizeSymbol(string symbol)
    {
        return symbol.ToUpperInvariant().Trim();
    }

    #endregion

    #region Alpaca API Models

    private sealed class AlpacaBarsResponse
    {
        [JsonPropertyName("bars")]
        public List<AlpacaBar>? Bars { get; set; }

        [JsonPropertyName("symbol")]
        public string? Symbol { get; set; }

        [JsonPropertyName("next_page_token")]
        public string? NextPageToken { get; set; }
    }

    private sealed class AlpacaBar
    {
        [JsonPropertyName("t")]
        public DateTimeOffset? Timestamp { get; set; }

        [JsonPropertyName("o")]
        public decimal Open { get; set; }

        [JsonPropertyName("h")]
        public decimal High { get; set; }

        [JsonPropertyName("l")]
        public decimal Low { get; set; }

        [JsonPropertyName("c")]
        public decimal Close { get; set; }

        [JsonPropertyName("v")]
        public long Volume { get; set; }

        [JsonPropertyName("n")]
        public int TradeCount { get; set; }

        [JsonPropertyName("vw")]
        public decimal VolumeWeightedAvgPrice { get; set; }
    }

    // Historical Quotes Response Models
    private sealed class AlpacaQuotesResponse
    {
        [JsonPropertyName("quotes")]
        public Dictionary<string, List<AlpacaQuote>>? Quotes { get; set; }

        [JsonPropertyName("next_page_token")]
        public string? NextPageToken { get; set; }
    }

    private sealed class AlpacaQuote
    {
        [JsonPropertyName("t")]
        public DateTimeOffset? Timestamp { get; set; }

        [JsonPropertyName("ax")]
        public string? AskExchange { get; set; }

        [JsonPropertyName("ap")]
        public decimal AskPrice { get; set; }

        [JsonPropertyName("as")]
        public long AskSize { get; set; }

        [JsonPropertyName("bx")]
        public string? BidExchange { get; set; }

        [JsonPropertyName("bp")]
        public decimal BidPrice { get; set; }

        [JsonPropertyName("bs")]
        public long BidSize { get; set; }

        [JsonPropertyName("c")]
        public List<string>? Conditions { get; set; }

        [JsonPropertyName("z")]
        public string? Tape { get; set; }
    }

    // Historical Trades Response Models
    private sealed class AlpacaTradesResponse
    {
        [JsonPropertyName("trades")]
        public Dictionary<string, List<AlpacaTrade>>? Trades { get; set; }

        [JsonPropertyName("next_page_token")]
        public string? NextPageToken { get; set; }
    }

    private sealed class AlpacaTrade
    {
        [JsonPropertyName("t")]
        public DateTimeOffset? Timestamp { get; set; }

        [JsonPropertyName("x")]
        public string? Exchange { get; set; }

        [JsonPropertyName("p")]
        public decimal Price { get; set; }

        [JsonPropertyName("s")]
        public long Size { get; set; }

        [JsonPropertyName("c")]
        public List<string>? Conditions { get; set; }

        [JsonPropertyName("i")]
        public string? TradeId { get; set; }

        [JsonPropertyName("z")]
        public string? Tape { get; set; }
    }

    // Historical Auctions Response Models
    private sealed class AlpacaAuctionsResponse
    {
        [JsonPropertyName("auctions")]
        public Dictionary<string, List<AlpacaAuctionDay>>? Auctions { get; set; }

        [JsonPropertyName("next_page_token")]
        public string? NextPageToken { get; set; }
    }

    private sealed class AlpacaAuctionDay
    {
        [JsonPropertyName("d")]
        public DateTimeOffset? Date { get; set; }

        [JsonPropertyName("o")]
        public AlpacaAuctionPrice[]? OpeningAuctions { get; set; }

        [JsonPropertyName("c")]
        public AlpacaAuctionPrice[]? ClosingAuctions { get; set; }
    }

    private sealed class AlpacaAuctionPrice
    {
        [JsonPropertyName("t")]
        public DateTimeOffset? Timestamp { get; set; }

        [JsonPropertyName("p")]
        public decimal Price { get; set; }

        [JsonPropertyName("s")]
        public long Size { get; set; }

        [JsonPropertyName("x")]
        public string? Exchange { get; set; }

        [JsonPropertyName("c")]
        public string? Condition { get; set; }
    }

    #endregion
}

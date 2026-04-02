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

namespace Meridian.Infrastructure.Adapters.NasdaqDataLink;

/// <summary>
/// Historical data provider using Nasdaq Data Link (formerly Quandl).
/// Free tier: 50 calls/day, 300 calls/10 seconds.
/// Provides access to various datasets including WIKI (end-of-life) and premium datasets.
/// Extends BaseHistoricalDataProvider for common functionality.
/// </summary>
[DataSource("nasdaq", "Nasdaq Data Link (Quandl)", DataSourceType.Historical, DataSourceCategory.Aggregator,
    Priority = 30, Description = "Historical data via Nasdaq Data Link (formerly Quandl) API")]
[ImplementsAdr("ADR-001", "Nasdaq Data Link historical data provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
[RequiresCredential("NASDAQ_DATA_LINK_API_KEY",
    EnvironmentVariables = new[] { "NASDAQ_DATA_LINK_API_KEY", "NASDAQ__APIKEY" },
    DisplayName = "API Key",
    Description = "Nasdaq Data Link API key from https://data.nasdaq.com/account/profile")]
public sealed class NasdaqDataLinkHistoricalDataProvider : BaseHistoricalDataProvider
{
    private const string ApiBaseUrl = "https://data.nasdaq.com/api/v3";

    private readonly string? _apiKey;
    private readonly string _database;

    public override string Name => "nasdaq";
    public override string DisplayName => "Nasdaq Data Link (Quandl)";
    public override string Description => "Alternative and financial datasets from Nasdaq Data Link (formerly Quandl).";
    protected override string HttpClientName => HttpClientNames.NasdaqDataLinkHistorical;

    public override int Priority => 30;
    public override TimeSpan RateLimitDelay => TimeSpan.FromMilliseconds(100);
    public override int MaxRequestsPerWindow => 50;
    public override TimeSpan RateLimitWindow => TimeSpan.FromDays(1);

    /// <summary>
    /// Nasdaq Data Link supports adjusted bars with corporate actions.
    /// </summary>
    public override HistoricalDataCapabilities Capabilities { get; } = HistoricalDataCapabilities.BarsOnly;

    /// <summary>
    /// Create a Nasdaq Data Link provider.
    /// </summary>
    /// <param name="apiKey">API key from data.nasdaq.com (optional but recommended)</param>
    /// <param name="database">Database to query (default: "WIKI" for legacy wiki prices, or use "EOD" for end-of-day)</param>
    /// <param name="httpClient">Optional HTTP client</param>
    /// <param name="log">Optional logger</param>
    public NasdaqDataLinkHistoricalDataProvider(
        string? apiKey = null,
        string database = "WIKI",
        HttpClient? httpClient = null,
        ILogger? log = null)
        : base(httpClient, log)
    {
        _apiKey = apiKey;
        _database = database;
    }

    /// <summary>
    /// Nasdaq Data Link expects uppercase symbols with dots replaced by underscores.
    /// </summary>
    protected override string NormalizeSymbol(string symbol)
    {
        return SymbolNormalization.NormalizeForNasdaqDataLink(symbol);
    }

    public override async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"{ApiBaseUrl}/datasets/{_database}/AAPL/metadata.json";
            if (!string.IsNullOrEmpty(_apiKey))
                url += $"?api_key={_apiKey}";

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

        await WaitForRateLimitSlotAsync(ct).ConfigureAwait(false);

        var normalizedSymbol = NormalizeSymbol(symbol);
        var url = BuildRequestUrl(normalizedSymbol, from, to);

        Log.Information("Requesting Nasdaq Data Link history for {Symbol} ({Url})", symbol, RedactApiKey(url));

        try
        {
            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);

            var httpResult = await ResponseHandler.HandleResponseAsync(response, symbol, "daily bars", ct: ct).ConfigureAwait(false);
            if (httpResult.IsNotFound)
            {
                Log.Warning("Nasdaq Data Link: Symbol {Symbol} not found", symbol);
                return Array.Empty<AdjustedHistoricalBar>();
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var result = DeserializeResponse<QuandlDatasetResponse>(json, symbol);

            if (result?.Dataset?.Data is null)
            {
                Log.Warning("No data returned from Nasdaq Data Link for {Symbol}", symbol);
                return Array.Empty<AdjustedHistoricalBar>();
            }

            var bars = ParseDatasetResponse(result.Dataset, symbol, from, to);

            Log.Information("Fetched {Count} bars for {Symbol} from Nasdaq Data Link", bars.Count, symbol);
            return bars.OrderBy(b => b.SessionDate).ToList();
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "Failed to parse Nasdaq Data Link response for {Symbol}", symbol);
            throw new InvalidOperationException($"Failed to parse Nasdaq Data Link data for {symbol}", ex);
        }
    }

    private string BuildRequestUrl(string normalizedSymbol, DateOnly? from, DateOnly? to)
    {
        var url = $"{ApiBaseUrl}/datasets/{_database}/{normalizedSymbol}.json";
        var queryParams = new List<string>();

        if (!string.IsNullOrEmpty(_apiKey))
            queryParams.Add($"api_key={_apiKey}");

        if (from.HasValue)
            queryParams.Add($"start_date={from.Value:yyyy-MM-dd}");

        if (to.HasValue)
            queryParams.Add($"end_date={to.Value:yyyy-MM-dd}");

        if (queryParams.Count > 0)
            url += "?" + string.Join("&", queryParams);

        return url;
    }

    private static string RedactApiKey(string url)
    {
        var tokenIndex = url.IndexOf("api_key=", StringComparison.OrdinalIgnoreCase);
        if (tokenIndex < 0)
        {
            return url;
        }

        var valueStart = tokenIndex + "api_key=".Length;
        var valueEnd = url.IndexOf('&', valueStart);
        if (valueEnd < 0)
        {
            valueEnd = url.Length;
        }

        return url.Substring(0, valueStart) + "REDACTED" + url.Substring(valueEnd);
    }

    private List<AdjustedHistoricalBar> ParseDatasetResponse(QuandlDataset dataset, string symbol, DateOnly? from, DateOnly? to)
    {
        var columns = dataset.ColumnNames ?? Array.Empty<string>();
        var columnIndex = BuildColumnIndex(columns);
        var bars = new List<AdjustedHistoricalBar>();

        foreach (var row in dataset.Data!)
        {
            var bar = ParseRow(row, columnIndex, symbol);
            if (bar is not null)
            {
                // Apply date filter
                if (from.HasValue && bar.SessionDate < from.Value)
                    continue;
                if (to.HasValue && bar.SessionDate > to.Value)
                    continue;

                bars.Add(bar);
            }
        }

        return bars;
    }

    private static Dictionary<string, int> BuildColumnIndex(string[] columns)
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < columns.Length; i++)
        {
            index[columns[i]] = i;
        }
        return index;
    }

    private AdjustedHistoricalBar? ParseRow(JsonElement[] row, Dictionary<string, int> columns, string symbol)
    {
        try
        {
            // Standard WIKI columns: Date, Open, High, Low, Close, Volume, Ex-Dividend, Split Ratio, Adj. Open, Adj. High, Adj. Low, Adj. Close, Adj. Volume
            if (!TryGetValue(row, columns, "Date", out string? dateStr) || !DateOnly.TryParse(dateStr, out var date))
                return null;

            var open = GetDecimalValue(row, columns, "Open") ?? GetDecimalValue(row, columns, "Adj. Open");
            var high = GetDecimalValue(row, columns, "High") ?? GetDecimalValue(row, columns, "Adj. High");
            var low = GetDecimalValue(row, columns, "Low") ?? GetDecimalValue(row, columns, "Adj. Low");
            var close = GetDecimalValue(row, columns, "Close") ?? GetDecimalValue(row, columns, "Adj. Close");
            var volume = GetLongValue(row, columns, "Volume") ?? GetLongValue(row, columns, "Adj. Volume");

            if (open is null || high is null || low is null || close is null)
                return null;

            if (!IsValidOhlc(open.Value, high.Value, low.Value, close.Value))
                return null;

            var adjOpen = GetDecimalValue(row, columns, "Adj. Open");
            var adjHigh = GetDecimalValue(row, columns, "Adj. High");
            var adjLow = GetDecimalValue(row, columns, "Adj. Low");
            var adjClose = GetDecimalValue(row, columns, "Adj. Close");
            var adjVolume = GetLongValue(row, columns, "Adj. Volume");

            var dividend = GetDecimalValue(row, columns, "Ex-Dividend");
            var splitRatio = GetDecimalValue(row, columns, "Split Ratio");

            return new AdjustedHistoricalBar(
                Symbol: symbol.ToUpperInvariant(),
                SessionDate: date,
                Open: open.Value,
                High: high.Value,
                Low: low.Value,
                Close: close.Value,
                Volume: volume ?? 0,
                Source: Name,
                SequenceNumber: date.DayNumber,
                AdjustedOpen: adjOpen,
                AdjustedHigh: adjHigh,
                AdjustedLow: adjLow,
                AdjustedClose: adjClose,
                AdjustedVolume: adjVolume,
                SplitFactor: splitRatio != 1m ? splitRatio : null,
                DividendAmount: dividend > 0 ? dividend : null
            );
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to parse row for {Symbol}", symbol);
            return null;
        }
    }

    private static bool TryGetValue(JsonElement[] row, Dictionary<string, int> columns, string column, out string? value)
    {
        value = null;
        if (!columns.TryGetValue(column, out var index) || index >= row.Length)
            return false;

        value = row[index].GetString();
        return value is not null;
    }

    private static decimal? GetDecimalValue(JsonElement[] row, Dictionary<string, int> columns, string column)
    {
        if (!columns.TryGetValue(column, out var index) || index >= row.Length)
            return null;

        var element = row[index];
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) => v,
            _ => null
        };
    }

    private static long? GetLongValue(JsonElement[] row, Dictionary<string, int> columns, string column)
    {
        if (!columns.TryGetValue(column, out var index) || index >= row.Length)
            return null;

        var element = row[index];
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetInt64(),
            JsonValueKind.String when long.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) => v,
            _ => null
        };
    }


    private sealed class QuandlDatasetResponse
    {
        [JsonPropertyName("dataset")]
        public QuandlDataset? Dataset { get; set; }
    }

    private sealed class QuandlDataset
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("dataset_code")]
        public string? DatasetCode { get; set; }

        [JsonPropertyName("database_code")]
        public string? DatabaseCode { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("column_names")]
        public string[]? ColumnNames { get; set; }

        [JsonPropertyName("data")]
        public JsonElement[][]? Data { get; set; }

        [JsonPropertyName("start_date")]
        public string? StartDate { get; set; }

        [JsonPropertyName("end_date")]
        public string? EndDate { get; set; }

        [JsonPropertyName("frequency")]
        public string? Frequency { get; set; }
    }

}

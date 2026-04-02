using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Logging;
using Meridian.Application.Subscriptions.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Http;
using Serilog;

namespace Meridian.Infrastructure.Adapters.OpenFigi;

/// <summary>
/// Client for the OpenFIGI API (https://www.openfigi.com/api).
/// Provides mapping between tickers, ISINs, CUSIPs, SEDOLs and FIGI identifiers.
/// Free tier: 25 requests/minute, 250 identifiers per request.
/// </summary>
public sealed class OpenFigiClient : IDisposable
{
    private const string BaseUrl = "https://api.openfigi.com/v3";
    private const int MaxIdentifiersPerRequest = 100; // Conservative limit (max is 100 for unauthenticated)
    private const int MaxRequestsPerMinute = 25;

    private readonly HttpClient _http;
    private readonly RateLimiter _rateLimiter;
    private readonly ILogger _log;
    private readonly string? _apiKey;
    private bool _disposed;

    public string Name => "openfigi";
    public string DisplayName => "OpenFIGI";

    /// <summary>
    /// Creates a new OpenFIGI client.
    /// </summary>
    /// <param name="apiKey">Optional API key for higher rate limits. Set OPENFIGI_API_KEY env var or pass directly.</param>
    /// <param name="httpClient">Optional HTTP client for testing.</param>
    /// <param name="log">Optional logger.</param>
    public OpenFigiClient(string? apiKey = null, HttpClient? httpClient = null, ILogger? log = null)
    {
        _log = log ?? LoggingSetup.ForContext<OpenFigiClient>();
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("OPENFIGI_API_KEY");

        // TD-10: Use HttpClientFactory instead of creating new HttpClient instances
        _http = httpClient ?? HttpClientFactoryProvider.CreateClient(HttpClientNames.OpenFigi);
        _http.DefaultRequestHeaders.Add("User-Agent", "Meridian/1.0");

        if (!string.IsNullOrEmpty(_apiKey))
        {
            _http.DefaultRequestHeaders.Add("X-OPENFIGI-APIKEY", _apiKey);
        }

        // Rate limit: 25/min unauthenticated, higher with API key
        var maxRequests = string.IsNullOrEmpty(_apiKey) ? MaxRequestsPerMinute : 250;
        _rateLimiter = new RateLimiter(maxRequests, TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(100), _log);
    }

    /// <summary>
    /// Check if the OpenFIGI API is available.
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            // Simple availability check with a known ticker
            var results = await LookupByTickerAsync("AAPL", ct: ct).ConfigureAwait(false);
            return results.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Lookup FIGI mappings by ticker symbol.
    /// </summary>
    /// <param name="ticker">Ticker symbol (e.g., AAPL).</param>
    /// <param name="exchangeCode">Optional exchange code to narrow results (e.g., US).</param>
    /// <param name="marketSector">Optional market sector (Equity, Govt, Mtge, etc.).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<IReadOnlyList<FigiMapping>> LookupByTickerAsync(
        string ticker,
        string? exchangeCode = null,
        string? marketSector = "Equity",
        CancellationToken ct = default)
    {
        var request = new OpenFigiMappingRequest
        {
            IdType = "TICKER",
            IdValue = ticker.ToUpperInvariant(),
            ExchCode = exchangeCode,
            MarketSecDes = marketSector
        };

        var results = await MappingRequestAsync(new[] { request }, ct).ConfigureAwait(false);
        return results.FirstOrDefault() ?? Array.Empty<FigiMapping>();
    }

    /// <summary>
    /// Lookup FIGI mappings by ISIN.
    /// </summary>
    public async Task<IReadOnlyList<FigiMapping>> LookupByIsinAsync(
        string isin,
        string? marketSector = "Equity",
        CancellationToken ct = default)
    {
        var request = new OpenFigiMappingRequest
        {
            IdType = "ID_ISIN",
            IdValue = isin.ToUpperInvariant(),
            MarketSecDes = marketSector
        };

        var results = await MappingRequestAsync(new[] { request }, ct).ConfigureAwait(false);
        return results.FirstOrDefault() ?? Array.Empty<FigiMapping>();
    }

    /// <summary>
    /// Lookup FIGI mappings by CUSIP.
    /// </summary>
    public async Task<IReadOnlyList<FigiMapping>> LookupByCusipAsync(
        string cusip,
        string? marketSector = "Equity",
        CancellationToken ct = default)
    {
        var request = new OpenFigiMappingRequest
        {
            IdType = "ID_CUSIP",
            IdValue = cusip.ToUpperInvariant(),
            MarketSecDes = marketSector
        };

        var results = await MappingRequestAsync(new[] { request }, ct).ConfigureAwait(false);
        return results.FirstOrDefault() ?? Array.Empty<FigiMapping>();
    }

    /// <summary>
    /// Lookup FIGI mappings by SEDOL.
    /// </summary>
    public async Task<IReadOnlyList<FigiMapping>> LookupBySedolAsync(
        string sedol,
        string? marketSector = "Equity",
        CancellationToken ct = default)
    {
        var request = new OpenFigiMappingRequest
        {
            IdType = "ID_SEDOL",
            IdValue = sedol.ToUpperInvariant(),
            MarketSecDes = marketSector
        };

        var results = await MappingRequestAsync(new[] { request }, ct).ConfigureAwait(false);
        return results.FirstOrDefault() ?? Array.Empty<FigiMapping>();
    }

    /// <summary>
    /// Bulk lookup FIGI mappings for multiple tickers.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<FigiMapping>>> BulkLookupByTickersAsync(
        IEnumerable<string> tickers,
        string? exchangeCode = null,
        string? marketSector = "Equity",
        CancellationToken ct = default)
    {
        var tickerList = tickers.Select(t => t.ToUpperInvariant()).Distinct().ToList();
        if (tickerList.Count == 0)
            return new Dictionary<string, IReadOnlyList<FigiMapping>>();

        var requests = tickerList.Select(ticker => new OpenFigiMappingRequest
        {
            IdType = "TICKER",
            IdValue = ticker,
            ExchCode = exchangeCode,
            MarketSecDes = marketSector
        }).ToList();

        var allResults = new Dictionary<string, IReadOnlyList<FigiMapping>>(StringComparer.OrdinalIgnoreCase);

        // Process in batches due to API limits
        for (int i = 0; i < requests.Count; i += MaxIdentifiersPerRequest)
        {
            var batch = requests.Skip(i).Take(MaxIdentifiersPerRequest).ToList();
            var batchTickers = tickerList.Skip(i).Take(MaxIdentifiersPerRequest).ToList();

            var results = await MappingRequestAsync(batch, ct).ConfigureAwait(false);

            for (int j = 0; j < batchTickers.Count && j < results.Count; j++)
            {
                allResults[batchTickers[j]] = results[j];
            }
        }

        return allResults;
    }

    /// <summary>
    /// Search for securities by query string (uses filter endpoint).
    /// </summary>
    public async Task<IReadOnlyList<FigiMapping>> SearchAsync(
        string query,
        string? exchangeCode = null,
        string? marketSector = "Equity",
        string? securityType = null,
        int limit = 20,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<FigiMapping>();

        await _rateLimiter.WaitForSlotAsync(ct).ConfigureAwait(false);

        var filterRequest = new OpenFigiFilterRequest
        {
            Query = query,
            ExchCode = exchangeCode,
            MarketSecDes = marketSector,
            SecurityType = securityType
        };

        try
        {
            using var response = await _http.PostAsJsonAsync(
                $"{BaseUrl}/filter",
                filterRequest,
                new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull },
                ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _log.Warning("OpenFIGI filter returned {Status}: {Error}", response.StatusCode, error);
                return Array.Empty<FigiMapping>();
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var data = JsonSerializer.Deserialize<OpenFigiFilterResponse>(json);

            if (data?.Data is null || data.Data.Count == 0)
                return Array.Empty<FigiMapping>();

            return data.Data
                .Take(limit)
                .Select(MapToFigiMapping)
                .ToList();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "OpenFIGI search failed for query {Query}", query);
            return Array.Empty<FigiMapping>();
        }
    }

    /// <summary>
    /// Enrich symbol search results with FIGI information.
    /// </summary>
    public async Task<IReadOnlyList<SymbolSearchResult>> EnrichWithFigiAsync(
        IEnumerable<SymbolSearchResult> results,
        CancellationToken ct = default)
    {
        var resultList = results.ToList();
        if (resultList.Count == 0)
            return resultList;

        var tickers = resultList.Select(r => r.Symbol).Distinct().ToList();
        var figiMappings = await BulkLookupByTickersAsync(tickers, ct: ct).ConfigureAwait(false);

        return resultList.Select(r =>
        {
            if (figiMappings.TryGetValue(r.Symbol, out var mappings) && mappings.Count > 0)
            {
                var best = mappings.First();
                return r with
                {
                    Figi = best.Figi,
                    CompositeFigi = best.CompositeFigi
                };
            }
            return r;
        }).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyList<FigiMapping>>> MappingRequestAsync(
        IReadOnlyList<OpenFigiMappingRequest> requests,
        CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (requests.Count == 0)
            return Array.Empty<IReadOnlyList<FigiMapping>>();

        await _rateLimiter.WaitForSlotAsync(ct).ConfigureAwait(false);

        _log.Debug("Requesting OpenFIGI mappings for {Count} identifiers", requests.Count);

        try
        {
            using var response = await _http.PostAsJsonAsync(
                $"{BaseUrl}/mapping",
                requests,
                new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull },
                ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _log.Warning("OpenFIGI mapping returned {Status}: {Error}", response.StatusCode, error);

                if ((int)response.StatusCode == 429)
                {
                    throw new HttpRequestException("OpenFIGI rate limit exceeded (429)");
                }

                return Enumerable.Repeat<IReadOnlyList<FigiMapping>>(Array.Empty<FigiMapping>(), requests.Count).ToList();
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var data = JsonSerializer.Deserialize<List<OpenFigiMappingResponse>>(json);

            if (data is null)
                return Enumerable.Repeat<IReadOnlyList<FigiMapping>>(Array.Empty<FigiMapping>(), requests.Count).ToList();

            return data.Select(r =>
            {
                if (r.Error is not null)
                {
                    _log.Debug("OpenFIGI error: {Error}", r.Error);
                    return Array.Empty<FigiMapping>();
                }

                return (IReadOnlyList<FigiMapping>)(r.Data?.Select(MapToFigiMapping).ToList() ?? Array.Empty<FigiMapping>().ToList());
            }).ToList();
        }
        catch (JsonException ex)
        {
            _log.Error(ex, "Failed to parse OpenFIGI response");
            return Enumerable.Repeat<IReadOnlyList<FigiMapping>>(Array.Empty<FigiMapping>(), requests.Count).ToList();
        }
    }

    private static FigiMapping MapToFigiMapping(OpenFigiDataItem item)
    {
        return new FigiMapping(
            Figi: item.Figi ?? "",
            CompositeFigi: item.CompositeFigi,
            SecurityType: item.SecurityType,
            MarketSector: item.MarketSector,
            Ticker: item.Ticker,
            Name: item.Name,
            ExchangeCode: item.ExchCode,
            ShareClassFigi: item.ShareClassFigi,
            SecurityDescription: item.SecurityDescription
        );
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _rateLimiter.Dispose();
        _http.Dispose();
    }


    private sealed class OpenFigiMappingRequest
    {
        [JsonPropertyName("idType")]
        public string? IdType { get; set; }

        [JsonPropertyName("idValue")]
        public string? IdValue { get; set; }

        [JsonPropertyName("exchCode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ExchCode { get; set; }

        [JsonPropertyName("marketSecDes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? MarketSecDes { get; set; }
    }

    private sealed class OpenFigiMappingResponse
    {
        [JsonPropertyName("data")]
        public List<OpenFigiDataItem>? Data { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    private sealed class OpenFigiFilterRequest
    {
        [JsonPropertyName("query")]
        public string? Query { get; set; }

        [JsonPropertyName("exchCode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ExchCode { get; set; }

        [JsonPropertyName("marketSecDes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? MarketSecDes { get; set; }

        [JsonPropertyName("securityType")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SecurityType { get; set; }
    }

    private sealed class OpenFigiFilterResponse
    {
        [JsonPropertyName("data")]
        public List<OpenFigiDataItem>? Data { get; set; }

        [JsonPropertyName("total")]
        public int Total { get; set; }
    }

    private sealed class OpenFigiDataItem
    {
        [JsonPropertyName("figi")]
        public string? Figi { get; set; }

        [JsonPropertyName("compositeFIGI")]
        public string? CompositeFigi { get; set; }

        [JsonPropertyName("securityType")]
        public string? SecurityType { get; set; }

        [JsonPropertyName("marketSector")]
        public string? MarketSector { get; set; }

        [JsonPropertyName("ticker")]
        public string? Ticker { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("exchCode")]
        public string? ExchCode { get; set; }

        [JsonPropertyName("shareClassFIGI")]
        public string? ShareClassFigi { get; set; }

        [JsonPropertyName("securityDescription")]
        public string? SecurityDescription { get; set; }
    }

}

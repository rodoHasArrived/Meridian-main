using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Meridian.Application.Logging;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Core.SymbolResolution;
using Meridian.Infrastructure.Http;
using Serilog;

namespace Meridian.Infrastructure.Adapters.OpenFigi;

/// <summary>
/// Symbol resolver using the OpenFIGI API (https://www.openfigi.com/).
/// Free tier: 25 requests/minute without API key, 250 requests/minute with key.
/// </summary>
public sealed class OpenFigiSymbolResolver : ISymbolResolver, IDisposable
{
    private const string BaseUrl = "https://api.openfigi.com/v3";
    private readonly HttpClient _http;
    private readonly RateLimiter _rateLimiter;
    private readonly ConcurrentDictionary<string, SymbolResolution> _cache = new();
    private readonly string? _apiKey;
    private readonly ILogger _log;
    private bool _disposed;

    public string Name => "openfigi";

    public OpenFigiSymbolResolver(string? apiKey = null, HttpClient? httpClient = null, ILogger? log = null)
    {
        _apiKey = apiKey;
        _log = log ?? LoggingSetup.ForContext<OpenFigiSymbolResolver>();

        // TD-10: Use HttpClientFactory instead of creating new HttpClient instances
        _http = httpClient ?? HttpClientFactoryProvider.CreateClient(HttpClientNames.OpenFigi);
        _http.DefaultRequestHeaders.Add("Accept", "application/json");

        if (!string.IsNullOrEmpty(apiKey))
        {
            _http.DefaultRequestHeaders.Add("X-OPENFIGI-APIKEY", apiKey);
        }

        // Rate limit: 25/min without key, 250/min with key
        var maxRequests = string.IsNullOrEmpty(apiKey) ? 25 : 250;
        _rateLimiter = new RateLimiter(maxRequests, TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(100), _log);
    }

    public async Task<SymbolResolution?> ResolveAsync(string symbol, string? exchange = null, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var cacheKey = $"{symbol}:{exchange ?? "ANY"}";
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            _log.Debug("Cache hit for {Symbol}", symbol);
            return cached;
        }

        await _rateLimiter.WaitForSlotAsync(ct).ConfigureAwait(false);

        try
        {
            var request = new List<OpenFigiRequest>
            {
                new()
                {
                    IdType = "TICKER",
                    IdValue = symbol.ToUpperInvariant(),
                    ExchCode = exchange,
                    MarketSecDes = "Equity"
                }
            };

            _log.Information("Resolving symbol {Symbol} via OpenFIGI", symbol);

            using var response = await _http.PostAsJsonAsync($"{BaseUrl}/mapping", request, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _log.Warning("OpenFIGI returned {Status}: {Error}", response.StatusCode, error);
                return null;
            }

            var results = await response.Content.ReadFromJsonAsync<List<OpenFigiResponse>>(ct).ConfigureAwait(false);
            var mapping = results?.FirstOrDefault()?.Data?.FirstOrDefault();

            if (mapping is null)
            {
                _log.Debug("No FIGI mapping found for {Symbol}", symbol);
                return null;
            }

            var resolution = new SymbolResolution(
                Ticker: mapping.Ticker ?? symbol,
                Figi: mapping.Figi,
                CompositeFigi: mapping.CompositeFigi,
                ShareClassFigi: mapping.ShareClassFigi,
                Name: mapping.Name,
                Exchange: mapping.ExchCode,
                ExchangeCode: mapping.ExchCode,
                MarketSector: mapping.MarketSector,
                SecurityType: mapping.SecurityType2,
                Currency: null
            )
            {
                ProviderSymbols =
                {
                    ["yahoo"] = mapping.Ticker ?? symbol,
                    ["stooq"] = $"{(mapping.Ticker ?? symbol).ToLowerInvariant()}.us",
                    ["alpaca"] = mapping.Ticker ?? symbol,
                    ["polygon"] = mapping.Ticker ?? symbol,
                    ["quandl"] = $"WIKI/{mapping.Ticker ?? symbol}"
                }
            };

            _cache.TryAdd(cacheKey, resolution);
            _log.Information("Resolved {Symbol} -> FIGI: {Figi}, Name: {Name}", symbol, resolution.Figi, resolution.Name);

            return resolution;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to resolve symbol {Symbol}", symbol);
            return null;
        }
    }

    public async Task<string?> MapSymbolAsync(string symbol, string fromProvider, string toProvider, CancellationToken ct = default)
    {
        var resolution = await ResolveAsync(symbol, ct: ct).ConfigureAwait(false);
        if (resolution is null)
            return null;

        if (resolution.ProviderSymbols.TryGetValue(toProvider.ToLowerInvariant(), out var mapped))
        {
            return mapped;
        }

        // Default mapping strategies
        return toProvider.ToLowerInvariant() switch
        {
            "stooq" => $"{symbol.ToLowerInvariant()}.us",
            "yahoo" => symbol.ToUpperInvariant(),
            "polygon" => symbol.ToUpperInvariant(),
            "alpaca" => symbol.ToUpperInvariant(),
            "quandl" => $"WIKI/{symbol.ToUpperInvariant()}",
            _ => symbol
        };
    }

    public async Task<IReadOnlyList<SymbolSearchResult>> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _rateLimiter.WaitForSlotAsync(ct).ConfigureAwait(false);

        try
        {
            var request = new List<OpenFigiRequest>
            {
                new()
                {
                    IdType = "TICKER",
                    IdValue = query.ToUpperInvariant(),
                    MarketSecDes = "Equity"
                }
            };

            using var response = await _http.PostAsJsonAsync($"{BaseUrl}/mapping", request, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<SymbolSearchResult>();
            }

            var results = await response.Content.ReadFromJsonAsync<List<OpenFigiResponse>>(ct).ConfigureAwait(false);
            var mappings = results?.FirstOrDefault()?.Data ?? new List<OpenFigiMapping>();

            return mappings
                .Take(maxResults)
                .Select(m => new SymbolSearchResult(
                    Ticker: m.Ticker ?? query,
                    Name: m.Name ?? "Unknown",
                    Exchange: m.ExchCode,
                    SecurityType: m.SecurityType2,
                    Figi: m.Figi
                ))
                .ToList();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to search for {Query}", query);
            return Array.Empty<SymbolSearchResult>();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _rateLimiter.Dispose();
        _http.Dispose();
    }

    #region OpenFIGI API Models

    private sealed class OpenFigiRequest
    {
        [JsonPropertyName("idType")]
        public string? IdType { get; set; }

        [JsonPropertyName("idValue")]
        public string? IdValue { get; set; }

        [JsonPropertyName("exchCode")]
        public string? ExchCode { get; set; }

        [JsonPropertyName("micCode")]
        public string? MicCode { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("marketSecDes")]
        public string? MarketSecDes { get; set; }
    }

    private sealed class OpenFigiResponse
    {
        [JsonPropertyName("data")]
        public List<OpenFigiMapping>? Data { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    private sealed class OpenFigiMapping
    {
        [JsonPropertyName("figi")]
        public string? Figi { get; set; }

        [JsonPropertyName("compositeFIGI")]
        public string? CompositeFigi { get; set; }

        [JsonPropertyName("shareClassFIGI")]
        public string? ShareClassFigi { get; set; }

        [JsonPropertyName("ticker")]
        public string? Ticker { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("exchCode")]
        public string? ExchCode { get; set; }

        [JsonPropertyName("marketSector")]
        public string? MarketSector { get; set; }

        [JsonPropertyName("securityType")]
        public string? SecurityType { get; set; }

        [JsonPropertyName("securityType2")]
        public string? SecurityType2 { get; set; }
    }

    #endregion
}

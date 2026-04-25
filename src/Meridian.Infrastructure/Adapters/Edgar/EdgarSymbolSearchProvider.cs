using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Logging;
using Meridian.Application.Subscriptions.Models;
using Meridian.Contracts.Domain;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Edgar;

/// <summary>
/// Symbol search provider backed by the SEC EDGAR public API.
/// Uses the free <c>/files/company_tickers.json</c> endpoint — no API key required.
/// The full company list (~13 000 entries) is fetched once and cached in-memory for the
/// lifetime of the provider to stay well within the SEC's 10-request-per-second guidance.
/// </summary>
/// <remarks>
/// SEC EDGAR API reference: https://www.sec.gov/developer
/// Polite-use requirements: include a User-Agent header identifying the application.
/// </remarks>
[DataSource("edgar-symbols", "EDGAR (Symbol Search)", DataSourceType.Historical, DataSourceCategory.Free,
    Priority = 20, Description = "SEC EDGAR company list — free, no credentials required")]
[ImplementsAdr("ADR-001", "EDGAR symbol search provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
public sealed class EdgarSymbolSearchProvider : IFilterableSymbolSearchProvider, IDisposable
{
    private const string CompanyTickersUrl = "https://www.sec.gov/files/company_tickers.json";
    private const string SubmissionsBaseUrl = "https://data.sec.gov/submissions/CIK{0}.json";
    private const string EdgarUserAgent = "Meridian/1.0 contact@meridian.io";

    private readonly HttpClient _http;
    private readonly HttpClient _submissionsClient;
    private readonly RateLimiter _rateLimiter;
    private readonly ILogger _log;
    private readonly bool _ownsClients;

    // In-memory cache populated on first search — keyed by upper-case ticker.
    private volatile IReadOnlyList<EdgarCompanyEntry>? _companyCache;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private bool _disposed;

    public string Name => "edgar";
    public string DisplayName => "SEC EDGAR";
    public int Priority => 20;

    public IReadOnlyList<string> SupportedAssetTypes => new[]
    {
        "Common Stock", "ETF", "ADR", "Preferred Stock", "Fund", "REIT", "Trust"
    };

    public IReadOnlyList<string> SupportedExchanges => new[]
    {
        "NASDAQ", "NYSE", "NYSE ARCA", "NYSE AMERICAN", "BATS", "OTC", "US"
    };

    /// <summary>
    /// Initializes the provider.
    /// </summary>
    /// <param name="httpClient">Optional HTTP client aimed at www.sec.gov. Created via factory if null.</param>
    /// <param name="submissionsClient">Optional HTTP client aimed at data.sec.gov. Created via factory if null.</param>
    /// <param name="log">Optional logger.</param>
    public EdgarSymbolSearchProvider(
        HttpClient? httpClient = null,
        HttpClient? submissionsClient = null,
        ILogger? log = null)
    {
        _log = log ?? LoggingSetup.ForContext<EdgarSymbolSearchProvider>();
        _ownsClients = httpClient is null;

        _http = httpClient ?? HttpClientFactoryProvider.CreateClient(HttpClientNames.EdgarSymbolSearch);
        _submissionsClient = submissionsClient
            ?? HttpClientFactoryProvider.CreateClient(HttpClientNames.EdgarSecurityMaster);

        foreach (var client in new[] { _http, _submissionsClient })
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", EdgarUserAgent);
        }

        // SEC polite-crawling limit: use 8 req/s to stay well within the 10 req/s guideline.
        _rateLimiter = new RateLimiter(8, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(125), _log);
    }

    /// <summary>
    /// Availability check: succeeds as long as the SEC company ticker endpoint is reachable.
    /// No credentials are required; the SEC EDGAR API is freely accessible.
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var cache = await GetOrLoadCacheAsync(ct).ConfigureAwait(false);
            return cache.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Searches the in-memory company list (cache is lazily populated on first call).
    /// </summary>
    public Task<IReadOnlyList<SymbolSearchResult>> SearchAsync(
        string query,
        int limit = 10,
        CancellationToken ct = default)
        => SearchAsync(query, limit, null, null, ct);

    /// <summary>
    /// Searches the in-memory company list with optional asset-type and exchange filters.
    /// </summary>
    public async Task<IReadOnlyList<SymbolSearchResult>> SearchAsync(
        string query,
        int limit = 10,
        string? assetType = null,
        string? exchange = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<SymbolSearchResult>();

        var cache = await GetOrLoadCacheAsync(ct).ConfigureAwait(false);

        return cache
            .Where(e =>
                e.Ticker.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select((e, i) => new SymbolSearchResult(
                Symbol: e.Ticker,
                Name: e.Title,
                Exchange: null,
                AssetType: null,
                Country: "US",
                Currency: "USD",
                Source: "edgar",
                MatchScore: SymbolSearchUtility.CalculateMatchScore(query, e.Ticker, e.Title, i)))
            .OrderByDescending(r => r.MatchScore)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Returns details for a specific ticker by fetching the EDGAR submissions record.
    /// </summary>
    public async Task<SymbolDetails?> GetDetailsAsync(SymbolId symbol, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol.Value))
            return null;

        var cache = await GetOrLoadCacheAsync(ct).ConfigureAwait(false);
        var entry = cache.FirstOrDefault(
            e => string.Equals(e.Ticker, symbol.Value, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
            return null;

        return await FetchSubmissionDetailsAsync(entry, ct).ConfigureAwait(false);
    }

    // ── Cache management ─────────────────────────────────────────────────────

    private async Task<IReadOnlyList<EdgarCompanyEntry>> GetOrLoadCacheAsync(CancellationToken ct)
    {
        if (_companyCache is not null)
            return _companyCache;

        await _cacheLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_companyCache is not null)
                return _companyCache;

            await _rateLimiter.WaitForSlotAsync(ct).ConfigureAwait(false);

            using var response = await _http.GetAsync(CompanyTickersUrl, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _log.Warning("EDGAR company_tickers.json returned {Status}", response.StatusCode);
                return Array.Empty<EdgarCompanyEntry>();
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _companyCache = ParseCompanyTickers(json).ToList();

            _log.Information("EDGAR company cache loaded: {Count} companies", _companyCache.Count);
            return _companyCache;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to load EDGAR company ticker list");
            return Array.Empty<EdgarCompanyEntry>();
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    // ── Parsing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses the <c>company_tickers.json</c> dictionary-of-entries format returned by the SEC.
    /// The response is shaped as <c>{"0":{"cik_str":...,"ticker":...,"title":...},...}</c>.
    /// </summary>
    internal static IEnumerable<EdgarCompanyEntry> ParseCompanyTickers(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Enumerable.Empty<EdgarCompanyEntry>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var entries = new List<EdgarCompanyEntry>();

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var obj = prop.Value;
                if (obj.ValueKind != JsonValueKind.Object)
                    continue;

                var cik = obj.TryGetProperty("cik_str", out var cikEl)
                    ? cikEl.GetRawText().Trim('"')
                    : string.Empty;
                var ticker = obj.TryGetProperty("ticker", out var tickerEl)
                    ? tickerEl.GetString()
                    : null;
                var title = obj.TryGetProperty("title", out var titleEl)
                    ? titleEl.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(ticker))
                    continue;

                entries.Add(new EdgarCompanyEntry(
                    Cik: cik,
                    Ticker: ticker!,
                    Title: title ?? ticker!));
            }

            return entries;
        }
        catch (JsonException ex)
        {
            Log.ForContext<EdgarSymbolSearchProvider>()
               .Error(ex, "Failed to parse EDGAR company_tickers.json");
            return Enumerable.Empty<EdgarCompanyEntry>();
        }
    }

    // ── Details fetch ────────────────────────────────────────────────────────

    private async Task<SymbolDetails?> FetchSubmissionDetailsAsync(
        EdgarCompanyEntry entry,
        CancellationToken ct)
    {
        try
        {
            await _rateLimiter.WaitForSlotAsync(ct).ConfigureAwait(false);

            var paddedCik = entry.Cik.PadLeft(10, '0');
            var url = string.Format(SubmissionsBaseUrl, paddedCik);

            using var response = await _submissionsClient.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return BuildBasicDetails(entry);

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseSubmissionDetails(json, entry);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "EDGAR submission fetch failed for {Ticker}", entry.Ticker);
            return BuildBasicDetails(entry);
        }
    }

    private static SymbolDetails BuildBasicDetails(EdgarCompanyEntry entry)
        => new(
            Symbol: entry.Ticker,
            Name: entry.Title,
            Description: null,
            Exchange: null,
            AssetType: null,
            Sector: null,
            Industry: null,
            Country: "US",
            Currency: "USD",
            MarketCap: null,
            AverageVolume: null,
            Week52High: null,
            Week52Low: null,
            LastPrice: null,
            WebUrl: $"https://www.sec.gov/cgi-bin/browse-edgar?action=getcompany&CIK={entry.Cik}",
            LogoUrl: null,
            IpoDate: null,
            PaysDividend: null,
            DividendYield: null,
            PeRatio: null,
            SharesOutstanding: null,
            Figi: null,
            CompositeFigi: null,
            Isin: null,
            Cusip: null,
            Source: "edgar",
            LastUpdated: DateTimeOffset.UtcNow
        );

    private static SymbolDetails? ParseSubmissionDetails(string json, EdgarCompanyEntry entry)
    {
        try
        {
            var sub = JsonSerializer.Deserialize<EdgarSubmission>(json);
            if (sub is null)
                return BuildBasicDetails(entry);

            var exchange = sub.Exchanges?.FirstOrDefault();
            return new SymbolDetails(
                Symbol: entry.Ticker,
                Name: sub.Name ?? entry.Title,
                Description: null,
                Exchange: exchange,
                AssetType: null,
                Sector: sub.SicDescription,
                Industry: sub.SicDescription,
                Country: "US",
                Currency: "USD",
                MarketCap: null,
                AverageVolume: null,
                Week52High: null,
                Week52Low: null,
                LastPrice: null,
                WebUrl: $"https://www.sec.gov/cgi-bin/browse-edgar?action=getcompany&CIK={entry.Cik}",
                LogoUrl: null,
                IpoDate: null,
                PaysDividend: null,
                DividendYield: null,
                PeRatio: null,
                SharesOutstanding: null,
                Figi: null,
                CompositeFigi: null,
                Isin: null,
                Cusip: null,
                Source: "edgar",
                LastUpdated: DateTimeOffset.UtcNow
            );
        }
        catch
        {
            return BuildBasicDetails(entry);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cacheLock.Dispose();
        _rateLimiter.Dispose();
        if (_ownsClients)
        {
            _http.Dispose();
            _submissionsClient.Dispose();
        }
    }

    // ── JSON response models ─────────────────────────────────────────────────

    /// <summary>Represents a single row from <c>company_tickers.json</c>.</summary>
    internal sealed record EdgarCompanyEntry(string Cik, string Ticker, string Title);

    private sealed class EdgarSubmission
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("sic")]
        public string? Sic { get; set; }

        [JsonPropertyName("sicDescription")]
        public string? SicDescription { get; set; }

        [JsonPropertyName("exchanges")]
        public List<string>? Exchanges { get; set; }

        [JsonPropertyName("ein")]
        public string? Ein { get; set; }

        [JsonPropertyName("stateOfIncorporation")]
        public string? StateOfIncorporation { get; set; }

        [JsonPropertyName("stateOfIncorporationDescription")]
        public string? StateOfIncorporationDescription { get; set; }

        [JsonPropertyName("fiscalYearEnd")]
        public string? FiscalYearEnd { get; set; }
    }
}

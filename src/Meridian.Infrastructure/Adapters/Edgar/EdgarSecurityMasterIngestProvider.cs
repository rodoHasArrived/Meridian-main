using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Http;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Adapters.Edgar;

/// <summary>
/// Pages through the SEC EDGAR <c>company_tickers.json</c> endpoint and optionally enriches
/// each record with submission-level metadata (SIC code, exchanges, state of incorporation)
/// by fetching individual <c>/submissions/CIK{cik}.json</c> records.
/// Yields <see cref="CreateSecurityRequest"/> objects ready for Security Master bulk ingest.
/// </summary>
/// <remarks>
/// No API key is required — the SEC EDGAR API is freely accessible.
/// The SEC asks callers to include a <c>User-Agent</c> header identifying the application and
/// to respect the 10-request-per-second rate limit.
/// See https://www.sec.gov/developer for guidance.
/// </remarks>
public sealed class EdgarSecurityMasterIngestProvider : IDisposable
{
    private const string CompanyTickersUrl = "https://www.sec.gov/files/company_tickers.json";
    private const string SubmissionsBaseUrl = "https://data.sec.gov/submissions/CIK{0}.json";
    private const string EdgarUserAgent = "Meridian/1.0 contact@meridian.io";

    private readonly HttpClient _tickersClient;
    private readonly HttpClient _submissionsClient;
    private readonly bool _ownsClients;
    private readonly RateLimiter _rateLimiter;
    private readonly ILogger<EdgarSecurityMasterIngestProvider> _logger;
    private bool _disposed;

    /// <param name="logger">Application logger.</param>
    /// <param name="tickersClient">
    ///   Optional HTTP client aimed at <c>www.sec.gov</c>. A new instance is created if null.
    /// </param>
    /// <param name="submissionsClient">
    ///   Optional HTTP client aimed at <c>data.sec.gov</c>. A new instance is created if null.
    /// </param>
    public EdgarSecurityMasterIngestProvider(
        ILogger<EdgarSecurityMasterIngestProvider> logger,
        HttpClient? tickersClient = null,
        HttpClient? submissionsClient = null)
    {
        _logger = logger;

        _ownsClients = tickersClient is null;

        _tickersClient = tickersClient
            ?? HttpClientFactoryProvider.CreateClient(HttpClientNames.EdgarSymbolSearch);
        _submissionsClient = submissionsClient
            ?? HttpClientFactoryProvider.CreateClient(HttpClientNames.EdgarSecurityMaster);

        foreach (var client in new[] { _tickersClient, _submissionsClient })
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", EdgarUserAgent);
        }

        // SEC polite-crawling limit: ≤ 10 req/s; use a comfortable 8 to leave headroom.
        _rateLimiter = new RateLimiter(8, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(125), null);
    }

    /// <summary>
    /// Fetches all companies from EDGAR and returns them as Security Master create-requests.
    /// </summary>
    /// <param name="enrichWithSubmissions">
    ///   When <c>true</c>, each company's <c>/submissions/CIK*.json</c> record is fetched to
    ///   populate SIC code, exchanges, and state-of-incorporation.  This is much slower (one
    ///   extra HTTP call per company) and is disabled by default.
    /// </param>
    /// <param name="progress">Optional progress callback invoked after each processed page.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All matching companies as create-security requests.</returns>
    public async Task<IReadOnlyList<CreateSecurityRequest>> FetchAllAsync(
        bool enrichWithSubmissions = false,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        await _rateLimiter.WaitForSlotAsync(ct).ConfigureAwait(false);

        var json = await FetchJsonAsync(_tickersClient, CompanyTickersUrl, ct).ConfigureAwait(false);
        if (json is null)
            return Array.Empty<CreateSecurityRequest>();

        var entries = ParseCompanyTickers(json).ToList();
        _logger.LogInformation("EDGAR: loaded {Count} companies from company_tickers.json", entries.Count);

        var results = new List<CreateSecurityRequest>(entries.Count);
        var processed = 0;

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            EdgarSubmission? submission = null;
            if (enrichWithSubmissions)
            {
                await _rateLimiter.WaitForSlotAsync(ct).ConfigureAwait(false);
                submission = await FetchSubmissionAsync(entry.Cik, ct).ConfigureAwait(false);
            }

            var request = MapToCreateRequest(entry, submission);
            if (request is not null)
                results.Add(request);

            processed++;
            if (processed % 500 == 0)
            {
                progress?.Report(processed);
                _logger.LogDebug("EDGAR ingest: {Processed}/{Total} processed", processed, entries.Count);
            }
        }

        progress?.Report(results.Count);
        _logger.LogInformation(
            "EDGAR Security Master ingest complete: {Count} securities from {Total} companies",
            results.Count, entries.Count);

        return results;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<string?> FetchJsonAsync(HttpClient client, string url, CancellationToken ct)
    {
        try
        {
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogWarning("EDGAR request to {Url} returned {Status}: {Body}",
                    url, response.StatusCode, body[..Math.Min(200, body.Length)]);
                return null;
            }

            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EDGAR HTTP request to {Url} failed", url);
            return null;
        }
    }

    private async Task<EdgarSubmission?> FetchSubmissionAsync(string cik, CancellationToken ct)
    {
        var paddedCik = cik.PadLeft(10, '0');
        var url = string.Format(SubmissionsBaseUrl, paddedCik);
        var json = await FetchJsonAsync(_submissionsClient, url, ct).ConfigureAwait(false);

        if (json is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<EdgarSubmission>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EDGAR: failed to parse submission for CIK {Cik}", cik);
            return null;
        }
    }

    /// <summary>
    /// Parses the dictionary-of-objects format returned by <c>company_tickers.json</c>.
    /// </summary>
    internal static IEnumerable<EdgarCompanyEntry> ParseCompanyTickers(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            yield break;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch
        {
            yield break;
        }

        using (doc)
        {
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

                yield return new EdgarCompanyEntry(
                    Cik: cik,
                    Ticker: ticker!,
                    Title: title ?? ticker!);
            }
        }
    }

    private static CreateSecurityRequest? MapToCreateRequest(
        EdgarCompanyEntry entry,
        EdgarSubmission? submission)
    {
        if (string.IsNullOrWhiteSpace(entry.Ticker))
            return null;

        var now = DateTimeOffset.UtcNow;
        var securityId = Guid.NewGuid();

        var assetClass = MapAssetClass(submission?.Sic, submission?.Category);

        var identifiers = new List<SecurityIdentifierDto>
        {
            new(SecurityIdentifierKind.Ticker, entry.Ticker, IsPrimary: true, ValidFrom: now, Provider: "edgar")
        };

        if (!string.IsNullOrWhiteSpace(entry.Cik))
        {
            identifiers.Add(new SecurityIdentifierDto(
                Kind: SecurityIdentifierKind.Cik,
                Value: entry.Cik.PadLeft(10, '0'),
                IsPrimary: false,
                ValidFrom: now,
                Provider: "edgar"));
        }

        var commonTerms = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["displayName"] = entry.Title,
            ["currency"] = "USD",
            ["exchangeCode"] = submission?.Exchanges?.FirstOrDefault(),
            ["country"] = "US",
            ["stateOfIncorporation"] = submission?.StateOfIncorporation,
            ["fiscalYearEnd"] = submission?.FiscalYearEnd
        };

        var assetSpecificTerms = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["cik"] = entry.Cik,
            ["sic"] = submission?.Sic,
            ["sicDescription"] = submission?.SicDescription,
            ["category"] = submission?.Category
        };

        var commonJson = JsonSerializer.SerializeToElement(commonTerms);
        var assetJson = JsonSerializer.SerializeToElement(assetSpecificTerms);

        return new CreateSecurityRequest(
            SecurityId: securityId,
            AssetClass: assetClass,
            CommonTerms: commonJson,
            AssetSpecificTerms: assetJson,
            Identifiers: identifiers,
            EffectiveFrom: now,
            SourceSystem: "edgar",
            UpdatedBy: "EdgarSecurityMasterIngestProvider",
            SourceRecordId: entry.Ticker,
            Reason: "Bulk ingest from SEC EDGAR company_tickers.json");
    }

    /// <summary>
    /// Maps an EDGAR SIC code range (or filer category) to a canonical asset class.
    /// SIC codes: see EDGAR company search at www.sec.gov/cgi-bin/browse-edgar
    /// </summary>
    private static string MapAssetClass(string? sic, string? category)
    {
        if (!string.IsNullOrEmpty(category))
        {
            if (category.Contains("investment company", StringComparison.OrdinalIgnoreCase) ||
                category.Contains("registered investment", StringComparison.OrdinalIgnoreCase))
                return "Fund";
        }

        if (!int.TryParse(sic, out var sicCode))
            return "Equity";

        return sicCode switch
        {
            // Investment trusts / companies
            >= 6700 and <= 6726 => "Fund",
            // Real estate investment trusts
            6798 => "REIT",
            // Banks and holding companies
            >= 6020 and <= 6099 => "Equity",
            // Finance services
            >= 6100 and <= 6199 => "Equity",
            // All others default to equity
            _ => "Equity"
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _rateLimiter.Dispose();
        if (_ownsClients)
        {
            _tickersClient.Dispose();
            _submissionsClient.Dispose();
        }
    }

    // ── Response models ──────────────────────────────────────────────────────

    internal sealed record EdgarCompanyEntry(string Cik, string Ticker, string Title);

    internal sealed class EdgarSubmission
    {
        [JsonPropertyName("cik")]
        public string? Cik { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("sic")]
        public string? Sic { get; set; }

        [JsonPropertyName("sicDescription")]
        public string? SicDescription { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("tickers")]
        public List<string>? Tickers { get; set; }

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

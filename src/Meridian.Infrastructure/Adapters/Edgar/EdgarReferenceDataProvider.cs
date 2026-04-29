using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Http;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Adapters.Edgar;

/// <summary>
/// Fetches SEC EDGAR ticker, submissions, and companyfacts reference data.
/// </summary>
public sealed class EdgarReferenceDataProvider : IEdgarReferenceDataProvider, IDisposable
{
    internal const string CompanyTickersUrl = "https://www.sec.gov/files/company_tickers.json";
    internal const string CompanyTickersExchangeUrl = "https://www.sec.gov/files/company_tickers_exchange.json";
    internal const string CompanyTickersMutualFundsUrl = "https://www.sec.gov/files/company_tickers_mf.json";
    internal const string SubmissionsBulkUrl = "https://www.sec.gov/Archives/edgar/daily-index/bulkdata/submissions.zip";
    internal const string CompanyFactsBulkUrl = "https://www.sec.gov/Archives/edgar/daily-index/xbrl/companyfacts.zip";
    internal const string SubmissionsBaseUrl = "https://data.sec.gov/submissions/CIK{0}.json";
    internal const string CompanyFactsBaseUrl = "https://data.sec.gov/api/xbrl/companyfacts/CIK{0}.json";

    private const string EdgarUserAgent = "Meridian/1.0 contact@meridian.io";

    private static readonly string[] AssetsConcepts = ["Assets"];
    private static readonly string[] LiabilitiesConcepts = ["Liabilities"];
    private static readonly string[] EquityConcepts =
    [
        "StockholdersEquity",
        "StockholdersEquityIncludingPortionAttributableToNoncontrollingInterest"
    ];
    private static readonly string[] RevenueConcepts =
    [
        "RevenueFromContractWithCustomerExcludingAssessedTax",
        "Revenues",
        "SalesRevenueNet"
    ];
    private static readonly string[] NetIncomeConcepts = ["NetIncomeLoss", "ProfitLoss"];
    private static readonly string[] EpsDilutedConcepts = ["EarningsPerShareDiluted"];
    private static readonly string[] OperatingCashFlowConcepts = ["NetCashProvidedByUsedInOperatingActivities"];
    private static readonly string[] SharesOutstandingConcepts =
    [
        "EntityCommonStockSharesOutstanding",
        "WeightedAverageNumberOfDilutedSharesOutstanding",
        "WeightedAverageNumberOfSharesOutstandingBasic"
    ];
    private static readonly string[] PublicFloatConcepts = ["EntityPublicFloat"];
    private static readonly string[] TradingSymbolConcepts = ["TradingSymbol"];
    private static readonly string[] SecurityTitleConcepts = ["Security12bTitle"];
    private static readonly string[] SecurityExchangeNameConcepts = ["SecurityExchangeName"];
    private static readonly string[] EntityFileNumberConcepts = ["EntityFileNumber"];
    private static readonly string[] CommonStockSharesIssuedConcepts = ["CommonStockSharesIssued"];
    private static readonly string[] WeightedAverageBasicSharesConcepts = ["WeightedAverageNumberOfSharesOutstandingBasic"];
    private static readonly string[] WeightedAverageDilutedSharesConcepts = ["WeightedAverageNumberOfDilutedSharesOutstanding"];

    private readonly HttpClient _webClient;
    private readonly HttpClient _dataClient;
    private readonly HttpClient _archiveClient;
    private readonly bool _ownsWebClient;
    private readonly bool _ownsDataClient;
    private readonly bool _ownsArchiveClient;
    private readonly RateLimiter _rateLimiter;
    private readonly ILogger<EdgarReferenceDataProvider> _logger;
    private bool _disposed;

    public EdgarReferenceDataProvider(
        ILogger<EdgarReferenceDataProvider> logger,
        IHttpClientFactory? httpClientFactory = null,
        HttpClient? webClient = null,
        HttpClient? dataClient = null,
        HttpClient? archiveClient = null)
    {
        _logger = logger;

        _webClient = webClient
            ?? httpClientFactory?.CreateClient(HttpClientNames.EdgarSymbolSearch)
            ?? HttpClientFactoryProvider.CreateClient(HttpClientNames.EdgarSymbolSearch);
        _dataClient = dataClient
            ?? httpClientFactory?.CreateClient(HttpClientNames.EdgarSecurityMaster)
            ?? HttpClientFactoryProvider.CreateClient(HttpClientNames.EdgarSecurityMaster);
        _archiveClient = archiveClient
            ?? httpClientFactory?.CreateClient(HttpClientNames.EdgarSecurityMaster)
            ?? HttpClientFactoryProvider.CreateClient(HttpClientNames.EdgarSecurityMaster);

        _ownsWebClient = webClient is null && httpClientFactory is null;
        _ownsDataClient = dataClient is null && httpClientFactory is null;
        _ownsArchiveClient = archiveClient is null && httpClientFactory is null;

        foreach (var client in new[] { _webClient, _dataClient, _archiveClient })
        {
            ConfigureEdgarHeaders(client);
        }

        // SEC fair-access guidance is currently 10 requests/second; use 8/sec to leave headroom.
        _rateLimiter = new RateLimiter(8, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(125), null);
    }

    public async Task<IReadOnlyList<EdgarTickerAssociation>> FetchTickerAssociationsAsync(CancellationToken ct = default)
    {
        var associations = new Dictionary<string, EdgarTickerAssociation>(StringComparer.OrdinalIgnoreCase);

        var companyTickerJson = await FetchTextAsync(_webClient, CompanyTickersUrl, ct).ConfigureAwait(false);
        AddAssociations(ParseCompanyTickers(companyTickerJson), associations);

        var exchangeJson = await FetchTextAsync(_webClient, CompanyTickersExchangeUrl, ct).ConfigureAwait(false);
        AddAssociations(ParseCompanyTickersExchange(exchangeJson), associations);

        var mutualFundJson = await FetchTextAsync(_webClient, CompanyTickersMutualFundsUrl, ct).ConfigureAwait(false);
        AddAssociations(ParseMutualFundTickers(mutualFundJson), associations);

        return associations.Values
            .OrderBy(a => a.Cik, StringComparer.Ordinal)
            .ThenBy(a => a.Ticker, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.SeriesId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.ClassId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<EdgarFilerRecord>> FetchBulkSubmissionsAsync(
        int? maxFilers = null,
        CancellationToken ct = default)
    {
        var records = new List<EdgarFilerRecord>();

        await _rateLimiter.WaitForSlotAsync(ct).ConfigureAwait(false);
        using var response = await _archiveClient.GetAsync(
                SubmissionsBulkUrl,
                HttpCompletionOption.ResponseHeadersRead,
                ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("EDGAR submissions bulk ZIP returned {Status}", response.StatusCode);
            return records;
        }

        await using var stream = await OpenDecodedStreamAsync(response.Content, ct).ConfigureAwait(false);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        // Stream ZIP entries one at a time so bulk ingest does not buffer every filer JSON payload.
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();

            if (IsDirectory(entry) || !entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;

            await using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var json = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            var record = ParseSubmission(json, fallbackCik: ExtractCikFromEntryName(entry.FullName));
            if (record is null)
                continue;

            records.Add(record);
            if (maxFilers is > 0 && records.Count >= maxFilers.Value)
                break;
        }

        return records;
    }

    public async Task<IReadOnlyList<EdgarXbrlFact>> FetchBulkCompanyFactsAsync(
        IReadOnlySet<string>? ciks = null,
        int? maxFilers = null,
        CancellationToken ct = default)
    {
        var facts = new List<EdgarXbrlFact>();
        var normalizedFilter = ciks?
            .Select(NormalizeCik)
            .Where(c => c.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        var matchedFilers = 0;

        await _rateLimiter.WaitForSlotAsync(ct).ConfigureAwait(false);
        using var response = await _archiveClient.GetAsync(
                CompanyFactsBulkUrl,
                HttpCompletionOption.ResponseHeadersRead,
                ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("EDGAR companyfacts bulk ZIP returned {Status}", response.StatusCode);
            return facts;
        }

        await using var stream = await OpenDecodedStreamAsync(response.Content, ct).ConfigureAwait(false);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        // Stream ZIP entries one at a time; companyfacts.zip is large enough that all-entry buffering is unsafe.
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();

            if (IsDirectory(entry) || !entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;

            var entryCik = ExtractCikFromEntryName(entry.FullName);
            if (normalizedFilter is not null && !normalizedFilter.Contains(entryCik))
                continue;

            await using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var json = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            var parsed = ParseCompanyFacts(json);
            if (parsed.Count == 0)
                continue;

            facts.AddRange(parsed);
            matchedFilers++;

            if (maxFilers is > 0 && matchedFilers >= maxFilers.Value)
                break;
        }

        return facts;
    }

    public async Task<EdgarFilerRecord?> FetchSubmissionAsync(string cik, CancellationToken ct = default)
    {
        var normalized = NormalizeCik(cik);
        if (normalized.Length == 0)
            return null;

        var url = string.Format(SubmissionsBaseUrl, normalized);
        var json = await FetchTextAsync(_dataClient, url, ct).ConfigureAwait(false);
        return ParseSubmission(json, normalized);
    }

    public async Task<IReadOnlyList<EdgarXbrlFact>> FetchCompanyFactsAsync(string cik, CancellationToken ct = default)
    {
        var normalized = NormalizeCik(cik);
        if (normalized.Length == 0)
            return Array.Empty<EdgarXbrlFact>();

        var url = string.Format(CompanyFactsBaseUrl, normalized);
        var json = await FetchTextAsync(_dataClient, url, ct).ConfigureAwait(false);
        return ParseCompanyFacts(json);
    }

    public async Task<EdgarSecurityDataRecord> FetchSecurityDataAsync(
        EdgarFilerRecord filer,
        int? maxFilings = null,
        CancellationToken ct = default)
    {
        var debtOfferings = new List<EdgarDebtOfferingTerms>();
        var fundHoldings = new List<EdgarFundHolding>();
        var candidateFilings = filer.RecentFilings
            .Where(IsSecurityDataCandidate)
            .Take(maxFilings.GetValueOrDefault(25))
            .ToArray();

        foreach (var filing in candidateFilings)
        {
            ct.ThrowIfCancellationRequested();

            var documents = await FetchCandidateFilingDocumentsAsync(filing, ct).ConfigureAwait(false);
            foreach (var document in documents)
            {
                ct.ThrowIfCancellationRequested();

                var content = await FetchTextAsync(_archiveClient, document.Url, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(content))
                    continue;

                if (IsFundHoldingCandidate(filing, document))
                {
                    fundHoldings.AddRange(EdgarSecurityDocumentParser.ParseFundHoldings(
                        filing,
                        document.Name,
                        content));
                }

                if (IsDebtCandidate(filing, document))
                {
                    debtOfferings.AddRange(EdgarSecurityDocumentParser.ParseDebtOfferings(
                        filing,
                        document.Name,
                        document.Description,
                        content));
                }
            }
        }

        return new EdgarSecurityDataRecord(
            filer.Cik,
            debtOfferings,
            fundHoldings,
            DateTimeOffset.UtcNow);
    }

    internal static IReadOnlyList<EdgarTickerAssociation> ParseCompanyTickers(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<EdgarTickerAssociation>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return Array.Empty<EdgarTickerAssociation>();

            var results = new List<EdgarTickerAssociation>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object)
                    continue;

                var obj = prop.Value;
                var cik = NormalizeCik(GetString(obj, "cik_str"));
                var ticker = NormalizeTicker(GetString(obj, "ticker"));
                var name = GetString(obj, "title");

                if (cik.Length == 0 || ticker.Length == 0)
                    continue;

                results.Add(new EdgarTickerAssociation(
                    Cik: cik,
                    Ticker: ticker,
                    Name: name,
                    Exchange: null,
                    SeriesId: null,
                    ClassId: null,
                    SecurityType: "OperatingCompany",
                    Source: "company_tickers"));
            }

            return results;
        }
        catch (JsonException)
        {
            return Array.Empty<EdgarTickerAssociation>();
        }
    }

    internal static IReadOnlyList<EdgarTickerAssociation> ParseCompanyTickersExchange(string? json)
        => ParseFieldsDataAssociations(json, "company_tickers_exchange", defaultSecurityType: "OperatingCompany");

    internal static IReadOnlyList<EdgarTickerAssociation> ParseMutualFundTickers(string? json)
        => ParseFieldsDataAssociations(json, "company_tickers_mf", defaultSecurityType: "MutualFund");

    internal static EdgarFilerRecord? ParseSubmission(string? json, string? fallbackCik)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var cik = NormalizeCik(GetString(root, "cik") ?? fallbackCik);
            if (cik.Length == 0)
                return null;

            var name = GetString(root, "name")
                ?? GetString(root, "entityName")
                ?? cik;

            return new EdgarFilerRecord(
                Cik: cik,
                Name: name,
                EntityType: GetString(root, "entityType"),
                Sic: GetString(root, "sic"),
                SicDescription: GetString(root, "sicDescription"),
                Category: GetString(root, "category"),
                StateOfIncorporation: GetString(root, "stateOfIncorporation"),
                StateOfIncorporationDescription: GetString(root, "stateOfIncorporationDescription"),
                FiscalYearEnd: GetString(root, "fiscalYearEnd"),
                Ein: GetString(root, "ein"),
                Description: GetString(root, "description"),
                Website: GetString(root, "website"),
                InvestorWebsite: GetString(root, "investorWebsite"),
                Phone: GetString(root, "phone"),
                BusinessAddress: ParseAddress(root, "business"),
                MailingAddress: ParseAddress(root, "mailing"),
                InsiderTransactionForOwnerExists: GetBool(root, "insiderTransactionForOwnerExists"),
                InsiderTransactionForIssuerExists: GetBool(root, "insiderTransactionForIssuerExists"),
                Flags: GetStringArray(root, "flags"),
                Tickers: GetStringArray(root, "tickers"),
                Exchanges: GetStringArray(root, "exchanges"),
                FormerNames: ParseFormerNames(root),
                RecentFilings: ParseRecentFilings(root, cik),
                RetrievedAtUtc: DateTimeOffset.UtcNow);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static IReadOnlyList<EdgarXbrlFact> ParseCompanyFacts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<EdgarXbrlFact>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("facts", out var factsNode))
                return Array.Empty<EdgarXbrlFact>();

            var cik = NormalizeCik(GetString(root, "cik"));
            if (cik.Length == 0)
                return Array.Empty<EdgarXbrlFact>();

            var facts = new List<EdgarXbrlFact>();
            foreach (var taxonomy in factsNode.EnumerateObject())
            {
                if (taxonomy.Value.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var concept in taxonomy.Value.EnumerateObject())
                {
                    if (concept.Value.ValueKind != JsonValueKind.Object ||
                        !concept.Value.TryGetProperty("units", out var units) ||
                        units.ValueKind != JsonValueKind.Object)
                        continue;

                    foreach (var unit in units.EnumerateObject())
                    {
                        if (unit.Value.ValueKind != JsonValueKind.Array)
                            continue;

                        foreach (var factNode in unit.Value.EnumerateArray())
                        {
                            if (factNode.ValueKind != JsonValueKind.Object)
                                continue;

                            facts.Add(new EdgarXbrlFact(
                                Cik: cik,
                                Taxonomy: taxonomy.Name,
                                Concept: concept.Name,
                                Unit: unit.Name,
                                NumericValue: GetDecimal(factNode, "val"),
                                RawValue: GetRawScalar(factNode, "val"),
                                StartDate: GetDateOnly(factNode, "start"),
                                EndDate: GetDateOnly(factNode, "end"),
                                FiscalYear: GetInt(factNode, "fy"),
                                FiscalPeriod: GetString(factNode, "fp"),
                                Form: GetString(factNode, "form"),
                                AccessionNumber: GetString(factNode, "accn"),
                                FiledDate: GetDateOnly(factNode, "filed"),
                                Frame: GetString(factNode, "frame")));
                        }
                    }
                }
            }

            return facts;
        }
        catch (JsonException)
        {
            return Array.Empty<EdgarXbrlFact>();
        }
    }

    public static IssuerFactSnapshot CreateIssuerFactSnapshot(
        string cik,
        IReadOnlyList<EdgarXbrlFact> facts)
        => new(
            NormalizeCik(cik),
            DateTimeOffset.UtcNow,
            PickAnyFact(facts, TradingSymbolConcepts),
            PickAnyFact(facts, SecurityTitleConcepts),
            PickAnyFact(facts, SecurityExchangeNameConcepts),
            PickAnyFact(facts, EntityFileNumberConcepts),
            PickFact(facts, AssetsConcepts),
            PickFact(facts, LiabilitiesConcepts),
            PickFact(facts, EquityConcepts),
            PickFact(facts, RevenueConcepts),
            PickFact(facts, NetIncomeConcepts),
            PickFact(facts, EpsDilutedConcepts),
            PickFact(facts, OperatingCashFlowConcepts),
            PickFact(facts, SharesOutstandingConcepts),
            PickFact(facts, CommonStockSharesIssuedConcepts),
            PickFact(facts, WeightedAverageBasicSharesConcepts),
            PickFact(facts, WeightedAverageDilutedSharesConcepts),
            PickFact(facts, PublicFloatConcepts));

    internal static string NormalizeCik(string? cik)
    {
        if (string.IsNullOrWhiteSpace(cik))
            return string.Empty;

        var digits = new string(cik.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
            return string.Empty;

        return digits.Length >= 10
            ? digits[^10..]
            : digits.PadLeft(10, '0');
    }

    private async Task<string?> FetchTextAsync(HttpClient client, string url, CancellationToken ct)
    {
        try
        {
            await _rateLimiter.WaitForSlotAsync(ct).ConfigureAwait(false);
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("EDGAR request to {Url} returned {Status}", url, response.StatusCode);
                return null;
            }

            await using var stream = await OpenDecodedStreamAsync(response.Content, ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EDGAR request to {Url} failed", url);
            return null;
        }
    }

    private static async Task<Stream> OpenDecodedStreamAsync(HttpContent content, CancellationToken ct)
    {
        var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var encoding = content.Headers.ContentEncoding.FirstOrDefault();

        return encoding?.ToLowerInvariant() switch
        {
            "gzip" => new GZipStream(stream, CompressionMode.Decompress),
            "deflate" => new DeflateStream(stream, CompressionMode.Decompress),
            "br" => new BrotliStream(stream, CompressionMode.Decompress),
            _ => stream
        };
    }

    private static void ConfigureEdgarHeaders(HttpClient client)
    {
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", EdgarUserAgent);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");
    }

    private static void AddAssociations(
        IEnumerable<EdgarTickerAssociation> source,
        IDictionary<string, EdgarTickerAssociation> target)
    {
        foreach (var association in source)
        {
            var key = AssociationKey(association);
            if (!target.TryGetValue(key, out var existing))
            {
                target[key] = association;
                continue;
            }

            target[key] = existing with
            {
                Name = association.Name ?? existing.Name,
                Exchange = association.Exchange ?? existing.Exchange,
                SeriesId = association.SeriesId ?? existing.SeriesId,
                ClassId = association.ClassId ?? existing.ClassId,
                SecurityType = association.SecurityType ?? existing.SecurityType,
                Source = association.Source
            };
        }
    }

    private static string AssociationKey(EdgarTickerAssociation association)
        => string.Join(
            '|',
            association.Cik,
            association.Ticker,
            association.SeriesId ?? string.Empty,
            association.ClassId ?? string.Empty);

    private static IReadOnlyList<EdgarTickerAssociation> ParseFieldsDataAssociations(
        string? json,
        string source,
        string defaultSecurityType)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<EdgarTickerAssociation>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Array.Empty<EdgarTickerAssociation>();

            if (root.TryGetProperty("fields", out var fieldsNode) &&
                root.TryGetProperty("data", out var dataNode) &&
                fieldsNode.ValueKind == JsonValueKind.Array &&
                dataNode.ValueKind == JsonValueKind.Array)
            {
                var fields = fieldsNode.EnumerateArray()
                    .Select(e => e.GetString() ?? string.Empty)
                    .ToArray();

                var results = new List<EdgarTickerAssociation>();
                foreach (var row in dataNode.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Array)
                        continue;

                    var values = row.EnumerateArray().ToArray();
                    var association = BuildAssociationFromRow(fields, values, source, defaultSecurityType);
                    if (association is not null)
                        results.Add(association);
                }

                return results;
            }

            var dictionaryResults = new List<EdgarTickerAssociation>();
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object)
                    continue;

                var obj = prop.Value;
                var cik = NormalizeCik(GetString(obj, "cik") ?? GetString(obj, "cik_str"));
                var ticker = NormalizeTicker(GetString(obj, "ticker"));
                if (cik.Length == 0 || ticker.Length == 0)
                    continue;

                dictionaryResults.Add(new EdgarTickerAssociation(
                    cik,
                    ticker,
                    GetString(obj, "name") ?? GetString(obj, "title"),
                    GetString(obj, "exchange"),
                    GetString(obj, "seriesId") ?? GetString(obj, "series"),
                    GetString(obj, "classId") ?? GetString(obj, "class"),
                    defaultSecurityType,
                    source));
            }

            return dictionaryResults;
        }
        catch (JsonException)
        {
            return Array.Empty<EdgarTickerAssociation>();
        }
    }

    private static EdgarTickerAssociation? BuildAssociationFromRow(
        IReadOnlyList<string> fields,
        IReadOnlyList<JsonElement> values,
        string source,
        string defaultSecurityType)
    {
        string? Get(params string[] aliases)
        {
            for (var i = 0; i < fields.Count && i < values.Count; i++)
            {
                if (aliases.Any(alias => string.Equals(fields[i], alias, StringComparison.OrdinalIgnoreCase)))
                    return JsonElementToString(values[i]);
            }

            return null;
        }

        var cik = NormalizeCik(Get("cik", "cik_str"));
        var ticker = NormalizeTicker(Get("ticker", "symbol"));
        if (cik.Length == 0 || ticker.Length == 0)
            return null;

        return new EdgarTickerAssociation(
            Cik: cik,
            Ticker: ticker,
            Name: Get("name", "title"),
            Exchange: Get("exchange"),
            SeriesId: Get("seriesId", "series_id", "series"),
            ClassId: Get("classId", "class_id", "class"),
            SecurityType: Get("securityType", "security_type") ?? defaultSecurityType,
            Source: source);
    }

    private static IReadOnlyList<EdgarFormerName> ParseFormerNames(JsonElement root)
    {
        if (!root.TryGetProperty("formerNames", out var node) || node.ValueKind != JsonValueKind.Array)
            return Array.Empty<EdgarFormerName>();

        var results = new List<EdgarFormerName>();
        foreach (var item in node.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var name = GetString(item, "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            results.Add(new EdgarFormerName(
                name,
                GetDateOnly(item, "from"),
                GetDateOnly(item, "to")));
        }

        return results;
    }

    private static IReadOnlyList<EdgarFilingSummary> ParseRecentFilings(JsonElement root, string cik)
    {
        if (!root.TryGetProperty("filings", out var filings) ||
            filings.ValueKind != JsonValueKind.Object ||
            !filings.TryGetProperty("recent", out var recent) ||
            recent.ValueKind != JsonValueKind.Object)
            return Array.Empty<EdgarFilingSummary>();

        var accessions = GetStringArray(recent, "accessionNumber");
        var forms = GetStringArray(recent, "form");
        var filingDates = GetStringArray(recent, "filingDate");
        var reportDates = GetStringArray(recent, "reportDate");
        var primaryDocuments = GetStringArray(recent, "primaryDocument");
        var primaryDocDescriptions = GetStringArray(recent, "primaryDocDescription");
        var fileNumbers = GetStringArray(recent, "fileNumber");
        var filmNumbers = GetStringArray(recent, "filmNumber");
        var items = GetStringArray(recent, "items");
        var sizes = GetLongArray(recent, "size");
        var xbrlFlags = GetBoolArray(recent, "isXBRL");
        var inlineXbrlFlags = GetBoolArray(recent, "isInlineXBRL");

        var count = new[]
            {
                accessions.Count,
                forms.Count,
                filingDates.Count,
                reportDates.Count,
                primaryDocuments.Count,
                primaryDocDescriptions.Count,
                fileNumbers.Count,
                filmNumbers.Count,
                items.Count,
                sizes.Count,
                xbrlFlags.Count,
                inlineXbrlFlags.Count
            }
            .DefaultIfEmpty(0)
            .Max();

        var results = new List<EdgarFilingSummary>(count);
        for (var i = 0; i < count; i++)
        {
            var accession = At(accessions, i);
            var form = At(forms, i);
            if (string.IsNullOrWhiteSpace(accession) || string.IsNullOrWhiteSpace(form))
                continue;

            results.Add(new EdgarFilingSummary(
                Cik: cik,
                AccessionNumber: accession,
                Form: form,
                FilingDate: ParseDateOnly(At(filingDates, i)),
                ReportDate: ParseDateOnly(At(reportDates, i)),
                PrimaryDocument: At(primaryDocuments, i),
                PrimaryDocDescription: At(primaryDocDescriptions, i),
                FileNumber: At(fileNumbers, i),
                FilmNumber: At(filmNumbers, i),
                Items: At(items, i),
                Size: At(sizes, i),
                IsXbrl: At(xbrlFlags, i),
                IsInlineXbrl: At(inlineXbrlFlags, i),
                SecurityDataTags: BuildSecurityDataTags(form, At(primaryDocDescriptions, i), At(items, i))));
        }

        return results;
    }

    private async Task<IReadOnlyList<EdgarArchiveDocument>> FetchCandidateFilingDocumentsAsync(
        EdgarFilingSummary filing,
        CancellationToken ct)
    {
        var baseUrl = ArchiveFilingBaseUrl(filing);
        if (baseUrl is null)
            return Array.Empty<EdgarArchiveDocument>();

        var documents = new List<EdgarArchiveDocument>();
        if (!string.IsNullOrWhiteSpace(filing.PrimaryDocument))
        {
            documents.Add(new EdgarArchiveDocument(
                filing.PrimaryDocument,
                filing.PrimaryDocDescription,
                filing.Form,
                $"{baseUrl}/{Uri.EscapeDataString(filing.PrimaryDocument)}"));
        }

        var indexUrl = $"{baseUrl}/{AccessionWithoutDashes(filing.AccessionNumber)}-index.json";
        var indexJson = await FetchTextAsync(_archiveClient, indexUrl, ct).ConfigureAwait(false);
        foreach (var document in ParseArchiveIndex(indexJson, baseUrl))
        {
            if (documents.Any(d => string.Equals(d.Name, document.Name, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (IsCandidateArchiveDocument(filing, document))
                documents.Add(document);
        }

        return documents.Take(8).ToArray();
    }

    internal static IReadOnlyList<EdgarArchiveDocument> ParseArchiveIndex(string? json, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<EdgarArchiveDocument>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("directory", out var directory) ||
                !directory.TryGetProperty("item", out var items) ||
                items.ValueKind != JsonValueKind.Array)
                return Array.Empty<EdgarArchiveDocument>();

            var documents = new List<EdgarArchiveDocument>();
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var name = GetString(item, "name");
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                documents.Add(new EdgarArchiveDocument(
                    name,
                    GetString(item, "description"),
                    GetString(item, "type"),
                    $"{baseUrl}/{Uri.EscapeDataString(name)}"));
            }

            return documents;
        }
        catch (JsonException)
        {
            return Array.Empty<EdgarArchiveDocument>();
        }
    }

    private static bool IsCandidateArchiveDocument(
        EdgarFilingSummary filing,
        EdgarArchiveDocument document)
    {
        var name = document.Name;
        if (!name.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) &&
            !name.EndsWith(".html", StringComparison.OrdinalIgnoreCase) &&
            !name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) &&
            !name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            return false;

        if (IsFundHoldingCandidate(filing, document) || IsDebtCandidate(filing, document))
            return true;

        return ContainsAny(
            $"{document.Name} {document.Description} {document.Type}",
            ["prospectus", "supplement", "indenture", "note", "debenture", "n-port", "nport", "13f"]);
    }

    private static bool IsSecurityDataCandidate(EdgarFilingSummary filing)
        => filing.SecurityDataTags.Count > 0 ||
           IsDebtRegistrationForm(filing.Form) ||
           IsFundHoldingForm(filing.Form);

    private static bool IsDebtCandidate(EdgarFilingSummary filing, EdgarArchiveDocument document)
        => IsDebtRegistrationForm(filing.Form) ||
           ContainsAny(
               $"{document.Name} {document.Description} {document.Type} {filing.PrimaryDocDescription}",
               ["424b", "prospectus", "supplement", "indenture", "note", "debenture", "bond"]);

    private static bool IsFundHoldingCandidate(EdgarFilingSummary filing, EdgarArchiveDocument document)
        => IsFundHoldingForm(filing.Form) ||
           ContainsAny(
               $"{document.Name} {document.Description} {document.Type} {filing.PrimaryDocDescription}",
               ["n-port", "nport", "13f", "information table", "infotable"]);

    private static bool IsDebtRegistrationForm(string form)
        => form.StartsWith("424B", StringComparison.OrdinalIgnoreCase) ||
           form.Equals("FWP", StringComparison.OrdinalIgnoreCase) ||
           form.StartsWith("S-3", StringComparison.OrdinalIgnoreCase) ||
           form.StartsWith("F-3", StringComparison.OrdinalIgnoreCase) ||
           form.StartsWith("S-1", StringComparison.OrdinalIgnoreCase) ||
           form.StartsWith("F-1", StringComparison.OrdinalIgnoreCase) ||
           form.Equals("8-K", StringComparison.OrdinalIgnoreCase);

    private static bool IsFundHoldingForm(string form)
        => form.StartsWith("NPORT", StringComparison.OrdinalIgnoreCase) ||
           form.StartsWith("N-PORT", StringComparison.OrdinalIgnoreCase) ||
           form.StartsWith("13F", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> BuildSecurityDataTags(
        string form,
        string? primaryDocDescription,
        string? items)
    {
        var tags = new List<string>();
        if (IsDebtRegistrationForm(form) ||
            ContainsAny($"{primaryDocDescription} {items}", ["prospectus", "indenture", "note", "debt", "bond"]))
            tags.Add("debt-offering");

        if (IsFundHoldingForm(form))
            tags.Add("fund-holdings");

        if (form.StartsWith("S-", StringComparison.OrdinalIgnoreCase) ||
            form.StartsWith("F-", StringComparison.OrdinalIgnoreCase))
            tags.Add("registration");

        return tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool ContainsAny(string? value, IReadOnlyList<string> needles)
        => !string.IsNullOrWhiteSpace(value) &&
           needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static string? ArchiveFilingBaseUrl(EdgarFilingSummary filing)
    {
        var cikPath = filing.Cik.TrimStart('0');
        var accession = AccessionWithoutDashes(filing.AccessionNumber);
        if (string.IsNullOrWhiteSpace(cikPath) || string.IsNullOrWhiteSpace(accession))
            return null;

        return $"https://www.sec.gov/Archives/edgar/data/{cikPath}/{accession}";
    }

    private static string AccessionWithoutDashes(string accessionNumber)
        => accessionNumber.Replace("-", string.Empty, StringComparison.Ordinal);

    private static string? At(IReadOnlyList<string> values, int index)
        => index >= 0 && index < values.Count ? values[index] : null;

    private static long? At(IReadOnlyList<long?> values, int index)
        => index >= 0 && index < values.Count ? values[index] : null;

    private static bool? At(IReadOnlyList<bool?> values, int index)
        => index >= 0 && index < values.Count ? values[index] : null;

    private static EdgarAddress? ParseAddress(JsonElement root, string addressKind)
    {
        if (!root.TryGetProperty("addresses", out var addresses) ||
            addresses.ValueKind != JsonValueKind.Object ||
            !TryGetProperty(addresses, addressKind, out var address) ||
            address.ValueKind != JsonValueKind.Object)
            return null;

        var parsed = new EdgarAddress(
            Street1: GetString(address, "street1"),
            Street2: GetString(address, "street2"),
            City: GetString(address, "city"),
            StateOrCountry: GetString(address, "stateOrCountry"),
            StateOrCountryDescription: GetString(address, "stateOrCountryDescription"),
            ZipCode: GetString(address, "zipCode"));

        return parsed.Street1 is null &&
               parsed.Street2 is null &&
               parsed.City is null &&
               parsed.StateOrCountry is null &&
               parsed.StateOrCountryDescription is null &&
               parsed.ZipCode is null
            ? null
            : parsed;
    }

    private static EdgarXbrlFact? PickFact(
        IReadOnlyList<EdgarXbrlFact> facts,
        IReadOnlyList<string> concepts)
        => PickAnyFact(facts, concepts, numericOnly: true);

    private static EdgarXbrlFact? PickAnyFact(
        IReadOnlyList<EdgarXbrlFact> facts,
        IReadOnlyList<string> concepts)
        => PickAnyFact(facts, concepts, numericOnly: false);

    private static EdgarXbrlFact? PickAnyFact(
        IReadOnlyList<EdgarXbrlFact> facts,
        IReadOnlyList<string> concepts,
        bool numericOnly)
        => concepts
            .SelectMany((concept, priority) => facts
                .Where(f => string.Equals(f.Concept, concept, StringComparison.OrdinalIgnoreCase))
                .Select(f => (Fact: f, Priority: priority)))
            .Where(item => !numericOnly || item.Fact.NumericValue.HasValue)
            .OrderBy(item => item.Priority)
            .ThenByDescending(item => item.Fact.FiledDate ?? DateOnly.MinValue)
            .ThenByDescending(item => item.Fact.EndDate ?? DateOnly.MinValue)
            .ThenByDescending(item => item.Fact.FiscalYear ?? 0)
            .Select(item => item.Fact)
            .FirstOrDefault();

    private static IReadOnlyList<string> GetStringArray(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var node))
            return Array.Empty<string>();

        if (node.ValueKind == JsonValueKind.String)
        {
            var single = node.GetString();
            return string.IsNullOrWhiteSpace(single) ? Array.Empty<string>() : new[] { single };
        }

        if (node.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var values = new List<string>();
        foreach (var item in node.EnumerateArray())
        {
            var value = JsonElementToString(item);
            if (!string.IsNullOrWhiteSpace(value))
                values.Add(value);
        }

        return values;
    }

    private static IReadOnlyList<long?> GetLongArray(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var node) || node.ValueKind != JsonValueKind.Array)
            return Array.Empty<long?>();

        var values = new List<long?>();
        foreach (var item in node.EnumerateArray())
        {
            values.Add(GetLong(item));
        }

        return values;
    }

    private static IReadOnlyList<bool?> GetBoolArray(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var node) || node.ValueKind != JsonValueKind.Array)
            return Array.Empty<bool?>();

        var values = new List<bool?>();
        foreach (var item in node.EnumerateArray())
        {
            values.Add(GetBool(item));
        }

        return values;
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var node))
            return null;

        return JsonElementToString(node);
    }

    private static int? GetInt(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var node))
            return null;

        return GetInt(node);
    }

    private static int? GetInt(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Number && node.TryGetInt32(out var value))
            return value;

        return int.TryParse(JsonElementToString(node), out var parsed) ? parsed : null;
    }

    private static long? GetLong(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Number && node.TryGetInt64(out var value))
            return value;

        return long.TryParse(JsonElementToString(node), out var parsed) ? parsed : null;
    }

    private static bool? GetBool(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var node))
            return null;

        return GetBool(node);
    }

    private static bool? GetBool(JsonElement node)
        => node.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when node.TryGetInt32(out var value) => value != 0,
            JsonValueKind.String when bool.TryParse(node.GetString(), out var value) => value,
            JsonValueKind.String when int.TryParse(node.GetString(), out var value) => value != 0,
            _ => null
        };

    private static decimal? GetDecimal(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var node))
            return null;

        if (node.ValueKind == JsonValueKind.Number && node.TryGetDecimal(out var value))
            return value;

        return decimal.TryParse(JsonElementToString(node), out var parsed) ? parsed : null;
    }

    private static DateOnly? GetDateOnly(JsonElement root, string propertyName)
    {
        var value = GetString(root, propertyName);
        return ParseDateOnly(value);
    }

    private static DateOnly? ParseDateOnly(string? value)
        => DateOnly.TryParse(value, out var parsed) ? parsed : null;

    private static string? GetRawScalar(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var node) ||
            node.ValueKind is JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        return node.GetRawText().Trim('"');
    }

    private static string NormalizeTicker(string? ticker)
        => string.IsNullOrWhiteSpace(ticker) ? string.Empty : ticker.Trim().ToUpperInvariant();

    private static bool TryGetProperty(JsonElement root, string propertyName, out JsonElement value)
    {
        foreach (var prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? JsonElementToString(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };

    private static bool IsDirectory(ZipArchiveEntry entry)
        => string.IsNullOrEmpty(entry.Name);

    private static string ExtractCikFromEntryName(string entryName)
    {
        var fileName = Path.GetFileNameWithoutExtension(entryName);
        return NormalizeCik(fileName);
    }

    internal sealed record EdgarArchiveDocument(
        string Name,
        string? Description,
        string? Type,
        string Url);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _rateLimiter.Dispose();

        if (_ownsWebClient)
            _webClient.Dispose();
        if (_ownsDataClient)
            _dataClient.Dispose();
        if (_ownsArchiveClient)
            _archiveClient.Dispose();
    }
}

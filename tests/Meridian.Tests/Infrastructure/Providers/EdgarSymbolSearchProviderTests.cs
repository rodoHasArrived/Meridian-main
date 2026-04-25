using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.Domain;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Edgar;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Unit tests for <see cref="EdgarSymbolSearchProvider"/>.
/// All HTTP calls are stubbed — no network access required.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EdgarSymbolSearchProviderTests
{
    // Minimal company_tickers.json payload (dictionary-of-objects format the SEC uses).
    private static readonly string SampleCompanyTickers = """
        {
          "0": {"cik_str": 789019, "ticker": "MSFT", "title": "MICROSOFT CORP"},
          "1": {"cik_str": 320193, "ticker": "AAPL", "title": "Apple Inc."},
          "2": {"cik_str": 1018724, "ticker": "AMZN", "title": "AMAZON COM INC"}
        }
        """;

    // Minimal submissions/CIK*.json payload from data.sec.gov.
    private static readonly string SampleSubmission = """
        {
          "cik": "0000789019",
          "name": "MICROSOFT CORP",
          "sic": "7372",
          "sicDescription": "Prepackaged Software",
          "tickers": ["MSFT"],
          "exchanges": ["Nasdaq"],
          "ein": "91-1144442",
          "stateOfIncorporation": "WA",
          "stateOfIncorporationDescription": "Washington",
          "fiscalYearEnd": "0630"
        }
        """;

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a provider backed by a stub that always returns
    /// <paramref name="tickersJson"/> for the company-tickers endpoint.
    /// Returns all resources so callers can dispose them.
    /// </summary>
    private static (EdgarSymbolSearchProvider provider,
                    StubHttpMessageHandler handler,
                    HttpClient tickersClient,
                    HttpClient subClient)
        CreateProvider(string tickersJson, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(tickersJson, Encoding.UTF8, "application/json")
            });
        var tickersClient = new HttpClient(handler) { BaseAddress = new Uri("https://www.sec.gov/") };
        var subClient = new HttpClient();   // idle — not called in most tests
        var provider = new EdgarSymbolSearchProvider(
            httpClient: tickersClient, submissionsClient: subClient);

        return (provider, handler, tickersClient, subClient);
    }

    // ── ParseCompanyTickers ────────────────────────────────────────────────

    [Fact]
    public void ParseCompanyTickers_WithValidJson_ReturnsAllEntries()
    {
        var entries = EdgarSymbolSearchProvider.ParseCompanyTickers(SampleCompanyTickers).ToList();

        entries.Should().HaveCount(3);
        entries.Should().Contain(e => e.Ticker == "MSFT" && e.Title == "MICROSOFT CORP");
        entries.Should().Contain(e => e.Ticker == "AAPL" && e.Title == "Apple Inc.");
        entries.Should().Contain(e => e.Ticker == "AMZN" && e.Title == "AMAZON COM INC");
    }

    [Fact]
    public void ParseCompanyTickers_WithEmptyString_ReturnsEmpty()
    {
        var entries = EdgarSymbolSearchProvider.ParseCompanyTickers(string.Empty).ToList();
        entries.Should().BeEmpty();
    }

    [Fact]
    public void ParseCompanyTickers_WithInvalidJson_ReturnsEmpty()
    {
        var entries = EdgarSymbolSearchProvider.ParseCompanyTickers("{not valid json").ToList();
        entries.Should().BeEmpty();
    }

    [Fact]
    public void ParseCompanyTickers_WithMissingTicker_SkipsEntry()
    {
        var json = """{"0": {"cik_str": 1, "title": "No Ticker Corp"}}""";
        var entries = EdgarSymbolSearchProvider.ParseCompanyTickers(json).ToList();
        entries.Should().BeEmpty();
    }

    [Fact]
    public void ParseCompanyTickers_CikIsString_ParsedCorrectly()
    {
        var json = """{"0": {"cik_str": 789019, "ticker": "MSFT", "title": "MICROSOFT CORP"}}""";
        var entries = EdgarSymbolSearchProvider.ParseCompanyTickers(json).ToList();
        entries.Should().HaveCount(1);
        entries[0].Cik.Should().NotBeEmpty();
    }

    // ── SearchAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_ByTicker_ReturnsMatchingResults()
    {
        var (provider, handler, tickersClient, subClient) = CreateProvider(SampleCompanyTickers);
        using (handler) using (tickersClient) using (subClient) using (provider)
        {
            var results = await provider.SearchAsync("MSFT", 10, CancellationToken.None);

            results.Should().ContainSingle(r => r.Symbol == "MSFT");
            results[0].Name.Should().Be("MICROSOFT CORP");
            results[0].Source.Should().Be("edgar");
            results[0].Country.Should().Be("US");
            results[0].Currency.Should().Be("USD");
        }
    }

    [Fact]
    public async Task SearchAsync_ByCompanyName_ReturnsMatchingResults()
    {
        var (provider, handler, tickersClient, subClient) = CreateProvider(SampleCompanyTickers);
        using (handler) using (tickersClient) using (subClient) using (provider)
        {
            var results = await provider.SearchAsync("Apple", 10, CancellationToken.None);
            results.Should().ContainSingle(r => r.Symbol == "AAPL");
        }
    }

    [Fact]
    public async Task SearchAsync_WithLimitLowerThanMatches_RespectsLimit()
    {
        var (provider, handler, tickersClient, subClient) = CreateProvider(SampleCompanyTickers);
        using (handler) using (tickersClient) using (subClient) using (provider)
        {
            // "A" matches AAPL and AMZN
            var results = await provider.SearchAsync("A", limit: 1, CancellationToken.None);
            results.Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task SearchAsync_WithEmptyQuery_ReturnsEmptyList()
    {
        using var tickersClient = new HttpClient();
        using var subClient = new HttpClient();
        using var provider = new EdgarSymbolSearchProvider(
            httpClient: tickersClient, submissionsClient: subClient);

        var results = await provider.SearchAsync(string.Empty, 10, CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WhenHttpError_ReturnsEmptyList()
    {
        var (provider, handler, tickersClient, subClient) =
            CreateProvider(string.Empty, HttpStatusCode.ServiceUnavailable);
        using (handler) using (tickersClient) using (subClient) using (provider)
        {
            var results = await provider.SearchAsync("AAPL", 10, CancellationToken.None);
            results.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task SearchAsync_CacheIsReusedOnSecondCall()
    {
        var callCount = 0;
        using var handler = new StubHttpMessageHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleCompanyTickers, Encoding.UTF8, "application/json")
            };
        });
        using var tickersClient = new HttpClient(handler) { BaseAddress = new Uri("https://www.sec.gov/") };
        using var subClient = new HttpClient();
        using var provider = new EdgarSymbolSearchProvider(
            httpClient: tickersClient, submissionsClient: subClient);

        _ = await provider.SearchAsync("AAPL", 10, CancellationToken.None);
        _ = await provider.SearchAsync("MSFT", 10, CancellationToken.None);

        callCount.Should().Be(1, "the cache should be populated after the first call");
    }

    // ── IsAvailableAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task IsAvailableAsync_WhenEndpointReachable_ReturnsTrue()
    {
        var (provider, handler, tickersClient, subClient) = CreateProvider(SampleCompanyTickers);
        using (handler) using (tickersClient) using (subClient) using (provider)
        {
            var available = await provider.IsAvailableAsync(CancellationToken.None);
            available.Should().BeTrue();
        }
    }

    [Fact]
    public async Task IsAvailableAsync_WhenEndpointFails_ReturnsFalse()
    {
        var (provider, handler, tickersClient, subClient) =
            CreateProvider(string.Empty, HttpStatusCode.ServiceUnavailable);
        using (handler) using (tickersClient) using (subClient) using (provider)
        {
            var available = await provider.IsAvailableAsync(CancellationToken.None);
            available.Should().BeFalse();
        }
    }

    // ── GetDetailsAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetDetailsAsync_WithValidTicker_ReturnsDetails()
    {
        using var tickersHandler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleCompanyTickers, Encoding.UTF8, "application/json")
            });
        using var tickersClient = new HttpClient(tickersHandler) { BaseAddress = new Uri("https://www.sec.gov/") };

        using var subHandler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleSubmission, Encoding.UTF8, "application/json")
            });
        using var subClient = new HttpClient(subHandler) { BaseAddress = new Uri("https://data.sec.gov/") };

        using var provider = new EdgarSymbolSearchProvider(
            httpClient: tickersClient, submissionsClient: subClient);

        var details = await provider.GetDetailsAsync(new SymbolId("MSFT"), CancellationToken.None);

        details.Should().NotBeNull();
        details!.Symbol.Should().Be("MSFT");
        details.Name.Should().Be("MICROSOFT CORP");
        details.Exchange.Should().Be("Nasdaq");
        details.Sector.Should().Be("Prepackaged Software");
        details.Cusip.Should().BeNull("SEC EIN values are tax identifiers and must not be mapped to CUSIP");
        details.Source.Should().Be("edgar");
    }

    [Fact]
    public async Task GetDetailsAsync_WithUnknownSymbol_ReturnsNull()
    {
        var (provider, handler, tickersClient, subClient) = CreateProvider(SampleCompanyTickers);
        using (handler) using (tickersClient) using (subClient) using (provider)
        {
            var details = await provider.GetDetailsAsync(new SymbolId("UNKNOWN_XYZ"), CancellationToken.None);
            details.Should().BeNull();
        }
    }

    // ── Provider metadata ──────────────────────────────────────────────────

    [Fact]
    public void Name_IsEdgar()
    {
        using var tickersClient = new HttpClient();
        using var subClient = new HttpClient();
        using var provider = new EdgarSymbolSearchProvider(
            httpClient: tickersClient, submissionsClient: subClient);
        provider.Name.Should().Be("edgar");
    }

    [Fact]
    public void SupportedAssetTypes_NotEmpty()
    {
        using var tickersClient = new HttpClient();
        using var subClient = new HttpClient();
        using var provider = new EdgarSymbolSearchProvider(
            httpClient: tickersClient, submissionsClient: subClient);
        provider.SupportedAssetTypes.Should().NotBeEmpty();
    }

    [Fact]
    public void SupportedExchanges_IncludesNasdaq()
    {
        using var tickersClient = new HttpClient();
        using var subClient = new HttpClient();
        using var provider = new EdgarSymbolSearchProvider(
            httpClient: tickersClient, submissionsClient: subClient);
        provider.SupportedExchanges.Should().Contain("NASDAQ");
    }
}

/// <summary>
/// Unit tests for <see cref="EdgarSecurityMasterIngestProvider"/> parsing logic.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EdgarSecurityMasterIngestProviderTests
{
    private static readonly string SampleCompanyTickers = """
        {
          "0": {"cik_str": 789019, "ticker": "MSFT", "title": "MICROSOFT CORP"},
          "1": {"cik_str": 320193, "ticker": "AAPL", "title": "Apple Inc."},
          "2": {"cik_str": 1018724, "ticker": "AMZN", "title": "AMAZON COM INC"}
        }
        """;

    // ── ParseCompanyTickers ────────────────────────────────────────────────

    [Fact]
    public void ParseCompanyTickers_WithValidJson_YieldsAllEntries()
    {
        var entries = EdgarSecurityMasterIngestProvider.ParseCompanyTickers(SampleCompanyTickers).ToList();

        entries.Should().HaveCount(3);
        entries.Should().Contain(e => e.Ticker == "MSFT");
        entries.Should().Contain(e => e.Ticker == "AAPL");
        entries.Should().Contain(e => e.Ticker == "AMZN");
    }

    [Fact]
    public void ParseCompanyTickers_CikIsNonEmpty_InIngestResult()
    {
        var entries = EdgarSecurityMasterIngestProvider.ParseCompanyTickers(SampleCompanyTickers).ToList();
        entries[0].Cik.Should().NotBeEmpty();
    }

    [Fact]
    public void ParseCompanyTickers_EmptyJson_ReturnsEmpty()
    {
        var entries = EdgarSecurityMasterIngestProvider.ParseCompanyTickers(string.Empty).ToList();
        entries.Should().BeEmpty();
    }

    // ── FetchAllAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task FetchAllAsync_WithStubHttpClient_ReturnsMappedRequests()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleCompanyTickers, Encoding.UTF8, "application/json")
            });
        using var tickersClient = new HttpClient(handler) { BaseAddress = new Uri("https://www.sec.gov/") };
        using var subClient = new HttpClient();
        using var provider = new EdgarSecurityMasterIngestProvider(
            NullLogger<EdgarSecurityMasterIngestProvider>.Instance,
            tickersClient: tickersClient,
            submissionsClient: subClient);

        var requests = await provider.FetchAllAsync(enrichWithSubmissions: false, ct: CancellationToken.None);

        requests.Should().HaveCount(3);
        requests.Should().AllSatisfy(r =>
        {
            r.SourceSystem.Should().Be("edgar");
            r.Identifiers.Should().Contain(id => id.Kind == SecurityIdentifierKind.Ticker);
            r.Identifiers.Should().Contain(id => id.Kind == SecurityIdentifierKind.Cik && id.Provider == "edgar");
        });
    }

    [Fact]
    public async Task FetchAllAsync_WhenHttpFails_ReturnsEmptyList()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var tickersClient = new HttpClient(handler) { BaseAddress = new Uri("https://www.sec.gov/") };
        using var subClient = new HttpClient();
        using var provider = new EdgarSecurityMasterIngestProvider(
            NullLogger<EdgarSecurityMasterIngestProvider>.Instance,
            tickersClient: tickersClient,
            submissionsClient: subClient);

        var requests = await provider.FetchAllAsync(enrichWithSubmissions: false, ct: CancellationToken.None);

        requests.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAllAsync_TickersMapToCorrectSecurityIds()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleCompanyTickers, Encoding.UTF8, "application/json")
            });
        using var tickersClient = new HttpClient(handler) { BaseAddress = new Uri("https://www.sec.gov/") };
        using var subClient = new HttpClient();
        using var provider = new EdgarSecurityMasterIngestProvider(
            NullLogger<EdgarSecurityMasterIngestProvider>.Instance,
            tickersClient: tickersClient,
            submissionsClient: subClient);

        var requests = await provider.FetchAllAsync(enrichWithSubmissions: false, ct: CancellationToken.None);

        var tickers = requests.Select(r =>
            r.Identifiers.First(id => id.Kind == SecurityIdentifierKind.Ticker).Value).ToList();

        tickers.Should().Contain("MSFT");
        tickers.Should().Contain("AAPL");
        tickers.Should().Contain("AMZN");
    }

    [Fact]
    public async Task FetchAllAsync_EachRequestHasUniqueSecurityId()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleCompanyTickers, Encoding.UTF8, "application/json")
            });
        using var tickersClient = new HttpClient(handler) { BaseAddress = new Uri("https://www.sec.gov/") };
        using var subClient = new HttpClient();
        using var provider = new EdgarSecurityMasterIngestProvider(
            NullLogger<EdgarSecurityMasterIngestProvider>.Instance,
            tickersClient: tickersClient,
            submissionsClient: subClient);

        var requests = await provider.FetchAllAsync(enrichWithSubmissions: false, ct: CancellationToken.None);

        var ids = requests.Select(r => r.SecurityId).ToList();
        ids.Distinct().Should().HaveCount(ids.Count, "every security must have a unique SecurityId");
    }

    [Fact]
    public async Task FetchAllAsync_AssetClassDefaultsToEquity()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleCompanyTickers, Encoding.UTF8, "application/json")
            });
        using var tickersClient = new HttpClient(handler) { BaseAddress = new Uri("https://www.sec.gov/") };
        using var subClient = new HttpClient();
        using var provider = new EdgarSecurityMasterIngestProvider(
            NullLogger<EdgarSecurityMasterIngestProvider>.Instance,
            tickersClient: tickersClient,
            submissionsClient: subClient);

        var requests = await provider.FetchAllAsync(enrichWithSubmissions: false, ct: CancellationToken.None);

        requests.Should().AllSatisfy(r => r.AssetClass.Should().Be("Equity"));
    }
}

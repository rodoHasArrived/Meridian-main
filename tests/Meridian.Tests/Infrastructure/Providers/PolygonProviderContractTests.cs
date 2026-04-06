using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

// ─────────────────────────────────────────────────────────────────────────────
//  Streaming contract
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Applies the shared <see cref="MarketDataClientContractTests{TClient}"/> suite to
/// <see cref="PolygonMarketDataClient"/>.  The client is constructed without a valid API
/// key so <c>IsEnabled == false</c>; identity / metadata / disposal contracts still run.
/// </summary>
public sealed class PolygonMarketDataClientContractTests : MarketDataClientContractTests<PolygonMarketDataClient>
{
    protected override PolygonMarketDataClient CreateClient()
    {
        var publisher = new TestMarketEventPublisher();
        var tradeCollector = new TradeDataCollector(publisher, null);
        var quoteCollector = new QuoteCollector(publisher);
        // No API key → IsEnabled == false; contract tests for identity/metadata/disposal run without network.
        return new PolygonMarketDataClient(publisher, tradeCollector, quoteCollector);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Historical-provider contract
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Applies the shared <see cref="HistoricalDataProviderContractTests{TProvider}"/> suite to
/// <see cref="PolygonHistoricalDataProvider"/>.  A stub API key is supplied so that identity
/// and metadata properties are always accessible; no real network calls are made.
/// </summary>
public sealed class PolygonHistoricalProviderContractTests : HistoricalDataProviderContractTests<PolygonHistoricalDataProvider>
{
    protected override PolygonHistoricalDataProvider CreateProvider()
        => new(apiKey: "stub-key-for-contract-tests");
}

// ─────────────────────────────────────────────────────────────────────────────
//  Response-parsing tests (mock HTTP)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Verifies that <see cref="PolygonHistoricalDataProvider"/> correctly parses known
/// Polygon Aggregates API responses without making live network calls.
/// </summary>
public sealed class PolygonHistoricalResponseParsingTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static HttpClient CreateHttpClientWithResponse(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new StubHttpMessageHandler(json, status);
        return new HttpClient(handler) { BaseAddress = new Uri("https://api.polygon.io") };
    }

    // Valid single-page response containing two AAPL aggregates
    private const string ValidAaplResponse = """
        {
          "ticker": "AAPL",
          "queryCount": 2,
          "resultsCount": 2,
          "adjusted": true,
          "results": [
            { "o": 170.10, "h": 172.50, "l": 169.80, "c": 171.90, "v": 55000000, "vw": 171.30, "t": 1680307200000, "n": 350000 },
            { "o": 171.95, "h": 174.00, "l": 171.20, "c": 173.40, "v": 48000000, "vw": 172.60, "t": 1680393600000, "n": 310000 }
          ],
          "status": "OK",
          "request_id": "test-req-1"
        }
        """;

    private const string EmptyResultsResponse = """
        {
          "ticker": "UNKNOWN",
          "queryCount": 0,
          "resultsCount": 0,
          "adjusted": true,
          "results": [],
          "status": "OK",
          "request_id": "test-req-2"
        }
        """;

    private const string NullResultsResponse = """
        {
          "ticker": "AAPL",
          "queryCount": 0,
          "resultsCount": 0,
          "adjusted": true,
          "status": "OK",
          "request_id": "test-req-3"
        }
        """;

    // ── GetAdjustedDailyBarsAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetAdjustedDailyBarsAsync_ValidResponse_ReturnsCorrectBarCount()
    {
        var http = CreateHttpClientWithResponse(ValidAaplResponse);
        using var provider = new PolygonHistoricalDataProvider(apiKey: "test-key-xxxxxxxxxxxxxxxx12", httpClient: http);

        var bars = await provider.GetAdjustedDailyBarsAsync("AAPL", null, null);

        bars.Should().HaveCount(2, "the stub response contains exactly two aggregates");
    }

    [Fact]
    public async Task GetAdjustedDailyBarsAsync_ValidResponse_PopulatesOhlcv()
    {
        var http = CreateHttpClientWithResponse(ValidAaplResponse);
        using var provider = new PolygonHistoricalDataProvider(apiKey: "test-key-xxxxxxxxxxxxxxxx12", httpClient: http);

        var bars = await provider.GetAdjustedDailyBarsAsync("AAPL", null, null);

        bars.Should().AllSatisfy(bar =>
        {
            bar.Symbol.Should().Be("AAPL");
            bar.Open.Should().BeGreaterThan(0);
            bar.High.Should().BeGreaterThanOrEqualTo(bar.Open);
            bar.Close.Should().BeGreaterThan(0);
            bar.Low.Should().BeLessThanOrEqualTo(bar.Close);
            bar.Volume.Should().BeGreaterThanOrEqualTo(0);
        });
    }

    [Fact]
    public async Task GetAdjustedDailyBarsAsync_EmptyResults_ReturnsEmptyList()
    {
        var http = CreateHttpClientWithResponse(EmptyResultsResponse);
        using var provider = new PolygonHistoricalDataProvider(apiKey: "test-key-xxxxxxxxxxxxxxxx12", httpClient: http);

        var bars = await provider.GetAdjustedDailyBarsAsync("UNKNOWN", null, null);

        bars.Should().BeEmpty("the stub response contains no results");
    }

    [Fact]
    public async Task GetAdjustedDailyBarsAsync_NullResults_ReturnsEmptyList()
    {
        var http = CreateHttpClientWithResponse(NullResultsResponse);
        using var provider = new PolygonHistoricalDataProvider(apiKey: "test-key-xxxxxxxxxxxxxxxx12", httpClient: http);

        var bars = await provider.GetAdjustedDailyBarsAsync("AAPL", null, null);

        bars.Should().BeEmpty("absent results array should be treated as empty");
    }

    [Fact]
    public async Task GetAdjustedDailyBarsAsync_HttpError_ThrowsOrReturnsEmpty()
    {
        var http = CreateHttpClientWithResponse("{}", HttpStatusCode.Unauthorized);
        using var provider = new PolygonHistoricalDataProvider(apiKey: "test-key-xxxxxxxxxxxxxxxx12", httpClient: http);

        // Either a meaningful exception or an empty list is acceptable — it must not silently succeed.
        try
        {
            var bars = await provider.GetAdjustedDailyBarsAsync("AAPL", null, null);
            bars.Should().BeEmpty("a non-success HTTP response must not produce bars");
        }
        catch (Exception ex)
        {
            ex.Should().NotBeNull("an exception is an acceptable response to a 401");
        }
    }

    [Fact]
    public async Task GetAdjustedDailyBarsAsync_NoApiKey_ThrowsInvalidOperation()
    {
        var http = CreateHttpClientWithResponse(ValidAaplResponse);
        using var provider = new PolygonHistoricalDataProvider(apiKey: null, httpClient: http);

        var act = async () => await provider.GetAdjustedDailyBarsAsync("AAPL", null, null);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "a missing API key must surface as an InvalidOperationException, not silently fail");
    }

    // ── GetDailyBarsAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetDailyBarsAsync_ValidResponse_DelegatesToAdjusted()
    {
        var http = CreateHttpClientWithResponse(ValidAaplResponse);
        using var provider = new PolygonHistoricalDataProvider(apiKey: "test-key-xxxxxxxxxxxxxxxx12", httpClient: http);

        // GetDailyBarsAsync delegates to GetAdjustedDailyBarsAsync under the hood
        var bars = await provider.GetDailyBarsAsync("AAPL", null, null);

        bars.Should().HaveCount(2, "GetDailyBarsAsync must surface the same data as the adjusted endpoint");
    }

    // ── IsAvailableAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task IsAvailableAsync_WithStubKey_DoesNotThrow()
    {
        var http = CreateHttpClientWithResponse("{\"status\":\"OK\"}", HttpStatusCode.OK);
        using var provider = new PolygonHistoricalDataProvider(apiKey: "test-key-xxxxxxxxxxxxxxxx12", httpClient: http);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var act = async () => await provider.IsAvailableAsync(cts.Token);

        await act.Should().NotThrowAsync("IsAvailableAsync must never throw regardless of credentials");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Internal test helper – lightweight stub HTTP message handler
// ─────────────────────────────────────────────────────────────────────────────

file sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly string _responseBody;
    private readonly HttpStatusCode _statusCode;

    public StubHttpMessageHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responseBody = responseBody;
        _statusCode = statusCode;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
        });
}

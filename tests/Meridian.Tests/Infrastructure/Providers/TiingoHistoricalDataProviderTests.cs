using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Meridian.Core.Exceptions;
using Meridian.Infrastructure.Adapters.Tiingo;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Provider-specific Tiingo tests for EOD adjusted bars, token routing, and rate-limit handling.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TiingoHistoricalDataProviderTests
{
    private const string ApiToken = "test-tiingo-token";

    private static readonly string TiingoEodResponse = """
        [
          {
            "date": "2024-01-03T00:00:00.000Z",
            "close": 374.5000,
            "high": 376.2500,
            "low": 371.1000,
            "open": 372.8000,
            "volume": 2180000,
            "adjClose": 187.2500,
            "adjHigh": 188.1250,
            "adjLow": 185.5500,
            "adjOpen": 186.4000,
            "adjVolume": 4360000,
            "divCash": 0.2400,
            "splitFactor": 2.0000
          },
          {
            "date": "2024-01-02T00:00:00.000Z",
            "close": 369.6000,
            "high": 372.0000,
            "low": 367.8000,
            "open": 370.1000,
            "volume": 1930000,
            "adjClose": 184.8000,
            "adjHigh": 186.0000,
            "adjLow": 183.9000,
            "adjOpen": 185.0500,
            "adjVolume": 3860000,
            "divCash": 0.0000,
            "splitFactor": 2.0000
          },
          {
            "date": "2024-01-01T00:00:00.000Z",
            "close": 368.0000,
            "high": 369.0000,
            "low": 367.0000,
            "open": 0.0000,
            "volume": 900000,
            "adjClose": 184.0000,
            "adjHigh": 184.5000,
            "adjLow": 183.5000,
            "adjOpen": 0.0000,
            "adjVolume": 1800000,
            "divCash": 0.0000,
            "splitFactor": 2.0000
          }
        ]
        """;

    [Fact]
    public async Task GetAdjustedDailyBarsAsync_AdjustedEodPayload_MapsCorporateActionsAndRequestShape()
    {
        HttpRequestMessage? observedRequest = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return JsonResponse(TiingoEodResponse);
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new TiingoHistoricalDataProvider(ApiToken, httpClient);
        var from = new DateOnly(2024, 1, 2);
        var to = new DateOnly(2024, 1, 3);

        var bars = await provider.GetAdjustedDailyBarsAsync("brk.b", from, to, cts.Token);

        bars.Should().HaveCount(2);
        bars.Should().BeInAscendingOrder(b => b.SessionDate);
        bars.Select(b => b.Symbol).Should().OnlyContain(symbol => symbol == "BRK.B");

        var dividendAndSplitBar = bars.Single(b => b.SessionDate == new DateOnly(2024, 1, 3));
        dividendAndSplitBar.Open.Should().Be(372.8000m);
        dividendAndSplitBar.AdjustedOpen.Should().Be(186.4000m);
        dividendAndSplitBar.AdjustedClose.Should().Be(187.2500m);
        dividendAndSplitBar.AdjustedVolume.Should().Be(4_360_000);
        dividendAndSplitBar.DividendAmount.Should().Be(0.2400m);
        dividendAndSplitBar.SplitFactor.Should().Be(2.0000m);
        dividendAndSplitBar.Source.Should().Be("tiingo");

        observedRequest.Should().NotBeNull();
        observedRequest!.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.AbsolutePath.Should().Be("/tiingo/daily/BRK-B/prices");
        observedRequest.RequestUri.GetComponents(UriComponents.Query, UriFormat.Unescaped)
            .Should().Contain("startDate=2024-01-02")
            .And.Contain("endDate=2024-01-03")
            .And.Contain($"token={ApiToken}");
        observedRequest.Headers.Authorization.Should().NotBeNull();
        observedRequest.Headers.Authorization!.Scheme.Should().Be("Token");
        observedRequest.Headers.Authorization.Parameter.Should().Be(ApiToken);
    }

    [Fact]
    public async Task GetDailyBarsAsync_AdjustedEodPayload_PrefersAdjustedPricesForDownstreamBackfill()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(TiingoEodResponse));
        using var httpClient = new HttpClient(handler);
        using var provider = new TiingoHistoricalDataProvider(ApiToken, httpClient);

        var bars = await provider.GetDailyBarsAsync(
            "BRK.B",
            new DateOnly(2024, 1, 3),
            new DateOnly(2024, 1, 3),
            cts.Token);

        bars.Should().ContainSingle();
        var bar = bars[0];
        bar.Open.Should().Be(186.4000m);
        bar.High.Should().Be(188.1250m);
        bar.Low.Should().Be(185.5500m);
        bar.Close.Should().Be(187.2500m);
        bar.Volume.Should().Be(4_360_000);
    }

    [Fact]
    public async Task GetAdjustedDailyBarsAsync_WhenTiingoRateLimits_ThrowsAndRecordsRetryAfter()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)429)
            {
                Content = new StringContent("rate limit", Encoding.UTF8, "text/plain")
            };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(45));
            return response;
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new TiingoHistoricalDataProvider(ApiToken, httpClient);

        Func<Task> act = () => provider.GetAdjustedDailyBarsAsync("AAPL", null, null, cts.Token);

        var exception = await act.Should().ThrowAsync<RateLimitException>();
        exception.Which.Provider.Should().Be("tiingo");
        exception.Which.Symbol.Should().Be("AAPL");
        exception.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(45));
        provider.GetRateLimitInfo().IsLimited.Should().BeTrue();
    }

    [Fact]
    public async Task IsAvailableAsync_WhenTokenMissing_ReturnsFalseWithoutHttpCall()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("Missing token health check should not call Tiingo."));
        using var httpClient = new HttpClient(handler);
        using var provider = new TiingoHistoricalDataProvider(string.Empty, httpClient);

        var available = await provider.IsAvailableAsync(cts.Token);

        available.Should().BeFalse();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task IsAvailableAsync_WithToken_UsesMetadataEndpointAndAuthorizationHeader()
    {
        HttpRequestMessage? observedRequest = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return JsonResponse("""{ "ticker": "AAPL", "name": "Apple Inc" }""");
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new TiingoHistoricalDataProvider(ApiToken, httpClient);

        var available = await provider.IsAvailableAsync(cts.Token);

        available.Should().BeTrue();
        observedRequest.Should().NotBeNull();
        observedRequest!.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.AbsolutePath.Should().Be("/tiingo/daily/AAPL");
        observedRequest.RequestUri.GetComponents(UriComponents.Query, UriFormat.Unescaped)
            .Should().Contain($"token={ApiToken}");
        observedRequest.Headers.Authorization.Should().NotBeNull();
        observedRequest.Headers.Authorization!.Scheme.Should().Be("Token");
        observedRequest.Headers.Authorization.Parameter.Should().Be(ApiToken);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}

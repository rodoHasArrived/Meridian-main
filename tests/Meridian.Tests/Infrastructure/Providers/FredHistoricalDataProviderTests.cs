using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Meridian.Core.Exceptions;
using Meridian.Infrastructure.Adapters.Fred;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Provider-specific FRED tests for economic-series backfill, GDP health probing, and rate-limit handling.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FredHistoricalDataProviderTests
{
    private const string ApiKey = "test-fred-key";

    private static readonly string ObservationsResponse = """
        {
          "observations": [
            { "date": "2024-01-04", "value": "3.8" },
            { "date": "2024-01-03", "value": "3.7" },
            { "date": "2024-01-02", "value": "3.9" },
            { "date": "2024-01-02", "value": "." },
            { "date": "2024-01-01", "value": "not-a-number" }
          ]
        }
        """;

    [Fact]
    public async Task GetDailyBarsAsync_EconomicSeriesPayload_MapsSyntheticBarsAndRequestShape()
    {
        HttpRequestMessage? observedRequest = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return JsonResponse(ObservationsResponse);
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new FredHistoricalDataProvider(ApiKey, httpClient);
        var from = new DateOnly(2024, 1, 2);
        var to = new DateOnly(2024, 1, 3);

        var bars = await provider.GetDailyBarsAsync("unrate", from, to, cts.Token);

        bars.Should().HaveCount(2);
        bars.Should().BeInAscendingOrder(b => b.SessionDate);
        bars.Select(b => b.Symbol).Should().OnlyContain(symbol => symbol == "UNRATE");
        bars.Select(b => b.Source).Should().OnlyContain(source => source == "fred");
        bars.Select(b => b.Volume).Should().OnlyContain(volume => volume == 0);

        bars[0].SessionDate.Should().Be(from);
        bars[0].Open.Should().Be(3.9m);
        bars[0].High.Should().Be(3.9m);
        bars[0].Low.Should().Be(3.9m);
        bars[0].Close.Should().Be(3.9m);
        bars[0].SequenceNumber.Should().Be(from.DayNumber);

        bars[1].SessionDate.Should().Be(to);
        bars[1].Open.Should().Be(3.7m);
        bars[1].High.Should().Be(3.7m);
        bars[1].Low.Should().Be(3.7m);
        bars[1].Close.Should().Be(3.7m);

        observedRequest.Should().NotBeNull();
        observedRequest!.Method.Should().Be(HttpMethod.Get);
        observedRequest.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.AbsolutePath.Should().Be("/fred/series/observations");
        observedRequest.RequestUri.GetComponents(UriComponents.Query, UriFormat.Unescaped)
            .Should().Contain("series_id=UNRATE")
            .And.Contain($"api_key={ApiKey}")
            .And.Contain("file_type=json")
            .And.Contain("sort_order=asc")
            .And.Contain("observation_start=2024-01-02")
            .And.Contain("observation_end=2024-01-03");
        observedRequest.Headers.Authorization.Should().BeNull();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetDailyBarsAsync_WhenApiKeyMissing_ThrowsWithoutHttpCall()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("Missing FRED credentials should not call HTTP."));
        using var httpClient = new HttpClient(handler);
        using var provider = new FredHistoricalDataProvider(string.Empty, httpClient);

        var available = await provider.IsAvailableAsync(CancellationToken.None);
        Func<Task> act = () => provider.GetDailyBarsAsync("UNRATE", null, null, CancellationToken.None);

        available.Should().BeFalse();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*FRED API key*");
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task IsAvailableAsync_WithApiKey_UsesGdpProbeRequest()
    {
        HttpRequestMessage? observedRequest = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return JsonResponse("""{ "observations": [{ "date": "2024-01-01", "value": "27500.0" }] }""");
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new FredHistoricalDataProvider(ApiKey, httpClient);

        var available = await provider.IsAvailableAsync(cts.Token);

        available.Should().BeTrue();
        observedRequest.Should().NotBeNull();
        observedRequest!.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.AbsolutePath.Should().Be("/fred/series/observations");
        observedRequest.RequestUri.GetComponents(UriComponents.Query, UriFormat.Unescaped)
            .Should().Contain("series_id=GDP")
            .And.Contain($"api_key={ApiKey}")
            .And.Contain("file_type=json")
            .And.Contain("limit=1");
        observedRequest.Headers.Authorization.Should().BeNull();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetDailyBarsAsync_WhenHttp429_ThrowsAndRecordsRetryAfter()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)429)
            {
                Content = new StringContent("Rate limited", Encoding.UTF8, "text/plain")
            };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(20));
            return response;
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new FredHistoricalDataProvider(ApiKey, httpClient);

        Func<Task> act = () => provider.GetDailyBarsAsync("UNRATE", null, null, cts.Token);

        var exception = await act.Should().ThrowAsync<RateLimitException>();
        exception.Which.Provider.Should().Be("fred");
        exception.Which.Symbol.Should().Be("UNRATE");
        exception.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(20));
        var rateLimitInfo = provider.GetRateLimitInfo();
        rateLimitInfo.IsLimited.Should().BeTrue();
        rateLimitInfo.RetryAfter.Should().NotBeNull();
        rateLimitInfo.RetryAfter!.Value.Should().BeCloseTo(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(1));
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}

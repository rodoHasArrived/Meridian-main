using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Meridian.Core.Exceptions;
using Meridian.Infrastructure.Adapters.Stooq;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Provider-specific Stooq tests for no-credential EOD backfill, request shape, and rate-limit handling.
/// </summary>
[Trait("Category", "Unit")]
public sealed class StooqHistoricalDataProviderTests
{
    private static readonly string EodCsvResponse =
        "Date,Open,High,Low,Close,Volume\r\n" +
        "2024-01-04,378.40,380.00,376.90,379.75,2400000\r\n" +
        "2024-01-03,372.80,376.25,371.10,374.50,2180000\r\n" +
        "2024-01-02,370.10,372.00,367.80,369.60,1930000\r\n" +
        "2024-01-02,0.00,372.00,367.80,369.60,900000\r\n";

    [Fact]
    public async Task GetDailyBarsAsync_EodCsvPayload_MapsFiltersAndRequestShape()
    {
        HttpRequestMessage? observedRequest = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return CsvResponse(EodCsvResponse);
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new StooqHistoricalDataProvider(httpClient);
        var from = new DateOnly(2024, 1, 2);
        var to = new DateOnly(2024, 1, 3);

        var bars = await provider.GetDailyBarsAsync("brk.b", from, to, cts.Token);

        bars.Should().HaveCount(2);
        bars.Should().BeInAscendingOrder(b => b.SessionDate);
        bars.Select(b => b.Symbol).Should().OnlyContain(symbol => symbol == "BRK.B");
        bars.Select(b => b.Source).Should().OnlyContain(source => source == "stooq");

        bars[0].SessionDate.Should().Be(from);
        bars[0].Open.Should().Be(370.10m);
        bars[0].High.Should().Be(372.00m);
        bars[0].Low.Should().Be(367.80m);
        bars[0].Close.Should().Be(369.60m);
        bars[0].Volume.Should().Be(1_930_000);
        bars[0].SequenceNumber.Should().Be(from.DayNumber);

        bars[1].SessionDate.Should().Be(to);
        bars[1].Open.Should().Be(372.80m);
        bars[1].High.Should().Be(376.25m);
        bars[1].Low.Should().Be(371.10m);
        bars[1].Close.Should().Be(374.50m);
        bars[1].Volume.Should().Be(2_180_000);

        observedRequest.Should().NotBeNull();
        observedRequest!.Method.Should().Be(HttpMethod.Get);
        observedRequest.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.Host.Should().Be("stooq.pl");
        observedRequest.RequestUri.AbsolutePath.Should().Be("/q/d/l/");
        observedRequest.RequestUri.GetComponents(UriComponents.Query, UriFormat.Unescaped)
            .Should().Contain("s=brk-b.us")
            .And.Contain("i=d");
        observedRequest.Headers.Authorization.Should().BeNull();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task IsAvailableAsync_NoCredentialRequired_ReturnsTrueWithoutHttpCall()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("Stooq availability should not call HTTP."));
        using var httpClient = new HttpClient(handler);
        using var provider = new StooqHistoricalDataProvider(httpClient);

        var available = await provider.IsAvailableAsync(cts.Token);

        available.Should().BeTrue();
        handler.CallCount.Should().Be(0);
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
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return response;
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new StooqHistoricalDataProvider(httpClient);

        Func<Task> act = () => provider.GetDailyBarsAsync("SPY", null, null, cts.Token);

        var exception = await act.Should().ThrowAsync<RateLimitException>();
        exception.Which.Provider.Should().Be("stooq");
        exception.Which.Symbol.Should().Be("SPY");
        exception.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
        var rateLimitInfo = provider.GetRateLimitInfo();
        rateLimitInfo.IsLimited.Should().BeTrue();
        rateLimitInfo.RetryAfter.Should().NotBeNull();
        rateLimitInfo.RetryAfter!.Value.Should().BeCloseTo(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GetDailyBarsAsync_AfterDispose_ThrowsWithoutHttpCall()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("Disposed Stooq provider should not call HTTP."));
        using var httpClient = new HttpClient(handler);
        var provider = new StooqHistoricalDataProvider(httpClient);
        provider.Dispose();

        Func<Task> act = () => provider.GetDailyBarsAsync("SPY", null, null, CancellationToken.None);

        await act.Should().ThrowAsync<ObjectDisposedException>();
        handler.CallCount.Should().Be(0);
    }

    private static HttpResponseMessage CsvResponse(string csv)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(csv, Encoding.UTF8, "text/csv")
        };
    }
}

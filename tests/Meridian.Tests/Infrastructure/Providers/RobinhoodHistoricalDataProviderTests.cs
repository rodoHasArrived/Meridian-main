using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Infrastructure.Adapters.Robinhood;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Unit tests for RobinhoodHistoricalDataProvider covering success path, error handling,
/// cancellation, and empty responses.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RobinhoodHistoricalDataProviderTests
{
    private static readonly string OkResponse5Year = """
        {
          "results": [
            {
              "begins_at": "2023-01-03T14:30:00Z",
              "open_price": "125.00",
              "close_price": "130.00",
              "high_price": "132.50",
              "low_price": "124.00",
              "volume": "80000000",
              "session": "reg",
              "interpolated": false
            },
            {
              "begins_at": "2023-01-04T14:30:00Z",
              "open_price": "130.00",
              "close_price": "128.50",
              "high_price": "131.00",
              "low_price": "127.00",
              "volume": "75000000",
              "session": "reg",
              "interpolated": false
            }
          ],
          "symbol": "AAPL",
          "interval": "day",
          "span": "5year"
        }
        """;

    private static readonly string EmptyResponse = """
        {
          "results": [],
          "symbol": "UNKNOWN",
          "interval": "day",
          "span": "5year"
        }
        """;

    [Fact]
    public async Task GetDailyBarsAsync_WithValidResponse_ParsesAllBars()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(OkResponse5Year, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var provider = new RobinhoodHistoricalDataProvider(httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("AAPL", null, null, CancellationToken.None);

        bars.Should().HaveCount(2);
        bars.Should().BeInAscendingOrder(b => b.SessionDate);

        var first = bars[0];
        first.Symbol.Should().Be("AAPL");
        first.Open.Should().Be(125.00m);
        first.High.Should().Be(132.50m);
        first.Low.Should().Be(124.00m);
        first.Close.Should().Be(130.00m);
        first.Volume.Should().Be(80_000_000);
        first.Source.Should().Be("robinhood");
    }

    [Fact]
    public async Task GetDailyBarsAsync_WithEmptyResults_ReturnsEmptyList()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(EmptyResponse, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var provider = new RobinhoodHistoricalDataProvider(httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("UNKNOWN", null, null, CancellationToken.None);

        bars.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDailyBarsAsync_WithNotFoundResponse_ReturnsEmptyList()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound));
        using var httpClient = new HttpClient(handler);
        using var provider = new RobinhoodHistoricalDataProvider(httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("INVALID", null, null, CancellationToken.None);

        bars.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDailyBarsAsync_WithDateFilter_FiltersCorrectly()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(OkResponse5Year, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var provider = new RobinhoodHistoricalDataProvider(httpClient: httpClient);

        var from = new DateOnly(2023, 1, 4);
        var bars = await provider.GetDailyBarsAsync("AAPL", from, null, CancellationToken.None);

        bars.Should().HaveCount(1);
        bars[0].SessionDate.Should().Be(new DateOnly(2023, 1, 4));
    }

    [Fact]
    public async Task GetDailyBarsAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        using var provider = new RobinhoodHistoricalDataProvider(httpClient: httpClient);

        var act = async () => await provider.GetDailyBarsAsync("AAPL", null, null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Name_ReturnsRobinhood()
    {
        using var provider = new RobinhoodHistoricalDataProvider();
        provider.Name.Should().Be("robinhood");
    }

    [Fact]
    public void Priority_Returns25()
    {
        using var provider = new RobinhoodHistoricalDataProvider();
        provider.Priority.Should().Be(25);
    }
}

using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Infrastructure.Adapters.Robinhood;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Unit tests for RobinhoodSymbolSearchProvider covering success, empty, error, and cancellation paths.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RobinhoodSymbolSearchProviderTests
{
    private static readonly string OkResponse = """
        {
          "results": [
            {
              "symbol": "AAPL",
              "name": "Apple Inc.",
              "type": "stock",
              "market": "https://api.robinhood.com/markets/XNAS/",
              "id": "450dfc6d-5510-4d40-abfb-f633b7d9be3e",
              "url": "https://api.robinhood.com/instruments/450dfc6d-5510-4d40-abfb-f633b7d9be3e/",
              "tradeable": true,
              "country": "US"
            }
          ],
          "next": null
        }
        """;

    private static readonly string EmptyResponse = """
        {
          "results": [],
          "next": null
        }
        """;

    [Fact]
    public async Task SearchAsync_WithValidResponse_ReturnsResults()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(OkResponse, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var provider = new RobinhoodSymbolSearchProvider(httpClient: httpClient);

        var results = await provider.SearchAsync("AAPL", 10, CancellationToken.None);

        results.Should().HaveCount(1);
        var first = results[0];
        first.Symbol.Should().Be("AAPL");
        first.Name.Should().Be("Apple Inc.");
        first.Exchange.Should().Be("NASDAQ");
        first.Source.Should().Be("robinhood");
    }

    [Fact]
    public async Task SearchAsync_WithEmptyResponse_ReturnsEmptyList()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(EmptyResponse, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var provider = new RobinhoodSymbolSearchProvider(httpClient: httpClient);

        var results = await provider.SearchAsync("UNKNOWN", 10, CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WithHttpError_ReturnsEmptyList()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var httpClient = new HttpClient(handler);
        using var provider = new RobinhoodSymbolSearchProvider(httpClient: httpClient);

        var results = await provider.SearchAsync("AAPL", 10, CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WithCancellation_ReturnsEmpty()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        using var provider = new RobinhoodSymbolSearchProvider(httpClient: httpClient);

        // BaseSymbolSearchProvider catches all exceptions (including cancellation) and returns empty
        var results = await provider.SearchAsync("AAPL", 10, cts.Token);

        results.Should().BeEmpty();
    }

    [Fact]
    public void Name_ReturnsRobinhood()
    {
        using var provider = new RobinhoodSymbolSearchProvider();
        provider.Name.Should().Be("robinhood");
    }

    [Fact]
    public void Priority_Returns25()
    {
        using var provider = new RobinhoodSymbolSearchProvider();
        provider.Priority.Should().Be(25);
    }
}

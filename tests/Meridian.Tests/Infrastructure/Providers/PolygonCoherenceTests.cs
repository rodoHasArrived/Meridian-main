using System.Net;
using FluentAssertions;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class PolygonCoherenceTests
{
    [Fact]
    public async Task OptionsProvider_WithShortApiKey_DoesNotIssueHttpRequests()
    {
        var handler = new CountingHandler();
        using var http = new HttpClient(handler);
        var factory = new TestHttpClientFactory(http);
        var provider = new PolygonOptionsChainProvider(factory, apiKey: "short-key");

        var expirations = await provider.GetExpirationsAsync("AAPL");

        expirations.Should().BeEmpty();
        handler.RequestCount.Should().Be(0);
    }

    [Fact]
    public void StreamingProvider_UsesDocumentedFreeTierRateLimits()
    {
        var publisher = new TestMarketEventPublisher();
        var trades = new TradeDataCollector(publisher, null);
        var quotes = new QuoteCollector(publisher);
        var client = new PolygonMarketDataClient(publisher, trades, quotes);

        client.ProviderCapabilities.MaxRequestsPerWindow.Should().Be(5);
        client.ProviderCapabilities.RateLimitWindow.Should().Be(TimeSpan.FromMinutes(1));
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"results\":[]}")
            });
        }
    }

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}

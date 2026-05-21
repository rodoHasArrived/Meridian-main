using System.Net;
using FluentAssertions;
using Meridian.Infrastructure.Adapters.Alpaca;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class AlpacaHistoricalDataProviderTests
{
    [Theory]
    [InlineData("iex")]
    [InlineData("sip")]
    [InlineData("delayed_sip")]
    [InlineData("boats")]
    [InlineData("overnight")]
    [InlineData("otc")]
    public void Constructor_DocumentedFeed_DoesNotThrow(string feed)
    {
        var act = () => new AlpacaHistoricalDataProvider(
            keyId: "test-key",
            secretKey: "test-secret",
            feed: feed,
            httpClient: new HttpClient());

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_ExposesFullCapabilitySurface()
    {
        var sut = new AlpacaHistoricalDataProvider(
            keyId: "test-key",
            secretKey: "test-secret",
            httpClient: new HttpClient());

        sut.Capabilities.Should().Be(Meridian.Infrastructure.Adapters.Core.HistoricalDataCapabilities.FullFeatured);
        sut.SupportsAdjustedPrices.Should().BeTrue();
        sut.SupportsIntraday.Should().BeTrue();
        sut.SupportsQuotes.Should().BeTrue();
        sut.SupportsTrades.Should().BeTrue();
        sut.SupportsAuctions.Should().BeTrue();
    }

    [Fact]
    public async Task GetAdjustedDailyBarsAsync_TracksRequestCountDeterministically()
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"bars\":[{\"t\":\"2024-01-03T00:00:00Z\",\"o\":10,\"h\":11,\"l\":9,\"c\":10.5,\"v\":1000}],\"next_page_token\":null}")
        });
        using var httpClient = new HttpClient(handler);
        var sut = new AlpacaHistoricalDataProvider(
            keyId: "test-key",
            secretKey: "test-secret",
            rateLimitPerMinute: 3,
            httpClient: httpClient);

        var bars = await sut.GetAdjustedDailyBarsAsync("AAPL", new DateOnly(2024, 1, 3), new DateOnly(2024, 1, 3));

        bars.Should().HaveCount(1);
        sut.GetRateLimitInfo().RequestCount.Should().Be(1);
        handler.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task IsAvailableAsync_ReturnsFalseOnDegradedResponse()
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        using var httpClient = new HttpClient(handler);
        var sut = new AlpacaHistoricalDataProvider(
            keyId: "test-key",
            secretKey: "test-secret",
            httpClient: httpClient);

        var available = await sut.IsAvailableAsync();

        available.Should().BeFalse();
        sut.GetRateLimitInfo().RequestCount.Should().Be(1);
    }

    [Theory]
    [InlineData("spin-off")]
    [InlineData("split,spin-off")]
    [InlineData("split,dividend,spin-off")]
    public void Constructor_DocumentedAdjustment_DoesNotThrow(string adjustment)
    {
        var act = () => new AlpacaHistoricalDataProvider(
            keyId: "test-key",
            secretKey: "test-secret",
            adjustment: adjustment,
            httpClient: new HttpClient());

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("raw,split")]
    [InlineData("all,spin-off")]
    [InlineData("split,unknown")]
    public void Constructor_InvalidAdjustment_ThrowsArgumentException(string adjustment)
    {
        var act = () => new AlpacaHistoricalDataProvider(
            keyId: "test-key",
            secretKey: "test-secret",
            adjustment: adjustment,
            httpClient: new HttpClient());

        act.Should().Throw<ArgumentException>();
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(_responseFactory(request));
        }
    }
}

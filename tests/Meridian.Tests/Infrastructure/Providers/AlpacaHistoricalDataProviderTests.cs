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
}

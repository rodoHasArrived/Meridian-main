using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Domain.Collectors;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Resilience;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests.Providers;

public sealed class AlpacaStreamDiagnosticsTests
{
    [Fact]
    public void ConnectionDiagnostics_ReportsEachAssetClassAndItsEntitlement()
    {
        var publisher = new TestMarketEventPublisher();
        var client = new AlpacaMarketDataClient(
            new TradeDataCollector(publisher),
            new QuoteCollector(publisher),
            new AlpacaOptions("key", "secret", Feed: "iex", OptionsFeed: "indicative"));

        var streams = client.GetConnectionDiagnosticsSnapshot().Streams;

        streams.Should().NotBeNull();
        streams!.Should().ContainSingle(stream =>
            stream.AssetClass == MarketDataAssetClass.Equities &&
            stream.Feed == "iex" &&
            stream.Entitlement == "iex" &&
            stream.IsDegraded);
        streams.Should().ContainSingle(stream =>
            stream.AssetClass == MarketDataAssetClass.Options &&
            stream.Feed == "indicative" &&
            stream.Entitlement == "indicative" &&
            stream.IsDegraded);
        streams.Should().ContainSingle(stream => stream.AssetClass == MarketDataAssetClass.Crypto);
        streams.Should().ContainSingle(stream => stream.AssetClass == MarketDataAssetClass.News);
    }

    [Fact]
    public void ConnectionDiagnostics_SipAndOpraDoNotClaimIndicativeDegradation()
    {
        var publisher = new TestMarketEventPublisher();
        var client = new AlpacaMarketDataClient(
            new TradeDataCollector(publisher),
            new QuoteCollector(publisher),
            new AlpacaOptions("key", "secret", Feed: "sip", OptionsFeed: "opra"));

        var streams = client.GetConnectionDiagnosticsSnapshot().Streams!;

        var equities = streams.Single(stream => stream.AssetClass == MarketDataAssetClass.Equities);
        equities.IsDegraded.Should().BeFalse();
        equities.Entitlement.Should().Be("sip");

        var options = streams.Single(stream => stream.AssetClass == MarketDataAssetClass.Options);
        options.IsDegraded.Should().BeFalse();
        options.Entitlement.Should().Be("opra");
    }
}

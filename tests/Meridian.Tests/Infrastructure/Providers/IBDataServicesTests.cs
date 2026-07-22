using FluentAssertions;
using Meridian.Contracts.Configuration;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class IBDataServicesTests
{
    [Fact]
    public void RequestContractDetails_TracksExchangeAndMarketRuleLineage()
    {
        var transport = new RecordingTransport();
        var services = new IBDataServices(transport);

        var requestId = services.RequestContractDetails(new SymbolConfig("AAPL", Exchange: "NASDAQ"));
        services.RecordContractMetadata(requestId, "NASDAQ", "26,31");
        services.RecordMarketDataType(requestId, 3);

        transport.Calls.Should().ContainSingle().Which.Should().Be($"contract:{requestId}:AAPL");
        var lineage = services.GetLineage().Should().ContainSingle().Which;
        lineage.Service.Should().Be("contract-details");
        lineage.Symbol.Should().Be("AAPL");
        lineage.Exchange.Should().Be("NASDAQ");
        lineage.MarketRuleIds.Should().Be("26,31");
        lineage.Availability.Should().Be(IBMarketDataAvailability.Delayed);
        lineage.IsDelayed.Should().BeTrue();
        lineage.Status.Should().Be("market-data-type");
    }

    [Fact]
    public void RequestOptionChain_RequiresUnderlyingContractIdentityAndTracksSubscriptionEvidence()
    {
        var transport = new RecordingTransport();
        var services = new IBDataServices(transport);

        var requestId = services.RequestOptionChain(new SymbolConfig("SPY", ConId: 756733));

        transport.Calls.Should().ContainSingle().Which.Should().Be($"option-chain:{requestId}:SPY");
        services.GetLineage().Single().Should().Match<IBDataLineage>(x =>
            x.Service == "option-chain" && x.Symbol == "SPY" && x.Status == "requested" && x.Availability == IBMarketDataAvailability.Unknown);
    }

    [Fact]
    public void RequestHistoricalNews_RejectsInvalidEntitlementRequestBeforeCallingTransport()
    {
        var transport = new RecordingTransport();
        var services = new IBDataServices(transport);

        var action = () => services.RequestHistoricalNews(0, "BRFG", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow);

        action.Should().Throw<ArgumentOutOfRangeException>();
        transport.Calls.Should().BeEmpty();
        services.GetLineage().Should().BeEmpty();
    }

    [Fact]
    public void SubscribeDividendEarnings_RetainsTheGenericTickSubscriptionAsLineage()
    {
        var transport = new RecordingTransport();
        var services = new IBDataServices(transport);

        var requestId = services.SubscribeDividendEarnings(new SymbolConfig("AAPL", Exchange: "NASDAQ"));

        transport.Calls.Should().ContainSingle().Which.Should().Be($"dividend-earnings:{requestId}:AAPL");
        services.GetLineage().Single().Subscription.Should().Be("456,258");
    }

    private sealed class RecordingTransport : IIBDataServiceTransport
    {
        public List<string> Calls { get; } = [];
        public void RequestScanner(int requestId, IBScannerRequest request) => Calls.Add($"scanner:{requestId}:{request.ScanCode}");
        public void RequestContractDetails(int requestId, SymbolConfig contract) => Calls.Add($"contract:{requestId}:{contract.Symbol}");
        public void RequestOptionChain(int requestId, SymbolConfig underlying) => Calls.Add($"option-chain:{requestId}:{underlying.Symbol}");
        public void RequestHistoricalNews(int requestId, int conId, string providerCodes, DateTimeOffset start, DateTimeOffset end, int maximumResults) => Calls.Add($"historical-news:{requestId}:{conId}");
        public void RequestNewsArticle(int requestId, string providerCode, string articleId) => Calls.Add($"news-article:{requestId}:{articleId}");
        public void RequestFundamentals(int requestId, SymbolConfig contract, string reportType) => Calls.Add($"fundamentals:{requestId}:{contract.Symbol}");
        public void RequestDividendEarnings(int requestId, SymbolConfig contract) => Calls.Add($"dividend-earnings:{requestId}:{contract.Symbol}");
        public void RequestTickByTick(int requestId, SymbolConfig contract, string tickType, int numberOfTicks, bool ignoreSize) => Calls.Add($"tick-by-tick:{requestId}:{contract.Symbol}");
        public void RequestPnl(int requestId, string account, string? modelCode) => Calls.Add($"pnl:{requestId}:{account}");
        public void RequestMarketRule(int requestId, int marketRuleId) => Calls.Add($"market-rule:{requestId}:{marketRuleId}");
        public void RequestDepthExchanges(int requestId) => Calls.Add($"depth-exchanges:{requestId}");
    }
}

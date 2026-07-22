using FluentAssertions;
using Meridian.Contracts.Configuration;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.ProviderSdk;

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

    [Fact]
    public void CallbackCorrelation_OnlyUpdatesItsOriginatingRequest()
    {
        var services = new IBDataServices(new RecordingTransport());
        var first = services.RequestScanner(new IBScannerRequest("STK", "STK.US.MAJOR", "TOP_PERC_GAIN"));
        var second = services.RequestScanner(new IBScannerRequest("STK", "STK.US.MAJOR", "HOT_BY_VOLUME"));

        services.RecordScannerResult(second, new ProviderScannerResult(0, "AAPL", "NASDAQ", "265598", null, null, null, null));
        services.CompleteRequest(second);

        services.GetRequests().Single(request => request.RequestId == first).Status.Should().Be(ProviderDataRequestStatus.Requested);
        var correlated = services.GetRequests().Single(request => request.RequestId == second);
        correlated.Status.Should().Be(ProviderDataRequestStatus.Completed);
        correlated.ScannerResults.Should().ContainSingle().Which.Symbol.Should().Be("AAPL");
    }

    [Fact]
    public void Cancellation_MarksOnlyTheRequestedStreamAndCallsTransportCancellation()
    {
        var transport = new RecordingTransport();
        var services = new IBDataServices(transport);
        var requestId = services.SubscribePnl("DU123", "model-a");

        services.CancelRequest(requestId, CancellationToken.None);

        services.GetRequests().Single().Status.Should().Be(ProviderDataRequestStatus.Cancelled);
        transport.Calls.Should().Contain($"cancel:{requestId}:pnl");
    }

    [Fact]
    public void PnlCallbacks_RetainAccountAndModelIsolation()
    {
        var services = new IBDataServices(new RecordingTransport());
        var first = services.SubscribePnl("DU111", "growth");
        var second = services.SubscribePnl("DU222", "income");

        services.RecordPnl(first, new ProviderAccountPnl("DU111", "growth", 10m, 4m, 6m));
        services.RecordPnl(second, new ProviderAccountPnl("DU222", "income", 20m, 7m, 13m));

        services.GetRequests().Single(x => x.RequestId == first).Pnl!.AccountId.Should().Be("DU111");
        services.GetRequests().Single(x => x.RequestId == second).Pnl!.ModelAccountId.Should().Be("income");
    }

    [Fact]
    public void UnavailableMarketDataPermission_RejectsCorrelatedRequestWithoutClaimingLiveData()
    {
        var services = new IBDataServices(new RecordingTransport());
        var requestId = services.SubscribeRealTimeBars(new IBRealTimeBarRequest(new SymbolConfig("AAPL")));

        services.RejectRequest(requestId, "354", "Requested market data is not subscribed.");

        var request = services.GetRequests().Single();
        request.Status.Should().Be(ProviderDataRequestStatus.Rejected);
        request.ErrorCode.Should().Be("354");
        request.RealTimeBars.Should().BeNull();
    }

    [Fact]
    public void PacingViolation_RejectsOnlyTheCorrelatedHistoricalTickRequest()
    {
        var services = new IBDataServices(new RecordingTransport());
        var first = services.RequestHistoricalTicks(new IBHistoricalTickRequest(new SymbolConfig("AAPL"), null, DateTimeOffset.UtcNow, 100));
        var second = services.RequestHistoricalTicks(new IBHistoricalTickRequest(new SymbolConfig("MSFT"), null, DateTimeOffset.UtcNow, 100));

        services.RejectRequest(first, IBApiLimits.ErrorPacingViolation.ToString(), "Historical data query pacing violation.");

        services.GetRequests().Single(request => request.RequestId == first).Status.Should().Be(ProviderDataRequestStatus.Rejected);
        services.GetRequests().Single(request => request.RequestId == second).Status.Should().Be(ProviderDataRequestStatus.Requested);
    }

    [Fact]
    public void CallbackSource_ProjectsEveryRichDataPayloadAndTerminalCallback()
    {
        var transport = new CallbackTransport();
        using var services = new IBDataServices(transport);
        var contract = services.RequestContractDetails(new SymbolConfig("AAPL"));
        var chain = services.RequestOptionChain(new SymbolConfig("AAPL", ConId: 265598));
        var headlines = services.RequestHistoricalNews(265598, "BRFG", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow);
        var article = services.RequestNewsArticle("BRFG", "article-1");
        var fundamentals = services.RequestFundamentals(new SymbolConfig("AAPL"), "ReportsFinSummary");
        var ticks = services.SubscribeTickByTick(new SymbolConfig("AAPL"));
        var depth = services.RequestDepthExchanges();
        var dividends = services.SubscribeDividendEarnings(new SymbolConfig("AAPL"));
        var scanner = services.RequestScanner(new IBScannerRequest("STK", "STK.US.MAJOR", "TOP_PERC_GAIN"));
        var pnl = services.SubscribePnl("DU123");
        var rules = services.RequestMarketRule(26);

        transport.RaiseContract(contract, new ProviderContractDetails("265598", "AAPL", null, "STK", "NASDAQ", null, "USD", null, null, null, null, "26", .01m, "Apple", null, null, null, null, null, null));
        transport.RaiseChain(chain, new ProviderOptionChainDefinition("SMART", "265598", "AAPL", "100", [new DateOnly(2027, 1, 15)], [250m]));
        transport.RaiseHeadline(headlines, new ProviderNewsHeadline(DateTimeOffset.UtcNow, "BRFG", "headline-1", "Apple headline"));
        transport.RaiseArticle(article, new ProviderNewsArticle(0, "article text"));
        transport.RaiseFundamental(fundamentals, new ProviderFundamentalReport("<Report />"));
        transport.RaiseTick(ticks, new ProviderTickByTickObservation(DateTimeOffset.UtcNow, "last", 200m, 10m));
        transport.RaiseDepth(depth, [new ProviderDepthExchangeDescription("NASDAQ", "STK", "NASDAQ", "Deep", false)]);
        transport.RaiseDividend(dividends, new ProviderDividendEarnings(1m, 1.1m, new DateOnly(2027, 2, 1), .30m, 7m, 30m));
        transport.RaiseScanner(scanner, new ProviderScannerResult(0, "AAPL", "NASDAQ", "265598", null, null, null, null));
        transport.RaisePnl(pnl, new ProviderAccountPnl("DU123", null, 1m, 2m, 3m));
        transport.RaiseMarketRule(rules, [new ProviderMarketRuleIncrement(0m, .01m)]);
        transport.RaiseCompleted(contract);
        transport.RaiseRejected(ticks, "354", "Not subscribed");

        services.GetRequests().Single(x => x.RequestId == contract).ContractDetails.Should().ContainSingle();
        services.GetRequests().Single(x => x.RequestId == chain).OptionChainDefinitions.Should().ContainSingle();
        services.GetRequests().Single(x => x.RequestId == headlines).NewsHeadlines.Should().ContainSingle();
        services.GetRequests().Single(x => x.RequestId == article).NewsArticle!.Content.Should().Be("article text");
        services.GetRequests().Single(x => x.RequestId == fundamentals).FundamentalReport!.Content.Should().Be("<Report />");
        services.GetRequests().Single(x => x.RequestId == ticks).Status.Should().Be(ProviderDataRequestStatus.Rejected);
        services.GetRequests().Single(x => x.RequestId == depth).DepthExchanges.Should().ContainSingle();
        services.GetRequests().Single(x => x.RequestId == dividends).DividendEarnings!.NextDividendAmount.Should().Be(.30m);
        services.GetRequests().Single(x => x.RequestId == scanner).ScannerResults.Should().ContainSingle();
        services.GetRequests().Single(x => x.RequestId == pnl).Pnl!.Daily.Should().Be(1m);
        services.GetRequests().Single(x => x.RequestId == rules).MarketRuleIncrements.Should().ContainSingle();
    }

    private class RecordingTransport : IIBDataServiceTransport
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
        public void CancelDataRequest(int requestId, string capability) => Calls.Add($"cancel:{requestId}:{capability}");
    }
    private sealed class CallbackTransport : RecordingTransport, IIBDataCallbackSource
    {
        public event EventHandler<(int RequestId, ProviderContractDetails Details)>? ContractDetailsReceived;
        public event EventHandler<(int RequestId, ProviderOptionChainDefinition Definition)>? OptionChainDefinitionReceived;
        public event EventHandler<(int RequestId, ProviderNewsHeadline Headline)>? HistoricalNewsReceived;
        public event EventHandler<(int RequestId, ProviderNewsArticle Article)>? NewsArticleReceived;
        public event EventHandler<(int RequestId, ProviderFundamentalReport Report)>? FundamentalReportReceived;
        public event EventHandler<(int RequestId, ProviderTickByTickObservation Observation)>? TickByTickReceived;
        public event EventHandler<(int RequestId, IReadOnlyList<ProviderDepthExchangeDescription> Exchanges)>? DepthExchangesReceived;
        public event EventHandler<(int RequestId, ProviderDividendEarnings Payload)>? DividendEarningsReceived;
        public event EventHandler<(int RequestId, ProviderOptionContract Contract)>? OptionContractReceived;
        public event EventHandler<(int RequestId, ProviderScannerResult Result)>? ScannerResultReceived;
        public event EventHandler<(int RequestId, ProviderRealTimeBar Bar)>? RealTimeBarReceived;
        public event EventHandler<(int RequestId, ProviderHistoricalTick Tick, bool Completed)>? HistoricalTickReceived;
        public event EventHandler<(int RequestId, ProviderAccountPnl Pnl)>? PnlReceived;
        public event EventHandler<(int RequestId, IReadOnlyList<ProviderMarketRuleIncrement> Increments)>? MarketRuleReceived;
        public event EventHandler<int>? RequestCompleted;
        public event EventHandler<(int RequestId, string Code, string Message)>? RequestRejected;
        public void RaiseContract(int id, ProviderContractDetails value) => ContractDetailsReceived?.Invoke(this, (id, value));
        public void RaiseChain(int id, ProviderOptionChainDefinition value) => OptionChainDefinitionReceived?.Invoke(this, (id, value));
        public void RaiseHeadline(int id, ProviderNewsHeadline value) => HistoricalNewsReceived?.Invoke(this, (id, value));
        public void RaiseArticle(int id, ProviderNewsArticle value) => NewsArticleReceived?.Invoke(this, (id, value));
        public void RaiseFundamental(int id, ProviderFundamentalReport value) => FundamentalReportReceived?.Invoke(this, (id, value));
        public void RaiseTick(int id, ProviderTickByTickObservation value) => TickByTickReceived?.Invoke(this, (id, value));
        public void RaiseDepth(int id, IReadOnlyList<ProviderDepthExchangeDescription> value) => DepthExchangesReceived?.Invoke(this, (id, value));
        public void RaiseDividend(int id, ProviderDividendEarnings value) => DividendEarningsReceived?.Invoke(this, (id, value));
        public void RaiseScanner(int id, ProviderScannerResult value) => ScannerResultReceived?.Invoke(this, (id, value));
        public void RaisePnl(int id, ProviderAccountPnl value) => PnlReceived?.Invoke(this, (id, value));
        public void RaiseMarketRule(int id, IReadOnlyList<ProviderMarketRuleIncrement> value) => MarketRuleReceived?.Invoke(this, (id, value));
        public void RaiseCompleted(int id) => RequestCompleted?.Invoke(this, id);
        public void RaiseRejected(int id, string code, string message) => RequestRejected?.Invoke(this, (id, code, message));
    }

}

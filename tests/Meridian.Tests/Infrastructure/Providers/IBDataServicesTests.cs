using FluentAssertions;
using Meridian.Contracts.Configuration;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.ProviderSdk;
using Meridian.Storage.Store;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class IBDataServicesTests
{
    [Fact]
    public async Task Materializer_PersistsAnUpdateBeforePublishingItToOperatorReaders()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-ib-materializer", Guid.NewGuid().ToString("N"));
        try
        {
            using var services = new IBDataServices(new RecordingTransport());
            var store = new JsonFileIBDataResultStore(new IBDataResultStoreOptions { DataRoot = root });
            var materializer = new IBDataResultMaterializer(services, store);
            using var cts = new CancellationTokenSource();
            var worker = materializer.MaterializeAsync(cts.Token);
            await Task.Delay(25);

            var requestId = services.RequestScanner(new IBScannerRequest("STK", "STK.US.MAJOR", "TOP_PERC_GAIN"));
            await using var enumerator = materializer.WatchAsync(cts.Token).GetAsyncEnumerator();
            (await enumerator.MoveNextAsync()).Should().BeTrue();

            var persisted = await store.QueryAsync(new IBDataResultQuery(RequestIdentity: requestId.ToString(), Limit: 1));
            persisted.Should().ContainSingle();
            persisted[0].Lineage.RequestId.Should().Be(requestId);
            persisted[0].NormalizedPayload.Should().Contain("interactive-brokers");
            cts.Cancel();
            await worker.ContinueWith(_ => { });
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

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
        public void CancelDataRequest(int requestId, string capability) => Calls.Add($"cancel:{requestId}:{capability}");
    }
}

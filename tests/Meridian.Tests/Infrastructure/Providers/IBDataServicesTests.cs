using FluentAssertions;
using Meridian.Contracts.Configuration;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.ProviderSdk;
using Meridian.Storage.Store;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class IBDataServicesTests
{
    [Fact]
    public void DurableRequest_WithoutTenantAndCompanyOwnership_FailsBeforeTransport()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-ib-durable", Guid.NewGuid().ToString("N"));
        var transport = new RecordingTransport();
        var store = new JsonIBDurableResultStore(Path.Combine(root, "results.json"));
        using var services = new IBDataServices(transport, new IBDurableResultProjector(store));

        var request = () => services.RequestScanner(
            new IBScannerRequest("STK", "STK.US.MAJOR", "TOP_PERC_GAIN"));

        request.Should().Throw<InvalidOperationException>()
            .WithMessage("*tenant and company ownership*");
        transport.Calls.Should().BeEmpty("ownership is required before transport submission");
        services.GetRequests().Should().BeEmpty();
    }

    [Fact]
    public void Scenario_SharedGatewayRequestIds_DurableCallbacksRemainTenantAndCompanyIsolated()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-ib-durable", Guid.NewGuid().ToString("N"));
        try
        {
            var storePath = Path.Combine(root, "results.json");
            var store = new JsonIBDurableResultStore(storePath);
            var alphaOwner = new IBDataRequestOwnership("tenant-shared", "company-alpha");
            var betaOwner = new IBDataRequestOwnership("tenant-shared", "company-beta");
            using var alpha = new IBDataServices(
                new RecordingTransport(),
                new IBDurableResultProjector(store),
                "ib-gateway:shared");
            using var beta = new IBDataServices(
                new RecordingTransport(),
                new IBDurableResultProjector(store),
                "ib-gateway:shared");

            var alphaRequest = alpha.RequestScanner(
                new IBScannerRequest("STK", "STK.US.MAJOR", "TOP_PERC_GAIN"),
                ownership: alphaOwner);
            var betaRequest = beta.RequestScanner(
                new IBScannerRequest("STK", "STK.US.MAJOR", "HOT_BY_VOLUME"),
                ownership: betaOwner);
            alpha.RecordScannerResult(
                alphaRequest,
                new ProviderScannerResult(
                    0,
                    "ALPHA",
                    "NASDAQ",
                    null,
                    null,
                    null,
                    null,
                    null,
                    ProviderDataProvenance.Unattributed(DateTimeOffset.UtcNow)));
            beta.RecordScannerResult(
                betaRequest,
                new ProviderScannerResult(
                    0,
                    "BETA",
                    "NASDAQ",
                    null,
                    null,
                    null,
                    null,
                    null,
                    ProviderDataProvenance.Unattributed(DateTimeOffset.UtcNow)));

            alphaRequest.Should().Be(betaRequest, "independent gateway lifetimes reuse IB request ids");
            var restarted = new JsonIBDurableResultStore(storePath);
            var alphaResults = restarted.Get("tenant-shared", "company-alpha");
            var betaResults = restarted.Get("tenant-shared", "company-beta");
            alphaResults.Should().ContainSingle();
            betaResults.Should().ContainSingle();
            alphaResults[0].Request.ScannerResults.Should().ContainSingle()
                .Which.Symbol.Should().Be("ALPHA");
            betaResults[0].Request.ScannerResults.Should().ContainSingle()
                .Which.Symbol.Should().Be("BETA");
            alphaResults[0].RequestCorrelationId.Should().NotBe(
                betaResults[0].RequestCorrelationId,
                "request correlation remains unique across gateway restarts");
            alpha.GetRequests("tenant-shared", "company-beta").Should().BeEmpty();
            beta.GetRequests("tenant-shared", "company-alpha").Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Materializer_PersistsAnUpdateBeforePublishingItToOperatorReaders()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-ib-materializer", Guid.NewGuid().ToString("N"));
        try
        {
            using var services = new IBDataServices(new RecordingTransport());
            var store = new JsonFileIBDataResultStore(new IBDataResultStoreOptions { DataRoot = root });
            using var materializer = new IBDataResultMaterializer(services, store);
            var owner = new IBDataRequestOwnership("tenant-shared", "company-alpha");
            using var cts = new CancellationTokenSource();
            var worker = materializer.MaterializeAsync(owner.TenantId, owner.CompanyId, cts.Token);
            await using var enumerator = materializer
                .WatchAsync(owner.TenantId, owner.CompanyId, cts.Token)
                .GetAsyncEnumerator();
            var observedUpdate = enumerator.MoveNextAsync().AsTask();

            var requestId = services.RequestScanner(
                new IBScannerRequest("STK", "STK.US.MAJOR", "TOP_PERC_GAIN"),
                ownership: owner);
            (await observedUpdate.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();

            var persisted = await store.QueryAsync(new IBDataResultQuery(
                owner.TenantId,
                owner.CompanyId,
                RequestIdentity: requestId.ToString(),
                Limit: 1));
            persisted.Should().ContainSingle();
            persisted[0].Lineage.RequestId.Should().Be(requestId);
            persisted[0].NormalizedPayload.Should().Contain("interactive-brokers");
            materializer.GetRequests().Should().BeEmpty("unscoped operator reads fail closed");
            materializer.GetRequests(owner.TenantId, owner.CompanyId)
                .Should().ContainSingle().Which.RequestId.Should().Be(requestId);
            materializer.GetRequests(owner.TenantId, "company-beta").Should().BeEmpty();
            cts.Cancel();
            await FluentActions.Awaiting(() => worker)
                .Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Scenario_ScopedWatch_ExcludesOtherTenantAndCompanyUpdates()
    {
        using var services = new IBDataServices(new RecordingTransport());
        var requestedOwner = new IBDataRequestOwnership("tenant-shared", "company-alpha");
        var otherOwner = new IBDataRequestOwnership("tenant-shared", "company-beta");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var enumerator = services
            .WatchAsync(requestedOwner.TenantId, requestedOwner.CompanyId, cts.Token)
            .GetAsyncEnumerator();
        var nextUpdate = enumerator.MoveNextAsync().AsTask();

        services.RequestScanner(
            new IBScannerRequest("STK", "STK.US.MAJOR", "HOT_BY_VOLUME"),
            ownership: otherOwner);
        var requestedId = services.RequestScanner(
            new IBScannerRequest("STK", "STK.US.MAJOR", "TOP_PERC_GAIN"),
            ownership: requestedOwner);

        (await nextUpdate).Should().BeTrue();
        enumerator.Current.RequestId.Should().Be(requestedId);
        services.GetRequests(requestedOwner.TenantId, requestedOwner.CompanyId)
            .Should().ContainSingle().Which.RequestId.Should().Be(requestedId);
    }

    [Fact]
    public async Task Scenario_UnscopedCompatibilityReadAndWatch_ExcludeOwnerBoundRequests()
    {
        using var services = new IBDataServices(new RecordingTransport());
        var owner = new IBDataRequestOwnership("tenant-shared", "company-alpha");
        var unscopedLineageUpdates = new List<int>();
        var unscopedReadModelUpdates = new List<int>();
        services.LineageUpdated += lineage => unscopedLineageUpdates.Add(lineage.RequestId);
        services.ReadModelUpdated += request => unscopedReadModelUpdates.Add(request.RequestId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var enumerator = services
            .WatchAsync(timeout.Token)
            .GetAsyncEnumerator();
        var nextUpdate = enumerator.MoveNextAsync().AsTask();

        var ownedRequestId = services.RequestScanner(
            new IBScannerRequest("STK", "STK.US.MAJOR", "TOP_PERC_GAIN"),
            ownership: owner);
        var ownerlessRequestId = services.RequestScanner(
            new IBScannerRequest("STK", "STK.US.MAJOR", "HOT_BY_VOLUME"));

        (await nextUpdate).Should().BeTrue();
        enumerator.Current.RequestId.Should().Be(
            ownerlessRequestId,
            "the unscoped compatibility stream must skip owner-bound updates");
        services.GetRequests().Should().ContainSingle()
            .Which.RequestId.Should().Be(ownerlessRequestId);
        services.GetRequests().Should().NotContain(
            request => request.RequestId == ownedRequestId);
        services.GetRequests(owner.TenantId, owner.CompanyId).Should().ContainSingle()
            .Which.RequestId.Should().Be(ownedRequestId);

        services.RecordStatus(ownedRequestId, "owned-status");
        services.RecordStatus(ownerlessRequestId, "ownerless-status");

        services.GetLineage().Should().ContainSingle()
            .Which.RequestId.Should().Be(ownerlessRequestId);
        services.GetLineage(owner.TenantId, owner.CompanyId).Should().ContainSingle()
            .Which.RequestId.Should().Be(ownedRequestId);
        services.GetLineage(owner.TenantId, "company-beta").Should().BeEmpty();
        unscopedLineageUpdates.Should().NotBeEmpty()
            .And.OnlyContain(requestId => requestId == ownerlessRequestId);
        unscopedReadModelUpdates.Should().NotBeEmpty()
            .And.OnlyContain(requestId => requestId == ownerlessRequestId);
    }

    [Fact]
    public void Scenario_SynchronousTransportCallback_CannotBeOverwrittenByRequestedState()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-ib-durable", Guid.NewGuid().ToString("N"));
        try
        {
            var storePath = Path.Combine(root, "results.json");
            var transport = new CallbackTransport
            {
                ScannerResultDuringRequest = new ProviderScannerResult(
                    0,
                    "AAPL",
                    "NASDAQ",
                    null,
                    null,
                    null,
                    null,
                    null,
                    ProviderDataProvenance.Unattributed(DateTimeOffset.UtcNow))
            };
            var owner = new IBDataRequestOwnership("tenant-a", "company-a");
            using var services = new IBDataServices(
                transport,
                new IBDurableResultProjector(new JsonIBDurableResultStore(storePath)));

            var requestId = services.RequestScanner(
                new IBScannerRequest("STK", "STK.US.MAJOR", "TOP_PERC_GAIN"),
                ownership: owner);

            var retained = services.GetRequests(owner.TenantId, owner.CompanyId)
                .Should().ContainSingle().Which;
            retained.RequestId.Should().Be(requestId);
            retained.Status.Should().Be(ProviderDataRequestStatus.Streaming);
            retained.ScannerResults.Should().ContainSingle().Which.Symbol.Should().Be("AAPL");

            var restarted = new JsonIBDurableResultStore(storePath);
            var durable = restarted.Get(owner.TenantId, owner.CompanyId)
                .Should().ContainSingle().Which.Request;
            durable.Status.Should().Be(ProviderDataRequestStatus.Streaming);
            durable.ScannerResults.Should().ContainSingle().Which.Symbol.Should().Be("AAPL");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
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

        services.RecordScannerResult(second, new ProviderScannerResult(0, "AAPL", "NASDAQ", "265598", null, null, null, null, ProviderDataProvenance.Unattributed(DateTimeOffset.UtcNow)));
        services.CompleteRequest(second);

        services.GetRequests().Single(request => request.RequestId == first).Status.Should().Be(ProviderDataRequestStatus.Requested);
        var correlated = services.GetRequests().Single(request => request.RequestId == second);
        correlated.Status.Should().Be(ProviderDataRequestStatus.Completed);
        correlated.ScannerResults.Should().ContainSingle().Which.Symbol.Should().Be("AAPL");
    }

    [Fact]
    public void ReturnedRecords_CarryCompleteProvenanceAndRepeatedCallbacksUseStableDeduplicationKeys()
    {
        var services = new IBDataServices(new RecordingTransport(), "ib-gateway:paper:17");
        var scanner = services.RequestScanner(new IBScannerRequest("STK", "STK.US.MAJOR", "TOP_PERC_GAIN"));
        var bars = services.SubscribeRealTimeBars(new IBRealTimeBarRequest(new SymbolConfig("AAPL")));
        var ticks = services.RequestHistoricalTicks(new IBHistoricalTickRequest(new SymbolConfig("AAPL"), null, DateTimeOffset.UtcNow, 1));
        var pnl = services.SubscribePnl("DU111", "model-a");
        var marketRule = services.RequestMarketRule(26);
        var options = services.RequestOptionChain(new SymbolConfig("SPY"));

        var callbackTime = DateTimeOffset.Parse("2026-07-22T12:00:00+00:00");
        var scannerRow = new ProviderScannerResult(0, "AAPL", "NASDAQ", "265598", null, null, null, null, ProviderDataProvenance.Unattributed(callbackTime));
        services.RecordScannerResult(scanner, scannerRow);
        services.RecordScannerResult(scanner, scannerRow);
        services.RecordRealTimeBar(bars, new ProviderRealTimeBar(callbackTime, 1m, 2m, 1m, 2m, 10m, 1.5m, 3, ProviderDataProvenance.Unattributed(callbackTime)));
        services.RecordHistoricalTick(ticks, new ProviderHistoricalTick(callbackTime, 2m, 10m, "TRADES", null, null, "NASDAQ", ProviderDataProvenance.Unattributed(callbackTime)));
        services.RecordPnl(pnl, new ProviderAccountPnl("DU111", "model-a", 1m, 2m, 3m, null, null, ProviderDataProvenance.Unattributed(callbackTime)));
        services.RecordMarketRule(marketRule, [new ProviderMarketRuleIncrement(0m, 0.01m, ProviderDataProvenance.Unattributed(callbackTime))]);
        services.RecordOptionContract(options, new ProviderOptionContract("SPY", "SPY", new DateOnly(2026, 8, 21), 650m, "C", "SMART", null, null, "756733", ProviderDataProvenance.Unattributed(callbackTime)));
        services.RecordMarketDataType(scanner, 3);

        var records = services.GetRequests();
        var observations = records.SelectMany(request => new object?[]
        {
            request.Provenance,
            request.OptionContracts?.SingleOrDefault()?.Provenance,
            request.RealTimeBars?.SingleOrDefault()?.Provenance,
            request.HistoricalTicks?.SingleOrDefault()?.Provenance,
            request.Pnl?.Provenance,
            request.MarketRuleIncrements?.SingleOrDefault()?.Provenance
        }).OfType<ProviderDataProvenance>().Concat(records.Single(x => x.RequestId == scanner).ScannerResults!.Select(x => x.Provenance));

        foreach (var provenance in observations)
        {
            provenance.ProviderId.Should().Be("interactive-brokers");
            provenance.ProviderConnectionId.Should().Be("ib-gateway:paper:17");
            provenance.SourceTimestamp.Should().NotBe(default);
            provenance.ReceiptTimestamp.Should().NotBe(default);
            provenance.Entitlement.Should().NotBeNullOrWhiteSpace();
            provenance.Feed.Should().NotBeNullOrWhiteSpace();
            provenance.MarketDataAvailability.Should().NotBeNullOrWhiteSpace();
            provenance.RequestOrSubscriptionDescriptor.Should().NotBeNullOrWhiteSpace();
            provenance.ProviderNativeId.Should().NotBeNullOrWhiteSpace();
            provenance.CorrelationId.Should().NotBeNullOrWhiteSpace();
            provenance.StableDeduplicationKey.Should().NotBeNullOrWhiteSpace();
        }

        var scannerKeys = records.Single(x => x.RequestId == scanner).ScannerResults!.Select(x => x.Provenance.StableDeduplicationKey).ToArray();
        scannerKeys.Should().HaveCount(2);
        scannerKeys[0].Should().Be(scannerKeys[1], "identical provider callbacks must retain the same deduplication key");
        records.Single(x => x.RequestId == bars).RealTimeBars!.Single().Provenance.SourceTimestamp.Should().Be(callbackTime);
        records.Single(x => x.RequestId == bars).RealTimeBars!.Single().Provenance.ReceiptTimestamp.Should().NotBe(callbackTime);
        records.Single(x => x.RequestId == scanner).ScannerResults!
            .Should().OnlyContain(result => result.Provenance.MarketDataAvailability == "Delayed");
    }

    [Fact]
    public void CallbackBridge_EnrichesPreviouslyReceivedObservationsWithEntitlementEvidence()
    {
        var transport = new CallbackTransport();
        using var services = new IBDataServices(transport, "ignored-because-transport-supplies-identity");
        var requestId = services.RequestScanner(new IBScannerRequest("STK", "STK.US.MAJOR", "TOP_PERC_GAIN"));
        var sourceTime = DateTimeOffset.Parse("2026-07-22T12:00:00+00:00");

        transport.RaiseScannerResult(requestId, new ProviderScannerResult(0, "AAPL", "NASDAQ", "265598", null, null, null, null, ProviderDataProvenance.Unattributed(sourceTime)));
        transport.RaiseMarketDataType(requestId, 1);

        var provenance = services.GetRequests().Single().ScannerResults!.Single().Provenance;
        provenance.ProviderConnectionId.Should().Be("ib-gateway:live:7");
        provenance.SourceTimestamp.Should().Be(sourceTime);
        provenance.ReceiptTimestamp.Should().NotBe(sourceTime);
        provenance.Entitlement.Should().Be("reported");
        provenance.MarketDataAvailability.Should().Be("Live");
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
    public void Cancellation_QueuedCallbacksArrivingAfterCancel_DoNotResurrectTheStream()
    {
        var transport = new CallbackTransport();
        using var services = new IBDataServices(transport);
        var requestId = services.SubscribeTickByTick(new SymbolConfig("AAPL"));
        transport.RaiseTick(requestId, new ProviderTickByTickObservation(DateTimeOffset.UtcNow, "last", 200m, 10m));

        services.CancelRequest(requestId, CancellationToken.None);

        // The IB reader loop can still hold queued events for the cancelled id; neither a
        // payload nor a completion may flip the operator-visible outcome back to a live status.
        transport.RaiseTick(requestId, new ProviderTickByTickObservation(DateTimeOffset.UtcNow, "last", 201m, 5m));
        transport.RaiseCompleted(requestId);

        var request = services.GetRequests().Single();
        request.Status.Should().Be(ProviderDataRequestStatus.Cancelled);
        request.TickByTickObservations.Should().ContainSingle();
    }

    [Fact]
    public void Cancellation_TransportCancelFailure_LeavesTheRequestRoutableForRejection()
    {
        var transport = new CallbackTransport { CancelFailure = new InvalidOperationException("socket write failed") };
        using var services = new IBDataServices(transport);
        var requestId = services.SubscribeTickByTick(new SymbolConfig("AAPL"));

        var act = () => services.CancelRequest(requestId, CancellationToken.None);

        // The vendor stream may still be live, so the request must not be reported cancelled
        // and the request-scoped error that follows must still reach the read model.
        act.Should().Throw<InvalidOperationException>();
        services.GetRequests().Single().Status.Should().Be(ProviderDataRequestStatus.Requested);

        transport.RaiseRejected(requestId, "10197", "No market data during competing live session.");
        services.GetRequests().Single().Status.Should().Be(ProviderDataRequestStatus.Rejected);
    }

    [Fact]
    public void PnlCallbacks_RetainAccountAndModelIsolation()
    {
        var services = new IBDataServices(new RecordingTransport());
        var first = services.SubscribePnl("DU111", "growth");
        var second = services.SubscribePnl("DU222", "income");

        services.RecordPnl(first, new ProviderAccountPnl("DU111", "growth", 10m, 4m, 6m, null, null, ProviderDataProvenance.Unattributed(DateTimeOffset.UtcNow)));
        services.RecordPnl(second, new ProviderAccountPnl("DU222", "income", 20m, 7m, 13m, null, null, ProviderDataProvenance.Unattributed(DateTimeOffset.UtcNow)));

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

        var callbackEvidence = ProviderDataProvenance.Unattributed(DateTimeOffset.UtcNow);
        transport.RaiseContract(contract, new ProviderContractDetails("265598", "AAPL", null, "STK", "NASDAQ", null, "USD", null, null, null, null, null, "26", .01m, "Apple", null, null, null, null, null, null));
        transport.RaiseChain(chain, new ProviderOptionChainDefinition("SMART", "265598", "AAPL", "100", [new DateOnly(2027, 1, 15)], [250m]));
        transport.RaiseHeadline(headlines, new ProviderNewsHeadline(DateTimeOffset.UtcNow, "BRFG", "headline-1", "Apple headline"));
        transport.RaiseArticle(article, new ProviderNewsArticlePayload(0, "article text"));
        transport.RaiseFundamental(fundamentals, new ProviderFundamentalReport("<Report />"));
        transport.RaiseTick(ticks, new ProviderTickByTickObservation(DateTimeOffset.UtcNow, "last", 200m, 10m));
        transport.RaiseDepth(depth, [new ProviderDepthExchangeDescription("NASDAQ", "STK", "NASDAQ", "Deep", false)]);
        transport.RaiseDividend(dividends, new ProviderDividendEarnings(1m, 1.1m, new DateOnly(2027, 2, 1), .30m, 7m, 30m));
        transport.RaiseScanner(scanner, new ProviderScannerResult(0, "AAPL", "NASDAQ", "265598", null, null, null, null, callbackEvidence));
        transport.RaisePnl(pnl, new ProviderAccountPnl("DU123", null, 1m, 2m, 3m, null, null, callbackEvidence));
        transport.RaiseMarketRule(rules, [new ProviderMarketRuleIncrement(0m, .01m, callbackEvidence)]);
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

    [Fact]
    public void CallbackSource_UnrelatedProviderRequestRejected_IsIgnoredWithoutAffectingTrackedRequests()
    {
        var transport = new CallbackTransport();
        using var services = new IBDataServices(transport);
        var requestId = services.RequestScanner(new IBScannerRequest("STK", "STK.US.MAJOR", "TOP_PERC_GAIN"));

        var action = () => transport.RaiseRejected(12345, "354", "Requested market data is not subscribed.");

        action.Should().NotThrow();
        services.GetRequests().Single().Should().Match<ProviderDataRequestReadModel>(request =>
            request.RequestId == requestId && request.Status == ProviderDataRequestStatus.Requested);
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
        public void RequestRealTimeBars(int requestId, IBRealTimeBarRequest request) => Calls.Add($"real-time-bars:{requestId}:{request.Contract.Symbol}");
        public void RequestHistoricalTicks(int requestId, IBHistoricalTickRequest request) => Calls.Add($"historical-ticks:{requestId}:{request.Contract.Symbol}");
        public void CancelDataRequest(int requestId, string capability) => Calls.Add($"cancel:{requestId}:{capability}");
    }

    private sealed class CallbackTransport : IIBDataServiceTransport, IIBDataLineageSource, IIBDataCallbackSource, IIBProviderConnectionIdentity
    {
        public string ProviderConnectionId => "ib-gateway:live:7";
        public ProviderScannerResult? ScannerResultDuringRequest { get; init; }
        public Exception? CancelFailure { get; init; }
        public event EventHandler<IBMarketDataTypeUpdate>? MarketDataTypeReceived;
        public event EventHandler<(int RequestId, ProviderContractDetails Details)>? ContractDetailsReceived;
        public event EventHandler<(int RequestId, ProviderOptionChainDefinition Definition)>? OptionChainDefinitionReceived;
        public event EventHandler<(int RequestId, ProviderNewsHeadline Headline)>? HistoricalNewsReceived;
        public event EventHandler<(int RequestId, ProviderNewsArticlePayload Article)>? NewsArticleReceived;
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

        public void RequestScanner(int requestId, IBScannerRequest request)
        {
            if (ScannerResultDuringRequest is { } scannerResult)
                ScannerResultReceived?.Invoke(this, (requestId, scannerResult));
        }
        public void RequestContractDetails(int requestId, SymbolConfig contract) { }
        public void RequestOptionChain(int requestId, SymbolConfig underlying) { }
        public void RequestHistoricalNews(int requestId, int conId, string providerCodes, DateTimeOffset start, DateTimeOffset end, int maximumResults) { }
        public void RequestNewsArticle(int requestId, string providerCode, string articleId) { }
        public void RequestFundamentals(int requestId, SymbolConfig contract, string reportType) { }
        public void RequestDividendEarnings(int requestId, SymbolConfig contract) { }
        public void RequestTickByTick(int requestId, SymbolConfig contract, string tickType, int numberOfTicks, bool ignoreSize) { }
        public void RequestPnl(int requestId, string account, string? modelCode) { }
        public void RequestMarketRule(int requestId, int marketRuleId) { }
        public void RequestDepthExchanges(int requestId) { }
        public void CancelDataRequest(int requestId, string capability)
        {
            if (CancelFailure is { } failure)
                throw failure;
        }
        public void RaiseContract(int id, ProviderContractDetails value) => ContractDetailsReceived?.Invoke(this, (id, value));
        public void RaiseChain(int id, ProviderOptionChainDefinition value) => OptionChainDefinitionReceived?.Invoke(this, (id, value));
        public void RaiseHeadline(int id, ProviderNewsHeadline value) => HistoricalNewsReceived?.Invoke(this, (id, value));
        public void RaiseArticle(int id, ProviderNewsArticlePayload value) => NewsArticleReceived?.Invoke(this, (id, value));
        public void RaiseFundamental(int id, ProviderFundamentalReport value) => FundamentalReportReceived?.Invoke(this, (id, value));
        public void RaiseTick(int id, ProviderTickByTickObservation value) => TickByTickReceived?.Invoke(this, (id, value));
        public void RaiseDepth(int id, IReadOnlyList<ProviderDepthExchangeDescription> value) => DepthExchangesReceived?.Invoke(this, (id, value));
        public void RaiseDividend(int id, ProviderDividendEarnings value) => DividendEarningsReceived?.Invoke(this, (id, value));
        public void RaiseScanner(int id, ProviderScannerResult value) => ScannerResultReceived?.Invoke(this, (id, value));
        public void RaisePnl(int id, ProviderAccountPnl value) => PnlReceived?.Invoke(this, (id, value));
        public void RaiseMarketRule(int id, IReadOnlyList<ProviderMarketRuleIncrement> value) => MarketRuleReceived?.Invoke(this, (id, value));
        public void RaiseCompleted(int id) => RequestCompleted?.Invoke(this, id);
        public void RaiseRejected(int id, string code, string message) => RequestRejected?.Invoke(this, (id, code, message));
        public void RaiseScannerResult(int requestId, ProviderScannerResult result) => ScannerResultReceived?.Invoke(this, (requestId, result));
        public void RaiseMarketDataType(int requestId, int marketDataType) => MarketDataTypeReceived?.Invoke(this, new IBMarketDataTypeUpdate(requestId, marketDataType));
    }
}

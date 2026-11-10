using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Risk.Rules;
using Meridian.Strategies.Services;
using Meridian.Tests.Ui;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Meridian.Tests.Integration;

/// <summary>
/// Opt-in order-lifecycle certification against Alpaca's real paper Trading API, driven through
/// the production order manager, brokerage gateway, recovery journal and portfolio
/// synchronization. Unlike <see cref="AlpacaPaperSandboxTests"/> these tests place, replace and
/// cancel paper orders and take a one-share paper position that they close again, so they need
/// their own second opt-in. Every order is one share, carries a run-unique client id, and is
/// cancelled or flattened in a <c>finally</c> block; the transport refuses bulk cancellation,
/// liquidation and anything outside the paper host.
/// </summary>
/// <remarks>
/// <para>
/// The order manager classifies every Alpaca route, the paper environment included, as live
/// routing. A run therefore states the production admission evidence explicitly for the paper
/// gateway (<see cref="SandboxRoutingConfiguration"/>, a run id, and a readiness gate that
/// approves only this run) instead of bypassing the gate; the first test proves the gate holds
/// when any of that is missing.
/// </para>
/// <para>
/// The trade-update WebSocket is deliberately not attached: this lane certifies the REST order,
/// recovery and reconciliation paths. Stream continuity is fixture evidence only.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Trait("Category", "LiveProvider")]
[Collection(AlpacaCredentialEnvironmentCollection.Name)]
public sealed class AlpacaPaperSandboxOrderLifecycleTests(ITestOutputHelper output)
{
    // A liquid, overnight-tradable ETF: resting orders rest and marketable orders fill in every
    // equity session (overnight, pre-market, regular, after-hours). The holdings assertions
    // assume nothing else trades this symbol on the account while the run is in flight.
    private const string Symbol = "IWM";

    // Far from any plausible price, so the order rests without filling.
    private const decimal RestingLimit = 1.00m;
    private const decimal AmendedRestingLimit = 1.50m;

    // Marketable caps: the paper venue fills at the market, not at these bounds.
    private const decimal MarketableBuyCap = 10_000m;
    private const decimal MarketableSellFloor = 1.00m;

    private const string SandboxRunId = "alpaca-paper-sandbox";

    // extended_hours keeps a limit order eligible outside the regular session; runId is the
    // run the readiness gate below approves.
    private static readonly IReadOnlyDictionary<string, string> SandboxOrderMetadata =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["extended_hours"] = "true",
            ["runId"] = SandboxRunId,
        };

    [AlpacaPaperSandboxOrdersFact]
    public async Task OrderManagerWithoutAdmissionEvidence_RefusesToRouteAndNothingReachesTheBroker()
    {
        var (key, secret) = AlpacaPaperSandbox.RequirePaperCredentials();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var ct = timeout.Token;
        using var clients = new AlpacaPaperSandboxHttpClientFactory(AlpacaPaperSandboxAccess.IndividualOrders);
        await using var gateway = CreateGateway(clients, key, secret);
        await gateway.ConnectAsync(ct);
        var localAccountId = Guid.NewGuid();

        await using (var unconfigured = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance))
        {
            await unconfigured.StartAsync();
            var refused = await unconfigured.PlaceOrderAsync(RestingBuy(NewClientOrderId(), localAccountId), ct);
            refused.Success.Should().BeFalse("a brokerage route needs explicit brokerage configuration");
        }

        await using (var withoutReadiness = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
            brokerageConfiguration: SandboxRoutingConfiguration()))
        {
            await withoutReadiness.StartAsync();
            var refused = await withoutReadiness.PlaceOrderAsync(RestingBuy(NewClientOrderId(), localAccountId), ct);
            refused.Success.Should().BeFalse("configuration alone does not approve a run");
        }

        await using (var gated = CreateOms(gateway))
        {
            await gated.StartAsync();
            var withoutRun = RestingBuy(NewClientOrderId(), localAccountId) with
            {
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["extended_hours"] = "true" },
            };
            (await gated.PlaceOrderAsync(withoutRun, ct)).Success.Should().BeFalse("an order outside an approved run is refused");
            var otherRun = RestingBuy(NewClientOrderId(), localAccountId) with
            {
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["extended_hours"] = "true", ["runId"] = "another-run" },
            };
            (await gated.PlaceOrderAsync(otherRun, ct)).Success.Should().BeFalse("the readiness gate approves only the sandbox run");
        }

        clients.SubmissionCount.Should().Be(0, "every refusal happened before the network boundary");
        clients.MutationCount.Should().Be(0);
        output.WriteLine("Admission gates refused four ungated dispatches; submissions reaching the broker={0}.", clients.SubmissionCount);
    }

    [AlpacaPaperSandboxOrdersFact]
    public async Task RestingOrder_IsSubmittedRecoveredAmendedAndCancelledThroughTheGovernedPath()
    {
        var (key, secret) = AlpacaPaperSandbox.RequirePaperCredentials();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var ct = timeout.Token;
        using var clients = new AlpacaPaperSandboxHttpClientFactory(AlpacaPaperSandboxAccess.IndividualOrders);
        await using var gateway = CreateGateway(clients, key, secret);
        await gateway.ConnectAsync(ct);
        var externalAccountId = (await gateway.GetAccountsAsync(ct)).Single().AccountId;
        var localAccountId = Guid.NewGuid();
        await using var oms = CreateOms(gateway);
        await oms.StartAsync();
        var sync = CreateSync(gateway, oms);
        var clientOrderId = NewClientOrderId();
        var ownedBrokerOrderIds = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var request = RestingBuy(clientOrderId, localAccountId);
            var placed = await oms.PlaceOrderAsync(request, ct);
            placed.Success.Should().BeTrue("a one-share resting paper limit order must be accepted; error: {0}", placed.ErrorMessage);
            placed.OrderId.Should().Be(clientOrderId);

            var working = await WaitForBrokerOrderAsync(gateway, clientOrderId,
                order => order.Status is OrderStatus.Accepted, ct);
            ownedBrokerOrderIds.Add(working.OrderId);
            working.Symbol.Should().Be(Symbol);
            working.Quantity.Should().Be(1m);
            working.FilledQuantity.Should().Be(0m);
            working.LimitPrice.Should().Be(RestingLimit);

            // Authoritative lookup by the retained client identity.
            var recovered = await gateway.GetOrderForRecoveryAsync(clientOrderId, ct);
            (recovered is not null).Should().BeTrue("the broker must return the order for its client id");
            recovered!.GatewayOrderId.Should().Be(working.OrderId);
            recovered.ClientOrderId.Should().Be(clientOrderId);
            recovered.OrderQuantity.Should().Be(1m);
            recovered.FilledQuantity.Should().Be(0m);
            recovered.HasOrderTerms.Should().BeTrue();
            recovered.OrderLimitPrice.Should().Be(RestingLimit);
            (await gateway.GetOrderForRecoveryAsync(NewClientOrderId(), ct)).Should().BeNull(
                "an identity the broker never saw is reported as absent, not as an error");

            // A repeated dispatch of the same identity must not create a second broker order,
            // neither through the order manager nor if it reached the broker anyway.
            var submissionsBeforeRepeat = clients.SubmissionCount;
            (await oms.PlaceOrderAsync(request, ct)).Success.Should().BeFalse("the order manager already tracks this client id");
            clients.SubmissionCount.Should().Be(submissionsBeforeRepeat, "a locally refused duplicate never reaches the broker");
            var brokerDuplicate = () => gateway.SubmitOrderAsync(request, ct);
            await brokerDuplicate.Should().ThrowAsync<HttpRequestException>(
                "the broker refuses a reused client id, which the gateway reports as an unresolved dispatch to recover");
            (await OpenOrdersOnceSettledAsync(gateway, order => order.ClientOrderId == clientOrderId, expected: 1, ct))
                .Should().ContainSingle("the refused duplicate left exactly the original order working");

            var ready = await SynchronizeUntilSettledAsync(sync, localAccountId, externalAccountId, ct);
            AssertReadyUnlessBlockedByForeignOrders(ready, [clientOrderId], ownedBrokerOrderIds,
                "the retained working order matches the broker's open book");

            // Amendment. A routed order is replaced by a successor with a new broker id; an order
            // the venue only queued (no equity session open) is refused. Either way the order
            // manager must end up holding exactly the terms the broker holds.
            var amended = await oms.ModifyOrderAsync(clientOrderId,
                new OrderModification { NewQuantity = 2m, NewLimitPrice = AmendedRestingLimit }, ct);
            var afterAmendment = await gateway.GetOrderForRecoveryAsync(clientOrderId, ct);
            (afterAmendment is not null).Should().BeTrue();
            if (afterAmendment!.GatewayOrderId is { Length: > 0 } activeBrokerOrderId)
                ownedBrokerOrderIds.Add(activeBrokerOrderId);
            var retained = oms.GetOrder(clientOrderId);
            (retained is not null).Should().BeTrue();
            if (amended.Success)
            {
                afterAmendment.ReportType.Should().Be(ExecutionReportType.Modified);
                afterAmendment.ReplacesOrderId.Should().Be(working.OrderId, "the successor names the order it replaced");
                afterAmendment.GatewayOrderId.Should().NotBe(working.OrderId, "a replacement is a new broker order");
                afterAmendment.OrderQuantity.Should().Be(2m);
                afterAmendment.OrderLimitPrice.Should().Be(AmendedRestingLimit);
                retained!.Quantity.Should().Be(2m);
                retained.LimitPrice.Should().Be(AmendedRestingLimit);
            }
            else
            {
                afterAmendment.OrderQuantity.Should().Be(1m, "a refused amendment leaves the broker's terms unchanged");
                afterAmendment.OrderLimitPrice.Should().Be(RestingLimit);
                retained!.Quantity.Should().Be(1m);
                retained.LimitPrice.Should().Be(RestingLimit);
            }
            (await OpenOrdersOnceSettledAsync(gateway, order => ownedBrokerOrderIds.Contains(order.OrderId), expected: 1, ct))
                .Should().ContainSingle("an amendment never leaves both the predecessor and the successor working");
            output.WriteLine("Amendment {0}; broker holds quantity {1} at {2}.",
                amended.Success ? "replaced the order" : "was refused by the venue (no session routing the order)",
                afterAmendment.OrderQuantity, afterAmendment.OrderLimitPrice);

            var cancelled = await oms.CancelOrderAsync(clientOrderId, ct);
            cancelled.Success.Should().BeTrue("cancelling a resting paper order must be confirmed; error: {0}", cancelled.ErrorMessage);
            var terminal = await PollAsync(
                () => gateway.GetOrderForRecoveryAsync(clientOrderId, ct),
                report => report?.OrderStatus is OrderStatus.Cancelled, TimeSpan.FromSeconds(20), ct);
            terminal!.OrderStatus.Should().Be(OrderStatus.Cancelled);
            terminal.FilledQuantity.Should().Be(0m);
            oms.GetOrder(clientOrderId)!.Status.Should().Be(OrderStatus.Cancelled);
            (await OpenOrdersOnceSettledAsync(gateway, order => ownedBrokerOrderIds.Contains(order.OrderId), expected: 0, ct))
                .Should().BeEmpty("a cancelled order leaves the broker's open book");

            output.WriteLine("Resting order lifecycle verified: {0} submission(s), {1} order mutation(s), {2} request(s).",
                clients.SubmissionCount, clients.MutationCount, clients.RequestCount);
        }
        finally
        {
            await CancelOwnedOrdersAsync(gateway, clientOrderId, ownedBrokerOrderIds);
        }
    }

    [AlpacaPaperSandboxOrdersFact]
    public async Task AcknowledgementLostAfterAcceptance_BlocksTradingUntilTheOneBrokerOrderIsRecovered()
    {
        var (key, secret) = AlpacaPaperSandbox.RequirePaperCredentials();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var ct = timeout.Token;
        using var clients = new AlpacaPaperSandboxHttpClientFactory(AlpacaPaperSandboxAccess.IndividualOrders);
        await using var gateway = CreateGateway(clients, key, secret);
        await gateway.ConnectAsync(ct);
        var externalAccountId = (await gateway.GetAccountsAsync(ct)).Single().AccountId;
        var localAccountId = Guid.NewGuid();
        await using var oms = CreateOms(gateway);
        await oms.StartAsync();
        var sync = CreateSync(gateway, oms);
        var exposure = new AggregatePortfolioExposureProvider(new AggregatePortfolioService(new PortfolioRegistry()),
            orderManagerAccessor: () => oms, livePortfolioAccessor: () => sync);
        var clientOrderId = NewClientOrderId();
        var ownedBrokerOrderIds = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var request = RestingBuy(clientOrderId, localAccountId);

            // The submission reaches the paper API and is accepted there; its response never
            // reaches the order manager.
            clients.DropNextOrderSubmissionResponse();
            var placed = await oms.PlaceOrderAsync(request, ct);

            placed.Success.Should().BeFalse("an unacknowledged dispatch is not a confirmed order");
            clients.SubmissionCount.Should().Be(1);
            oms.GetRecoveryOrders(localAccountId).Select(order => order.OrderId).Should().Equal(
                [clientOrderId], "the dispatched identity is retained for authoritative recovery");
            sync.GetStatus(localAccountId).IsReady.Should().BeFalse();
            var blocked = await new BrokeragePortfolioStateRule(exposure).EvaluateAsync(request with { ClientOrderId = NewClientOrderId() }, ct);
            blocked.IsApproved.Should().BeFalse("an unresolved dispatch blocks further orders on the account");
            (await oms.PlaceOrderAsync(request, ct)).Success.Should().BeFalse("the unresolved client id cannot be dispatched again");
            clients.SubmissionCount.Should().Be(1, "exactly one submission reached the broker");

            // The broker really holds the order the order manager never heard back about.
            var brokerOrder = await WaitForBrokerOrderAsync(gateway, clientOrderId, static _ => true, ct);
            ownedBrokerOrderIds.Add(brokerOrder.OrderId);

            var recovered = await SynchronizeUntilSettledAsync(sync, localAccountId, externalAccountId, ct);

            oms.GetRecoveryOrders(localAccountId).Should().BeEmpty("the broker's answer resolves the retained dispatch");
            var retained = oms.GetOrder(clientOrderId);
            (retained is not null).Should().BeTrue();
            retained!.Status.Should().BeOneOf(OrderStatus.Accepted, OrderStatus.PendingNew);
            retained.Quantity.Should().Be(1m);
            retained.FilledQuantity.Should().Be(0m);
            clients.SubmissionCount.Should().Be(1, "recovery looks the order up; it never submits again");
            (await OpenOrdersOnceSettledAsync(gateway, order => order.ClientOrderId == clientOrderId, expected: 1, ct))
                .Should().ContainSingle("recovery found the one order the broker holds");
            AssertReadyUnlessBlockedByForeignOrders(recovered, [clientOrderId], ownedBrokerOrderIds,
                "the recovered order reconciles with the broker's open book");

            // The recovered order is routable again: cancelling it by its retained identity works.
            var cancelled = await oms.CancelOrderAsync(clientOrderId, ct);
            cancelled.Success.Should().BeTrue("the recovered order must be cancellable; error: {0}", cancelled.ErrorMessage);
            oms.GetOrder(clientOrderId)!.Status.Should().Be(OrderStatus.Cancelled);
            (await OpenOrdersOnceSettledAsync(gateway, order => order.ClientOrderId == clientOrderId, expected: 0, ct))
                .Should().BeEmpty("a cancelled order leaves the broker's open book");

            output.WriteLine("Lost acknowledgement recovered with {0} submission(s); ready after recovery={1}; requests={2}.",
                clients.SubmissionCount, recovered.IsReady, clients.RequestCount);
        }
        finally
        {
            await CancelOwnedOrdersAsync(gateway, clientOrderId, ownedBrokerOrderIds);
        }
    }

    [AlpacaPaperSandboxOrdersFact]
    public async Task MarketableOrder_FillReconcilesHoldingsActivityAndRetainedStateAcrossARestart()
    {
        var (key, secret) = AlpacaPaperSandbox.RequirePaperCredentials();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var ct = timeout.Token;
        using var clients = new AlpacaPaperSandboxHttpClientFactory(AlpacaPaperSandboxAccess.IndividualOrders);
        var recoveryRoot = Path.Combine(Path.GetTempPath(), $"meridian-alpaca-sandbox-{Guid.NewGuid():N}");
        Directory.CreateDirectory(recoveryRoot);
        var recoveryPath = Path.Combine(recoveryRoot, "brokerage-order-recovery.json");
        var localAccountId = Guid.NewGuid();
        var buyClientOrderId = NewClientOrderId();
        var sellClientOrderId = NewClientOrderId();
        var ownedBrokerOrderIds = new HashSet<string>(StringComparer.Ordinal);
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        decimal boughtQuantity = 0m, soldQuantity = 0m;
        await using var gateway = CreateGateway(clients, key, secret);
        try
        {
            await gateway.ConnectAsync(ct);
            var externalAccountId = (await gateway.GetAccountsAsync(ct)).Single().AccountId;
            var baselineQuantity = PositionQuantity(await gateway.GetPositionsAsync(ct));
            decimal fillPrice;
            string buyBrokerOrderId;

            await using (var oms = CreateOms(gateway, new FileBrokerageOrderRecoveryStore(recoveryPath, gateway.GatewayId)))
            {
                await oms.StartAsync();
                var placed = await oms.PlaceOrderAsync(new OrderRequest
                {
                    Symbol = Symbol,
                    Side = OrderSide.Buy,
                    Type = OrderType.Limit,
                    Quantity = 1m,
                    LimitPrice = MarketableBuyCap,
                    ClientOrderId = buyClientOrderId,
                    FundAccountId = localAccountId,
                    Metadata = SandboxOrderMetadata,
                }, ct);
                placed.Success.Should().BeTrue("a one-share marketable paper order must be accepted; error: {0}", placed.ErrorMessage);

                var filled = await PollAsync(
                    () => gateway.GetOrderForRecoveryAsync(buyClientOrderId, ct),
                    report => report?.OrderStatus is OrderStatus.Filled, TimeSpan.FromSeconds(45), ct);
                if (filled?.GatewayOrderId is { Length: > 0 } observedBrokerOrderId)
                    ownedBrokerOrderIds.Add(observedBrokerOrderId);
                (filled?.OrderStatus is OrderStatus.Filled).Should().BeTrue(
                    "the paper venue fills marketable orders only while an equity session (overnight, pre-market, regular or after-hours) is open; " +
                    "re-run during a session. Last observed status: {0}", filled?.OrderStatus);
                boughtQuantity = filled!.FilledQuantity;
                boughtQuantity.Should().Be(1m);
                (filled.FillPrice > 0m).Should().BeTrue("a fill carries its execution price");
                fillPrice = filled.FillPrice!.Value;
                buyBrokerOrderId = filled.GatewayOrderId!;

                // The exact FILL activity the execution stream replays after a reconnect. The broker
                // publishes the activity shortly after the order itself reports filled.
                var exactFill = await PollAsync(
                    async () => (await gateway.ReconcileExecutionSnapshotsAsync(startedAt, ct))
                        .Where(report => report.Source == "rest-fill-activity" && report.Report.GatewayOrderId == buyBrokerOrderId)
                        .Select(report => report.Report).ToArray(),
                    reports => reports.Length > 0, TimeSpan.FromSeconds(30), ct);
                exactFill.Should().ContainSingle("a one-share order fills in one execution");
                exactFill[0].ReportType.Should().Be(ExecutionReportType.Fill);
                exactFill[0].ClientOrderId.Should().Be(buyClientOrderId);
                exactFill[0].FilledQuantity.Should().Be(1m);
                exactFill[0].LastFillQuantity.Should().Be(1m);
                exactFill[0].FillPrice.Should().Be(fillPrice);
                exactFill[0].Side.Should().Be(OrderSide.Buy);
                exactFill[0].Symbol.Should().Be(Symbol);

                // Synchronization recovers the fill into the order manager and reads holdings.
                var sync = CreateSync(gateway, oms);
                var status = await SynchronizeUntilSettledAsync(sync, localAccountId, externalAccountId, ct);
                var retained = oms.GetOrder(buyClientOrderId);
                (retained is not null).Should().BeTrue();
                retained!.Status.Should().Be(OrderStatus.Filled);
                retained.FilledQuantity.Should().Be(1m);
                retained.AverageFillPrice.Should().Be(fillPrice);
                (status.Snapshot is not null).Should().BeTrue("the broker must provide portfolio evidence");
                status.Snapshot!.IsComplete.Should().BeTrue("blockers: {0}", string.Join(" | ", status.BlockingReasons));
                var held = status.Snapshot.Positions.Single(position => position.Symbol == Symbol);
                held.Quantity.Should().Be(baselineQuantity + 1m);
                held.Currency.Should().Be("USD");
                held.AssetClass.Should().Be("us_equity");
                (held.MarketPrice > 0m && held.AverageEntryPrice > 0m).Should().BeTrue();
                AssertReadyUnlessBlockedByForeignOrders(status, [buyClientOrderId], ownedBrokerOrderIds,
                    "a filled retained order and matching holdings reconcile");

                var activity = await gateway.GetActivitySnapshotAsync(externalAccountId, startedAt, ct);
                var fillActivity = activity.Fills.Where(fill => fill.OrderId == buyBrokerOrderId).ToArray();
                fillActivity.Should().ContainSingle();
                fillActivity[0].Symbol.Should().Be(Symbol);
                fillActivity[0].Side.Should().Be(OrderSide.Buy);
                fillActivity[0].Quantity.Should().Be(1m);
                fillActivity[0].Price.Should().Be(fillPrice);
                activity.Activities.Should().Contain(candidate =>
                    candidate.OrderId == buyBrokerOrderId
                    && candidate.Subtype == BrokerageActivitySubtype.TradeFill
                    && candidate.Currency == "USD"
                    && candidate.Quantity == 1m);
            }

            // Restart: a new gateway connection and order manager over the same retained journal.
            var submissionsBeforeRestart = clients.SubmissionCount;
            await using var restartedGateway = CreateGateway(clients, key, secret);
            await restartedGateway.ConnectAsync(ct);
            await using var restartedOms = CreateOms(restartedGateway,
                new FileBrokerageOrderRecoveryStore(recoveryPath, restartedGateway.GatewayId));
            await restartedOms.StartAsync();
            var restored = restartedOms.GetRetainedBrokerageOrders(localAccountId).SingleOrDefault(order => order.OrderId == buyClientOrderId);
            (restored is not null).Should().BeTrue("the dispatched identity survives a restart");
            restored!.FilledQuantity.Should().Be(1m);
            var restartedSync = CreateSync(restartedGateway, restartedOms);
            restartedSync.GetStatus(localAccountId).IsReady.Should().BeFalse("a restart requires new synchronization");
            var afterRestart = await SynchronizeUntilSettledAsync(restartedSync, localAccountId, externalAccountId, ct);
            var replayedOrder = restartedOms.GetOrder(buyClientOrderId);
            replayedOrder!.Status.Should().Be(OrderStatus.Filled);
            replayedOrder.FilledQuantity.Should().Be(1m, "recovery after a restart does not book the same fill twice");
            replayedOrder.AverageFillPrice.Should().Be(fillPrice);
            clients.SubmissionCount.Should().Be(submissionsBeforeRestart, "recovery after a restart submits nothing");
            AssertReadyUnlessBlockedByForeignOrders(afterRestart, [buyClientOrderId], ownedBrokerOrderIds,
                "the restored filled order and holdings reconcile after a restart");

            // Flatten through the same governed path and prove holdings return to the baseline.
            var sold = await restartedOms.PlaceOrderAsync(new OrderRequest
            {
                Symbol = Symbol,
                Side = OrderSide.Sell,
                Type = OrderType.Limit,
                Quantity = 1m,
                LimitPrice = MarketableSellFloor,
                ClientOrderId = sellClientOrderId,
                FundAccountId = localAccountId,
                Metadata = SandboxOrderMetadata,
            }, ct);
            sold.Success.Should().BeTrue("the closing paper order must be accepted; error: {0}", sold.ErrorMessage);
            var closed = await PollAsync(
                () => restartedGateway.GetOrderForRecoveryAsync(sellClientOrderId, ct),
                report => report?.OrderStatus is OrderStatus.Filled, TimeSpan.FromSeconds(45), ct);
            if (closed?.GatewayOrderId is { Length: > 0 } closingBrokerOrderId)
                ownedBrokerOrderIds.Add(closingBrokerOrderId);
            (closed?.OrderStatus is OrderStatus.Filled).Should().BeTrue("the closing order must fill; last observed status: {0}", closed?.OrderStatus);
            soldQuantity = closed!.FilledQuantity;
            var flattened = await PollAsync(
                async () => PositionQuantity(await restartedGateway.GetPositionsAsync(ct)),
                quantity => quantity == baselineQuantity, TimeSpan.FromSeconds(20), ct);
            flattened.Should().Be(baselineQuantity, "the run leaves the paper account's holdings as it found them");

            output.WriteLine("Fill, holdings, activity and restart recovery verified: {0} submission(s), {1} order mutation(s), {2} request(s).",
                clients.SubmissionCount, clients.MutationCount, clients.RequestCount);
        }
        finally
        {
            await CancelOwnedOrdersAsync(gateway, buyClientOrderId, ownedBrokerOrderIds);
            await CancelOwnedOrdersAsync(gateway, sellClientOrderId, ownedBrokerOrderIds);
            await FlattenRemainderAsync(gateway, boughtQuantity - soldQuantity, localAccountId);
            if (Directory.Exists(recoveryRoot))
                Directory.Delete(recoveryRoot, recursive: true);
        }
    }

    private static AlpacaBrokerageGateway CreateGateway(IHttpClientFactory clients, string key, string secret) =>
        new(clients, new AlpacaOptions(KeyId: key, SecretKey: secret, UseSandbox: true), NullLogger<AlpacaBrokerageGateway>.Instance);

    private static OrderManagementSystem CreateOms(AlpacaBrokerageGateway gateway, FileBrokerageOrderRecoveryStore? recoveryStore = null) =>
        new(gateway, NullLogger<OrderManagementSystem>.Instance,
            brokerageConfiguration: SandboxRoutingConfiguration(),
            liveOrderReadinessGate: new SandboxRunReadinessGate(),
            recoveryStore: recoveryStore);

    /// <summary>
    /// The admission evidence the order manager demands before any Alpaca route, stated for the
    /// paper gateway only. Validation artifacts are an operator release control and are not
    /// claimed here; the open-order ceiling bounds what a faulty run could leave working.
    /// </summary>
    private static BrokerageConfiguration SandboxRoutingConfiguration()
    {
        var configuration = new BrokerageConfiguration
        {
            Gateway = "alpaca",
            LiveExecutionEnabled = true,
            ReadOnlyPhaseEnabled = true,
            PaperTradingPhaseEnabled = true,
            ProductionRoutingPhaseEnabled = true,
            ReadOnlyVerificationPassed = true,
            PaperLifecycleTestsPassed = true,
            ReplayEvidencePassed = true,
            MaxOpenOrders = 3,
            ValidationGates = new BrokerValidationGateOptions { RequireValidationArtifactsForOrderPlacement = false },
        };
        configuration.BrokerFlows["alpaca"] = new BrokerFlowFlags { PaperOrderFlowEnabled = true, ProductionOrderRoutingEnabled = true };
        return configuration;
    }

    /// <summary>Approves the sandbox run on the Alpaca gateway for the sandbox symbol, one share at a time, and nothing else.</summary>
    private sealed class SandboxRunReadinessGate : ILiveOrderReadinessGate
    {
        public Task<LiveOrderReadinessDecision> EvaluateAsync(LiveOrderReadinessRequest request, CancellationToken ct = default) =>
            Task.FromResult(request is { RunId: SandboxRunId, BrokerName: "alpaca", Symbol: Symbol, Quantity: 1m, OrderType: OrderType.Limit }
                ? LiveOrderReadinessDecision.Approved("alpaca-paper-sandbox-run")
                : LiveOrderReadinessDecision.Rejected("Only the one-share Alpaca paper sandbox run is approved."));
    }

    private static LiveBrokeragePortfolioSyncService CreateSync(AlpacaBrokerageGateway gateway, OrderManagementSystem oms) =>
        new(() => gateway, () => oms,
            new BrokerageExecutionReconciliationService(NullLogger<BrokerageExecutionReconciliationService>.Instance));

    private static string NewClientOrderId() => $"meridian-sandbox-{Guid.NewGuid():N}";

    private static OrderRequest RestingBuy(string clientOrderId, Guid localAccountId) => new()
    {
        Symbol = Symbol,
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 1m,
        LimitPrice = RestingLimit,
        ClientOrderId = clientOrderId,
        FundAccountId = localAccountId,
        Metadata = SandboxOrderMetadata,
    };

    /// <summary>
    /// A paper account is not necessarily dedicated to this run. Readiness is required unless
    /// the only reconciliation breaks are open orders this run does not own, which the order
    /// manager is right to refuse to adopt; anything else that blocks readiness is a failure.
    /// </summary>
    private void AssertReadyUnlessBlockedByForeignOrders(
        LiveBrokeragePortfolioStatus status, IReadOnlyCollection<string> ownClientOrderIds,
        IReadOnlyCollection<string> ownBrokerOrderIds, string because)
    {
        if (status.IsReady)
            return;

        var breaks = status.Reconciliation?.Breaks ?? [];
        var ownBreaks = breaks.Count(candidate =>
            candidate.ClientOrderId is { } clientOrderId && ownClientOrderIds.Contains(clientOrderId)
            || candidate.LocalOrderId is { } localOrderId && ownClientOrderIds.Contains(localOrderId)
            || candidate.BrokerOrderId is { } brokerOrderId && ownBrokerOrderIds.Contains(brokerOrderId));
        (breaks.Count > 0 && ownBreaks == 0).Should().BeTrue(
            "{0}; not ready with {1} break(s), {2} on this run's own orders; blockers: {3}",
            because, breaks.Count, ownBreaks, string.Join(" | ", status.BlockingReasons));
        output.WriteLine("Readiness withheld only by {0} open order(s) this run does not own; use a dedicated paper account to certify the ready path.",
            breaks.Count);
    }

    private static decimal PositionQuantity(IReadOnlyList<BrokerPosition> positions) =>
        positions.Where(static position => position.Symbol == Symbol).Sum(static position => position.Quantity);

    /// <summary>
    /// Balances and marks move between the reads a synchronization brackets; a moving mark is a
    /// reason to read again, not evidence of a discrepancy.
    /// </summary>
    private static async Task<LiveBrokeragePortfolioStatus> SynchronizeUntilSettledAsync(
        LiveBrokeragePortfolioSyncService sync, Guid localAccountId, string externalAccountId, CancellationToken ct)
    {
        LiveBrokeragePortfolioStatus status = null!;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            status = await sync.SynchronizeAsync(localAccountId, externalAccountId, ct);
            if (status.IsReady || status.Reconciliation?.Breaks.Count > 0)
                break;
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }

        return status;
    }

    /// <summary>
    /// Reads this run's orders from the broker's open-order list once it shows the expected
    /// number. The list is eventually consistent with single-order reads: a just-cancelled order
    /// can still be listed as working for a moment after its own record reports it cancelled.
    /// </summary>
    private static Task<BrokerOrder[]> OpenOrdersOnceSettledAsync(
        AlpacaBrokerageGateway gateway, Func<BrokerOrder, bool> owned, int expected, CancellationToken ct) =>
        PollAsync(
            async () => (await gateway.GetOpenOrdersAsync(ct)).Where(owned).ToArray(),
            orders => orders.Length == expected, TimeSpan.FromSeconds(20), ct);

    private static async Task<BrokerOrder> WaitForBrokerOrderAsync(
        AlpacaBrokerageGateway gateway, string clientOrderId, Func<BrokerOrder, bool> ready, CancellationToken ct)
    {
        var order = await PollAsync(
            async () => (await gateway.GetOpenOrdersAsync(ct)).SingleOrDefault(candidate => candidate.ClientOrderId == clientOrderId),
            candidate => candidate is not null && ready(candidate), TimeSpan.FromSeconds(30), ct);
        (order is not null).Should().BeTrue("the broker's open book must show the submitted order");
        return order!;
    }

    private static async Task<T> PollAsync<T>(Func<Task<T>> read, Func<T, bool> done, TimeSpan patience, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + patience;
        while (true)
        {
            var value = await read();
            if (done(value) || DateTimeOffset.UtcNow >= deadline)
                return value;
            await Task.Delay(TimeSpan.FromMilliseconds(750), ct);
        }
    }

    /// <summary>
    /// Cancels only orders this run created: those carrying its client id and the broker ids it
    /// recorded (a replacement gets a broker-assigned client id). Cleanup has its own deadline so
    /// a timed-out test still tidies up.
    /// </summary>
    private async Task CancelOwnedOrdersAsync(AlpacaBrokerageGateway gateway, string clientOrderId, HashSet<string> ownedBrokerOrderIds)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            if (!gateway.IsConnected)
                await gateway.ConnectAsync(cleanup.Token);
            if (await gateway.GetOrderForRecoveryAsync(clientOrderId, cleanup.Token) is { GatewayOrderId.Length: > 0 } current)
                ownedBrokerOrderIds.Add(current.GatewayOrderId);
            foreach (var order in await gateway.GetOpenOrdersAsync(cleanup.Token))
            {
                if (order.ClientOrderId == clientOrderId || ownedBrokerOrderIds.Contains(order.OrderId))
                    await gateway.CancelOrderAsync(order.OrderId, cleanup.Token);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Type only: a provider exception message can carry request detail.
            output.WriteLine("CLEANUP INCOMPLETE ({0}): check the paper account for open '{1}' orders.", ex.GetType().Name, Symbol);
        }
    }

    private async Task FlattenRemainderAsync(AlpacaBrokerageGateway gateway, decimal remainder, Guid localAccountId)
    {
        if (remainder <= 0m)
            return;
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            if (!gateway.IsConnected)
                await gateway.ConnectAsync(cleanup.Token);
            await gateway.SubmitOrderAsync(new OrderRequest
            {
                Symbol = Symbol,
                Side = OrderSide.Sell,
                Type = OrderType.Limit,
                Quantity = remainder,
                LimitPrice = MarketableSellFloor,
                ClientOrderId = NewClientOrderId(),
                FundAccountId = localAccountId,
                Metadata = SandboxOrderMetadata,
            }, cleanup.Token);
            output.WriteLine("Cleanup submitted a closing order for the {0} share(s) this run still held.", remainder);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            output.WriteLine("CLEANUP INCOMPLETE ({0}): the paper account may still hold {1} '{2}' share(s) from this run.",
                ex.GetType().Name, remainder, Symbol);
        }
    }
}

/// <summary>
/// Gates the paper tests that place, replace and cancel orders behind a second, separate opt-in
/// on top of the read-only sandbox opt-in.
/// </summary>
public sealed class AlpacaPaperSandboxOrdersFactAttribute : FactAttribute
{
    public AlpacaPaperSandboxOrdersFactAttribute()
    {
        if (!AlpacaPaperSandbox.IsOptedIn(AlpacaPaperSandbox.OptInVariable)
            || !AlpacaPaperSandbox.IsOptedIn(AlpacaPaperSandbox.OrdersOptInVariable))
            Skip = "Paper order-lifecycle validation places and cancels paper orders and is opt-in: set MERIDIAN_RUN_ALPACA_PAPER_SANDBOX=1, MERIDIAN_RUN_ALPACA_PAPER_SANDBOX_ORDERS=1 and the documented paper credentials.";
    }
}

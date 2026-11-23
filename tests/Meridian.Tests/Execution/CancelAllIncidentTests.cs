using System.Reflection;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using SweepGateway = Meridian.Tests.Execution.KillSwitchBrokerTruthTests.UnionSweepBrokerageGateway;

namespace Meridian.Tests.Execution;

/// <summary>
/// Synthetic kill-switch incidents during a session halt: broker refusals, execution racing a
/// cancellation, incomplete broker visibility and audit disk failure must survive honest readback.
/// These scenarios verify the OMS, normal fill handling and existing durable execution audit together.
/// </summary>
public sealed class CancelAllIncidentTests : IDisposable
{
    private static readonly Guid AccountA = Guid.Parse("f3b5ad2c-218c-4a46-9c3e-7edf10b41c61");
    private static readonly Guid AccountB = Guid.Parse("239dedde-53e3-44b7-bcc0-70f6bed31050");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", "cancel-incidents", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SessionHalt_ThirteenRefusedCancellations_ReopenEveryOrderBeyondAuditDisplayRetention()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var options = new ExecutionAuditTrailOptions(_root, InMemoryRetention: 100,
            InMemoryRetentionWindow: TimeSpan.FromTicks(1));
        string incidentId;
        await using (var audit = new ExecutionAuditTrailService(options, NullLogger<ExecutionAuditTrailService>.Instance))
        await using (var gateway = new SweepGateway())
        await using (var oms = CreateOms(gateway, audit))
        {
            await oms.StartAsync(timeout.Token);
            for (var index = 0; index < 13; index++)
            {
                await PlaceAsync(oms, gateway, $"refused-{index:D2}", AccountA, timeout.Token);
                gateway.RefuseToCancel.Add($"broker-refused-{index:D2}");
            }

            var result = await oms.CancelAllAsync(new OrderCancelAllCommand(
                OrderSubmissionAuthority.ForKillSwitch("solo-operator", "session-halt")), timeout.Token).WaitAsync(timeout.Token);

            result.EvidenceRetained.Should().BeTrue();
            result.EvidenceError.Should().BeNull();
            result.Requested.Should().Be(13);
            result.StillWorking.Should().HaveCount(13);
            result.Describe().Should().Contain("3 more");
            incidentId = result.IncidentId!;
            for (var index = 0; index < 101; index++)
                await audit.RecordAsync("test", "LaterActivity", "Observed", ct: timeout.Token);
            (await audit.GetAllAsync(timeout.Token)).Should().NotContain(entry => entry.CancelAllEvidence != null,
                "the incident reader must not depend on the bounded display cache");
        }

        await using var reopened = new ExecutionAuditTrailService(options, NullLogger<ExecutionAuditTrailService>.Instance);
        var incident = await reopened.GetCancelAllIncidentAsync(incidentId, AccountA, timeout.Token);
        incident.Should().NotBeNull();
        incident!.Orders.Should().HaveCount(13);
        incident.Orders.Select(order => order.OrderId).Should().BeEquivalentTo(
            Enumerable.Range(0, 13).Select(index => $"refused-{index:D2}"));
        incident.Orders.Should().AllSatisfy(order =>
        {
            order.Disposition.Should().Be(CancelAllOrderDisposition.StillWorking);
            order.InitialQuantity.Should().Be(10m);
            order.FilledQuantity.Should().Be(0m);
            order.RemainingQuantity.Should().Be(10m);
            order.ResidualNotional.Should().BeNull("no measured instrument and quote basis was supplied");
        });
        incident.Actor.Should().Be("solo-operator");
        incident.CorrelationId.Should().Be("session-halt");
        incident.Authority!.Origin.Should().Be(OrderCommandOrigin.KillSwitch);
        incident.GatewayId.Should().Be("test-broker");
        incident.ExecutionMode.Should().Be(ExecutionMode.Paper);
        incident.EvidenceStatus.Should().Be("Complete");
        incident.CompletedAt.Should().NotBeNull();
        incident.BrokerViewUnavailable.Should().BeFalse();
        incident.IsAccountProjection.Should().BeTrue();
        (await reopened.GetCancelAllIncidentsAsync(AccountA, ct: timeout.Token)).Should()
            .ContainSingle(item => item.IncidentId == incidentId);
    }

    [Fact]
    public async Task SessionHalt_FillsRaceCancellation_RetainsDistinctOutcomesAndBookedQuantities()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var audit = CreateAudit();
        await using var gateway = new SweepGateway();
        var portfolio = new PaperTradingPortfolio(100_000m);
        await using var oms = CreateOms(gateway, audit, portfolio);
        await oms.StartAsync(timeout.Token);
        foreach (var id in new[] { "cancelled", "filled", "partial", "rejected", "pending", "lost-ack" })
            await PlaceAsync(oms, gateway, id, AccountA, timeout.Token);
        gateway.FillDuringCancel.Add("broker-filled");
        gateway.PartialFillDuringCancel["broker-partial"] = 4m;
        gateway.TerminalRejectDuringCancel.Add("broker-rejected");
        gateway.RemainPendingAfterCancel.Add("broker-pending");
        gateway.LoseCancellationAcknowledgement.Add("broker-lost-ack");

        var result = await oms.CancelAllAsync(timeout.Token).WaitAsync(timeout.Token);
        var incident = (await audit.GetCancelAllIncidentAsync(result.IncidentId!, AccountA, timeout.Token))!;
        var orders = incident.Orders.ToDictionary(order => order.OrderId);
        orders["cancelled"].Disposition.Should().Be(CancelAllOrderDisposition.ConfirmedCancelled);
        orders["filled"].Disposition.Should().Be(CancelAllOrderDisposition.FilledDuringCancel);
        orders["filled"].FilledQuantity.Should().Be(10m);
        orders["filled"].RemainingQuantity.Should().Be(0m);
        orders["partial"].Disposition.Should().Be(CancelAllOrderDisposition.ConfirmedCancelled);
        orders["partial"].FilledQuantity.Should().Be(4m);
        orders["partial"].RemainingQuantity.Should().Be(0m, "unfilled shares were authoritatively cancelled");
        orders["rejected"].Disposition.Should().Be(CancelAllOrderDisposition.AlreadyTerminal);
        orders["rejected"].RemainingQuantity.Should().Be(0m);
        orders["pending"].Disposition.Should().Be(CancelAllOrderDisposition.StillWorking);
        orders["pending"].RemainingQuantity.Should().Be(10m);
        orders["lost-ack"].Disposition.Should().Be(CancelAllOrderDisposition.Unknown);
        orders["lost-ack"].RequiresReconciliation.Should().BeTrue();
        orders["lost-ack"].ResidualNotional.Should().BeNull();
        portfolio.Positions["AAPL"].ExactQuantity.Should().Be(14m,
            "the existing fill funnel books the two actual executions; incident observation cannot book again");
        result.Cancelled.Should().Be(2, "fills and terminal rejections remain distinct from confirmed cancellations");
    }

    [Fact]
    public async Task SessionHalt_UnknownBrokerBook_RetainsTheCoverageGapDespiteLocalCancellation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var audit = CreateAudit();
        await using var gateway = new SweepGateway { OpenOrdersFailure = new IOException("Broker listing unavailable") };
        await using var oms = CreateOms(gateway, audit);
        await oms.StartAsync(timeout.Token);
        await PlaceAsync(oms, gateway, "known", AccountA, timeout.Token);

        var result = await oms.CancelAllAsync(timeout.Token).WaitAsync(timeout.Token);
        var incident = (await audit.GetCancelAllIncidentAsync(result.IncidentId!, AccountA, timeout.Token))!;

        incident.EvidenceStatus.Should().Be("Complete", "completion refers to evidence capture, not an empty broker book");
        incident.BrokerViewUnavailable.Should().BeTrue();
        incident.BrokerViewError.Should().Contain("unavailable");
        incident.Orders.Should().ContainSingle().Which.Disposition.Should().Be(CancelAllOrderDisposition.ConfirmedCancelled);
        result.RequiresOperatorAction.Should().BeTrue();
    }

    [Fact]
    public async Task SessionHalt_MixedAccountBook_OnlyReturnsRowsOwnedByTheRequestedAccount()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var audit = CreateAudit();
        await using var gateway = new SweepGateway();
        await using var oms = CreateOms(gateway, audit);
        await oms.StartAsync(timeout.Token);
        await PlaceAsync(oms, gateway, "account-a", AccountA, timeout.Token);
        await PlaceAsync(oms, gateway, "account-b", AccountB, timeout.Token);
        gateway.BrokerOpenOrders.Add(BrokerOrder("unattributed", "broker-unattributed"));

        var result = await oms.CancelAllAsync(timeout.Token).WaitAsync(timeout.Token);
        var retained = (await audit.GetAllAsync(timeout.Token)).Single(entry => entry.Action == "CancelAllIncidentCompleted")
            .CancelAllEvidence!.Snapshot!;
        retained.Orders.Should().HaveCount(3, "full broker-only evidence must survive internally even when it has no account attribution");
        var accountA = (await audit.GetCancelAllIncidentAsync(result.IncidentId!, AccountA, timeout.Token))!;
        accountA.Orders.Should().ContainSingle().Which.OrderId.Should().Be("account-a");
        var accountB = (await audit.GetCancelAllIncidentAsync(result.IncidentId!, AccountB, timeout.Token))!;
        accountB.Orders.Should().ContainSingle().Which.OrderId.Should().Be("account-b");
        (await audit.GetCancelAllIncidentAsync(result.IncidentId!, Guid.NewGuid(), timeout.Token)).Should().BeNull();
        var noAccount = () => audit.GetCancelAllIncidentAsync(result.IncidentId!, Guid.Empty, timeout.Token);
        await noAccount.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SessionHalt_QuoteIsMeasured_ValuesOnlyKnownRemainingUsdEquityQuantity()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var audit = CreateAudit();
        await using var gateway = new SweepGateway();
        var quotes = new SessionQuote(new OrderMarketReference(150.25m, DateTimeOffset.UtcNow, "synthetic-session-ask"));
        await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
            auditTrail: audit, marketReferences: quotes, instrumentCoverage: quotes);
        await oms.StartAsync(timeout.Token);
        await PlaceAsync(oms, gateway, "working", AccountA, timeout.Token);
        gateway.RefuseToCancel.Add("broker-working");

        var result = await oms.CancelAllAsync(timeout.Token).WaitAsync(timeout.Token);
        var observed = (await audit.GetCancelAllIncidentAsync(result.IncidentId!, AccountA, timeout.Token))!.Orders.Single();

        observed.RemainingQuantity.Should().Be(10m);
        observed.ResidualNotional.Should().Be(1_502.50m);
        observed.Currency.Should().Be("USD");
        observed.ValuationCoverage.Should().Be("ObservedUsdEquityQuote");
        observed.ValuationReference.Should().Be(quotes.Reference);
        observed.ObservedAt.Should().BeOnOrAfter(quotes.Reference.ObservedAt);
    }

    [Fact]
    public async Task SessionHalt_AuditDiskFailure_StillAttemptsEveryCancellationAndReportsUnretainedEvidence()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var audit = CreateAudit();
        await using var gateway = new SweepGateway();
        await using var oms = CreateOms(gateway, audit);
        await oms.StartAsync(timeout.Token);
        await PlaceAsync(oms, gateway, "first", AccountA, timeout.Token);
        await PlaceAsync(oms, gateway, "second", AccountA, timeout.Token);
        // Reuse the WAL's I/O fault seam rather than pretending a logged audit message was durable.
        var wal = (WriteAheadLog)typeof(ExecutionAuditTrailService)
            .GetField("_wal", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(audit)!;
        wal.FlushFault = () => throw new IOException("Synthetic disk full during emergency halt");
        KillSwitchSweepResult result;
        try { result = await oms.CancelAllAsync(timeout.Token).WaitAsync(timeout.Token); }
        finally { wal.FlushFault = null; }

        gateway.CancelRequests.Should().BeEquivalentTo(["broker-first", "broker-second"]);
        gateway.BrokerOpenOrders.Should().BeEmpty();
        result.EvidenceRetained.Should().BeFalse();
        result.EvidenceError.Should().Contain("IOException");
        result.IncidentId.Should().NotBeNullOrWhiteSpace();
        result.Describe().Should().Contain("evidence was not retained");
        (await audit.GetCancelAllIncidentAsync(result.IncidentId!, AccountA, timeout.Token)).Should().BeNull();
    }

    [Theory]
    [InlineData(WalSyncMode.NoSync)]
    [InlineData(WalSyncMode.BatchedSync)]
    public async Task SessionHalt_BufferedAuditMode_CancelsWithoutClaimingDurableEvidence(WalSyncMode mode)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var audit = new ExecutionAuditTrailService(new ExecutionAuditTrailOptions(_root, SyncMode: mode),
            NullLogger<ExecutionAuditTrailService>.Instance);
        await using var gateway = new SweepGateway();
        await using var oms = CreateOms(gateway, audit);
        await oms.StartAsync(timeout.Token);
        await PlaceAsync(oms, gateway, "resting", AccountA, timeout.Token);

        var result = await oms.CancelAllAsync(timeout.Token).WaitAsync(timeout.Token);

        result.Cancelled.Should().Be(1);
        result.EvidenceRetained.Should().BeFalse();
        result.EvidenceError.Should().Contain("EveryWrite");
        gateway.BrokerOpenOrders.Should().BeEmpty();
        (await audit.GetCancelAllIncidentsAsync(AccountA, ct: timeout.Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task SessionHalt_UnmeasuredBrokerNotionalQuantity_RemainsUnknownForTrackedAndBrokerOnlyOrders()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var audit = CreateAudit();
        await using var gateway = new SweepGateway { SupportsNativeNotional = true };
        await using var oms = CreateOms(gateway, audit);
        await oms.StartAsync(timeout.Token);
        var placed = await oms.PlaceOrderAsync(new OrderRequest
        {
            ClientOrderId = "dollar-sized", Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Limit,
            Quantity = 10m, LimitPrice = 150m, FundAccountId = AccountA,
            Metadata = new Dictionary<string, string> { ["notional"] = "1000" }
        }, timeout.Token);
        placed.Success.Should().BeTrue(placed.ErrorMessage);
        gateway.BrokerOpenOrders.Add(BrokerOrder("dollar-sized", "broker-dollar-sized") with { Quantity = 0m });
        gateway.BrokerOpenOrders.Add(BrokerOrder("unattributed-dollar", "broker-unattributed-dollar") with { Quantity = 0m });
        gateway.RefuseToCancel.UnionWith(["broker-dollar-sized", "broker-unattributed-dollar"]);

        var result = await oms.CancelAllAsync(timeout.Token).WaitAsync(timeout.Token);
        var retained = (await audit.GetAllAsync(timeout.Token)).Single(entry => entry.Action == "CancelAllIncidentCompleted")
            .CancelAllEvidence!.Snapshot!;

        retained.Orders.Should().HaveCount(2);
        retained.Orders.Should().AllSatisfy(order =>
        {
            order.Disposition.Should().Be(CancelAllOrderDisposition.StillWorking);
            order.Quantity.Should().BeNull("zero is the broker adapter's missing-share-quantity sentinel");
            order.RemainingQuantity.Should().BeNull();
            order.ResidualNotional.Should().BeNull("unknown sizing cannot become a zero-exposure claim");
            order.ValuationCoverage.Should().StartWith("Unavailable");
        });
        (await audit.GetCancelAllIncidentAsync(result.IncidentId!, AccountA, timeout.Token))!.Orders
            .Should().ContainSingle().Which.OrderId.Should().Be("dollar-sized");
    }

    [Fact]
    public async Task SessionHalt_InstrumentCoverageStopsResponding_CancelsBeforeBoundedValuationAndKeepsValueUnavailable()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var audit = CreateAudit();
        await using var gateway = new SweepGateway();
        var coverage = new BlockedCoverage(() => gateway.CancelRequests.Count);
        await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
            auditTrail: audit, instrumentCoverage: coverage);
        await oms.StartAsync(timeout.Token);
        await PlaceAsync(oms, gateway, "resting", AccountA, timeout.Token);
        gateway.RefuseToCancel.Add("broker-resting");
        coverage.Block = true;
        KillSwitchSweepResult result;
        try { result = await oms.CancelAllAsync(timeout.Token).WaitAsync(timeout.Token); }
        finally { coverage.Release.TrySetResult(false); }

        gateway.CancelRequests.Should().Equal("broker-resting");
        coverage.CancellationCountOnFirstProbe.Should().Be(1, "valuation must never precede cancel dispatch");
        var order = (await audit.GetCancelAllIncidentAsync(result.IncidentId!, AccountA, timeout.Token))!.Orders.Single();
        order.RemainingQuantity.Should().Be(10m);
        order.ResidualNotional.Should().BeNull();
        order.ValuationCoverage.Should().Contain("failed");
    }

    [Fact]
    public async Task SessionHalt_AuditGateStalls_CancelsEveryOrderWithoutWaitingIndefinitelyForEvidence()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        await using var audit = CreateAudit();
        await using var gateway = new SweepGateway();
        await using var oms = CreateOms(gateway, audit);
        await oms.StartAsync(timeout.Token);
        await PlaceAsync(oms, gateway, "first", AccountA, timeout.Token);
        await PlaceAsync(oms, gateway, "second", AccountA, timeout.Token);
        var gate = (SemaphoreSlim)typeof(ExecutionAuditTrailService)
            .GetField("_persistenceGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(audit)!;
        await gate.WaitAsync(timeout.Token);
        KillSwitchSweepResult result;
        try
        {
            result = await oms.CancelAllAsync(new OrderCancelAllCommand(
                OrderSubmissionAuthority.ForKillSwitch("solo-operator", "stalled-audit")), timeout.Token).WaitAsync(timeout.Token);
        }
        finally { gate.Release(); }

        gateway.CancelRequests.Should().BeEquivalentTo(["broker-first", "broker-second"]);
        gateway.BrokerOpenOrders.Should().BeEmpty();
        result.EvidenceRetained.Should().BeFalse();
        result.EvidenceError.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(4, CancelAllOrderDisposition.ConfirmedCancelled, 1)]
    [InlineData(10, CancelAllOrderDisposition.FilledDuringCancel, 0)]
    public async Task SessionHalt_LateCumulativeFillDuringBrokerVerification_RefreshesCompletedIncidentWithoutRebooking(
        int cumulativeFill, CancelAllOrderDisposition expectedDisposition, int cancelled)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var audit = CreateAudit();
        await using var gateway = new SweepGateway { BlockVerification = true };
        var portfolio = new PaperTradingPortfolio(100_000m);
        await using var oms = CreateOms(gateway, audit, portfolio);
        await oms.StartAsync(timeout.Token);
        await PlaceAsync(oms, gateway, "late-fill", AccountA, timeout.Token);
        var cancelling = oms.CancelAllAsync(timeout.Token);
        KillSwitchSweepResult result;
        try
        {
            await gateway.VerificationEntered.Task.WaitAsync(timeout.Token);
            await gateway.PublishReportAsync(new ExecutionReport
            {
                OrderId = "broker-late-fill", GatewayOrderId = "broker-late-fill", ClientOrderId = "late-fill",
                Symbol = "AAPL", Side = OrderSide.Buy, ReportType = ExecutionReportType.Cancelled,
                OrderStatus = OrderStatus.Cancelled, OrderQuantity = 10m, FilledQuantity = cumulativeFill,
                FillPrice = 151.25m, Timestamp = DateTimeOffset.UtcNow
            });
            ExecutionReport delivered;
            do { delivered = await oms.ExecutionReports.ReadAsync(timeout.Token); }
            while (delivered.FilledQuantity == 0m);
            delivered.FilledQuantity.Should().Be(cumulativeFill);
            portfolio.Positions["AAPL"].ExactQuantity.Should().Be(cumulativeFill);
            gateway.ReleaseVerification.TrySetResult();
            result = await cancelling.WaitAsync(timeout.Token);
        }
        finally { gateway.ReleaseVerification.TrySetResult(); }

        var order = (await audit.GetCancelAllIncidentAsync(result.IncidentId!, AccountA, timeout.Token))!.Orders.Single();
        order.Disposition.Should().Be(expectedDisposition);
        order.InitialFilledQuantity.Should().Be(0m);
        order.FilledQuantity.Should().Be(cumulativeFill, "completion must include fills applied while broker verification was in flight");
        order.RemainingQuantity.Should().Be(0m);
        portfolio.Positions["AAPL"].ExactQuantity.Should().Be(cumulativeFill, "refreshing evidence cannot apply the execution again");
        result.Cancelled.Should().Be(cancelled, "a full fill learned during verification is not a confirmed cancellation");
        result.RequiresOperatorAction.Should().Be(cancelled == 0);
    }

    private ExecutionAuditTrailService CreateAudit() => new(_root, NullLogger<ExecutionAuditTrailService>.Instance);

    private static OrderManagementSystem CreateOms(SweepGateway gateway, ExecutionAuditTrailService audit,
        PaperTradingPortfolio? portfolio = null) => new(gateway, NullLogger<OrderManagementSystem>.Instance,
            auditTrail: audit, portfolioState: portfolio);

    private static async Task PlaceAsync(OrderManagementSystem oms, SweepGateway gateway, string id, Guid account,
        CancellationToken ct)
    {
        var placed = await oms.PlaceOrderAsync(new OrderRequest { ClientOrderId = id, Symbol = "AAPL",
            Side = OrderSide.Buy, Type = OrderType.Limit, Quantity = 10m, LimitPrice = 150m, FundAccountId = account }, ct);
        placed.Success.Should().BeTrue(placed.ErrorMessage);
        gateway.BrokerOpenOrders.Add(BrokerOrder(id, $"broker-{id}"));
    }

    private static BrokerOrder BrokerOrder(string clientId, string brokerId) => new()
    {
        OrderId = brokerId, ClientOrderId = clientId, Symbol = "AAPL", Side = OrderSide.Buy,
        Type = OrderType.Limit, Quantity = 10m, LimitPrice = 150m, Status = OrderStatus.Accepted
    };

    private sealed record SessionQuote(OrderMarketReference Reference) : IOrderMarketReferenceSource, IOrderInstrumentCoverageSource
    {
        public OrderMarketReference? GetReference(string symbol, OrderSide side) => Reference;
        public Task<bool> IsUsdEquityAsync(OrderRequest request, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class BlockedCoverage(Func<int> cancellationCount) : IOrderInstrumentCoverageSource
    {
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Block { get; set; }
        public int? CancellationCountOnFirstProbe { get; private set; }
        public Task<bool> IsUsdEquityAsync(OrderRequest request, CancellationToken ct = default)
        {
            if (!Block)
                return Task.FromResult(false);
            CancellationCountOnFirstProbe ??= cancellationCount();
            return Release.Task; // Deliberately ignores cancellation to model an unavailable dependency.
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}

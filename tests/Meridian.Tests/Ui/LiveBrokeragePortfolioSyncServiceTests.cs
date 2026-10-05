using System.Threading.Channels;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Risk.Rules;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class LiveBrokeragePortfolioSyncServiceTests
{
    [Fact]
    public async Task AcknowledgedWorkingOrder_MissedTerminalFillIsRecoveredWithoutRestartOrResubmission()
    {
        var fixture = new Fixture();
        const string orderId = "acknowledged-before-stream-outage";
        var accepted = new ExecutionReport
        {
            OrderId = orderId,
            ClientOrderId = orderId,
            GatewayOrderId = "broker-accepted-order",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            OrderQuantity = 4m,
            OrderStatus = OrderStatus.Accepted,
            ReportType = ExecutionReportType.New,
            FilledQuantity = 0m,
            Timestamp = fixture.Clock.Now
        };
        var filled = accepted with
        {
            OrderStatus = OrderStatus.Filled,
            ReportType = ExecutionReportType.Fill,
            FilledQuantity = 4m,
            FillPrice = 100m
        };
        var recovery = fixture.Gateway.As<IBrokerageOrderRecoveryGateway>();
        recovery.Setup(gateway => gateway.GetOrderForRecoveryAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(filled);
        fixture.Gateway.As<IExecutionGatewayModeProvider>().SetupGet(gateway => gateway.ExecutionMode)
            .Returns(ExecutionMode.Paper);
        fixture.Gateway.Setup(gateway => gateway.SubmitOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(accepted);
        // Keep the normal report consumer alive, but deliver no fill: its notification was lost
        // during the stream outage. Only the original-client-ID lookup can recover that fill.
        var reports = Channel.CreateUnbounded<ExecutionReport>();
        fixture.Gateway.Setup(gateway => gateway.StreamExecutionReportsAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => reports.Reader.ReadAllAsync(ct));
        var localPortfolio = new PaperTradingPortfolio(2000m);
        await using var oms = new OrderManagementSystem(fixture.Gateway.Object,
            NullLogger<OrderManagementSystem>.Instance, portfolioState: localPortfolio);
        var synchronization = new LiveBrokeragePortfolioSyncService(() => fixture.Gateway.Object, () => oms,
            new BrokerageExecutionReconciliationService(NullLogger<BrokerageExecutionReconciliationService>.Instance),
            fixture.Clock);
        var request = new OrderRequest
        {
            ClientOrderId = orderId,
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 4m,
            LimitPrice = 100m,
            FundAccountId = fixture.AccountId
        };
        (await oms.PlaceOrderAsync(request)).Success.Should().BeTrue();
        oms.GetOrder(orderId)!.Status.Should().Be(OrderStatus.Accepted);
        oms.GetRecoveryOrders(fixture.AccountId).Should().BeEmpty(
            "the acknowledged order has no unresolved submission; an explicit sync must still query its working state");
        fixture.Current = fixture.Snapshot(quantity: 4m, cash: 1600m);

        var result = await synchronization.SynchronizeAsync(fixture.AccountId, "broker-a");

        result.IsReady.Should().BeTrue();
        recovery.Verify(gateway => gateway.GetOrderForRecoveryAsync(orderId, It.IsAny<CancellationToken>()), Times.Once);
        oms.GetOrder(orderId)!.Status.Should().Be(OrderStatus.Filled);
        oms.GetOrder(orderId)!.FilledQuantity.Should().Be(4m);
        oms.GetOpenOrders().Should().BeEmpty();
        localPortfolio.Positions["AAPL"].ExactQuantity.Should().Be(4m);
        localPortfolio.Cash.Should().Be(1600m);
        result.Snapshot!.Positions.Single().Quantity.Should().Be(4m);
        var exposure = new AggregatePortfolioExposureProvider(Mock.Of<IAggregatePortfolioService>(),
            orderManagerAccessor: () => oms, livePortfolioAccessor: () => synchronization);
        exposure.GetSnapshot(fixture.AccountId).GrossExposure.Should().Be(400m);

        (await synchronization.SynchronizeAsync(fixture.AccountId, "broker-a")).IsReady.Should().BeTrue();
        (await oms.PlaceOrderAsync(request)).Success.Should().BeFalse();
        localPortfolio.Positions["AAPL"].ExactQuantity.Should().Be(4m);
        localPortfolio.Cash.Should().Be(1600m);
        fixture.Gateway.Verify(gateway => gateway.SubmitOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false, 0.01, true)]
    [InlineData(false, 0.02, false)]
    [InlineData(true, 0.01, true)]
    [InlineData(true, 0.02, false)]
    public async Task MonetaryReconciliation_AllowsOneCentRoundingOnly(bool holdingValue, decimal difference, bool expectedReady)
    {
        var fixture = new Fixture();
        var snapshot = fixture.Current;
        fixture.Current = snapshot with
        {
            Balance = snapshot.Balance with { Equity = snapshot.Balance.Equity + difference },
            AccountSnapshot = snapshot.AccountSnapshot! with { Equity = snapshot.Balance.Equity + difference },
            Positions = holdingValue
                ? [snapshot.Positions[0] with { MarketValue = snapshot.Positions[0].MarketValue + difference }]
                : snapshot.Positions
        };
        (await fixture.Service.SynchronizeAsync(fixture.AccountId, "broker-a")).IsReady.Should().Be(expectedReady);
    }

    [Fact]
    public async Task CompleteAccount_PublishesBalancesAndOnlyItsBrokerHoldingsToRisk()
    {
        var fixture = new Fixture();
        var status = await fixture.Service.SynchronizeAsync(fixture.AccountId, "broker-a");
        status.IsReady.Should().BeTrue();
        status.Snapshot!.Balance.Cash.Should().Be(900m);
        status.Snapshot.Balance.BuyingPower.Should().Be(800m);
        status.Snapshot.Balance.Currency.Should().Be("USD");

        var aggregate = new Mock<IAggregatePortfolioService>(MockBehavior.Strict);
        var provider = new AggregatePortfolioExposureProvider(aggregate.Object,
            orderManagerAccessor: () => fixture.Oms.Object, livePortfolioAccessor: () => fixture.Service);
        var exposure = provider.GetSnapshot(fixture.AccountId);
        exposure.GrossExposure.Should().Be(100m);
        exposure.PortfolioValue.Should().Be(1000m);
        exposure.Cash.Should().Be(900m);
        exposure.IsComplete.Should().BeTrue();
        exposure.GetSymbolExposure("AAPL").AccountNetNotional![fixture.AccountId.ToString("D")].Should().Be(100m);
        provider.GetSnapshot(Guid.NewGuid()).IsComplete.Should().BeFalse();
        provider.GetSnapshot().IsComplete.Should().BeFalse();
        aggregate.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PartialFill_ReservesOnlyRemainderAndLateFillRevokesReadiness()
    {
        var fixture = new Fixture();
        fixture.LocalOrders = [fixture.Order() with { Quantity = 10m, FilledQuantity = 4m, Status = OrderStatus.PartiallyFilled }];
        fixture.BrokerOrders = [Broker(fixture.LocalOrders[0])];
        fixture.Current = fixture.Snapshot(quantity: 4m, cash: 600m);
        var result = await fixture.Service.SynchronizeAsync(fixture.AccountId, "broker-a");
        result.IsReady.Should().BeTrue();
        var provider = new AggregatePortfolioExposureProvider(Mock.Of<IAggregatePortfolioService>(),
            orderManagerAccessor: () => fixture.Oms.Object, livePortfolioAccessor: () => fixture.Service);
        provider.GetSnapshot(fixture.AccountId).GrossExposure.Should().Be(1000m, "four held shares plus six still working");

        fixture.LocalOrders = [fixture.LocalOrders[0] with { FilledQuantity = 6m }];
        fixture.Service.GetStatus(fixture.AccountId).IsReady.Should().BeFalse();
        provider.GetSnapshot(fixture.AccountId).IsComplete.Should().BeFalse();
        provider.GetSnapshot(fixture.AccountId).GrossExposure.Should().Be(1000m, "the late fill cannot disappear between retained holdings and the unfilled reserve");
        fixture.BrokerOrders = [Broker(fixture.LocalOrders[0])];
        fixture.Current = fixture.Snapshot(quantity: 6m, cash: 400m);
        (await fixture.Service.SynchronizeAsync(fixture.AccountId, "broker-a")).IsReady.Should().BeTrue();
        provider.GetSnapshot(fixture.AccountId).GrossExposure.Should().Be(1000m);
    }

    [Fact]
    public async Task CompletedLateFill_RequiresResyncAndRestartNeverTrustsCachedPortfolio()
    {
        var fixture = new Fixture();
        await fixture.Service.SynchronizeAsync(fixture.AccountId, "broker-a");
        fixture.CompletedOrders = [fixture.Order() with { FilledQuantity = 1m, Status = OrderStatus.Filled }];
        fixture.Service.GetStatus(fixture.AccountId).IsReady.Should().BeFalse();
        var provider = new AggregatePortfolioExposureProvider(Mock.Of<IAggregatePortfolioService>(),
            orderManagerAccessor: () => fixture.Oms.Object, livePortfolioAccessor: () => fixture.Service);
        provider.GetSnapshot(fixture.AccountId).GrossExposure.Should().Be(200m, "the newly completed fill remains reserved until broker holdings include it");
        fixture.CreateService().GetStatus(fixture.AccountId).IsReady.Should().BeFalse();
    }

    [Fact]
    public async Task DisconnectReconnectAndCredentialGeneration_RequireNewSynchronization()
    {
        var fixture = new Fixture();
        await fixture.Service.SynchronizeAsync(fixture.AccountId, "broker-a");
        fixture.Connected = false;
        fixture.Service.GetStatus(fixture.AccountId).IsReady.Should().BeFalse();
        fixture.Connected = true;
        fixture.Generation++;
        fixture.Service.GetStatus(fixture.AccountId).IsReady.Should().BeFalse();
        (await fixture.Service.SynchronizeAsync(fixture.AccountId, "broker-a")).IsReady.Should().BeTrue();
        fixture.Generation++;
        fixture.Service.GetStatus(fixture.AccountId).IsReady.Should().BeFalse();
    }

    [Fact]
    public async Task RegisteredPendingDispatchOrUnscopedAdoptedFill_RevokesSnapshot()
    {
        var fixture = new Fixture();
        await fixture.Service.SynchronizeAsync(fixture.AccountId, "broker-a");
        fixture.LocalOrders = [fixture.Order() with { Status = OrderStatus.PendingNew }];
        fixture.Service.GetStatus(fixture.AccountId).IsReady.Should().BeFalse();
        fixture.LocalOrders = [];
        fixture.CompletedOrders = [fixture.Order() with { FundAccountId = null, Status = OrderStatus.Filled, FilledQuantity = 1m }];
        fixture.Service.GetStatus(fixture.AccountId).IsReady.Should().BeFalse();
    }

    [Fact]
    public async Task StaleOrFutureEvidence_FailsClosed()
    {
        var fixture = new Fixture();
        await fixture.Service.SynchronizeAsync(fixture.AccountId, "broker-a");
        fixture.Clock.Now += TimeSpan.FromSeconds(31);
        fixture.Service.GetStatus(fixture.AccountId).IsFresh.Should().BeFalse();
        (await fixture.Service.SynchronizeAsync(fixture.AccountId, "broker-a")).IsReady.Should().BeFalse();
        fixture.Current = fixture.Snapshot() with { RetrievedAt = fixture.Clock.Now.AddMinutes(1) };
        (await fixture.Service.SynchronizeAsync(fixture.AccountId, "broker-a")).IsReady.Should().BeFalse();
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("cash")]
    [InlineData("currency")]
    [InlineData("duplicate")]
    [InlineData("wrong-account")]
    [InlineData("restricted")]
    public async Task InvalidEvidence_RemainsVisibleAndBlocked(string scenario)
    {
        var fixture = new Fixture();
        var valid = fixture.Current;
        fixture.Current = scenario switch
        {
            "incomplete" => valid with { IsComplete = false },
            "cash" => valid with { Balance = valid.Balance with { Cash = 100000m } },
            "currency" => valid with { Balance = valid.Balance with { Currency = "EUR" } },
            "duplicate" => valid with { Positions = [valid.Positions[0], valid.Positions[0]] },
            "wrong-account" => valid with { Account = valid.Account with { AccountId = "broker-b" } },
            _ => valid with { AccountSnapshot = valid.AccountSnapshot! with { TradingBlocked = true } }
        };
        var result = await fixture.Service.SynchronizeAsync(fixture.AccountId, "broker-a");
        result.IsReady.Should().BeFalse();
        if (scenario == "wrong-account")
            result.Snapshot.Should().BeNull("foreign broker holdings cannot be published under this account");
        else
            result.Snapshot.Should().NotBeNull();
        result.BlockingReasons.Should().NotBeEmpty();
        fixture.Gateway.Verify(g => g.SubmitOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BrokerChangesDuringSynchronization_RemainsBlocked()
    {
        var fixture = new Fixture();
        var read = 0;
        fixture.Gateway.As<IBrokeragePortfolioSync>()
            .Setup(g => g.GetPortfolioSnapshotAsync("broker-a", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++read == 1 ? fixture.Snapshot() : fixture.Snapshot(quantity: 2m, cash: 800m));
        var result = await fixture.Service.SynchronizeAsync(fixture.AccountId, "broker-a");
        result.IsReady.Should().BeFalse();
        result.BlockingReasons.Should().Contain(r => r.Contains("changed during synchronization"));
    }

    [Fact]
    public async Task ExplicitSync_ConnectsSelectedGatewayBeforeReadingPortfolio()
    {
        var fixture = new Fixture { Connected = false };
        fixture.Gateway.Setup(g => g.ConnectAsync(It.IsAny<CancellationToken>()))
            .Callback(() => fixture.Connected = true).Returns(Task.CompletedTask);
        (await fixture.Service.SynchronizeAsync(fixture.AccountId, "broker-a")).IsReady.Should().BeTrue();
        fixture.Gateway.Verify(g => g.ConnectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Relink_InvalidatesAuthorityAndOldQueuedSyncCannotReactivateIt()
    {
        var fixture = new Fixture();
        await fixture.Service.SynchronizeAsync(fixture.AccountId, "broker-a");
        fixture.Service.InvalidateAccountBinding(fixture.AccountId, "alpaca", "broker-b");
        fixture.Service.GetStatus(fixture.AccountId).IsReady.Should().BeFalse();
        (await fixture.Service.SynchronizeAsync(fixture.AccountId, "broker-a")).IsReady.Should().BeFalse();
        fixture.Service.InvalidateAccountBinding(fixture.AccountId, "alpaca", "broker-a");
        (await fixture.Service.SynchronizeAsync(fixture.AccountId, "broker-a")).IsReady.Should().BeTrue();
    }

    [Fact]
    public async Task RelinkDuringRead_PreventsSnapshotPublicationAsReady()
    {
        var fixture = new Fixture();
        fixture.Gateway.As<IBrokeragePortfolioSync>()
            .Setup(g => g.GetPortfolioSnapshotAsync("broker-a", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                fixture.Service.InvalidateAccountBinding(fixture.AccountId, "alpaca", "broker-b");
                return fixture.Current;
            });
        (await fixture.Service.SynchronizeAsync(fixture.AccountId, "broker-a")).IsReady.Should().BeFalse();
        fixture.Service.GetStatus(fixture.AccountId).BlockingReasons.Should().Contain(r => r.Contains("binding changed"));
    }

    [Fact]
    public async Task MissingBrokerOrderAndCrossAccountBinding_CannotClearReadiness()
    {
        var fixture = new Fixture();
        fixture.LocalOrders = [fixture.Order()];
        var result = await fixture.Service.SynchronizeAsync(fixture.AccountId, "broker-a");
        result.IsReady.Should().BeFalse();
        result.Reconciliation!.Breaks.Should().Contain(b => b.Kind == BrokerageExecutionReconciliationBreakKind.MissingInBrokerage);
        fixture.LocalOrders = [];
        (await fixture.Service.SynchronizeAsync(fixture.AccountId, "broker-a")).IsReady.Should().BeTrue();
        (await fixture.Service.SynchronizeAsync(Guid.NewGuid(), "broker-a")).IsReady.Should().BeFalse();
        fixture.Service.GetStatus(fixture.AccountId).IsReady.Should().BeTrue();
    }

    [Fact]
    public async Task RiskGate_EnforcesBrokerBuyingPowerWithoutConfiguredCeilings()
    {
        var fixture = new Fixture();
        var provider = new AggregatePortfolioExposureProvider(Mock.Of<IAggregatePortfolioService>(),
            orderManagerAccessor: () => fixture.Oms.Object, livePortfolioAccessor: () => fixture.Service);
        var rule = new BrokeragePortfolioStateRule(provider);
        var order = new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 9m,
            LimitPrice = 100m,
            FundAccountId = fixture.AccountId
        };
        (await rule.EvaluateAsync(order)).Code.Should().Be("BROKER_PORTFOLIO_BLOCKED");
        await fixture.Service.SynchronizeAsync(fixture.AccountId, "broker-a");
        (await rule.EvaluateAsync(order)).Code.Should().Be("BROKER_BUYING_POWER_EXCEEDED");
        (await rule.EvaluateAsync(order with { Quantity = 1m })).IsApproved.Should().BeTrue();
        (await rule.EvaluateAsync(order with { FundAccountId = Guid.NewGuid() })).IsApproved.Should().BeFalse();
        (await rule.EvaluateAsync(order with { Metadata = new Dictionary<string, string> { ["currency"] = "EUR" } })).Code.Should().Be("BROKER_CURRENCY_MISMATCH");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task CoveredSale_CannotReuseSharesAlreadyReservedByWorkingSell(bool existingSale, bool expectedApproved)
    {
        var fixture = new Fixture();
        fixture.Current = fixture.Current with
        {
            Balance = fixture.Current.Balance with { BuyingPower = 0m },
            AccountSnapshot = fixture.Current.AccountSnapshot! with { BuyingPower = 0m }
        };
        if (existingSale)
        {
            fixture.LocalOrders = [fixture.Order() with { Side = OrderSide.Sell }];
            fixture.BrokerOrders = [Broker(fixture.LocalOrders[0])];
        }
        (await fixture.Service.SynchronizeAsync(fixture.AccountId, "broker-a")).IsReady.Should().BeTrue();
        var provider = new AggregatePortfolioExposureProvider(Mock.Of<IAggregatePortfolioService>(),
            orderManagerAccessor: () => fixture.Oms.Object, livePortfolioAccessor: () => fixture.Service);
        var rule = new BrokeragePortfolioStateRule(provider);
        var order = new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Sell,
            Type = OrderType.Limit,
            Quantity = 1m,
            LimitPrice = 100m,
            FundAccountId = fixture.AccountId
        };
        (await rule.EvaluateAsync(order)).IsApproved.Should().Be(expectedApproved);
    }

    private static BrokerOrder Broker(OrderState order) => new()
    {
        OrderId = "broker-" + order.OrderId,
        ClientOrderId = order.OrderId,
        Symbol = order.Symbol,
        Side = order.Side,
        Type = order.Type,
        Quantity = order.Quantity,
        FilledQuantity = order.FilledQuantity,
        Status = order.Status,
        LimitPrice = order.LimitPrice
    };

    private sealed class Fixture
    {
        public Guid AccountId { get; } = Guid.NewGuid();
        public TestClock Clock { get; } = new();
        public Mock<IBrokerageGateway> Gateway { get; } = new();
        public Mock<IOrderManager> Oms { get; } = new();
        public BrokeragePortfolioSnapshotDto Current { get; set; }
        public IReadOnlyList<OrderState> LocalOrders { get; set; } = [];
        public IReadOnlyList<OrderState> CompletedOrders { get; set; } = [];
        public IReadOnlyList<BrokerOrder> BrokerOrders { get; set; } = [];
        public bool Connected { get; set; } = true;
        public long Generation { get; set; } = 1;
        public LiveBrokeragePortfolioSyncService Service { get; }

        public Fixture()
        {
            Current = Snapshot();
            Gateway.As<IBrokeragePortfolioSync>().Setup(g => g.GetPortfolioSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => Current);
            var state = Gateway.As<IBrokerageConnectionState>();
            state.SetupGet(g => g.ConnectionGeneration).Returns(() => Generation);
            state.SetupGet(g => g.IsExecutionStreamHealthy).Returns(() => Connected);
            state.SetupGet(g => g.ScopeIdentity).Returns("paper:broker-a");
            Gateway.SetupGet(g => g.GatewayId).Returns("alpaca");
            Gateway.SetupGet(g => g.IsConnected).Returns(() => Connected);
            Gateway.SetupGet(g => g.BrokerDisplayName).Returns("Alpaca");
            Gateway.Setup(g => g.CheckHealthAsync(It.IsAny<CancellationToken>())).ReturnsAsync(BrokerHealthStatus.Healthy());
            Gateway.Setup(g => g.GetOpenOrdersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => BrokerOrders);
            Oms.Setup(o => o.GetOpenOrders()).Returns(() => LocalOrders);
            Oms.Setup(o => o.GetExposureReservingOrders()).Returns(() => LocalOrders);
            Oms.Setup(o => o.GetCompletedOrders(It.IsAny<int>())).Returns(() => CompletedOrders);
            Oms.Setup(o => o.GetOrder(It.IsAny<string>())).Returns((string id) => LocalOrders.Concat(CompletedOrders).FirstOrDefault(o => o.OrderId == id));
            Service = CreateService();
        }

        public LiveBrokeragePortfolioSyncService CreateService() => new(() => Gateway.Object, () => Oms.Object,
            new BrokerageExecutionReconciliationService(NullLogger<BrokerageExecutionReconciliationService>.Instance), Clock);

        public BrokeragePortfolioSnapshotDto Snapshot(decimal quantity = 1m, decimal cash = 900m) => new(
            new("alpaca", "broker-a", "Broker account", "active", "USD", Clock.Now),
            new(cash, cash + quantity * 100m, 800m, "USD"),
            [new("AAPL", quantity, 100m, 100m, quantity * 100m, 0m, "us_equity", Currency: "USD")], Clock.Now,
            new("alpaca", "broker-a", Clock.Now, "USD", "active", BrokerageMarginRegime.RegulationT,
                cash, cash + quantity * 100m, 800m), IsComplete: true);

        public OrderState Order() => new()
        {
            OrderId = "local-a",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 1m,
            LimitPrice = 100m,
            Status = OrderStatus.Accepted,
            FundAccountId = AccountId
        };
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}

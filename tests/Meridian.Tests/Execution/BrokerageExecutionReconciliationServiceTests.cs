using System.Threading.Channels;
using FluentAssertions;
using FluentAssertions.Execution;
using Meridian.Execution;
using Meridian.Execution.Services;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class BrokerageExecutionReconciliationServiceTests
{
    [Fact]
    public async Task RecoverOrdersAsync_ReconcilesOnlyRequestedAccountAndDoesNotResubmit()
    {
        var accountId = Guid.NewGuid();
        var foreignAccountId = Guid.NewGuid();
        await using var context = new RecoveryContext([
            CreateLocalOrder("ord-1") with { FundAccountId = accountId, Status = OrderStatus.PendingNew },
            CreateLocalOrder("foreign-order") with { FundAccountId = foreignAccountId, Status = OrderStatus.PendingNew }
        ]);
        context.RecoveryGateway.GetOrderForRecoveryAsync("ord-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ExecutionReport?>(CreateRecoveryReport("ord-1")));

        var service = CreateService();
        await service.RecoverOrdersAsync(context.Gateway, context.OrderManager, accountId);
        await service.RecoverOrdersAsync(context.Gateway, context.OrderManager, accountId);

        using (new AssertionScope())
        {
            context.OrderManager.GetRecoveryOrders(accountId).Should().BeEmpty();
            context.OrderManager.GetRecoveryOrders(foreignAccountId).Should().ContainSingle();
            context.OrderManager.GetOrder("ord-1")!.FilledQuantity.Should().Be(10m);
            context.Portfolio.Positions["AAPL"].Quantity.Should().Be(10L);
            context.Portfolio.Cash.Should().Be(98_500m);
            context.Gateway.ReceivedCalls().Should().NotContain(call =>
                call.GetMethodInfo().Name == nameof(IExecutionGateway.SubmitOrderAsync));
        }
        await context.RecoveryGateway.Received(1).GetOrderForRecoveryAsync("ord-1", Arg.Any<CancellationToken>());
        await context.RecoveryGateway.DidNotReceive().GetOrderForRecoveryAsync("foreign-order", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoverOrdersAsync_AcknowledgedOrderFilledDuringDisconnect_RecoversOnNextSync()
    {
        var accountId = Guid.NewGuid();
        await using var context = new RecoveryContext([
            CreateLocalOrder("ord-1") with { FundAccountId = accountId }
        ]);
        var accepted = CreateRecoveryReport("ord-1") with
        {
            OrderStatus = OrderStatus.Accepted,
            ReportType = ExecutionReportType.New,
            FilledQuantity = 0m,
            FillPrice = null
        };
        context.RecoveryGateway.GetOrderForRecoveryAsync("ord-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ExecutionReport?>(accepted),
                Task.FromResult<ExecutionReport?>(CreateRecoveryReport("ord-1")));

        var service = CreateService();
        await service.RecoverOrdersAsync(context.Gateway, context.OrderManager, accountId);
        context.OrderManager.GetRecoveryOrders(accountId).Should().BeEmpty();
        context.OrderManager.GetOpenOrders().Should().ContainSingle();

        await service.RecoverOrdersAsync(context.Gateway, context.OrderManager, accountId);

        context.OrderManager.GetOpenOrders().Should().BeEmpty();
        context.OrderManager.GetOrder("ord-1")!.Status.Should().Be(OrderStatus.Filled);
        context.Portfolio.Positions["AAPL"].Quantity.Should().Be(10L);
        context.Portfolio.Cash.Should().Be(98_500m);
        await context.RecoveryGateway.Received(2).GetOrderForRecoveryAsync("ord-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoverOrdersAsync_MissingEvidenceOrUnsupportedGateway_LeavesOrderUnresolved()
    {
        var accountId = Guid.NewGuid();
        await using var context = new RecoveryContext([
            CreateLocalOrder("ord-1") with { FundAccountId = accountId, Status = OrderStatus.PendingNew }
        ]);
        context.RecoveryGateway.GetOrderForRecoveryAsync("ord-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ExecutionReport?>(null));

        var service = CreateService();
        await service.RecoverOrdersAsync(CreateGateway([]), context.OrderManager, accountId);
        await service.RecoverOrdersAsync(context.Gateway, context.OrderManager, accountId);

        context.OrderManager.GetRecoveryOrders(accountId).Should().ContainSingle();
        context.OrderManager.GetOrder("ord-1")!.Status.Should().Be(OrderStatus.PendingNew);
        context.Portfolio.Positions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("symbol")]
    [InlineData("side")]
    [InlineData("quantity")]
    [InlineData("regressed-fill")]
    [InlineData("excess-fill")]
    [InlineData("missing-price")]
    [InlineData("incomplete-terminal-fill")]
    public async Task RecoverOrdersAsync_InconsistentEvidence_LeavesRetainedExposureBlocked(string inconsistency)
    {
        var accountId = Guid.NewGuid();
        await using var context = new RecoveryContext([
            CreateLocalOrder("ord-1") with
            {
                FundAccountId = accountId,
                Status = OrderStatus.PartiallyFilled,
                FilledQuantity = 4m
            }
        ]);
        var report = CreateRecoveryReport("ord-1");
        report = inconsistency switch
        {
            "identity" => report with { ClientOrderId = "different-order" },
            "symbol" => report with { Symbol = "MSFT" },
            "side" => report with { Side = OrderSide.Sell },
            "quantity" => report with { OrderQuantity = 12m },
            "regressed-fill" => report with { OrderStatus = OrderStatus.PartiallyFilled, FilledQuantity = 3m },
            "excess-fill" => report with { FilledQuantity = 11m },
            "missing-price" => report with { FillPrice = null },
            "incomplete-terminal-fill" => report with { FilledQuantity = 6m },
            _ => throw new InvalidOperationException()
        };
        context.RecoveryGateway.GetOrderForRecoveryAsync("ord-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ExecutionReport?>(report));

        var recover = () => CreateService().RecoverOrdersAsync(context.Gateway, context.OrderManager, accountId);

        await recover.Should().ThrowAsync<InvalidDataException>();
        context.OrderManager.GetRecoveryOrders(accountId).Should().ContainSingle();
        context.OrderManager.GetOrder("ord-1")!.FilledQuantity.Should().Be(4m);
        context.Portfolio.Positions.Should().BeEmpty();
    }

    [Fact]
    public async Task RecoverOrdersAsync_ChangedBrokerScope_RefusesBeforeLookup()
    {
        var accountId = Guid.NewGuid();
        await using var context = new RecoveryContext([
            CreateLocalOrder("ord-1") with { FundAccountId = accountId, Status = OrderStatus.PendingNew }
        ]);
        ((IBrokerageConnectionState)context.Gateway).ScopeIdentity.Returns("alpaca:live:another-account");

        var recover = () => CreateService().RecoverOrdersAsync(context.Gateway, context.OrderManager, accountId);

        await recover.Should().ThrowAsync<InvalidOperationException>();
        await context.RecoveryGateway.DidNotReceive().GetOrderForRecoveryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        context.OrderManager.GetRecoveryOrders(accountId).Should().ContainSingle();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReconcileOpenOrdersAsync_CallerCancellationIsPropagated(bool cancelHealthCheck)
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var gateway = CreateGateway([]);
        if (cancelHealthCheck)
            gateway.CheckHealthAsync(Arg.Any<CancellationToken>()).Returns(Task.FromCanceled<BrokerHealthStatus>(cts.Token));
        else
            gateway.GetOpenOrdersAsync(Arg.Any<CancellationToken>()).Returns(Task.FromCanceled<IReadOnlyList<BrokerOrder>>(cts.Token));
        var act = () => CreateService().ReconcileOpenOrdersAsync(gateway, CreateOrderManager([]), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ReconcileOpenOrdersAsync_WhenBrokerAndOmsMatch_ReturnsCleanReport()
    {
        var localOrder = CreateLocalOrder("ord-1");
        var brokerOrder = CreateBrokerOrder("broker-1", "ord-1");
        var gateway = CreateGateway([brokerOrder]);
        var orderManager = CreateOrderManager([localOrder]);
        var sut = CreateService();

        var report = await sut.ReconcileOpenOrdersAsync(gateway, orderManager);

        using (new AssertionScope())
        {
            report.IsClean.Should().BeTrue();
            report.GatewayId.Should().Be("alpaca");
            report.BrokerDisplayName.Should().Be("Alpaca Markets");
            report.Health.IsHealthy.Should().BeTrue();
            report.MatchedOpenOrders.Should().ContainSingle(match =>
                match.LocalOrderId == "ord-1" &&
                match.BrokerOrderId == "broker-1" &&
                match.Symbol == "AAPL" &&
                match.Status == OrderStatus.Accepted);
            report.Breaks.Should().BeEmpty();
            report.LocalOpenOrderCount.Should().Be(1);
            report.BrokerOpenOrderCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task ReconcileOpenOrdersAsync_AccountScope_ExcludesOtherAccountsAndUnscopedOrders()
    {
        var accountId = Guid.NewGuid();
        var localOrder = CreateLocalOrder("ord-1") with { FundAccountId = accountId };
        var otherOrder = CreateLocalOrder("ord-other") with { FundAccountId = Guid.NewGuid() };
        var orderManager = CreateOrderManager([localOrder, otherOrder, CreateLocalOrder("unscoped")]);

        var report = await CreateService().ReconcileOpenOrdersAsync(
            CreateGateway([CreateBrokerOrder("broker-1", "ord-1")]), orderManager, accountId);

        using (new AssertionScope())
        {
            report.IsClean.Should().BeTrue();
            report.FundAccountId.Should().Be(accountId);
            report.LocalOpenOrderCount.Should().Be(1);
            report.BrokerOpenOrderCount.Should().Be(1);
            report.MatchedOpenOrders.Should().ContainSingle(match => match.LocalOrderId == "ord-1");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReconcileOpenOrdersAsync_BrokerOrderMatchesForeignAccount_BlocksWithoutMutatingOms(bool retainedTerminalOrder)
    {
        var accountId = Guid.NewGuid();
        var otherAccountId = Guid.NewGuid();
        var foreignOrder = CreateLocalOrder("foreign-order") with
        {
            FundAccountId = otherAccountId,
            Status = retainedTerminalOrder ? OrderStatus.Cancelled : OrderStatus.Accepted
        };
        var orderManager = CreateOrderManager(retainedTerminalOrder ? [] : [foreignOrder]);
        orderManager.GetOrder("foreign-order").Returns(foreignOrder);
        orderManager.ClearReceivedCalls();

        var report = await CreateService().ReconcileOpenOrdersAsync(
            CreateGateway([CreateBrokerOrder("broker-foreign", "foreign-order")]), orderManager, accountId);

        using (new AssertionScope())
        {
            report.IsClean.Should().BeFalse();
            report.MatchedOpenOrders.Should().BeEmpty();
            report.LocalOpenOrderCount.Should().Be(0);
            report.BrokerOpenOrderCount.Should().Be(1);
            report.Breaks.Should().ContainSingle(item =>
                item.Kind == BrokerageExecutionReconciliationBreakKind.AccountScopeMismatch &&
                item.LocalOrderId == "foreign-order" &&
                item.LocalValue == otherAccountId.ToString() &&
                item.BrokerValue == accountId.ToString());
            orderManager.ReceivedCalls().Should().OnlyContain(call =>
                call.GetMethodInfo().Name == nameof(IOrderManager.GetExposureReservingOrders) ||
                call.GetMethodInfo().Name == nameof(IOrderManager.GetOrder));
        }
    }

    [Theory]
    [InlineData("ord-1")]
    [InlineData(" ORD-1 ")]
    public async Task ReconcileOpenOrdersAsync_DuplicateBrokerClientIds_BlockAndDoNotCountAsCleanMatches(string duplicateId)
    {
        var orderManager = CreateOrderManager([CreateLocalOrder("ord-1")]);
        var gateway = CreateGateway([
            CreateBrokerOrder("broker-1", "ord-1"),
            CreateBrokerOrder("broker-2", duplicateId)
        ]);

        var report = await CreateService().ReconcileOpenOrdersAsync(gateway, orderManager);

        using (new AssertionScope())
        {
            report.IsClean.Should().BeFalse();
            report.MatchedOpenOrders.Should().BeEmpty();
            report.BrokerOpenOrderCount.Should().Be(2);
            report.Breaks.Should().ContainSingle(item =>
                item.Kind == BrokerageExecutionReconciliationBreakKind.DuplicateBrokerClientOrderId &&
                item.ClientOrderId == "ord-1" &&
                item.BrokerValue == "broker-1, broker-2");
        }
    }

    [Fact]
    public async Task ReconcileOpenOrdersAsync_PendingCancelInExposureBook_IsStillReconciled()
    {
        var accountId = Guid.NewGuid();
        var pendingCancel = CreateLocalOrder("cancel-pending") with
        {
            FundAccountId = accountId,
            Status = OrderStatus.PendingCancel
        };
        var orderManager = CreateOrderManager([]);
        orderManager.GetExposureReservingOrders().Returns([pendingCancel]);

        var report = await CreateService().ReconcileOpenOrdersAsync(CreateGateway([]), orderManager, accountId);

        using (new AssertionScope())
        {
            report.IsClean.Should().BeFalse();
            report.LocalOpenOrderCount.Should().Be(1);
            report.Breaks.Should().ContainSingle(item =>
                item.Kind == BrokerageExecutionReconciliationBreakKind.MissingInBrokerage &&
                item.LocalOrderId == "cancel-pending");
        }
    }

    [Fact]
    public async Task ReconcileOpenOrdersAsync_PendingFillHandoffAlongsideOpenOrder_RemainsBlocked()
    {
        var localOrder = CreateLocalOrder("ord-1");
        var orderManager = CreateOrderManager([localOrder]);
        orderManager.GetExposureReservingOrders().Returns([
            localOrder,
            localOrder with { Quantity = 2m, Status = OrderStatus.PendingNew }
        ]);

        var report = await CreateService().ReconcileOpenOrdersAsync(
            CreateGateway([CreateBrokerOrder("broker-1", "ord-1")]), orderManager);

        using (new AssertionScope())
        {
            report.IsClean.Should().BeFalse();
            report.LocalOpenOrderCount.Should().Be(1);
            report.MatchedOpenOrders.Should().BeEmpty();
            report.Breaks.Should().ContainSingle(item =>
                item.Kind == BrokerageExecutionReconciliationBreakKind.PendingLocalExposure &&
                item.LocalOrderId == "ord-1");
        }
    }

    [Fact]
    public async Task ReconcileOpenOrdersAsync_BrokerQueryFails_BrokerCountRemainsUnknown()
    {
        var gateway = CreateGateway([]);
        gateway.GetOpenOrdersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<BrokerOrder>>(new IOException("Disconnected")));

        var report = await CreateService().ReconcileOpenOrdersAsync(
            gateway, CreateOrderManager([CreateLocalOrder("ord-1")]));

        using (new AssertionScope())
        {
            report.IsClean.Should().BeFalse();
            report.LocalOpenOrderCount.Should().Be(1);
            report.BrokerOpenOrderCount.Should().BeNull();
            report.Breaks.Should().ContainSingle(item =>
                item.Kind == BrokerageExecutionReconciliationBreakKind.BrokerOpenOrderQueryFailed);
        }
    }

    [Fact]
    public async Task ReconcileOpenOrdersAsync_WhenBrokerOrderIsMissingFromOms_ReportsBreak()
    {
        var brokerOrder = CreateBrokerOrder("broker-1", "broker-only");
        var gateway = CreateGateway([brokerOrder]);
        var orderManager = CreateOrderManager([]);
        var sut = CreateService();

        var report = await sut.ReconcileOpenOrdersAsync(gateway, orderManager);

        report.IsClean.Should().BeFalse();
        report.Breaks.Should().ContainSingle(breakItem =>
            breakItem.Kind == BrokerageExecutionReconciliationBreakKind.MissingInOrderManager &&
            breakItem.BrokerOrderId == "broker-1" &&
            breakItem.ClientOrderId == "broker-only");
    }

    [Fact]
    public async Task ReconcileOpenOrdersAsync_WhenOmsOrderIsMissingFromBroker_ReportsBreak()
    {
        var localOrder = CreateLocalOrder("local-only");
        var gateway = CreateGateway([]);
        var orderManager = CreateOrderManager([localOrder]);
        var sut = CreateService();

        var report = await sut.ReconcileOpenOrdersAsync(gateway, orderManager);

        report.IsClean.Should().BeFalse();
        report.Breaks.Should().ContainSingle(breakItem =>
            breakItem.Kind == BrokerageExecutionReconciliationBreakKind.MissingInBrokerage &&
            breakItem.LocalOrderId == "local-only" &&
            breakItem.BrokerOrderId == null);
    }

    [Fact]
    public async Task ReconcileOpenOrdersAsync_WhenMatchedOrderFieldsDiverge_ReportsFieldBreaks()
    {
        var localOrder = CreateLocalOrder("ord-1");
        var brokerOrder = CreateBrokerOrder(
            "broker-1",
            "ord-1") with
        {
            Quantity = 12m,
            FilledQuantity = 2m,
            Status = OrderStatus.PartiallyFilled
        };
        var gateway = CreateGateway([brokerOrder]);
        var orderManager = CreateOrderManager([localOrder]);
        var sut = CreateService();

        var report = await sut.ReconcileOpenOrdersAsync(gateway, orderManager);

        using (new AssertionScope())
        {
            report.IsClean.Should().BeFalse();
            report.MatchedOpenOrders.Should().BeEmpty();
            report.Breaks.Select(static item => item.Kind).Should().Contain([
                BrokerageExecutionReconciliationBreakKind.QuantityMismatch,
                BrokerageExecutionReconciliationBreakKind.FilledQuantityMismatch,
                BrokerageExecutionReconciliationBreakKind.StatusMismatch
            ]);
            report.Breaks.Should().Contain(item =>
                item.Kind == BrokerageExecutionReconciliationBreakKind.QuantityMismatch &&
                item.LocalValue == "10" &&
                item.BrokerValue == "12");
        }
    }

    [Fact]
    public async Task ReconcileOpenOrdersAsync_WhenBrokerOrderHasNoClientOrderId_ReportsUntraceableBreak()
    {
        var brokerOrder = CreateBrokerOrder("broker-1", clientOrderId: null);
        var gateway = CreateGateway([brokerOrder]);
        var orderManager = CreateOrderManager([]);
        var sut = CreateService();

        var report = await sut.ReconcileOpenOrdersAsync(gateway, orderManager);

        report.IsClean.Should().BeFalse();
        report.Breaks.Should().ContainSingle(item =>
            item.Kind == BrokerageExecutionReconciliationBreakKind.BrokerOrderMissingClientOrderId &&
            item.BrokerOrderId == "broker-1" &&
            item.ClientOrderId == null);
    }

    private static BrokerageExecutionReconciliationService CreateService() =>
        new(NullLogger<BrokerageExecutionReconciliationService>.Instance);

    private static ExecutionReport CreateRecoveryReport(string clientOrderId) => new()
    {
        OrderId = clientOrderId,
        ClientOrderId = clientOrderId,
        GatewayOrderId = $"broker-{clientOrderId}",
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        OrderQuantity = 10m,
        FilledQuantity = 10m,
        FillPrice = 150m,
        OrderStatus = OrderStatus.Filled,
        ReportType = ExecutionReportType.Fill
    };

    private sealed class RecoveryContext : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"meridian-recovery-reconcile-{Guid.NewGuid():N}");

        public RecoveryContext(IReadOnlyList<OrderState> retainedOrders)
        {
            Gateway = Substitute.For<IBrokerageGateway, IBrokerageOrderRecoveryGateway, IBrokerageConnectionState>();
            Gateway.GatewayId.Returns("alpaca");
            ((IBrokerageConnectionState)Gateway).ScopeIdentity.Returns("alpaca:paper:test-account");
            var reports = Channel.CreateUnbounded<ExecutionReport>();
            Gateway.StreamExecutionReportsAsync(Arg.Any<CancellationToken>())
                .Returns(call => reports.Reader.ReadAllAsync(call.Arg<CancellationToken>()));
            var store = new FileBrokerageOrderRecoveryStore(Path.Combine(_directory, "orders.json"), "alpaca");
            store.BindScope("alpaca:paper:test-account");
            foreach (var order in retainedOrders)
                store.Save(new RetainedBrokerageOrder(order, $"broker-{order.OrderId}", true));
            OrderManager = new OrderManagementSystem(Gateway, NullLogger<OrderManagementSystem>.Instance,
                portfolioState: Portfolio, recoveryStore: store);
        }

        public IBrokerageGateway Gateway { get; }
        public IBrokerageOrderRecoveryGateway RecoveryGateway => (IBrokerageOrderRecoveryGateway)Gateway;
        public OrderManagementSystem OrderManager { get; }
        public PaperTradingPortfolio Portfolio { get; } = new(100_000m);

        public async ValueTask DisposeAsync()
        {
            await OrderManager.DisposeAsync();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }

    private static IBrokerageGateway CreateGateway(IReadOnlyList<BrokerOrder> openOrders)
    {
        var gateway = Substitute.For<IBrokerageGateway>();
        gateway.GatewayId.Returns("alpaca");
        gateway.BrokerDisplayName.Returns("Alpaca Markets");
        gateway.CheckHealthAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(BrokerHealthStatus.Healthy("ready")));
        gateway.GetOpenOrdersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(openOrders));
        return gateway;
    }

    private static IOrderManager CreateOrderManager(IReadOnlyList<OrderState> openOrders)
    {
        var orderManager = Substitute.For<IOrderManager>();
        orderManager.GetOpenOrders().Returns(openOrders);
        orderManager.GetExposureReservingOrders().Returns(openOrders);
        return orderManager;
    }

    private static OrderState CreateLocalOrder(string orderId) => new()
    {
        OrderId = orderId,
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 10m,
        FilledQuantity = 0m,
        LimitPrice = 180m,
        Status = OrderStatus.Accepted,
        CreatedAt = DateTimeOffset.Parse("2026-07-05T12:00:00Z")
    };

    private static BrokerOrder CreateBrokerOrder(string brokerOrderId, string? clientOrderId) => new()
    {
        OrderId = brokerOrderId,
        ClientOrderId = clientOrderId,
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 10m,
        FilledQuantity = 0m,
        LimitPrice = 180m,
        Status = OrderStatus.Accepted,
        CreatedAt = DateTimeOffset.Parse("2026-07-05T12:00:00Z")
    };
}

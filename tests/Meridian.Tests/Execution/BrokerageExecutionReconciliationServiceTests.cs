using FluentAssertions;
using FluentAssertions.Execution;
using Meridian.Execution.Services;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class BrokerageExecutionReconciliationServiceTests
{
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

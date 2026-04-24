using FluentAssertions;
using Meridian.Execution.Exceptions;
using Meridian.Execution.Adapters;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using GatewayOrderStatus = Meridian.Execution.Models.OrderStatus;
using SdkOrderStatus = Meridian.Execution.Sdk.OrderStatus;
using SdkOrderType = Meridian.Execution.Sdk.OrderType;

namespace Meridian.Tests.Execution;

public sealed class BrokerageGatewayAdapterTests
{
    private static IBrokerageGateway CreateMockGateway(
        BrokerageCapabilities? capabilities = null)
    {
        var gateway = Substitute.For<IBrokerageGateway>();
        gateway.BrokerDisplayName.Returns("Test Broker");
        gateway.GatewayId.Returns("test");
        gateway.IsConnected.Returns(true);
        gateway.BrokerageCapabilities.Returns(capabilities ?? BrokerageCapabilities.UsEquity());
        return gateway;
    }

    private static BrokerageGatewayAdapter CreateAdapter(IBrokerageGateway? gateway = null)
    {
        return new BrokerageGatewayAdapter(
            gateway ?? CreateMockGateway(),
            NullLogger<BrokerageGatewayAdapter>.Instance);
    }

    // ── ValidateOrderAsync ─────────────────────────────────────────────

    [Fact]
    public async Task ValidateOrderAsync_RejectsUnsupportedOrderType()
    {
        var caps = BrokerageCapabilities.UsEquity();
        // Remove Market from supported types by creating custom caps
        var limitOnlyCaps = caps with
        {
            SupportedOrderTypes = new HashSet<SdkOrderType> { SdkOrderType.Limit }
        };
        await using var adapter = CreateAdapter(CreateMockGateway(limitOnlyCaps));
        var request = new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = SdkOrderType.Market,
            Quantity = 10
        };

        var result = await adapter.ValidateOrderAsync(request);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("Market");
    }

    [Theory]
    [InlineData(SdkOrderType.MarketOnOpen)]
    [InlineData(SdkOrderType.MarketOnClose)]
    [InlineData(SdkOrderType.LimitOnOpen)]
    [InlineData(SdkOrderType.LimitOnClose)]
    public async Task ValidateOrderAsync_DefaultCapabilitiesRejectSessionScopedOrderTypes(SdkOrderType orderType)
    {
        await using var adapter = CreateAdapter();
        var request = new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = orderType,
            Quantity = 10,
            LimitPrice = orderType is SdkOrderType.LimitOnOpen or SdkOrderType.LimitOnClose ? 150m : null
        };

        var result = await adapter.ValidateOrderAsync(request);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain(orderType.ToString());
    }

    [Fact]
    public async Task ValidateOrderAsync_RejectsUnsupportedTimeInForce()
    {
        var dayOnlyCaps = BrokerageCapabilities.UsEquity() with
        {
            SupportedTimeInForce = new HashSet<TimeInForce> { TimeInForce.Day }
        };
        await using var adapter = CreateAdapter(CreateMockGateway(dayOnlyCaps));
        var request = new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = SdkOrderType.Market,
            Quantity = 10,
            TimeInForce = TimeInForce.GoodTilCancelled
        };

        var result = await adapter.ValidateOrderAsync(request);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("GoodTilCancelled");
    }

    [Fact]
    public async Task ValidateOrderAsync_RejectsZeroQuantity()
    {
        await using var adapter = CreateAdapter();
        var request = new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = SdkOrderType.Market,
            Quantity = 0
        };

        var result = await adapter.ValidateOrderAsync(request);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("quantity");
    }

    [Fact]
    public async Task ValidateOrderAsync_RejectsLimitOrderWithoutLimitPrice()
    {
        await using var adapter = CreateAdapter();
        var request = new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = SdkOrderType.Limit,
            Quantity = 10
            // LimitPrice intentionally omitted
        };

        var result = await adapter.ValidateOrderAsync(request);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("limit price");
    }

    [Fact]
    public async Task ValidateOrderAsync_RejectsStopLimitOrderWithoutStopPrice()
    {
        await using var adapter = CreateAdapter();
        var request = new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = SdkOrderType.StopLimit,
            Quantity = 10,
            LimitPrice = 150m
            // StopPrice intentionally omitted
        };

        var result = await adapter.ValidateOrderAsync(request);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("stop price");
    }

    [Fact]
    public async Task ValidateOrderAsync_AcceptsValidMarketOrder()
    {
        await using var adapter = CreateAdapter();
        var request = new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = SdkOrderType.Market,
            Quantity = 5
        };

        var result = await adapter.ValidateOrderAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateOrderAsync_RejectsLimitOnCloseWithoutLimitPrice()
    {
        var caps = BrokerageCapabilities.UsEquity() with
        {
            SupportedOrderTypes = new HashSet<SdkOrderType>(BrokerageCapabilities.UsEquity().SupportedOrderTypes)
            {
                SdkOrderType.LimitOnClose
            }
        };
        await using var adapter = CreateAdapter(CreateMockGateway(caps));
        var request = new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = SdkOrderType.LimitOnClose,
            Quantity = 10
        };

        var result = await adapter.ValidateOrderAsync(request);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("limit price");
    }

    // ── SubmitAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitAsync_ThrowsUnsupportedOrderRequestException_WhenValidationFails()
    {
        await using var adapter = CreateAdapter();
        var request = new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = SdkOrderType.Market,
            Quantity = 0  // invalid
        };

        Func<Task> act = async () => await adapter.SubmitAsync(request);

        await act.Should().ThrowAsync<UnsupportedOrderRequestException>();
    }

    [Fact]
    public async Task SubmitAsync_DoesNotForwardUnsupportedSessionScopedOrderType()
    {
        var gateway = CreateMockGateway();
        await using var adapter = CreateAdapter(gateway);
        var request = new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = SdkOrderType.MarketOnClose,
            Quantity = 1m
        };

        Func<Task> act = async () => await adapter.SubmitAsync(request);

        await act.Should().ThrowAsync<UnsupportedOrderRequestException>()
            .WithMessage("*MarketOnClose*");
        await gateway.DidNotReceive()
            .SubmitOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_MapsExecutionReportToOrderAcknowledgement_WithClientOrderId()
    {
        var gateway = CreateMockGateway();
        var report = new ExecutionReport
        {
            OrderId = "broker-123",
            ReportType = ExecutionReportType.New,
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            OrderStatus = SdkOrderStatus.Accepted,
            ClientOrderId = "client-abc",
            GatewayOrderId = "broker-123",
        };
        gateway.SubmitOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(report);

        await using var adapter = new BrokerageGatewayAdapter(gateway, NullLogger<BrokerageGatewayAdapter>.Instance);
        var request = new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = SdkOrderType.Market,
            Quantity = 10,
            ClientOrderId = "client-abc"
        };

        var ack = await adapter.SubmitAsync(request);

        ack.OrderId.Should().Be("broker-123");
        ack.ClientOrderId.Should().Be("client-abc");
        ack.Symbol.Should().Be("AAPL");
        ack.Status.Should().Be(GatewayOrderStatus.Accepted);
    }

    // ── StreamOrderUpdatesAsync ────────────────────────────────────────

    [Fact]
    public async Task StreamOrderUpdatesAsync_MapsClientOrderId_FromReportClientOrderId()
    {
        var gateway = CreateMockGateway();
        var report = new ExecutionReport
        {
            OrderId = "broker-456",
            ReportType = ExecutionReportType.Fill,
            Symbol = "TSLA",
            Side = OrderSide.Sell,
            OrderStatus = SdkOrderStatus.Filled,
            FilledQuantity = 5m,
            ClientOrderId = "my-client-id",
            GatewayOrderId = "broker-456",
        };
        gateway.StreamExecutionReportsAsync(Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerableOf(report));

        await using var adapter = new BrokerageGatewayAdapter(gateway, NullLogger<BrokerageGatewayAdapter>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var updates = new List<OrderStatusUpdate>();
        await foreach (var update in adapter.StreamOrderUpdatesAsync(cts.Token))
        {
            updates.Add(update);
        }

        updates.Should().HaveCount(1);
        updates[0].ClientOrderId.Should().Be("my-client-id");
        updates[0].OrderId.Should().Be("broker-456");
        updates[0].FilledQuantity.Should().Be(5m);
        updates[0].Status.Should().Be(GatewayOrderStatus.Filled);
    }

    [Fact]
    public async Task StreamOrderUpdatesAsync_FallsBackToOrderId_WhenClientOrderIdIsNull()
    {
        var gateway = CreateMockGateway();
        var report = new ExecutionReport
        {
            OrderId = "broker-789",
            ReportType = ExecutionReportType.New,
            Symbol = "MSFT",
            Side = OrderSide.Buy,
            OrderStatus = SdkOrderStatus.Accepted,
            FilledQuantity = 0m,
            // ClientOrderId intentionally null
        };
        gateway.StreamExecutionReportsAsync(Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerableOf(report));

        await using var adapter = new BrokerageGatewayAdapter(gateway, NullLogger<BrokerageGatewayAdapter>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var updates = new List<OrderStatusUpdate>();
        await foreach (var update in adapter.StreamOrderUpdatesAsync(cts.Token))
        {
            updates.Add(update);
        }

        updates.Should().HaveCount(1);
        updates[0].ClientOrderId.Should().Be("broker-789");
    }

    [Fact]
    public async Task StreamOrderUpdatesAsync_PreservesFractionalFilledQuantity()
    {
        var gateway = CreateMockGateway();
        var report = new ExecutionReport
        {
            OrderId = "broker-frac",
            ReportType = ExecutionReportType.Fill,
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            OrderStatus = SdkOrderStatus.Filled,
            FilledQuantity = 1.5m,  // fractional — should throw
        };
        gateway.StreamExecutionReportsAsync(Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerableOf(report));

        await using var adapter = new BrokerageGatewayAdapter(gateway, NullLogger<BrokerageGatewayAdapter>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var updates = new List<OrderStatusUpdate>();
        await foreach (var update in adapter.StreamOrderUpdatesAsync(cts.Token))
        {
            updates.Add(update);
        }

        updates.Should().ContainSingle();
        updates[0].FilledQuantity.Should().Be(1.5m);
        updates[0].Status.Should().Be(GatewayOrderStatus.Filled);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static async IAsyncEnumerable<T> AsyncEnumerableOf<T>(params T[] items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }
}

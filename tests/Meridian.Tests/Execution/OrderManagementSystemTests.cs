using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

// Disambiguate the two PaperTradingGateway types that exist in this project
using ExecutionGateway = Meridian.Execution.PaperTradingGateway;

namespace Meridian.Tests.Execution;

/// <summary>
/// Tests for <see cref="OrderManagementSystem"/>, focused on completed-order tracking
/// via <see cref="IOrderManager.GetCompletedOrders"/>.
/// </summary>
public sealed class OrderManagementSystemTests : IDisposable
{
    private readonly ExecutionGateway _gateway;
    private readonly OrderManagementSystem _oms;

    public OrderManagementSystemTests()
    {
        _gateway = new ExecutionGateway(NullLogger<ExecutionGateway>.Instance);
        _oms = new OrderManagementSystem(_gateway, NullLogger<OrderManagementSystem>.Instance);
    }

    public void Dispose()
    {
        _oms.Dispose();
    }

    // ---- GetCompletedOrders — no orders yet ----

    [Fact]
    public void GetCompletedOrders_WhenNoOrdersExist_ReturnsEmpty()
    {
        var completed = _oms.GetCompletedOrders();

        completed.Should().BeEmpty();
    }

    // ---- GetCompletedOrders — open orders are excluded ----

    [Fact]
    public async Task GetCompletedOrders_WhenOnlyOpenOrdersExist_ReturnsEmpty()
    {
        // Limit orders are accepted but NOT immediately filled by PaperTradingGateway,
        // so they remain in the open/accepted state.
        var request = new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10,
            LimitPrice = 150m
        };

        await _oms.PlaceOrderAsync(request);

        var completed = _oms.GetCompletedOrders();

        completed.Should().BeEmpty("an accepted (open) limit order must not appear in the completed feed");
    }

    // ---- GetCompletedOrders — filled orders are included ----

    [Fact]
    public async Task GetCompletedOrders_AfterMarketFill_ReturnsFilled()
    {
        // PaperTradingGateway fills market orders immediately.
        var request = new OrderRequest
        {
            Symbol = "MSFT",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 5
        };

        var result = await _oms.PlaceOrderAsync(request);
        result.Success.Should().BeTrue();

        var completed = _oms.GetCompletedOrders();
        completed.Should().ContainSingle(o =>
            o.Symbol == "MSFT" && o.Status == OrderStatus.Filled,
            "an immediately-filled market order should appear in the completed feed");

        var open = _oms.GetOpenOrders();
        open.Should().NotContain(o => o.Symbol == "MSFT",
            "a filled order must not remain in the open order list");
    }

    // ---- GetCompletedOrders — cancelled orders are included ----

    [Fact]
    public async Task GetCompletedOrders_AfterCancel_ReturnsCancelledOrder()
    {
        var request = new OrderRequest
        {
            Symbol = "TSLA",
            Side = OrderSide.Sell,
            Type = OrderType.Limit,
            Quantity = 3,
            LimitPrice = 200m
        };

        var placeResult = await _oms.PlaceOrderAsync(request);
        placeResult.Success.Should().BeTrue();

        var cancelResult = await _oms.CancelOrderAsync(placeResult.OrderId);
        cancelResult.Success.Should().BeTrue();

        var completed = _oms.GetCompletedOrders();
        completed.Should().ContainSingle(o =>
            o.OrderId == placeResult.OrderId &&
            o.Status == OrderStatus.Cancelled);
    }

    // ---- GetCompletedOrders — take limit is respected ----

    [Fact]
    public async Task GetCompletedOrders_TakeLimit_ReturnsAtMostTake()
    {
        // Place and cancel 5 orders
        for (var i = 0; i < 5; i++)
        {
            var r = await _oms.PlaceOrderAsync(new OrderRequest
            {
                Symbol = $"SYM{i:D2}",
                Side = OrderSide.Buy,
                Type = OrderType.Limit,
                Quantity = 1,
                LimitPrice = 100m
            });
            await _oms.CancelOrderAsync(r.OrderId);
        }

        var completed = _oms.GetCompletedOrders(take: 3);

        completed.Should().HaveCount(3);
    }

    // ---- GetCompletedOrders — default take returns no more than 20 ----

    [Fact]
    public async Task GetCompletedOrders_DefaultTake_ReturnsAtMost20()
    {
        for (var i = 0; i < 25; i++)
        {
            var r = await _oms.PlaceOrderAsync(new OrderRequest
            {
                Symbol = $"SYM{i:D2}",
                Side = OrderSide.Buy,
                Type = OrderType.Limit,
                Quantity = 1,
                LimitPrice = 50m
            });
            await _oms.CancelOrderAsync(r.OrderId);
        }

        var completed = _oms.GetCompletedOrders();

        completed.Should().HaveCountLessThanOrEqualTo(20);
    }

    // ---- GetCompletedOrders — most recent first ----

    [Fact]
    public async Task GetCompletedOrders_IsOrderedByCompletionTimeDescending()
    {
        var ids = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var r = await _oms.PlaceOrderAsync(new OrderRequest
            {
                Symbol = $"SYM{i:D2}",
                Side = OrderSide.Buy,
                Type = OrderType.Limit,
                Quantity = 1,
                LimitPrice = 100m
            });
            await _oms.CancelOrderAsync(r.OrderId);
            ids.Add(r.OrderId);
        }

        var completed = _oms.GetCompletedOrders();
        var completedIds = completed.Select(o => o.OrderId).ToList();

        // Last cancelled should appear first
        completedIds[0].Should().Be(ids[^1]);
    }
}

// ---------------------------------------------------------------------------
// Security Master gate tests (separate fixture to keep constructor clean)
// ---------------------------------------------------------------------------

public sealed class OrderManagementSystemGateTests : IDisposable
{
    private readonly ExecutionGateway _gateway;

    public OrderManagementSystemGateTests()
    {
        _gateway = new ExecutionGateway(NullLogger<ExecutionGateway>.Instance);
    }

    public void Dispose() { }

    [Fact]
    public async Task PlaceOrderAsync_WhenGateApproves_OrderIsAccepted()
    {
        var gate = new ApproveAllGate();
        using var oms = new OrderManagementSystem(_gateway, NullLogger<OrderManagementSystem>.Instance,
            securityMasterGate: gate);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 5
        });

        result.Success.Should().BeTrue("the gate approved the symbol");
        gate.CheckCount.Should().Be(1);
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenGateRejects_ReturnsFailureWithoutSubmittingToGateway()
    {
        var gate = new RejectAllGate("UNKNWN is not in Security Master");
        using var oms = new OrderManagementSystem(_gateway, NullLogger<OrderManagementSystem>.Instance,
            securityMasterGate: gate);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "UNKNWN",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 10
        });

        result.Success.Should().BeFalse("the gate rejected the symbol");
        result.ErrorMessage.Should().Contain("UNKNWN");
        oms.GetOpenOrders().Should().BeEmpty("rejected orders must not be tracked");
    }

    [Fact]
    public async Task PlaceOrderAsync_WithNoGateWired_AcceptsAnySymbol()
    {
        using var oms = new OrderManagementSystem(_gateway, NullLogger<OrderManagementSystem>.Instance);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "ANYTHING",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1
        });

        // Gateway fills market orders immediately, so no rejection from missing gate
        result.Success.Should().BeTrue("no gate means any symbol is accepted");
    }

    // ---- Stubs ----

    private sealed class ApproveAllGate : ISecurityMasterGate
    {
        public int CheckCount { get; private set; }

        public Task<SecurityMasterGateResult> CheckAsync(string symbol, CancellationToken ct = default)
        {
            CheckCount++;
            return Task.FromResult(new SecurityMasterGateResult(true));
        }
    }

    private sealed class RejectAllGate : ISecurityMasterGate
    {
        private readonly string _reason;

        public RejectAllGate(string reason) => _reason = reason;

        public Task<SecurityMasterGateResult> CheckAsync(string symbol, CancellationToken ct = default)
            => Task.FromResult(new SecurityMasterGateResult(false, _reason));
    }
}

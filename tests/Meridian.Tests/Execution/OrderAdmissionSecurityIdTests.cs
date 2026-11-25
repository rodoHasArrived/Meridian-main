using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Execution;

/// <summary>
/// Security Master review F2: the SecurityId resolved by the admission gate travels on the order
/// so fills post to SecurityId-keyed ledger accounts.
/// </summary>
public sealed class OrderAdmissionSecurityIdTests
{
    private static readonly Guid AaplSecurityId = Guid.Parse("f2f2f2f2-0000-4000-8000-0000000000c1");

    [Fact]
    public async Task PlaceOrderAsync_GateResolvesSecurity_StampsSecurityIdOnTheDispatchedOrder()
    {
        var (gateway, submitted) = CapturingGateway();
        using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
            securityMasterGate: new ResolvingGate(AaplSecurityId));
        await oms.StartAsync();

        var result = await oms.PlaceOrderAsync(LimitBuy());

        result.Success.Should().BeTrue();
        submitted.Should().ContainSingle().Which.SecurityId.Should().Be(AaplSecurityId);
    }

    [Fact]
    public async Task PlaceOrderAsync_CallerSecurityDisagreesWithGate_IsRefusedBeforeDispatch()
    {
        var (gateway, submitted) = CapturingGateway();
        using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
            securityMasterGate: new ResolvingGate(AaplSecurityId));
        await oms.StartAsync();

        var result = await oms.PlaceOrderAsync(LimitBuy() with { SecurityId = Guid.NewGuid() });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("does not match the Security Master identity");
        submitted.Should().BeEmpty();
    }

    [Fact]
    public void ValidatedOrder_WithSecurityId_KeepsPricingAndRejectsEmptyIdentity()
    {
        ValidatedOrder.TryCreate(LimitBuy(), out var order, out _).Should().BeTrue();

        var stamped = order!.WithSecurityId(AaplSecurityId);

        stamped.Request.SecurityId.Should().Be(AaplSecurityId);
        stamped.Pricing.Should().BeSameAs(order.Pricing);
        stamped.WithSecurityId(AaplSecurityId).Should().BeSameAs(stamped);
        var empty = () => order.WithSecurityId(Guid.Empty);
        empty.Should().Throw<ArgumentException>();
    }

    private static OrderRequest LimitBuy() => new()
    {
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 10m,
        LimitPrice = 100m
    };

    private static (IExecutionGateway Gateway, List<OrderRequest> Submitted) CapturingGateway()
    {
        var submitted = new List<OrderRequest>();
        var gateway = Substitute.For<IExecutionGateway>();
        gateway.SubmitOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var request = callInfo.Arg<OrderRequest>();
                submitted.Add(request);
                return new ExecutionReport
                {
                    OrderId = request.ClientOrderId ?? "ord-1",
                    ClientOrderId = request.ClientOrderId,
                    ReportType = ExecutionReportType.New,
                    Symbol = request.Symbol,
                    Side = request.Side,
                    OrderStatus = OrderStatus.Accepted,
                    OrderQuantity = request.Quantity,
                    Timestamp = DateTimeOffset.UtcNow
                };
            });
        return (gateway, submitted);
    }

    private sealed class ResolvingGate(Guid securityId) : ISecurityMasterGate
    {
        public Task<SecurityMasterGateResult> CheckAsync(string symbol, CancellationToken ct = default)
            => Task.FromResult(new SecurityMasterGateResult(true) { SecurityId = securityId });
    }
}

using FluentAssertions;
using Meridian.Execution.Adapters;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using GatewayExecutionMode = Meridian.Execution.Models.ExecutionMode;

namespace Meridian.Tests.Execution;

/// <summary>
/// Verifies that the live <c>IOrderGateway</c> surface exposed to DI consumers cannot be used to
/// submit or cancel live orders directly — those lifecycle operations are owned by the OMS gate
/// stack (<c>IOrderManager</c>). Read-only broker metadata is still delegated so health, capability,
/// and blotter surfaces keep working.
/// </summary>
public sealed class OmsGovernedBrokerageOrderGatewayTests
{
    [Fact]
    public void Constructor_WithNullAdapter_Throws()
    {
        var act = () => new OmsGovernedBrokerageOrderGateway(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task SubmitAsync_IsBlocked_AndNeverReachesTheBroker()
    {
        var gateway = CreateMockGateway();
        await using var sut = CreateSut(gateway);

        Func<Task> act = async () => await sut.SubmitAsync(MarketBuy());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Order Management System*");
        await gateway.DidNotReceive().SubmitOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelAsync_IsBlocked_AndNeverReachesTheBroker()
    {
        var gateway = CreateMockGateway();
        await using var sut = CreateSut(gateway);

        Func<Task> act = async () => await sut.CancelAsync("order-1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Order Management System*");
        await gateway.DidNotReceive().CancelOrderAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadOnlyMetadata_DelegatesToUnderlyingBroker()
    {
        await using var sut = CreateSut();

        sut.BrokerName.Should().Be("Test Broker");
        sut.Mode.Should().Be(GatewayExecutionMode.Live);
        sut.Capabilities.Should().NotBeNull();
    }

    [Fact]
    public async Task ValidateOrderAsync_RemainsAvailableForPreflight()
    {
        await using var sut = CreateSut();

        var result = await sut.ValidateOrderAsync(MarketBuy());

        result.IsValid.Should().BeTrue();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static OmsGovernedBrokerageOrderGateway CreateSut(IBrokerageGateway? gateway = null)
    {
        var adapter = new BrokerageGatewayAdapter(
            gateway ?? CreateMockGateway(),
            NullLogger<BrokerageGatewayAdapter>.Instance);
        return new OmsGovernedBrokerageOrderGateway(adapter);
    }

    private static IBrokerageGateway CreateMockGateway()
    {
        var gateway = Substitute.For<IBrokerageGateway>();
        gateway.BrokerDisplayName.Returns("Test Broker");
        gateway.GatewayId.Returns("test");
        gateway.IsConnected.Returns(true);
        gateway.BrokerageCapabilities.Returns(BrokerageCapabilities.UsEquity());
        return gateway;
    }

    private static OrderRequest MarketBuy(string symbol = "AAPL", decimal qty = 10m) => new()
    {
        Symbol = symbol,
        Side = OrderSide.Buy,
        Type = OrderType.Market,
        Quantity = qty,
    };
}

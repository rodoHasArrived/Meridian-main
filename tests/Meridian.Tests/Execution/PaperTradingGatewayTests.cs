using FluentAssertions;
using Meridian.Execution.Adapters;
using Meridian.Execution.Exceptions;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class PaperTradingGatewayTests
{
    [Fact]
    public async Task ValidateOrderAsync_RejectsStopLimitWithoutPrices()
    {
        await using var gateway = new PaperTradingGateway(NullLogger<PaperTradingGateway>.Instance);
        var request = new OrderRequest
        {
            Symbol = "SPY",
            Side = OrderSide.Buy,
            Type = OrderType.StopLimit,
            Quantity = 10,
            StopPrice = 401m
        };

        var result = await gateway.ValidateOrderAsync(request);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("limit price");
    }

    [Fact]
    public async Task SubmitAsync_UsesValidationAndThrowsForUnsupportedRequests()
    {
        await using var gateway = new PaperTradingGateway(NullLogger<PaperTradingGateway>.Instance);
        var request = new OrderRequest
        {
            Symbol = "SPY",
            Side = OrderSide.Buy,
            Type = OrderType.StopMarket,
            Quantity = 10
        };

        Func<Task> submit = async () => await gateway.SubmitAsync(request);

        await submit.Should().ThrowAsync<UnsupportedOrderRequestException>();
    }

    [Fact]
    public async Task SubmitAsync_AcceptsStopLimitOrders_WhenFullySpecified()
    {
        await using var gateway = new PaperTradingGateway(NullLogger<PaperTradingGateway>.Instance);
        var request = new OrderRequest
        {
            Symbol = "SPY",
            Side = OrderSide.Buy,
            Type = OrderType.StopLimit,
            Quantity = 10,
            LimitPrice = 402m,
            StopPrice = 401m,
            TimeInForce = TimeInForce.GoodTilCancelled
        };

        var acknowledgement = await gateway.SubmitAsync(request);

        acknowledgement.Status.Should().Be(Meridian.Execution.Models.OrderStatus.Accepted);
        gateway.Capabilities.SupportedOrderTypes.Should().Contain(Meridian.Execution.Sdk.OrderType.StopLimit);
        gateway.Capabilities.SupportedTimeInForce.Should().Contain(TimeInForce.GoodTilCancelled);
    }
}

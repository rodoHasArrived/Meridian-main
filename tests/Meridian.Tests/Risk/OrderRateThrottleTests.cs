using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Risk.Rules;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Risk;

public sealed class OrderRateThrottleTests
{
    private static OrderRateThrottle CreateSut(int maxOrdersPerMinute = 10) =>
        new(maxOrdersPerMinute, NullLogger<OrderRateThrottle>.Instance);

    private static OrderRequest CreateOrder(string symbol = "AAPL") => new()
    {
        Symbol = symbol,
        Side = OrderSide.Buy,
        Type = OrderType.Market,
        Quantity = 1m,
    };

    [Fact]
    public void RuleName_ReturnsOrderRateThrottle()
    {
        var sut = CreateSut();

        sut.RuleName.Should().Be("OrderRateThrottle");
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var act = () => new OrderRateThrottle(10, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task EvaluateAsync_FirstOrder_IsApproved()
    {
        var sut = CreateSut(maxOrdersPerMinute: 5);

        var result = await sut.EvaluateAsync(CreateOrder());

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_WhenUnderLimit_ReturnsApproved()
    {
        var sut = CreateSut(maxOrdersPerMinute: 5);

        // Submit 4 orders (under the limit of 5)
        for (var i = 0; i < 4; i++)
            await sut.EvaluateAsync(CreateOrder());

        var result = await sut.EvaluateAsync(CreateOrder());

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_WhenAtLimit_ReturnsRejected()
    {
        var sut = CreateSut(maxOrdersPerMinute: 3);

        // Fill up to the limit
        for (var i = 0; i < 3; i++)
            await sut.EvaluateAsync(CreateOrder());

        // This order pushes count to 3 which equals the limit → rejected
        var result = await sut.EvaluateAsync(CreateOrder());

        result.IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_WhenRejected_IncludesCountInReason()
    {
        var sut = CreateSut(maxOrdersPerMinute: 2);

        for (var i = 0; i < 2; i++)
            await sut.EvaluateAsync(CreateOrder());

        var result = await sut.EvaluateAsync(CreateOrder());

        result.RejectReason.Should().Contain("2").And.Contain("limit");
    }

    [Fact]
    public async Task EvaluateAsync_WithZeroLimit_FirstOrderIsRejected()
    {
        // maxOrdersPerMinute=0: 0 >= 0 is true, so even the first order is rejected
        var sut = CreateSut(maxOrdersPerMinute: 0);

        var result = await sut.EvaluateAsync(CreateOrder());

        result.IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_WhenApproved_EnqueuesOrderForRateTracking()
    {
        var sut = CreateSut(maxOrdersPerMinute: 2);

        // First approved order is tracked
        await sut.EvaluateAsync(CreateOrder());

        // Second order is the limit → rejected, proving the first was tracked
        await sut.EvaluateAsync(CreateOrder());
        var result = await sut.EvaluateAsync(CreateOrder());

        result.IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_WithLimitOfOne_SecondOrderWithinMinuteIsRejected()
    {
        var sut = CreateSut(maxOrdersPerMinute: 1);

        var first = await sut.EvaluateAsync(CreateOrder());
        var second = await sut.EvaluateAsync(CreateOrder());

        first.IsApproved.Should().BeTrue();
        second.IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_ConsecutiveRejectsReturnRejected()
    {
        var sut = CreateSut(maxOrdersPerMinute: 1);

        await sut.EvaluateAsync(CreateOrder()); // fills limit

        var result1 = await sut.EvaluateAsync(CreateOrder());
        var result2 = await sut.EvaluateAsync(CreateOrder());

        result1.IsApproved.Should().BeFalse();
        result2.IsApproved.Should().BeFalse();
    }
}

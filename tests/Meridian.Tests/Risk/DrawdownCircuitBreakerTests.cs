using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Risk.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.Risk;

public sealed class DrawdownCircuitBreakerTests
{
    private const decimal DefaultInitialCapital = 100_000m;
    private const decimal DefaultMaxDrawdownPercent = 10m;

    private static DrawdownCircuitBreaker CreateSut(
        IPositionTracker? positionTracker = null,
        decimal initialCapital = DefaultInitialCapital,
        decimal maxDrawdownPercent = DefaultMaxDrawdownPercent)
    {
        positionTracker ??= new Mock<IPositionTracker>().Object;
        return new DrawdownCircuitBreaker(
            positionTracker,
            initialCapital,
            maxDrawdownPercent,
            NullLogger<DrawdownCircuitBreaker>.Instance);
    }

    private static OrderRequest CreateOrder(string symbol = "AAPL") => new()
    {
        Symbol = symbol,
        Side = OrderSide.Buy,
        Type = OrderType.Market,
        Quantity = 10m,
    };

    [Fact]
    public void RuleName_ReturnsDrawdownCircuitBreaker()
    {
        var sut = CreateSut();

        sut.RuleName.Should().Be("DrawdownCircuitBreaker");
    }

    [Fact]
    public void Constructor_WithNullPositionTracker_ThrowsArgumentNullException()
    {
        var act = () => new DrawdownCircuitBreaker(
            null!,
            DefaultInitialCapital,
            DefaultMaxDrawdownPercent,
            NullLogger<DrawdownCircuitBreaker>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("positionTracker");
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var act = () => new DrawdownCircuitBreaker(
            new Mock<IPositionTracker>().Object,
            DefaultInitialCapital,
            DefaultMaxDrawdownPercent,
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task EvaluateAsync_WhenDrawdownBelowThreshold_ReturnsApproved()
    {
        // 2% drawdown vs 10% threshold → approve
        var tracker = new Mock<IPositionTracker>();
        tracker.Setup(t => t.GetPortfolioValue()).Returns(98_000m);
        var sut = CreateSut(tracker.Object, initialCapital: 100_000m, maxDrawdownPercent: 10m);

        var result = await sut.EvaluateAsync(CreateOrder());

        result.IsApproved.Should().BeTrue();
        result.RejectReason.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateAsync_WhenDrawdownExceedsThreshold_ReturnsRejected()
    {
        // 15% drawdown vs 10% threshold → reject
        var tracker = new Mock<IPositionTracker>();
        tracker.Setup(t => t.GetPortfolioValue()).Returns(85_000m);
        var sut = CreateSut(tracker.Object, initialCapital: 100_000m, maxDrawdownPercent: 10m);

        var result = await sut.EvaluateAsync(CreateOrder());

        result.IsApproved.Should().BeFalse();
        result.RejectReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task EvaluateAsync_WhenDrawdownEqualsThreshold_ReturnsRejected()
    {
        // Exactly 10% drawdown vs 10% threshold — boundary condition, >= triggers rejection
        var tracker = new Mock<IPositionTracker>();
        tracker.Setup(t => t.GetPortfolioValue()).Returns(90_000m);
        var sut = CreateSut(tracker.Object, initialCapital: 100_000m, maxDrawdownPercent: 10m);

        var result = await sut.EvaluateAsync(CreateOrder());

        result.IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_WhenPortfolioAboveInitialCapital_ReturnsApproved()
    {
        // Negative drawdown (portfolio gained value) → approve
        var tracker = new Mock<IPositionTracker>();
        tracker.Setup(t => t.GetPortfolioValue()).Returns(115_000m);
        var sut = CreateSut(tracker.Object, initialCapital: 100_000m, maxDrawdownPercent: 10m);

        var result = await sut.EvaluateAsync(CreateOrder());

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_WhenInitialCapitalIsZero_ReturnsApproved()
    {
        // F# rule short-circuits when initialCapital == 0 to avoid division by zero
        var tracker = new Mock<IPositionTracker>();
        tracker.Setup(t => t.GetPortfolioValue()).Returns(0m);
        var sut = CreateSut(tracker.Object, initialCapital: 0m, maxDrawdownPercent: 10m);

        var result = await sut.EvaluateAsync(CreateOrder());

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_InvokesPositionTrackerForPortfolioValue()
    {
        var tracker = new Mock<IPositionTracker>();
        tracker.Setup(t => t.GetPortfolioValue()).Returns(100_000m);
        var sut = CreateSut(tracker.Object);

        await sut.EvaluateAsync(CreateOrder());

        tracker.Verify(t => t.GetPortfolioValue(), Times.Once);
    }

    [Theory]
    [InlineData(100_000, 95_000, 5.0, false)]  // 5% drawdown == 5% limit → reject
    [InlineData(100_000, 95_001, 5.0, true)]   // just under 5% drawdown → approve
    [InlineData(100_000, 80_000, 20.0, false)] // 20% drawdown == 20% limit → reject
    [InlineData(100_000, 80_001, 20.0, true)]  // just under 20% drawdown → approve
    public async Task EvaluateAsync_BoundaryConditions(
        double initialCapital, double portfolioValue, double maxDrawdown, bool shouldApprove)
    {
        var tracker = new Mock<IPositionTracker>();
        tracker.Setup(t => t.GetPortfolioValue()).Returns((decimal)portfolioValue);
        var sut = CreateSut(tracker.Object, (decimal)initialCapital, (decimal)maxDrawdown);

        var result = await sut.EvaluateAsync(CreateOrder());

        result.IsApproved.Should().Be(shouldApprove);
    }
}

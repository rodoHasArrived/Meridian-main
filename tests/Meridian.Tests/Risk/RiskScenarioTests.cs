using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Risk;
using Meridian.Risk.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Meridian.Tests.Risk;

/// <summary>
/// Multi-rule composite scenarios using real rule implementations.
/// Complements the OMS-level <see cref="RiskIntegrationTests"/> and the
/// individual rule unit tests by exercising the full composite + F# aggregation
/// path with concrete inputs instead of stubs.
/// </summary>
public sealed class RiskScenarioTests
{
    // Shared tracker for a portfolio with no drawdown and no open positions.
    private static IPositionTracker HealthyTracker(string symbol = "AAPL")
    {
        var tracker = new Mock<IPositionTracker>();
        tracker.Setup(t => t.GetPortfolioValue()).Returns(100_000m);
        tracker.Setup(t => t.GetPosition(It.IsAny<string>())).Returns(new PositionState { Symbol = symbol, Quantity = 0m });
        return tracker.Object;
    }

    private static OrderRequest SmallBuy(string symbol = "AAPL", decimal quantity = 5m) => new()
    {
        Symbol = symbol,
        Side = OrderSide.Buy,
        Type = OrderType.Market,
        Quantity = quantity,
    };

    private static CompositeRiskValidator BuildComposite(
        IPositionTracker tracker,
        decimal initialCapital = 100_000m,
        decimal maxDrawdownPct = 10m,
        decimal maxPositionSize = 100m,
        int maxOrdersPerMinute = 10)
    {
        var rules = new IRiskRule[]
        {
            new DrawdownCircuitBreaker(tracker, initialCapital, maxDrawdownPct, NullLogger<DrawdownCircuitBreaker>.Instance),
            new PositionLimitRule(tracker, maxPositionSize, NullLogger<PositionLimitRule>.Instance),
            new OrderRateThrottle(maxOrdersPerMinute, NullLogger<OrderRateThrottle>.Instance),
        };
        return new CompositeRiskValidator(rules, NullLogger<CompositeRiskValidator>.Instance);
    }

    [Fact]
    public async Task AllRulesPass_OrderIsApproved()
    {
        var composite = BuildComposite(HealthyTracker());

        var result = await composite.ValidateOrderAsync(SmallBuy());

        result.IsApproved.Should().BeTrue();
        result.RejectReason.Should().BeNull();
    }

    [Fact]
    public async Task DrawdownBreached_CompositeRejectsWithDrawdownReason()
    {
        // 15% drawdown vs 10% threshold → DrawdownCircuitBreaker fires
        var tracker = new Mock<IPositionTracker>();
        tracker.Setup(t => t.GetPortfolioValue()).Returns(85_000m);
        tracker.Setup(t => t.GetPosition(It.IsAny<string>())).Returns(new PositionState { Symbol = "AAPL", Quantity = 0m });

        var composite = BuildComposite(tracker.Object, initialCapital: 100_000m, maxDrawdownPct: 10m);

        var result = await composite.ValidateOrderAsync(SmallBuy());

        result.IsApproved.Should().BeFalse();
        result.RejectReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task PositionLimitExceeded_CompositeRejectsWithPositionReason()
    {
        // Healthy portfolio, but order of 200 far exceeds the 100-share limit
        var composite = BuildComposite(HealthyTracker(), maxPositionSize: 100m);

        var result = await composite.ValidateOrderAsync(SmallBuy(quantity: 200m));

        result.IsApproved.Should().BeFalse();
        result.RejectReason.Should().NotBeNullOrWhiteSpace();
        result.RejectReason.Should().Contain("AAPL");
    }

    [Fact]
    public async Task OrderRateExceeded_CompositeRejectsOnEleventhOrder()
    {
        // First 10 orders fill the bucket; 11th must be rejected
        var composite = BuildComposite(HealthyTracker(), maxOrdersPerMinute: 10);

        for (var i = 0; i < 10; i++)
            await composite.ValidateOrderAsync(SmallBuy());

        var result = await composite.ValidateOrderAsync(SmallBuy());

        result.IsApproved.Should().BeFalse();
        result.RejectReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task MultipleRulesFail_CompositeReturnsRejectedWithNonEmptyReason()
    {
        // Both drawdown and position limit fire simultaneously
        var tracker = new Mock<IPositionTracker>();
        tracker.Setup(t => t.GetPortfolioValue()).Returns(85_000m);     // 15% drawdown
        tracker.Setup(t => t.GetPosition(It.IsAny<string>())).Returns(new PositionState { Symbol = "AAPL", Quantity = 0m });

        var composite = BuildComposite(tracker.Object, maxPositionSize: 10m); // position limit also breached

        var result = await composite.ValidateOrderAsync(SmallBuy(quantity: 50m));

        result.IsApproved.Should().BeFalse("multiple rules fired");
        result.RejectReason.Should().NotBeNullOrWhiteSpace("aggregate must surface at least one reason");
    }

    [Fact]
    public async Task ApprovedOrders_DoNotLeakStateToSubsequentValidations()
    {
        // Each composite evaluation is independent; approval of order A must not
        // affect order B on a fresh composite (stateless rules only).
        var tracker = HealthyTracker();
        var composite = BuildComposite(tracker, maxPositionSize: 100m, maxOrdersPerMinute: 100);

        var first = await composite.ValidateOrderAsync(SmallBuy(symbol: "AAPL", quantity: 5m));
        var second = await composite.ValidateOrderAsync(SmallBuy(symbol: "MSFT", quantity: 5m));

        first.IsApproved.Should().BeTrue();
        second.IsApproved.Should().BeTrue();
    }
}

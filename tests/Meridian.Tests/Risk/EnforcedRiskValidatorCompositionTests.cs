using FluentAssertions;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Risk;
using Meridian.Risk.Rules;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.Risk;

/// <summary>
/// Covers the enforced pre-trade risk composition: Meridian.Risk's
/// <see cref="CompositeRiskValidator"/> assembled from the operator-tuned guardrails —
/// the same shape the workstation registers as the <c>IRiskValidator</c> the OMS invokes.
/// </summary>
public sealed class EnforcedRiskValidatorCompositionTests
{
    private static OrderRequest CreateBuyOrder(decimal quantity = 10m, string symbol = "AAPL") => new()
    {
        Symbol = symbol,
        Side = OrderSide.Buy,
        Type = OrderType.Market,
        Quantity = quantity,
    };

    private static IServiceProvider ServicesWithPortfolio(
        decimal portfolioValue,
        decimal realisedPnl,
        decimal unrealisedPnl)
    {
        var portfolio = new Mock<IPortfolioState>();
        portfolio.SetupGet(p => p.PortfolioValue).Returns(portfolioValue);
        portfolio.SetupGet(p => p.RealisedPnl).Returns(realisedPnl);
        portfolio.SetupGet(p => p.UnrealisedPnl).Returns(unrealisedPnl);

        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IPortfolioState))).Returns(portfolio.Object);
        return services.Object;
    }

    private static RiskRuleRuntimeService CreateRuntime(IServiceProvider services)
    {
        var snapshotPath = Path.Combine(
            Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"), "risk-rules.json");
        return new RiskRuleRuntimeService(
            services,
            NullLogger<RiskRuleRuntimeService>.Instance,
            new RiskRuleRuntimeOptions(snapshotPath));
    }

    private static CompositeRiskValidator CreateEnforcedValidator(
        RiskRuleRuntimeService runtime,
        params IRiskRule[] extraRules)
    {
        // Mirrors the workstation registration: drawdown first, order-rate second,
        // then any additional rules.
        var rules = new List<IRiskRule>
        {
            new DrawdownGuardrailRule(runtime),
            new OrderRateThrottle(() => runtime.MaxOrdersPerMinute, NullLogger<OrderRateThrottle>.Instance),
        };
        rules.AddRange(extraRules);
        return new CompositeRiskValidator(rules, NullLogger<CompositeRiskValidator>.Instance);
    }

    [Fact]
    public async Task ValidateOrderAsync_WithBreachedDrawdown_RejectsOrder()
    {
        // -6% drawdown breaches the default 5% threshold.
        var runtime = CreateRuntime(ServicesWithPortfolio(100_000m, -6_000m, 0m));
        var validator = CreateEnforcedValidator(runtime);

        var result = await validator.ValidateOrderAsync(CreateBuyOrder());

        result.IsApproved.Should().BeFalse();
        result.RejectReason.Should().Contain("Drawdown circuit breaker");
    }

    [Fact]
    public async Task ValidateOrderAsync_WithHealthyPortfolio_EnforcesOperatorTunedOrderRate()
    {
        var runtime = CreateRuntime(ServicesWithPortfolio(100_000m, 2_000m, 0m));
        await runtime.UpdateConfigAsync(
            "OrderRateThrottle",
            new RiskRuleConfigUpdateRequest(MaxOrdersPerMinute: 2),
            actor: "test");
        var validator = CreateEnforcedValidator(runtime);

        (await validator.ValidateOrderAsync(CreateBuyOrder())).IsApproved.Should().BeTrue();
        (await validator.ValidateOrderAsync(CreateBuyOrder())).IsApproved.Should().BeTrue();

        var third = await validator.ValidateOrderAsync(CreateBuyOrder());
        third.IsApproved.Should().BeFalse();
        third.RejectReason.Should().Contain("Order rate limit");
    }

    [Fact]
    public async Task ValidateOrderAsync_DrawdownRuleRunsBeforeOrderRate()
    {
        // Both guardrails would reject; the drawdown circuit breaker must win because it
        // runs first, matching the previously enforced ordering.
        var runtime = CreateRuntime(ServicesWithPortfolio(100_000m, -50_000m, 0m));
        await runtime.UpdateConfigAsync(
            "OrderRateThrottle",
            new RiskRuleConfigUpdateRequest(MaxOrdersPerMinute: 1),
            actor: "test");
        var validator = CreateEnforcedValidator(runtime);

        var result = await validator.ValidateOrderAsync(CreateBuyOrder());

        result.IsApproved.Should().BeFalse();
        result.RejectReason.Should().Contain("Drawdown circuit breaker");
    }

    [Fact]
    public async Task PositionLimitRule_WithNoConfiguredLimit_Approves()
    {
        var tracker = new Mock<IPositionTracker>();
        tracker.Setup(t => t.GetPosition(It.IsAny<string>())).Returns(new PositionState
        {
            Symbol = "AAPL",
            Quantity = 1_000_000m,
        });
        var rule = new PositionLimitRule(
            tracker.Object,
            () => null,
            NullLogger<PositionLimitRule>.Instance);

        var result = await rule.EvaluateAsync(CreateBuyOrder());

        result.IsApproved.Should().BeTrue("a null limit means no position limit is configured");
    }

    [Fact]
    public async Task PositionLimitRule_WithOperatorLimitExceeded_Rejects()
    {
        var tracker = new Mock<IPositionTracker>();
        tracker.Setup(t => t.GetPosition("AAPL")).Returns(new PositionState
        {
            Symbol = "AAPL",
            Quantity = 95m,
        });
        var rule = new PositionLimitRule(
            tracker.Object,
            () => 100m,
            NullLogger<PositionLimitRule>.Instance);

        var result = await rule.EvaluateAsync(CreateBuyOrder(quantity: 10m));

        result.IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateOrderAsync_WithExtraRule_ComposesHostContributedRules()
    {
        var runtime = CreateRuntime(ServicesWithPortfolio(100_000m, 2_000m, 0m));
        var extraRule = new Mock<IRiskRule>();
        extraRule.SetupGet(r => r.RuleName).Returns("HostRule");
        extraRule
            .Setup(r => r.EvaluateAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Meridian.Execution.RiskValidationResult.Rejected("host rule rejected"));
        var validator = CreateEnforcedValidator(runtime, extraRule.Object);

        var result = await validator.ValidateOrderAsync(CreateBuyOrder());

        result.IsApproved.Should().BeFalse();
        result.RejectReason.Should().Contain("host rule rejected");
    }
}

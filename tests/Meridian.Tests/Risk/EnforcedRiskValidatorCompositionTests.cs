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
        // Status builders read the position book; an unstubbed member would return null.
        portfolio.SetupGet(p => p.Positions).Returns(
            new Dictionary<string, Meridian.Execution.Sdk.IPosition>(StringComparer.OrdinalIgnoreCase));

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
        // Moq returns the enum default (Info, flag-only) for unstubbed members; a blocking
        // host rule declares Error severity like the IRiskRule interface default.
        extraRule.SetupGet(r => r.Severity).Returns(RiskRuleSeverity.Error);
        extraRule
            .Setup(r => r.EvaluateAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Meridian.Execution.RiskValidationResult.Rejected("host rule rejected"));
        var validator = CreateEnforcedValidator(runtime, extraRule.Object);

        var result = await validator.ValidateOrderAsync(CreateBuyOrder());

        result.IsApproved.Should().BeFalse();
        result.RejectReason.Should().Contain("host rule rejected");
    }

    [Fact]
    public async Task ValidateOrderAsync_DrawdownJustInsideTheLimit_DoesNotTripTheBreaker()
    {
        // 100k of capital down to ~95.24k is a 4.76% drawdown. Dividing the loss by the
        // already-reduced current value reads 5.0% and would breach the default 5% limit —
        // and now trip the global circuit breaker — on a loss that never reached it.
        var runtime = CreateRuntime(ServicesWithPortfolio(95_238m, -4_762m, 0m));
        var validator = CreateEnforcedValidator(runtime);

        var result = await validator.ValidateOrderAsync(CreateBuyOrder());

        result.IsApproved.Should().BeTrue("the true drawdown from starting capital is under the limit");
    }

    [Fact]
    public async Task ValidateOrderAsync_DrawdownBeyondTheLimit_StillRejects()
    {
        // 100k down to 94k is a genuine 6% drawdown.
        var runtime = CreateRuntime(ServicesWithPortfolio(94_000m, -6_000m, 0m));
        var validator = CreateEnforcedValidator(runtime);

        var result = await validator.ValidateOrderAsync(CreateBuyOrder());

        result.IsApproved.Should().BeFalse();
        result.RejectReason.Should().Contain("Drawdown circuit breaker");
    }

    [Fact]
    public async Task GetAllStatusesAsync_OrderNotionalWithoutEscalationBand_ReportsErrorSeverity()
    {
        var runtime = CreateRuntime(ServicesWithPortfolio(100_000m, 0m, 0m));
        await runtime.UpdateConfigAsync(
            "OrderNotional",
            new RiskRuleConfigUpdateRequest(MaxOrderNotional: 250_000m),
            actor: "risk-desk");

        var status = (await runtime.GetAllStatusesAsync()).Single(s => s.RuleName == "OrderNotional");

        status.Severity.Should().Be("Error", "a ceiling-only configuration can only reject, never park");

        // Configuring the band restores the escalation outcome.
        await runtime.UpdateConfigAsync(
            "OrderNotional",
            new RiskRuleConfigUpdateRequest(EscalateOrderNotional: 50_000m),
            actor: "risk-desk");
        (await runtime.GetAllStatusesAsync()).Single(s => s.RuleName == "OrderNotional")
            .Severity.Should().Be("Escalate");
    }

    [Fact]
    public async Task GetAllStatusesAsync_StaleBreach_StopsConstrainingLiveState()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "Meridian.Tests", $"risk-status-{Guid.NewGuid():N}");
        await using var auditTrail = new Meridian.Execution.Services.ExecutionAuditTrailService(
            new Meridian.Execution.Services.ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")),
            NullLogger<Meridian.Execution.Services.ExecutionAuditTrailService>.Instance);

        // A gross-exposure rejection from days ago: retained as evidence, but the audit
        // window is bounded by entry count, not age, so it must not pin the guardrail
        // Constrained forever on a quiet installation.
        await auditTrail.RecordAsync(new Meridian.Execution.Services.ExecutionAuditEntry(
            AuditId: Guid.NewGuid().ToString("N"),
            Category: "Order",
            Action: "OrderRejected",
            Outcome: "Rejected",
            OccurredAt: DateTimeOffset.UtcNow.AddDays(-3),
            Actor: "trade-desk",
            BrokerName: "paper",
            OrderId: "OLD-1",
            RunId: null,
            Symbol: "AAPL",
            CorrelationId: null,
            Message: "Gross exposure limit: projected 150000.00 exceeds 100000.00 ceiling"));

        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(Meridian.Execution.Services.ExecutionAuditTrailService)))
            .Returns(auditTrail);
        var runtime = CreateRuntime(services.Object);

        var statuses = await runtime.GetAllStatusesAsync();
        var gross = statuses.Single(status => status.RuleName == "GrossExposure");

        gross.IsBreached.Should().BeFalse("a days-old rejection no longer describes live state");
        gross.State.Should().NotBe("Constrained");
        gross.RecentViolations.Should().ContainSingle(violation => violation.Contains("Gross exposure limit"),
            "the breach is still retained as evidence");
    }

    [Fact]
    public async Task GetAllStatusesAsync_RecentBreach_ConstrainsLiveState()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "Meridian.Tests", $"risk-status-{Guid.NewGuid():N}");
        await using var auditTrail = new Meridian.Execution.Services.ExecutionAuditTrailService(
            new Meridian.Execution.Services.ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")),
            NullLogger<Meridian.Execution.Services.ExecutionAuditTrailService>.Instance);

        await auditTrail.RecordAsync(new Meridian.Execution.Services.ExecutionAuditEntry(
            AuditId: Guid.NewGuid().ToString("N"),
            Category: "Order",
            Action: "OrderRejected",
            Outcome: "Rejected",
            OccurredAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            Actor: "trade-desk",
            BrokerName: "paper",
            OrderId: "NEW-1",
            RunId: null,
            Symbol: "AAPL",
            CorrelationId: null,
            Message: "Gross exposure limit: projected 150000.00 exceeds 100000.00 ceiling"));

        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(Meridian.Execution.Services.ExecutionAuditTrailService)))
            .Returns(auditTrail);
        var runtime = CreateRuntime(services.Object);

        var gross = (await runtime.GetAllStatusesAsync()).Single(status => status.RuleName == "GrossExposure");

        gross.IsBreached.Should().BeTrue("a breach inside the liveness window still describes live state");
        gross.State.Should().Be("Constrained");
    }

    [Fact]
    public async Task UpdateConfigAsync_WhenSnapshotWriteFails_LeavesLiveThresholdsUnchanged()
    {
        // A file squatting on the snapshot's parent directory makes the durable write fail,
        // so the update must throw without publishing the new threshold to enforcement.
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", $"risk-rules-blocked-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.GetDirectoryName(root)!);
        File.WriteAllText(root, "blocks the snapshot directory");
        try
        {
            var runtime = new RiskRuleRuntimeService(
                ServicesWithPortfolio(100_000m, 0m, 0m),
                NullLogger<RiskRuleRuntimeService>.Instance,
                new RiskRuleRuntimeOptions(Path.Combine(root, "risk-rules.json")));

            var act = () => runtime.UpdateConfigAsync(
                "GrossExposure",
                new RiskRuleConfigUpdateRequest(MaxGrossExposure: 250_000m),
                actor: "risk-desk");

            await act.Should().ThrowAsync<IOException>("a threshold change must not outlive a failed durable write");
            runtime.GetConfig("GrossExposure")!.MaxGrossExposure.Should().BeNull(
                "a threshold that could not be persisted must not be live");
            runtime.MaxGrossExposure.Should().BeNull();
        }
        finally
        {
            File.Delete(root);
        }
    }
}

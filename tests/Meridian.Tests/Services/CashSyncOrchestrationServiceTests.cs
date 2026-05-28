using FluentAssertions;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Services;

public sealed class CashSyncOrchestrationServiceTests
{
    private readonly CashSyncOrchestrationService _service = new();

    [Fact]
    public void ValidateCompleteness_WhenIncompleteSnapshotExists_ReturnsError()
    {
        var result = _service.ValidateCompleteness([
            new SyncAccountSnapshot("acct-1", "cp-a", "USD", 100m, 20m, new DateOnly(2026, 5, 27), true),
            new SyncAccountSnapshot("acct-2", "cp-b", "EUR", 200m, 30m, new DateOnly(2026, 5, 27), false)
        ]);

        result.Passed.Should().BeFalse();
        result.Issues.Should().ContainSingle(issue => issue.Code == "completeness");
    }

    [Fact]
    public void ValidateOutliers_WhenDeltaJumpExceedsThreshold_ReturnsWarning()
    {
        var prior = new[] { new SyncAccountSnapshot("acct-1", "cp-a", "USD", 100m, 10m, new DateOnly(2026, 5, 26), true) };
        var current = new[] { new SyncAccountSnapshot("acct-1", "cp-a", "USD", 110m, 30m, new DateOnly(2026, 5, 27), true) };

        var result = _service.ValidateOutliers(current, prior, 1.5m);

        result.Passed.Should().BeFalse();
        result.Issues.Should().ContainSingle(issue => issue.Code == "outlier");
    }

    [Fact]
    public void BuildApprovalChain_HighRiskOrThreshold_ShouldRequireDualControl()
    {
        var payment = new PaymentInstruction("p-1", "acct-1", "cp-risk", "USD", 200_000m, new DateOnly(2026, 5, 27), "Margin", "New");
        var policy = new ApprovalPolicy(
            DualControlThreshold: 100_000m,
            CurrencyThresholds: new Dictionary<string, decimal> { ["USD"] = 150_000m },
            AccountThresholds: new Dictionary<string, decimal> { ["acct-1"] = 250_000m },
            HighRiskCounterparties: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cp-risk" });

        var decision = _service.BuildApprovalChain(payment, policy, "alice", "bob");

        decision.RequiresDualControl.Should().BeTrue();
        decision.Steps.Should().HaveCount(2);
    }

    [Fact]
    public void BuildCashLadder_AndWarnings_ShouldProjectAndFlagLiquidity()
    {
        var ladder = _service.BuildCashLadder(
            new DateOnly(2026, 5, 27),
            7,
            [new PaymentInstruction("p-1", "acct-1", "cp-a", "USD", -500m, new DateOnly(2026, 5, 28), "Fee", "Submitted")],
            [new SettlementInstruction("s-1", "acct-1", "USD", 100m, new DateOnly(2026, 5, 27), new DateOnly(2026, 5, 28), DateTimeOffset.UtcNow, "Matched")],
            [new CouponEvent("c-1", "sec-1", "acct-1", "USD", 10m, new DateOnly(2026, 5, 27), new DateOnly(2026, 5, 29), "Projected")]);

        ladder.Should().NotBeEmpty();
        var warnings = _service.BuildLiquidityWarnings(ladder, -100m);
        warnings.Should().Contain(w => w.AccountId == "acct-1" && w.Currency == "USD");
    }

    [Theory]
    [InlineData(true, true, null, "Settled", false)]
    [InlineData(false, false, null, "ValidationPending", false)]
    [InlineData(true, false, null, "ApprovalPending", false)]
    [InlineData(true, true, "failed enrichment", "Exception", true)]
    public void AdvanceStpState_TransitionsAsExpected(bool validated, bool approved, string? reason, string expectedState, bool expectedBreakQueue)
    {
        var result = _service.AdvanceStpState("entity-1", validated, approved, reason);

        result.State.Should().Be(expectedState);
        result.RoutedToBreakQueue.Should().Be(expectedBreakQueue);
    }
}

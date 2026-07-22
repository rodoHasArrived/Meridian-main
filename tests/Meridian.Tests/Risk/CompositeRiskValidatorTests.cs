using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Risk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Risk;

public sealed class CompositeRiskValidatorTests
{
    [Fact]
    public async Task ValidateOrderAsync_WithRejectedRule_ReturnsRejectedResult()
    {
        var validator = new CompositeRiskValidator(
            new IRiskRule[]
            {
                new StubRiskRule("first", RiskValidationResult.Approved()),
                new StubRiskRule("second", RiskValidationResult.Rejected("blocked")),
            },
            NullLogger<CompositeRiskValidator>.Instance);

        var result = await validator.ValidateOrderAsync(CreateOrder());

        result.IsApproved.Should().BeFalse();
        result.RejectReason.Should().Be("blocked");
    }

    [Fact]
    public async Task ValidateOrderAsync_WhenPriorityRuleRejects_ShortCircuitsBeforeLaterRules()
    {
        var first = new StubRiskRule("first", RiskValidationResult.Approved(), priority: 20);
        var rejecting = new StubRiskRule("urgent", RiskValidationResult.Rejected("halted"), priority: 10);
        var skipped = new StubRiskRule("skipped", RiskValidationResult.Approved(), priority: 30);
        var validator = new CompositeRiskValidator(
            [first, rejecting, skipped],
            NullLogger<CompositeRiskValidator>.Instance);

        var result = await validator.ValidateOrderAsync(CreateOrder());

        result.IsApproved.Should().BeFalse();
        result.RejectReason.Should().Be("halted");
        first.EvaluateCalls.Should().Be(0, "the lower priority-number rule should run first");
        rejecting.EvaluateCalls.Should().Be(1);
        skipped.EvaluateCalls.Should().Be(0, "risk evaluation should stop at the first rejection");
    }

    [Fact]
    public async Task ValidateOrderAsync_WhenRuleHasSyncFastPath_DoesNotCallAsyncPath()
    {
        var fastRule = new StubRiskRule(
            "sync",
            RiskValidationResult.Rejected("sync block"),
            syncResult: RiskValidationResult.Rejected("sync block"));
        var validator = new CompositeRiskValidator(
            [fastRule],
            NullLogger<CompositeRiskValidator>.Instance);

        var result = await validator.ValidateOrderAsync(CreateOrder());

        result.IsApproved.Should().BeFalse();
        result.RejectReason.Should().Be("sync block");
        fastRule.SyncEvaluateCalls.Should().Be(1);
        fastRule.EvaluateCalls.Should().Be(0);
    }

    private static OrderRequest CreateOrder() => new()
    {
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Market,
        Quantity = 10m,
    };

    private sealed class StubRiskRule(
        string ruleName,
        RiskValidationResult result,
        int priority = 0,
        RiskValidationResult? syncResult = null) : IRiskRule
    {
        public string RuleName => ruleName;

        public int Priority => priority;

        public int EvaluateCalls { get; private set; }

        public int SyncEvaluateCalls { get; private set; }

        public RiskValidationResult? TryEvaluate(OrderRequest request)
        {
            if (syncResult is null)
            {
                return null;
            }

            SyncEvaluateCalls++;
            return syncResult;
        }

        public Task<RiskValidationResult> EvaluateAsync(OrderRequest request, CancellationToken ct = default) =>
            Task.FromResult(RecordAsyncResult());

        private RiskValidationResult RecordAsyncResult()
        {
            EvaluateCalls++;
            return result;
        }
    }
}

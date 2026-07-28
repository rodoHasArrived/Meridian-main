using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
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

    [Fact]
    public async Task ValidateOrderAsync_WarningSeverityFailure_ApprovesWithFlag()
    {
        var warningRule = new StubRiskRule(
            "concentration-watch",
            RiskValidationResult.Rejected("nearing cap"),
            severity: RiskRuleSeverity.Warning);
        var downstream = new StubRiskRule("downstream", RiskValidationResult.Approved());
        var validator = new CompositeRiskValidator(
            [warningRule, downstream],
            NullLogger<CompositeRiskValidator>.Instance);

        var result = await validator.ValidateOrderAsync(CreateOrder());

        result.IsApproved.Should().BeTrue("warning severity flags without blocking");
        result.Warnings.Should().ContainSingle(warning => warning.Contains("concentration-watch"));
        downstream.EvaluateCalls.Should().Be(1, "evaluation continues past a warning-severity failure");
    }

    [Fact]
    public async Task ValidateOrderAsync_ApprovedRuleWarnings_PropagateToFinalResult()
    {
        var validator = new CompositeRiskValidator(
            [new StubRiskRule("soft-band", RiskValidationResult.ApprovedWithWarnings("soft-band: approaching cap"))],
            NullLogger<CompositeRiskValidator>.Instance);

        var result = await validator.ValidateOrderAsync(CreateOrder());

        result.IsApproved.Should().BeTrue();
        result.Warnings.Should().ContainSingle(warning => warning.Contains("approaching cap"));
    }

    [Fact]
    public async Task ValidateOrderAsync_CriticalSeverityFailure_RejectsAndTripsCircuitBreaker()
    {
        var controls = CreateOperatorControls();
        var validator = new CompositeRiskValidator(
            [new StubRiskRule("gross-exposure", RiskValidationResult.Rejected("book over ceiling"), severity: RiskRuleSeverity.Critical)],
            NullLogger<CompositeRiskValidator>.Instance,
            operatorControls: controls);

        var result = await validator.ValidateOrderAsync(CreateOrder());

        result.IsApproved.Should().BeFalse();
        controls.GetSnapshot().CircuitBreaker.IsOpen.Should().BeTrue("Critical severity trips the execution circuit breaker");
        controls.GetSnapshot().CircuitBreaker.Reason.Should().Contain("gross-exposure");
    }

    [Fact]
    public async Task ValidateOrderAsync_CriticalSeverityWithBreakerAlreadyOpen_DoesNotRewriteBreakerState()
    {
        var controls = CreateOperatorControls();
        await controls.SetCircuitBreakerAsync(true, "manual halt", "operator");
        var validator = new CompositeRiskValidator(
            [new StubRiskRule("gross-exposure", RiskValidationResult.Rejected("book over ceiling"), severity: RiskRuleSeverity.Critical)],
            NullLogger<CompositeRiskValidator>.Instance,
            operatorControls: controls);

        var result = await validator.ValidateOrderAsync(CreateOrder());

        result.IsApproved.Should().BeFalse();
        controls.GetSnapshot().CircuitBreaker.Reason.Should().Be("manual halt", "an already-open breaker keeps its original attribution");
    }

    [Fact]
    public async Task ValidateOrderAsync_CriticalSeverityWithoutControls_StillRejects()
    {
        var validator = new CompositeRiskValidator(
            [new StubRiskRule("gross-exposure", RiskValidationResult.Rejected("book over ceiling"), severity: RiskRuleSeverity.Critical)],
            NullLogger<CompositeRiskValidator>.Instance);

        var result = await validator.ValidateOrderAsync(CreateOrder());

        result.IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateOrderAsync_EscalationWithQueue_ParksOrderForGovernedApproval()
    {
        var queue = CreateQueue();
        var validator = new CompositeRiskValidator(
            [new StubRiskRule("order-notional", RiskValidationResult.Escalated("above governed band"))],
            NullLogger<CompositeRiskValidator>.Instance,
            escalationQueue: queue);

        var result = await validator.ValidateOrderAsync(CreateOrder());

        result.IsApproved.Should().BeFalse();
        result.RequiresApproval.Should().BeTrue();
        result.EscalationId.Should().NotBeNullOrWhiteSpace();
        queue.GetPending().Should().ContainSingle(entry => entry.EscalationId == result.EscalationId);
    }

    [Fact]
    public async Task ValidateOrderAsync_EscalateSeverityRule_ParksEvenWithPlainRejection()
    {
        var queue = CreateQueue();
        var validator = new CompositeRiskValidator(
            [new StubRiskRule("desk-review", RiskValidationResult.Rejected("needs desk review"), severity: RiskRuleSeverity.Escalate)],
            NullLogger<CompositeRiskValidator>.Instance,
            escalationQueue: queue);

        var result = await validator.ValidateOrderAsync(CreateOrder());

        result.RequiresApproval.Should().BeTrue("Escalate severity converts a rejection into a governed-approval parking");
        queue.GetPending().Should().HaveCount(1);
    }

    [Fact]
    public async Task ValidateOrderAsync_EscalationWithoutQueue_FailsClosedAsRejection()
    {
        var validator = new CompositeRiskValidator(
            [new StubRiskRule("order-notional", RiskValidationResult.Escalated("above governed band"))],
            NullLogger<CompositeRiskValidator>.Instance);

        var result = await validator.ValidateOrderAsync(CreateOrder());

        result.IsApproved.Should().BeFalse();
        result.RequiresApproval.Should().BeFalse("without a queue the escalation degrades to a hard rejection");
    }

    [Fact]
    public async Task ValidateOrderAsync_ApprovedEscalation_ReleasesOrderThroughRemainingRules()
    {
        var queue = CreateQueue();
        var escalating = new StubRiskRule("order-notional", RiskValidationResult.Escalated("above governed band"));
        var downstream = new StubRiskRule("downstream", RiskValidationResult.Approved());
        var validator = new CompositeRiskValidator(
            [escalating, downstream],
            NullLogger<CompositeRiskValidator>.Instance,
            escalationQueue: queue);

        var parked = await validator.ValidateOrderAsync(CreateOrder());
        queue.Approve(parked.EscalationId!, actor: "risk-desk", reason: "cleared");

        var resubmission = CreateOrder() with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [RiskEscalationQueueService.ApprovalMetadataKey] = parked.EscalationId!
            }
        };
        var released = await validator.ValidateOrderAsync(resubmission);

        released.IsApproved.Should().BeTrue();
        released.Warnings.Should().ContainSingle(warning => warning.Contains("governed approval"));
        downstream.EvaluateCalls.Should().BeGreaterThan(0, "later rules still run after an approval releases the escalation");

        // The approval is one-shot: an identical third submission parks again.
        var reparked = await validator.ValidateOrderAsync(resubmission);
        reparked.RequiresApproval.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateOrderAsync_ApprovalRetiredEvenWhenRuleNoLongerEscalates()
    {
        // Park under a tight threshold, approve, then relax the threshold so the rule
        // approves outright: the armed token must still be retired by the release it
        // authorized, never surviving for replay after thresholds tighten again.
        var queue = CreateQueue();
        var escalating = new ThresholdStubRule("order-notional");
        var validator = new CompositeRiskValidator(
            [escalating],
            NullLogger<CompositeRiskValidator>.Instance,
            escalationQueue: queue);

        var parked = await validator.ValidateOrderAsync(CreateOrder());
        parked.RequiresApproval.Should().BeTrue();
        queue.Approve(parked.EscalationId!, actor: "risk-desk");

        escalating.Escalates = false;
        var resubmission = CreateOrder() with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [RiskEscalationQueueService.ApprovalMetadataKey] = parked.EscalationId!
            }
        };
        var released = await validator.ValidateOrderAsync(resubmission);

        released.IsApproved.Should().BeTrue();
        queue.TryGet(parked.EscalationId!)!.Status.Should().Be(
            RiskEscalationStatus.Released,
            "the token is consumed up front, independent of whether the evaluation still escalates");

        // Tighten again: the retired token cannot bypass a later identical order.
        escalating.Escalates = true;
        var replay = await validator.ValidateOrderAsync(resubmission);
        replay.RequiresApproval.Should().BeTrue("a released token never authorizes a second order");
    }

    [Fact]
    public async Task ValidateOrderAsync_EscalationRetainsSubmittingActorForSegregation()
    {
        var queue = CreateQueue();
        var validator = new CompositeRiskValidator(
            [new StubRiskRule("order-notional", RiskValidationResult.Escalated("above governed band"))],
            NullLogger<CompositeRiskValidator>.Instance,
            escalationQueue: queue);

        var order = CreateOrder() with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["actor"] = "trade-desk-1"
            }
        };
        var result = await validator.ValidateOrderAsync(order);

        queue.TryGet(result.EscalationId!)!.Actor.Should().Be(
            "trade-desk-1",
            "the queue needs the initiator identity to refuse self-approval");
    }

    [Fact]
    public async Task ValidateOrderAsync_EscalationRetainsTheExecutionRunId()
    {
        var queue = CreateQueue();
        var validator = new CompositeRiskValidator(
            [new StubRiskRule("order-notional", RiskValidationResult.Escalated("above governed band"))],
            NullLogger<CompositeRiskValidator>.Instance,
            escalationQueue: queue);

        // Live runs stamp their unique run id in metadata; StrategyId only names the
        // reusable strategy definition shared by every run of it.
        var order = CreateOrder() with
        {
            StrategyId = "mean-reversion",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["runId"] = "run-2026-07-28-a"
            }
        };
        var result = await validator.ValidateOrderAsync(order);

        queue.TryGet(result.EscalationId!)!.RunId.Should().Be(
            "run-2026-07-28-a",
            "queue audit correlation must identify the run, not the strategy definition");
    }

    [Fact]
    public async Task ValidateOrderAsync_EscalationWithoutRunMetadata_FallsBackToStrategyId()
    {
        var queue = CreateQueue();
        var validator = new CompositeRiskValidator(
            [new StubRiskRule("order-notional", RiskValidationResult.Escalated("above governed band"))],
            NullLogger<CompositeRiskValidator>.Instance,
            escalationQueue: queue);

        var result = await validator.ValidateOrderAsync(CreateOrder() with { StrategyId = "manual-desk" });

        queue.TryGet(result.EscalationId!)!.RunId.Should().Be("manual-desk");
    }

    [Fact]
    public async Task ValidateOrderAsync_ConsumedApproval_IsReportedOnTheApprovedResult()
    {
        var queue = CreateQueue();
        var escalating = new ThresholdStubRule("order-notional");
        var validator = new CompositeRiskValidator(
            [escalating],
            NullLogger<CompositeRiskValidator>.Instance,
            escalationQueue: queue);

        var parked = await validator.ValidateOrderAsync(CreateOrder());
        queue.Approve(parked.EscalationId!, actor: "risk-desk");
        escalating.Escalates = false;

        var released = await validator.ValidateOrderAsync(CreateOrder() with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [RiskEscalationQueueService.ApprovalMetadataKey] = parked.EscalationId!
            }
        });

        released.IsApproved.Should().BeTrue();
        released.ConsumedApprovalId.Should().Be(
            parked.EscalationId,
            "the OMS needs the consumed token to re-arm it if the gateway faults before routing");
    }

    [Fact]
    public async Task ValidateOrderAsync_ApprovalBypassesOnlyTheParkedRule()
    {
        // Token parked by rule A must not release an escalation from rule B.
        var queue = CreateQueue();
        var ruleA = new StubRiskRule("order-notional", RiskValidationResult.Escalated("band A"));
        var ruleB = new StubRiskRule("desk-review", RiskValidationResult.Escalated("band B"));
        var validator = new CompositeRiskValidator(
            [ruleA, ruleB],
            NullLogger<CompositeRiskValidator>.Instance,
            escalationQueue: queue);

        var parked = await validator.ValidateOrderAsync(CreateOrder());
        queue.Approve(parked.EscalationId!, actor: "risk-desk");

        var resubmission = CreateOrder() with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [RiskEscalationQueueService.ApprovalMetadataKey] = parked.EscalationId!
            }
        };
        var second = await validator.ValidateOrderAsync(resubmission);

        second.RequiresApproval.Should().BeTrue("rule B requires its own governed approval");
        queue.GetPending().Should().ContainSingle(entry => entry.RuleName == "desk-review");
    }

    [Fact]
    public async Task ValidateOrderAsync_HardRejectionAfterConsumption_ReArmsTheApproval()
    {
        var queue = CreateQueue();
        var escalating = new StubRiskRule("order-notional", RiskValidationResult.Escalated("band"));
        var hardStop = new StubRiskRule("position-limit", RiskValidationResult.Rejected("position limit exceeded"));
        var validator = new CompositeRiskValidator(
            [escalating, hardStop],
            NullLogger<CompositeRiskValidator>.Instance,
            escalationQueue: queue);

        var parked = await validator.ValidateOrderAsync(CreateOrder());
        queue.Approve(parked.EscalationId!, actor: "risk-desk");

        var resubmission = CreateOrder() with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [RiskEscalationQueueService.ApprovalMetadataKey] = parked.EscalationId!
            }
        };
        var blocked = await validator.ValidateOrderAsync(resubmission);

        blocked.IsApproved.Should().BeFalse();
        queue.TryGet(parked.EscalationId!)!.Status.Should().Be(
            RiskEscalationStatus.Approved,
            "no order routed, so the operator's approval stays retryable");
    }

    [Fact]
    public async Task ValidateOrderAsync_RuleFaultAfterConsumption_ReArmsTheApproval()
    {
        var queue = CreateQueue();
        var escalating = new StubRiskRule("order-notional", RiskValidationResult.Escalated("band"));
        var faulting = new FaultingRule("flaky-limit-feed");
        var validator = new CompositeRiskValidator(
            [escalating, faulting],
            NullLogger<CompositeRiskValidator>.Instance,
            escalationQueue: queue);

        var parked = await validator.ValidateOrderAsync(CreateOrder());
        queue.Approve(parked.EscalationId!, actor: "risk-desk");

        var resubmission = CreateOrder() with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [RiskEscalationQueueService.ApprovalMetadataKey] = parked.EscalationId!
            }
        };

        // The token is consumed up front; a later rule then faults out of validation.
        var act = () => validator.ValidateOrderAsync(resubmission);
        await act.Should().ThrowAsync<InvalidOperationException>();

        queue.TryGet(parked.EscalationId!)!.Status.Should().Be(
            RiskEscalationStatus.Approved,
            "an exceptional exit routed nothing, so the operator's approval must not stay retired");
    }

    [Fact]
    public async Task ValidateOrderAsync_ApprovedEscalation_DoesNotBypassLaterHardRules()
    {
        var queue = CreateQueue();
        var escalating = new StubRiskRule("order-notional", RiskValidationResult.Escalated("above governed band"));
        var hardStop = new StubRiskRule("position-limit", RiskValidationResult.Rejected("position limit exceeded"));
        var validator = new CompositeRiskValidator(
            [escalating, hardStop],
            NullLogger<CompositeRiskValidator>.Instance,
            escalationQueue: queue);

        var parked = await validator.ValidateOrderAsync(CreateOrder());
        queue.Approve(parked.EscalationId!, actor: "risk-desk");

        var resubmission = CreateOrder() with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [RiskEscalationQueueService.ApprovalMetadataKey] = parked.EscalationId!
            }
        };
        var released = await validator.ValidateOrderAsync(resubmission);

        released.IsApproved.Should().BeFalse("a governed approval clears the escalation, never the hard limits");
        released.RejectReason.Should().Contain("position limit");
    }

    [Fact]
    public async Task ValidateOrderAsync_WhenBreakerTripCannotPersist_FailsClosedUntilTripLands()
    {
        // A file squatting on the controls root makes every snapshot persist fail, so the
        // critical trip cannot land durably and the validator must fail closed.
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", $"controls-blocked-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.GetDirectoryName(root)!);
        File.WriteAllText(root, "blocks the controls directory");
        try
        {
            var controls = new ExecutionOperatorControlService(
                new ExecutionOperatorControlOptions(root),
                NullLogger<ExecutionOperatorControlService>.Instance);
            var critical = new StubRiskRule(
                "gross-exposure",
                RiskValidationResult.Rejected("book over ceiling"),
                severity: RiskRuleSeverity.Critical);
            var benign = new StubRiskRule("benign", RiskValidationResult.Approved(), priority: 100);
            var validator = new CompositeRiskValidator(
                [critical, benign],
                NullLogger<CompositeRiskValidator>.Instance,
                operatorControls: controls);

            var tripping = await validator.ValidateOrderAsync(CreateOrder());
            tripping.IsApproved.Should().BeFalse();
            controls.GetSnapshot().CircuitBreaker.IsOpen.Should().BeFalse("the durable trip failed");

            // While the trip is owed, every order — including ones no rule would block —
            // is rejected: the promised global halt holds without the breaker.
            var duringLatch = await validator.ValidateOrderAsync(CreateOrder());
            duringLatch.IsApproved.Should().BeFalse();
            duringLatch.RejectReason.Should().Contain("failing closed");

            // Clearing the blockage lets the retry land: the breaker opens and the
            // validator resumes normal evaluation (the breaker gate itself lives in the
            // operator-controls check, not in this validator).
            File.Delete(root);
            var afterRecovery = await validator.ValidateOrderAsync(CreateOrder());
            controls.GetSnapshot().CircuitBreaker.IsOpen.Should().BeTrue("the pending trip must land once persistence recovers");
            controls.GetSnapshot().CircuitBreaker.Reason.Should().Contain("gross-exposure");
            afterRecovery.RejectReason.Should().NotContain("failing closed", "the latch releases once the trip lands");
        }
        finally
        {
            if (File.Exists(root))
            {
                File.Delete(root);
            }
        }
    }

    private static RiskEscalationQueueService CreateQueue() => new(
        NullLogger<RiskEscalationQueueService>.Instance,
        options: new RiskEscalationQueueOptions(
            Path.Combine(Path.GetTempPath(), "Meridian.Tests", $"escalations-{Guid.NewGuid():N}", "escalations.json")));

    private static ExecutionOperatorControlService CreateOperatorControls() => new(
        new ExecutionOperatorControlOptions(
            Path.Combine(Path.GetTempPath(), "Meridian.Tests", $"controls-{Guid.NewGuid():N}")),
        NullLogger<ExecutionOperatorControlService>.Instance);

    private static OrderRequest CreateOrder() => new()
    {
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Market,
        Quantity = 10m,
    };

    private sealed class FaultingRule(string ruleName) : IRiskRule
    {
        public string RuleName => ruleName;

        public Task<RiskValidationResult> EvaluateAsync(OrderRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException("limit feed unavailable");
    }

    private sealed class ThresholdStubRule(string ruleName) : IRiskRule
    {
        public string RuleName => ruleName;

        public bool Escalates { get; set; } = true;

        public Task<RiskValidationResult> EvaluateAsync(OrderRequest request, CancellationToken ct = default) =>
            Task.FromResult(Escalates
                ? RiskValidationResult.Escalated("above governed band")
                : RiskValidationResult.Approved());
    }

    private sealed class StubRiskRule(
        string ruleName,
        RiskValidationResult result,
        int priority = 0,
        RiskValidationResult? syncResult = null,
        RiskRuleSeverity severity = RiskRuleSeverity.Error) : IRiskRule
    {
        public string RuleName => ruleName;

        public int Priority => priority;

        public RiskRuleSeverity Severity => severity;

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

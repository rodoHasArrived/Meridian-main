using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;

namespace Meridian.Tests.Strategies;

public sealed class ReconciliationCaseWorkflowServiceTests
{
    [Fact]
    public void Apply_valid_transition_appends_hash_chain()
    {
        var svc = new ReconciliationCaseWorkflowService();
        var item = Seed();
        var one = svc.Apply(item, new ReconciliationCaseTransitionCommand(item.BreakId, ReconciliationCaseTransitionAction.StartReview, "a", "triage", ["ev1"]), DateTimeOffset.UtcNow);
        var two = svc.Apply(one.Item!, new ReconciliationCaseTransitionCommand(item.BreakId, ReconciliationCaseTransitionAction.RequestApproval, "a", "ready", ["ev2"]), DateTimeOffset.UtcNow.AddMinutes(1));
        two.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        two.Item!.StateTransitions!.Should().HaveCount(2);
        two.Item.StateTransitions[1].PreviousHash.Should().Be(two.Item.StateTransitions[0].EntryHash);
    }

    [Fact]
    public void Apply_rejects_missing_actor_evidence_and_dual_review_violation()
    {
        var svc = new ReconciliationCaseWorkflowService();
        var item = Seed() with { LifecycleState = ReconciliationCaseLifecycleState.AwaitingApproval, ReviewedBy = "same" };
        svc.Apply(item, new ReconciliationCaseTransitionCommand(item.BreakId, ReconciliationCaseTransitionAction.Approve, "", "ok", ["ev"]), DateTimeOffset.UtcNow)
            .ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.MissingActor);
        svc.Apply(item, new ReconciliationCaseTransitionCommand(item.BreakId, ReconciliationCaseTransitionAction.Approve, "same", "ok", []), DateTimeOffset.UtcNow)
            .ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.MissingEvidence);
        svc.Apply(item, new ReconciliationCaseTransitionCommand(item.BreakId, ReconciliationCaseTransitionAction.Approve, "same", "ok", ["ev"]), DateTimeOffset.UtcNow)
            .ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.DualReviewRequired);
    }

    private static ReconciliationBreakQueueItem Seed() => new(
        "b1","r1","s",ReconciliationBreakCategory.CashMismatch,ReconciliationBreakQueueStatus.Open,1,"reason",null,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow);
}

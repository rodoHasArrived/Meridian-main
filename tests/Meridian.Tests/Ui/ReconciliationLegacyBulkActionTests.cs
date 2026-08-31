using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Endpoints;

namespace Meridian.Tests.Ui;

/// <summary>
/// Regression coverage for the legacy bulk break endpoint's operator attribution: the resolved
/// actor used to be computed and then dropped, so bulk mutations persisted with no operator
/// identity and the repository's save audit fell back to "repository-save". Every legacy bulk
/// action must now stamp the acting operator into the fields the audit actor is derived from
/// (AssignedTo ?? ReviewedBy ?? ResolvedBy).
/// </summary>
public sealed class ReconciliationLegacyBulkActionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Assign_StampsAssigneeAndActingOperator()
    {
        var next = WorkstationEndpoints.ApplyLegacyBulkBreakAction(
            Item(),
            new WorkstationEndpoints.ReconciliationBreakBulkActionRequest(
                ["break-legacy-bulk"],
                Action: "assign",
                Assignee: "recon-analyst",
                CommentTemplate: "Bulk assigned for month-end triage."),
            actor: "ops-user",
            Now);

        next.AssignedTo.Should().Be("recon-analyst");
        next.ReviewedBy.Should().Be("ops-user", "the executing operator must be recorded, not dropped");
        next.ReviewedAt.Should().Be(Now);
        next.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.InReview);
        next.LifecycleRationale.Should().Be("Bulk assigned for month-end triage.");
    }

    [Fact]
    public void StatusResolved_AttributesTheResolvingOperator()
    {
        var next = WorkstationEndpoints.ApplyLegacyBulkBreakAction(
            Item(),
            new WorkstationEndpoints.ReconciliationBreakBulkActionRequest(
                ["break-legacy-bulk"],
                Action: "status",
                Status: ReconciliationBreakQueueStatus.Resolved),
            actor: "ops-user",
            Now);

        next.Status.Should().Be(ReconciliationBreakQueueStatus.Resolved);
        next.ResolvedBy.Should().Be("ops-user");
        next.ResolvedAt.Should().Be(Now);
    }

    [Fact]
    public void StatusNonTerminal_AttributesTheReviewingOperator()
    {
        var next = WorkstationEndpoints.ApplyLegacyBulkBreakAction(
            Item(),
            new WorkstationEndpoints.ReconciliationBreakBulkActionRequest(
                ["break-legacy-bulk"],
                Action: "status",
                Status: ReconciliationBreakQueueStatus.InReview),
            actor: "ops-user",
            Now);

        next.Status.Should().Be(ReconciliationBreakQueueStatus.InReview);
        next.ReviewedBy.Should().Be("ops-user");
        next.ResolvedBy.Should().BeNull();
    }

    [Fact]
    public void Comment_AttributesTheCommentingOperator()
    {
        var next = WorkstationEndpoints.ApplyLegacyBulkBreakAction(
            Item(),
            new WorkstationEndpoints.ReconciliationBreakBulkActionRequest(
                ["break-legacy-bulk"],
                Action: "comment",
                CommentTemplate: "Custodian confirmed the wire."),
            actor: "ops-user",
            Now);

        next.ResolutionNote.Should().Be("Custodian confirmed the wire.");
        next.ReviewedBy.Should().Be("ops-user");
    }

    [Fact]
    public void EveryMutatingAction_LeavesARealAuditActorForTheRepositoryDerivation()
    {
        // FileReconciliationBreakQueueRepository.SaveAsync derives the audit actor as
        // AssignedTo ?? ReviewedBy ?? ResolvedBy ?? "repository-save". Before the fix, a bulk
        // status change on an unassigned break left all three null and the mutation was audited
        // as "repository-save" — the operator identity was lost.
        foreach (var request in new[]
        {
            new WorkstationEndpoints.ReconciliationBreakBulkActionRequest(
                ["break-legacy-bulk"], Action: "assign", Assignee: "recon-analyst"),
            new WorkstationEndpoints.ReconciliationBreakBulkActionRequest(
                ["break-legacy-bulk"], Action: "status", Status: ReconciliationBreakQueueStatus.Dismissed),
            new WorkstationEndpoints.ReconciliationBreakBulkActionRequest(
                ["break-legacy-bulk"], Action: "comment", CommentTemplate: "Noted.")
        })
        {
            var next = WorkstationEndpoints.ApplyLegacyBulkBreakAction(Item(), request, "ops-user", Now);
            var derivedAuditActor = next.AssignedTo ?? next.ReviewedBy ?? next.ResolvedBy;
            derivedAuditActor.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void UnknownAction_LeavesTheItemUntouched()
    {
        var item = Item();

        var next = WorkstationEndpoints.ApplyLegacyBulkBreakAction(
            item,
            new WorkstationEndpoints.ReconciliationBreakBulkActionRequest(
                ["break-legacy-bulk"],
                Action: "escalate"),
            actor: "ops-user",
            Now);

        next.Should().Be(item);
    }

    private static ReconciliationBreakQueueItem Item() => new(
        BreakId: "break-legacy-bulk",
        RunId: "run-legacy-bulk",
        StrategyName: "Statement reconciliation",
        Category: ReconciliationBreakCategory.CashMismatch,
        Status: ReconciliationBreakQueueStatus.Open,
        Variance: 125m,
        Reason: "Cash variance requires review.",
        AssignedTo: null,
        DetectedAt: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
        LastUpdatedAt: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));
}

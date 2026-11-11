using FluentAssertions;
using Meridian.Contracts.Workstation;

namespace Meridian.Tests.Application;

public sealed partial class OperationsContinuityWorkflowServiceTests
{
    [Fact]
    public async Task ApprovalComment_RetainsAuthorMentionsAndOneIdempotentAudit_WithoutApproving()
    {
        var service = CreateService(out var repository, out var auditStore);
        var workflow = await CreateApprovalSubmittedWorkflowAsync(service);
        var approval = workflow.Approvals.Last();
        var request = new OperationsApprovalCommentRequestDto(workflow.Version, approval.ApprovalId, "discussion-1", "reviewer",
            "Please inspect retained cash evidence.", ["cash-reviewer"]);

        var first = await service.AddApprovalCommentAsync(workflow.WorkflowId, request);
        var replay = await service.AddApprovalCommentAsync(workflow.WorkflowId, request);

        first.Success.Should().BeTrue(); replay.Success.Should().BeTrue();
        var discussion = await service.GetApprovalCollaborationAsync(workflow.WorkflowId);
        discussion!.Comments.Should().ContainSingle().Which.Should().BeEquivalentTo(new {
            Author = "reviewer", Body = request.Body, ApprovalId = approval.ApprovalId, Mentions = new[] { "cash-reviewer" }, CommandId = "discussion-1"
        });
        discussion.ApprovalState.Should().Be(OperationsApprovalStateDto.Submitted);
        discussion.ApprovalHistory.Should().BeEquivalentTo(workflow.Approvals);
        (await repository.GetAsync(workflow.WorkflowId))!.ApprovalGate.Status.Should().Be(workflow.Gates.Single(gate => gate.GateKey == OperationsGateKeyDto.Approval).Status);
        (await auditStore.GetTimelineAsync(workflow.WorkflowId)).Should().ContainSingle(entry => entry.EventType == "approval-comment-added");
        var changedCommand = await service.AddApprovalCommentAsync(workflow.WorkflowId, request with { Body = "Different command payload" });
        changedCommand.Success.Should().BeFalse(); changedCommand.ErrorCode.Should().Be("COMMAND_ID_REUSE");
    }

    [Fact]
    public async Task ReviewerReassignment_PreservesOriginalSubmitterEvidenceAndHistory_AndRequiresIndependentReviewer()
    {
        var service = CreateService(out _, out _);
        var workflow = await CreateApprovalSubmittedWorkflowAsync(service);
        var submitted = workflow.Approvals.Last();
        var unsafeRequest = new OperationsApprovalReassignRequestDto(workflow.Version, submitted.ApprovalId, "unsafe-reviewer", "ops-user", submitted.Operator!, "Desk rotation");
        var refused = await service.ReassignApprovalAsync(workflow.WorkflowId, unsafeRequest);
        refused.Success.Should().BeFalse();
        refused.Blockers.Should().Contain(blocker => blocker.Code == "APPROVAL_INDEPENDENT_REVIEWER_REQUIRED");

        var current = (await service.GetAsync(workflow.WorkflowId))!;
        var reassigned = await service.ReassignApprovalAsync(workflow.WorkflowId,
            unsafeRequest with { ExpectedVersion = current.Version, CommandId = "reviewer-rotation", Reviewer = "independent-reviewer" });
        reassigned.Success.Should().BeTrue();
        var latest = reassigned.Workflow!.Approvals.Last();
        latest.Status.Should().Be(OperationsApprovalStateDto.ReviewerAssigned);
        latest.Operator.Should().Be(submitted.Operator);
        latest.SubmittedAtUtc.Should().Be(submitted.SubmittedAtUtc);
        latest.EvidenceLinks.Should().BeEquivalentTo(submitted.EvidenceLinks);
        latest.DecidedAtUtc.Should().BeNull();
        reassigned.Workflow.Approvals.Should().HaveCount(workflow.Approvals.Count + 1);
        reassigned.Workflow.Approvals.First().Should().BeEquivalentTo(submitted);
    }

    [Fact]
    public async Task ApprovalDiscussion_RejectsStaleVersionAndChangedApprovalIdentity()
    {
        var service = CreateService(out _, out _);
        var workflow = await CreateApprovalSubmittedWorkflowAsync(service);
        var request = new OperationsApprovalCommentRequestDto(workflow.Version, "foreign-approval", "comment-1", "reviewer", "Review source.");
        (await service.AddApprovalCommentAsync(workflow.WorkflowId, request)).Success.Should().BeFalse();
        var current = (await service.GetAsync(workflow.WorkflowId))!;
        var stale = await service.AddApprovalCommentAsync(workflow.WorkflowId, request with { ApprovalId = workflow.Approvals.Last().ApprovalId, ExpectedVersion = current.Version - 1 });
        stale.Success.Should().BeFalse(); stale.ErrorCode.Should().Be("VERSION_MISMATCH");
        (await service.GetApprovalCollaborationAsync(workflow.WorkflowId))!.Comments.Should().BeEmpty();
    }
}

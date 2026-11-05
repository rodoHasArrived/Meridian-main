using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Microsoft.Extensions.Options;
using Moq;

namespace Meridian.Tests.SecurityMaster.Workbench;

/// <summary>
/// Remediation slice 1: <see cref="SecurityMasterWorkbenchOptions.RequireIndependentReviewer"/> is
/// now read. With it on, the drafter cannot be named as reviewer (even when someone else submits)
/// and the submitter cannot approve. With it off, which is the code default, behavior is unchanged.
/// </summary>
public sealed partial class SecurityMasterWorkbenchCommandServiceTests
{
    [Fact]
    public async Task Submit_NamingTheDrafterAsReviewer_IsRefusedWhenIndependentReviewIsRequired()
    {
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 2, workbenchOptions: IndependentReview(true));
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Draft, actor: "ops.drafter");

        await harness.Service.Invoking(s => s.SubmitForApprovalAsync(Submit(revisionId, workflowId, submitter: "ops.submitter", reviewer: "OPS.DRAFTER")))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*drafted the revision*");
        harness.Workflow.Verify(
            w => w.SubmitForApprovalAsync(It.IsAny<Guid>(), It.IsAny<OperationsSubmitApprovalRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the refusal comes before the approval gate is mutated");
    }

    [Fact]
    public async Task Submit_NamingTheDrafterAsReviewer_IsAllowedWhenIndependentReviewIsOff()
    {
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 2, workbenchOptions: IndependentReview(false));
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Draft, actor: "ops.drafter");
        harness.Workflow
            .Setup(w => w.SubmitForApprovalAsync(workflowId, It.IsAny<OperationsSubmitApprovalRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessTransition(newVersion: 3));

        var result = await harness.Service.SubmitForApprovalAsync(Submit(revisionId, workflowId, submitter: "ops.submitter", reviewer: "ops.drafter"));

        result.State.Should().Be(SecurityMasterRevisionStateDto.Submitted, "the default keeps today's behavior");
    }

    [Fact]
    public async Task Submit_NamingAnIndependentReviewer_SucceedsWhenIndependentReviewIsRequired()
    {
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 2, workbenchOptions: IndependentReview(true));
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Draft, actor: "ops.drafter");
        harness.Workflow
            .Setup(w => w.SubmitForApprovalAsync(workflowId, It.IsAny<OperationsSubmitApprovalRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessTransition(newVersion: 3));

        var result = await harness.Service.SubmitForApprovalAsync(Submit(revisionId, workflowId, submitter: "ops.drafter", reviewer: "ops.reviewer"));

        result.State.Should().Be(SecurityMasterRevisionStateDto.Submitted);
    }

    [Fact]
    public async Task Approve_ByTheSubmitter_IsRefusedWhenIndependentReviewIsRequired()
    {
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 4, workbenchOptions: IndependentReview(true));
        var revisionId = await harness.SeedRevisionAsync(
            SecurityMasterRevisionStateDto.Submitted, actor: "ops.analyst", workflowId: workflowId);

        await harness.Service.Invoking(s => s.ApproveRevisionAsync(Approve(revisionId, workflowId, approver: "Ops.Analyst")))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*submitted the revision*");
        harness.Workflow.Verify(
            w => w.ApproveWorkflowAsync(It.IsAny<Guid>(), It.IsAny<OperationsApprovalDecisionRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Submitted);
    }

    [Fact]
    public async Task Approve_ByAnIndependentReviewer_SucceedsWhenIndependentReviewIsRequired()
    {
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 4, workbenchOptions: IndependentReview(true));
        var revisionId = await harness.SeedRevisionAsync(
            SecurityMasterRevisionStateDto.Submitted, actor: "ops.analyst", workflowId: workflowId);
        harness.Workflow
            .Setup(w => w.ApproveWorkflowAsync(workflowId, It.IsAny<OperationsApprovalDecisionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessTransition(newVersion: 5));

        var result = await harness.Service.ApproveRevisionAsync(Approve(revisionId, workflowId, approver: "ops.reviewer"));

        result.State.Should().Be(SecurityMasterRevisionStateDto.Approved);
    }

    [Fact]
    public async Task Approve_ByTheSubmitter_KeepsTodaysBehaviorWhenIndependentReviewIsOff()
    {
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 4, workbenchOptions: IndependentReview(false));
        var revisionId = await harness.SeedRevisionAsync(
            SecurityMasterRevisionStateDto.Submitted, actor: "ops.analyst", workflowId: workflowId);
        harness.Workflow
            .Setup(w => w.ApproveWorkflowAsync(workflowId, It.IsAny<OperationsApprovalDecisionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessTransition(newVersion: 5));

        var result = await harness.Service.ApproveRevisionAsync(Approve(revisionId, workflowId, approver: "ops.analyst"));

        result.State.Should().Be(SecurityMasterRevisionStateDto.Approved, "the gate, not this setting, decides when it is off");
    }

    [Fact]
    public void RequireIndependentReviewer_DefaultsOff()
        => new SecurityMasterWorkbenchOptions().RequireIndependentReviewer.Should().BeFalse(
            "turning the check on is a deployment decision; the sample configuration opts in");

    private static IOptionsMonitor<SecurityMasterWorkbenchOptions> IndependentReview(bool required)
    {
        var monitor = new Mock<IOptionsMonitor<SecurityMasterWorkbenchOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new SecurityMasterWorkbenchOptions { RequireIndependentReviewer = required });
        return monitor.Object;
    }

    private static SubmitSecurityMasterRevisionRequest Submit(Guid revisionId, Guid workflowId, string submitter, string reviewer)
        => new(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: submitter,
            Note: "Submit.",
            FundProfileId: null,
            WorkflowId: workflowId,
            ExpectedWorkflowVersion: 2,
            Reviewer: reviewer,
            ReportPackId: "rp-1");

    private static ApproveSecurityMasterRevisionRequest Approve(Guid revisionId, Guid workflowId, string approver)
        => new(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            WorkflowId: workflowId,
            ExpectedWorkflowVersion: 4,
            Actor: approver,
            Reviewer: approver,
            Rationale: "Approved.",
            ReportPackId: "rp-1");
}

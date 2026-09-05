using FluentAssertions;
using Meridian.Contracts.Workstation;

namespace Meridian.Tests.Application;

public sealed partial class OperationsContinuityWorkflowServiceTests
{
    [Theory]
    [InlineData("reviewer")]
    [InlineData("rationale")]
    [InlineData("decision-time")]
    [InlineData("report-evidence")]
    [InlineData("current-decision")]
    public async Task ReviewedAutomation_CurrentRetainedReviewClearsPendingReview_AndMissingEvidenceBlocksAgain(string defect)
    {
        var service = CreateService(out var repository, out _);
        var submitted = await CreateApprovalSubmittedWorkflowAsync(service);
        submitted.ReviewedAutomation!.RequiresHumanReview.Should().BeTrue();
        var approved = await service.ApproveWorkflowAsync(submitted.WorkflowId, new OperationsApprovalDecisionRequestDto(
            submitted.Version, "ops-user", "reviewer", "Review current retained support", "report-pack-1",
            ChecklistControlApprovals: RequiredChecklistControlApprovals()));
        approved.Success.Should().BeTrue();
        approved.Workflow!.ClosePackage.Should().BeNull();
        approved.Workflow.ReviewedAutomation!.Status.Should().Be(EvidenceStatusDto.Ready);
        approved.Workflow.ReviewedAutomation.RequiresHumanReview.Should().BeFalse();
        approved.Workflow.ReviewedAutomation.Artifacts.Should().OnlyContain(artifact =>
            artifact.Status == EvidenceStatusDto.Ready && !artifact.RequiresHumanReview);

        var retained = (await repository.GetAsync(submitted.WorkflowId))!;
        var validReview = retained.Approvals[^1];
        retained.Approvals[^1] = defect switch
        {
            "reviewer" => validReview with { Reviewer = null },
            "rationale" => validReview with { Rationale = null },
            "decision-time" => validReview with { DecidedAtUtc = null },
            "report-evidence" => validReview with { EvidenceLinks = [] },
            _ => validReview with { Status = OperationsApprovalStateDto.Submitted }
        };
        await repository.SaveAsync(retained);
        var missing = (await service.GetAsync(submitted.WorkflowId))!;
        missing.ReviewedAutomation!.RequiresHumanReview.Should().BeTrue();
        missing.ReviewedAutomation.Status.Should().Be(EvidenceStatusDto.ReviewRequired);

        retained.Approvals[^1] = validReview;
        await repository.SaveAsync(retained);
        var repaired = (await service.GetAsync(submitted.WorkflowId))!;
        repaired.ReviewedAutomation!.RequiresHumanReview.Should().BeFalse();
        repaired.ReviewedAutomation.EvidenceLinks.Should().Contain(link => link.EvidenceId == "report-pack-1");
        repaired.ClosePackage.Should().BeNull();
    }
}

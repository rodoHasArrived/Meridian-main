using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;

namespace Meridian.Tests.FinancialOperations.OperationsContinuity;

public sealed class OperationsReviewedAutomationProjectionTests
{
    private static readonly DateTimeOffset RetainedAt = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(OperationsBrokerIntakeStateDto.Imported)]
    [InlineData(OperationsBrokerIntakeStateDto.Normalized)]
    public void Build_UnreviewedIntakeTakesPrecedenceOverLaterWorkflowStates(OperationsBrokerIntakeStateDto intake)
    {
        var workflow = ApprovedWorkflow();
        workflow.BrokerIntakeState = intake;
        workflow.ReconciliationState = OperationsReconciliationStateDto.AutoMatched;
        workflow.LedgerPostingState = OperationsLedgerPostingStateDto.Validated;

        var result = OperationsReviewedAutomationProjection.Build(workflow, []);

        result.Stage.Should().Be("Extraction review");
        result.Status.Should().Be(EvidenceStatusDto.ReviewRequired);
        result.RequiresHumanReview.Should().BeTrue();
        result.Artifacts.Should().HaveCount(2).And.OnlyContain(artifact => artifact.RequiresHumanReview);
    }

    [Fact]
    public void Build_UnreviewedMatchesTakePrecedenceOverJournalAndApprovalStates()
    {
        var workflow = ApprovedWorkflow();
        workflow.ReconciliationState = OperationsReconciliationStateDto.AutoMatched;
        workflow.LedgerPostingState = OperationsLedgerPostingStateDto.Drafted;

        var result = OperationsReviewedAutomationProjection.Build(workflow, []);

        result.Stage.Should().Be("Suggested matches require review");
        result.RequiresHumanReview.Should().BeTrue();
        result.Artifacts.Should().Contain(artifact => artifact.ArtifactId == "reviewed-automation:reconciliation-match-suggestion");
    }

    [Theory]
    [InlineData(OperationsLedgerPostingStateDto.Drafted)]
    [InlineData(OperationsLedgerPostingStateDto.Validated)]
    public void Build_UnpostedJournalTakesPrecedenceOverRetainedApproval(OperationsLedgerPostingStateDto posting)
    {
        var workflow = ApprovedWorkflow();
        workflow.LedgerPostingState = posting;

        var result = OperationsReviewedAutomationProjection.Build(workflow, []);

        result.Stage.Should().Be("Journal draft review");
        result.RequiresHumanReview.Should().BeTrue();
        result.ProhibitedActions.Should().Contain("Post material journals without approval");
    }

    [Fact]
    public void Build_ArtifactsDeduplicateAndCapEvidenceWithoutTruncatingSummaryOrMutatingInputs()
    {
        var workflow = ApprovedWorkflow();
        workflow.BrokerIntakeState = OperationsBrokerIntakeStateDto.Imported;
        var evidence = new List<OperationsEvidenceLinkDto> { Evidence("source-1"), Evidence("SOURCE-1") };
        evidence.AddRange(Enumerable.Range(2, 6).Select(index => Evidence($"source-{index}")));
        var retainedWorkflow = JsonSerializer.Serialize(workflow);
        var retainedEvidence = evidence.ToArray();

        var result = OperationsReviewedAutomationProjection.Build(workflow, evidence);

        result.EvidenceLinks.Should().Equal(retainedEvidence);
        result.Artifacts.Should().OnlyContain(artifact => artifact.EvidenceLinks.Count == 5);
        foreach (var artifact in result.Artifacts)
        {
            artifact.EvidenceLinks.Select(link => link.EvidenceId).Should()
                .Equal("source-1", "source-2", "source-3", "source-4", "source-5");
            artifact.ReviewChecklist.Should().Equal(result.RequiredActions);
        }
        evidence.Should().Equal(retainedEvidence);
        JsonSerializer.Serialize(workflow).Should().Be(retainedWorkflow);
    }

    [Fact]
    public void Build_ClosedWorkflowUsesRetainedPackageEvidenceBeforeEarlierReviewStages()
    {
        var workflow = ApprovedWorkflow();
        workflow.IsClosed = true;
        workflow.BrokerIntakeState = OperationsBrokerIntakeStateDto.Imported;
        workflow.ClosePackage = new(
            "close-package", "report-pack", "retained-manifest", "/evidence/manifest", "retained-hash",
            RetainedAt, "operator", "Retain reviewed support",
            [Evidence("retained-support"), Evidence("RETAINED-SUPPORT")], []);

        var result = OperationsReviewedAutomationProjection.Build(workflow, [Evidence("unretained-support")]);

        result.Stage.Should().Be("Reviewed evidence retained");
        result.Status.Should().Be(EvidenceStatusDto.Ready);
        result.RequiresHumanReview.Should().BeFalse();
        result.EvidenceLinks.Should().ContainSingle().Which.EvidenceId.Should().Be("retained-support");
        result.Artifacts.Should().ContainSingle().Which.EvidenceLinks.Should().Equal(result.EvidenceLinks);
        result.RequiredActions.Should().BeEmpty();
        workflow.ClosePackage.EvidenceLinks.Should().HaveCount(2);
    }

    [Fact]
    public void Build_ApprovedProjectionRetainsReviewerEvidenceWithoutClosingWorkflow()
    {
        var workflow = ApprovedWorkflow();
        var retainedWorkflow = JsonSerializer.Serialize(workflow);

        var result = OperationsReviewedAutomationProjection.Build(workflow,
            [Evidence("REPORT-PACK"), Evidence("source-support")]);

        result.Stage.Should().Be("Approved evidence ready for publication");
        result.Status.Should().Be(EvidenceStatusDto.Ready);
        result.RequiresHumanReview.Should().BeFalse();
        result.EvidenceLinks.Select(link => link.EvidenceId).Should().Equal("report-pack", "source-support");
        result.Artifacts.Should().ContainSingle().Which.ConfidencePercent.Should().BeNull();
        result.ProhibitedActions.Should().Contain("Publish reports");
        JsonSerializer.Serialize(workflow).Should().Be(retainedWorkflow);
        workflow.IsClosed.Should().BeFalse();
        workflow.ClosePackage.Should().BeNull();
    }

    [Theory]
    [InlineData("unpassed-gate")]
    [InlineData("unready-report")]
    [InlineData("different-report-evidence-case")]
    [InlineData("newer-unapproved-review")]
    public void Build_IncompleteCurrentApprovalCannotInheritReadyStatus(string defect)
    {
        var workflow = ApprovedWorkflow();
        switch (defect)
        {
            case "unpassed-gate":
                workflow.ReconciliationGate = OperationsGateState.Create(OperationsGateKeyDto.Reconciliation);
                break;
            case "unready-report":
                workflow.ReportPackReadiness = workflow.ReportPackReadiness with { IsReady = false };
                break;
            case "different-report-evidence-case":
                workflow.Approvals[0] = workflow.Approvals[0] with { EvidenceLinks = [Evidence("REPORT-PACK")] };
                break;
            case "newer-unapproved-review":
                workflow.Approvals.Add(workflow.Approvals[0] with { ApprovalId = "newer-review", Status = OperationsApprovalStateDto.Submitted });
                break;
        }

        var result = OperationsReviewedAutomationProjection.Build(workflow, []);

        result.Stage.Should().Be("Suggestions only");
        result.Status.Should().Be(EvidenceStatusDto.ReviewRequired);
        result.RequiresHumanReview.Should().BeTrue();
        result.Artifacts.Should().OnlyContain(artifact => artifact.RequiresHumanReview);
    }

    private static OperationsContinuityWorkflow ApprovedWorkflow()
    {
        var workflow = OperationsContinuityWorkflow.Start(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "2026-11", null, "synthetic", RetainedAt);
        workflow.BrokerIntakeState = OperationsBrokerIntakeStateDto.Complete;
        workflow.ReconciliationState = OperationsReconciliationStateDto.Complete;
        workflow.LedgerPostingState = OperationsLedgerPostingStateDto.Posted;
        workflow.ApprovalState = OperationsApprovalStateDto.Approved;
        workflow.ReportPackReadiness = new(true, "report-pack", null, [Evidence("report-pack")]);
        workflow.BrokerIngestGate = workflow.BrokerIngestGate.WithStatus(OperationsGateStatusDto.Passed);
        workflow.SecurityMasterGate = workflow.SecurityMasterGate.WithStatus(OperationsGateStatusDto.Passed);
        workflow.LedgerPostingGate = workflow.LedgerPostingGate.WithStatus(OperationsGateStatusDto.Passed);
        workflow.ReconciliationGate = workflow.ReconciliationGate.WithStatus(OperationsGateStatusDto.Passed);
        workflow.ApprovalGate = workflow.ApprovalGate.WithStatus(OperationsGateStatusDto.Passed);
        workflow.Approvals.Add(new("review", OperationsApprovalStateDto.Approved, "operator", "reviewer",
            "Reviewed retained report support", RetainedAt, RetainedAt, [Evidence("report-pack")]));
        return workflow;
    }

    private static OperationsEvidenceLinkDto Evidence(string id) =>
        new(id, id, $"/evidence/{id}", "synthetic", RetainedAt);
}

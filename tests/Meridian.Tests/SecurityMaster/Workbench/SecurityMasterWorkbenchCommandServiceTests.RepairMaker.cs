using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.OperationsContinuity;
using Moq;

namespace Meridian.Tests.SecurityMaster.Workbench;

public sealed partial class SecurityMasterWorkbenchCommandServiceTests
{
    [Fact]
    public async Task RepairSubmit_RefusesOriginalMakerAsReviewerEvenWhenOrdinaryIndependentReviewIsOff()
    {
        var harness = new Harness(currentVersion: 3, workbenchOptions: IndependentReview(false));
        harness.SetPassportAssetClass("CertificateOfDeposit");
        harness.SetProjectionAssetTerms("CertificateOfDeposit", new
        {
            schemaVersion = 1,
            termsFormat = SecurityTermsFormats.Flat,
            assetClassTermsVersion = SecurityAssetClassCatalog.GetTermsVersion("CertificateOfDeposit"),
            issuerName = "Synthetic issuer",
            maturity = "2027-11-14",
            issueDate = "2026-11-14",
            couponRate = 5.25m,
            principal = 1000m,
            paymentFrequency = "AtMaturity",
            dayCount = "Actual365Fixed"
        });
        var draft = await harness.Revisions.CreateDraftAsync(SecurityId, "maker", "assetSpecificTerms.rateUnit",
            DateTimeOffset.UtcNow, "Repair vendor unit", null, new("Percent"), new(3, "{}"),
            context: new SecurityMasterRevisionDraftContext(Guid.NewGuid(), "retained-evidence", new string('a', 64)));
        await harness.Service.Invoking(service => service.SubmitForApprovalAsync(
            Submit(draft.RevisionId, Guid.NewGuid(), submitter: "delegate", reviewer: "MAKER")))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*drafted the revision*");
        harness.Workflow.Verify(workflow => workflow.SubmitForApprovalAsync(It.IsAny<Guid>(),
            It.IsAny<OperationsSubmitApprovalRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Approval_RefusesOriginalMakerAfterDelegatedSubmissionAndReviewerReassignment()
    {
        var harness = new Harness(currentVersion: 2, workbenchOptions: IndependentReview(true));
        var workflowId = Guid.NewGuid();
        var draft = await harness.Revisions.CreateDraftAsync(SecurityId, "maker");
        await harness.Revisions.TransitionAsync(draft.RevisionId, SecurityMasterRevisionStateDto.Draft,
            SecurityMasterRevisionStateDto.Submitted, "delegate", workflowId);
        await harness.Service.Invoking(service => service.ApproveRevisionAsync(
            Approve(draft.RevisionId, workflowId, approver: "MAKER")))
            .Should().ThrowAsync<ArgumentException>();
        harness.Workflow.Verify(workflow => workflow.ApproveWorkflowAsync(It.IsAny<Guid>(),
            It.IsAny<OperationsApprovalDecisionRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OrdinaryRevision_CannotBorrowAnotherRevisionsSubjectApprovalLane()
    {
        var harness = new Harness(currentVersion: 3);
        var revision = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Draft, actor: "maker");
        var workflowId = Guid.NewGuid();
        var otherRevision = Guid.NewGuid();
        var authority = new Mock<ISecurityMasterRevisionWorkflowAuthority>();
        authority.Setup(authority => authority.ResolveAsync(otherRevision, "maker", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecurityMasterRevisionWorkflowAuthority(workflowId, otherRevision,
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString("D"), "maker", "retained-source", new string('a', 64)));
        var realGate = new OperationsContinuityWorkflowService(
            new InMemoryOperationsContinuityRepository(new OperationsStatusDerivationService()),
            new InMemoryOperationsWorkflowAuditStore(), new OperationsStatusDerivationService(),
            revisionWorkflowAuthority: authority.Object);
        var started = await realGate.StartSecurityMasterRevisionWorkflowAsync(workflowId, otherRevision, "maker");
        started.Success.Should().BeTrue();
        harness.Workflow.Setup(workflow => workflow.GetAsync(workflowId, It.IsAny<CancellationToken>())).ReturnsAsync(started.Workflow);

        await harness.Service.Invoking(service => service.SubmitForApprovalAsync(
            Submit(revision, workflowId, submitter: "maker", reviewer: "checker")))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*exact subject-scoped workflow*");
        harness.Workflow.Verify(workflow => workflow.SubmitForApprovalAsync(It.IsAny<Guid>(),
            It.IsAny<OperationsSubmitApprovalRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

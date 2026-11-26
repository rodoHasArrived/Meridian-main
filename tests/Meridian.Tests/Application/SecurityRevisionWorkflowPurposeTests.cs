using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;

namespace Meridian.Tests.Application;

public sealed class SecurityRevisionWorkflowPurposeTests
{
    private sealed class Authority : ISecurityMasterRevisionWorkflowAuthority
    {
        public Dictionary<Guid, SecurityMasterRevisionWorkflowAuthority> Rows { get; } = [];
        public Task<SecurityMasterRevisionWorkflowAuthority?> ResolveAsync(Guid revisionId, string actor, CancellationToken ct = default)
            => Task.FromResult(Rows.GetValueOrDefault(revisionId));
    }

    [Fact]
    public async Task DistinctRevisionSubjects_CoexistWithOneCloseLane_AndStayOutOfCloseLists()
    {
        var repository = new InMemoryOperationsContinuityRepository(new OperationsStatusDerivationService());
        var authority = new Authority();
        var service = new OperationsContinuityWorkflowService(repository, new InMemoryOperationsWorkflowAuditStore(),
            new OperationsStatusDerivationService(), revisionWorkflowAuthority: authority);
        var account = Guid.NewGuid();
        var book = Guid.NewGuid();
        var period = Guid.NewGuid().ToString("D");
        var close = await service.StartWorkflowAsync(new(account, period, null, null, "operator", LedgerBookId: book));
        close.Success.Should().BeTrue();
        (await service.StartWorkflowAsync(new(account, period, null, null, "operator", LedgerBookId: book))).Success.Should().BeFalse();
        foreach (var _ in Enumerable.Range(0, 2))
        {
            var revision = Guid.NewGuid();
            var workflow = Guid.NewGuid();
            authority.Rows[revision] = new(workflow, revision, account, book, period, "maker", "owned-document", new string('a', 64));
            var started = await service.StartSecurityMasterRevisionWorkflowAsync(workflow, revision, "operator");
            started.Success.Should().BeTrue();
            var retained = (await repository.GetAsync(workflow))!;
            retained.Purpose.Should().Be(OperationsWorkflowPurposeDto.SecurityMasterRevision);
            retained.BrokerIngestGate.Status.Should().NotBe(OperationsGateStatusDto.Passed,
                "subject approval does not fabricate completed close prerequisites");
            (await service.StartSecurityMasterRevisionWorkflowAsync(Guid.NewGuid(), revision, "operator")).Success.Should().BeFalse(
                "only the workflow identity retained by the repair roster can start this subject");
        }
        (await repository.ListAsync(account, period)).Should().ContainSingle()
            .Which.WorkflowId.Should().Be(close.Workflow!.WorkflowId);
    }

    [Fact]
    public async Task SubjectApproval_RequiresItsExactRevisionEvidenceAndIndependentMakerChecker()
    {
        var repository = new InMemoryOperationsContinuityRepository(new OperationsStatusDerivationService());
        var authority = new Authority();
        var service = new OperationsContinuityWorkflowService(repository, new InMemoryOperationsWorkflowAuditStore(),
            new OperationsStatusDerivationService(), revisionWorkflowAuthority: authority);
        var revision = Guid.NewGuid();
        var workflow = Guid.NewGuid();
        authority.Rows[revision] = new(workflow, revision, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString("D"),
            "original-maker", "owned-evidence", new string('a', 64));
        var started = await service.StartSecurityMasterRevisionWorkflowAsync(workflow, revision, "delegate");
        var reference = $"security-master-revision:{revision:D}";
        var wrong = await service.SubmitForApprovalAsync(workflow,
            new(started.Workflow!.Version, "delegate", "reviewer", $"Review [{reference}]", "another-subject"));
        wrong.Success.Should().BeFalse();
        var current = (await service.GetAsync(workflow))!;
        var maker = await service.SubmitForApprovalAsync(workflow,
            new(current.Version, "delegate", "ORIGINAL-MAKER", $"Review [{reference}]", reference));
        maker.Success.Should().BeFalse();
        current = (await service.GetAsync(workflow))!;
        var submitted = await service.SubmitForApprovalAsync(workflow,
            new(current.Version, "delegate", "reviewer", $"Review [{reference}]", reference));
        submitted.Success.Should().BeTrue();
        var approved = await service.ApproveWorkflowAsync(workflow,
            new(submitted.Workflow!.Version, "reviewer", "reviewer", "Source and exact interpretation reviewed", reference));
        approved.Success.Should().BeTrue();
        approved.Workflow!.Approvals.Last().Reviewer.Should().Be("reviewer");
        var direct = (await service.GetAsync(workflow))!;
        direct.SubjectReviewState.Should().Be("Approved");
        direct.Status.Should().Be(OperationsWorkflowStatusDto.NotStarted, "a revision approval starts no accounting close");
        direct.CloseReadiness.Should().BeNull();
        direct.CloseChecklist.Should().BeEmpty();
        direct.AccountingRecordSummary.Should().BeNull();
        direct.DashboardSummary.Should().BeNull();
        direct.Gates.Should().ContainSingle().Which.GateKey.Should().Be(OperationsGateKeyDto.Approval);
        direct.NextActions.Should().BeEmpty();
        (await service.ImportBrokerDataAsync(workflow, new(approved.Workflow.Version, "operator")))
            .ErrorCode.Should().Be("WORKFLOW_PURPOSE_MISMATCH");
    }

    [Fact]
    public void MissingPurpose_RemainsClose_AndUnknownPurposeCannotBecomeExecutable()
    {
        JsonSerializer.Deserialize<OperationsContinuityWorkflow>("{}")!.Purpose.Should().Be(OperationsWorkflowPurposeDto.Close);
        Action unknown = () => JsonSerializer.Deserialize<OperationsContinuityWorkflow>("""{"Purpose":"UnknownPurpose"}""");
        unknown.Should().Throw<JsonException>();
        var workflow = new OperationsContinuityWorkflow { Purpose = (OperationsWorkflowPurposeDto)999 };
        workflow.GetSubmitForApprovalTransitionBlocker(new(0, "maker", "checker", "Review", "reference"))!
            .Code.Should().Be("WORKFLOW_PURPOSE_UNSUPPORTED");
    }
}

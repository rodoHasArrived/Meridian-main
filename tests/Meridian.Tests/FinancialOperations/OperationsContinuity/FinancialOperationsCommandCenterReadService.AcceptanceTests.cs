using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.FinancialOperations.OperationsContinuity;
using Moq;

namespace Meridian.Tests.FinancialOperations.OperationsContinuity;

public sealed partial class FinancialOperationsCommandCenterReadServiceTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("stale")]
    [InlineData("workflow")]
    [InlineData("account")]
    [InlineData("book")]
    [InlineData("period")]
    [InlineData("version")]
    [InlineData("posting")]
    public async Task ClosePlanEvidenceFailure_BlocksThenUnderlyingRepairRestoresReadiness(string defect)
    {
        var workflow = CreateWorkflow();
        var plans = new AccountingCloseManagementService(new StubOperationsContinuityWorkflowService(workflow), ReadyPostingWorkbench());
        var retained = (await plans.GetPeriodPlanAsync(workflow.WorkflowId))!;
        // Use the real plan projector: its legacy FundProfileId is the account identifier,
        // so readiness must bind its explicit workflow/account stamps through the checked book.
        retained.FundProfileId.Should().Be(workflow.FundAccountId.ToString("D"));
        retained.WorkflowId.Should().Be(workflow.WorkflowId);
        retained.EvidenceVersion.Should().HaveLength(64);
        var broken = defect switch
        {
            "missing" => null,
            "stale" => retained with { EvaluatedAtUtc = DateTimeOffset.UtcNow.AddHours(-1) },
            "workflow" => retained with { WorkflowId = Guid.NewGuid() },
            "account" => retained with { FundAccountId = Guid.NewGuid() },
            "book" => retained with { LedgerBookId = Guid.NewGuid() },
            "period" => retained with { PeriodId = "2026-07" },
            "version" => retained with { WorkflowVersion = retained.WorkflowVersion + 1 },
            _ => retained with { ClosingEntriesGate = retained.ClosingEntriesGate! with { IsReadyForLock = false } }
        };
        var current = broken;
        var reader = new Mock<IAccountingCloseManagementService>();
        reader.Setup(x => x.GetPeriodPlanScopedAsync(workflow.WorkflowId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => current);
        var service = AcceptanceService(workflow, reader.Object);

        var blocked = await ReadScoped(service, workflow);
        blocked.IsReadyToComplete.Should().BeFalse();
        blocked.CloseSupportDecision!.IsReady.Should().BeFalse();
        blocked.CloseReadiness!.Blockers.Should().Contain(b => b.ContributorId == "close-plan");

        current = await plans.GetPeriodPlanAsync(workflow.WorkflowId);
        var repaired = await ReadScoped(service, workflow);
        repaired.CloseReadiness!.IsComplete.Should().BeTrue();
        repaired.CloseReadiness.IsReadyToClose.Should().BeTrue();
        repaired.IsReadyToComplete.Should().BeTrue();
        repaired.CloseSupportDecision!.IsReady.Should().BeTrue();
        repaired.CloseReadiness.Blockers.Should().BeEmpty();
    }

    [Fact]
    public async Task CloseManagementChangedWithoutWorkflowVersionChange_BlocksUntilConsistentRefresh()
    {
        var workflow = CreateWorkflow();
        var plans = new AccountingCloseManagementService(new StubOperationsContinuityWorkflowService(workflow), ReadyPostingWorkbench());
        var before = (await plans.GetPeriodPlanAsync(workflow.WorkflowId))!;
        // Separate retained close configuration/sign-off changes need their own snapshot token.
        var after = before with { EvidenceVersion = "changed-retained-evidence" };
        var reader = new Mock<IAccountingCloseManagementService>();
        reader.SetupSequence(x => x.GetPeriodPlanScopedAsync(workflow.WorkflowId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(before).ReturnsAsync(after).ReturnsAsync(after).ReturnsAsync(after);
        var service = AcceptanceService(workflow, reader.Object);

        var mixed = await ReadScoped(service, workflow);
        mixed.CloseReadiness!.IsComplete.Should().BeFalse();
        mixed.CloseReadiness.Blockers.Should().Contain(b => b.ContributorId == "close-plan-snapshot" && b.Type == "Stale");
        mixed.IsReadyToComplete.Should().BeFalse();
        (await ReadScoped(service, workflow)).IsReadyToComplete.Should().BeTrue();
    }

    [Fact]
    public async Task CockpitForEarlierWorkflowRevision_CannotClearCurrentClose()
    {
        var workflow = CreateWorkflow();
        var cockpit = FreshCockpit(workflow);
        cockpit = cockpit with { Workflows = [cockpit.Workflows[0] with { Version = workflow.Version - 1 }] };
        var result = await ReadScoped(CreateService(workflow, ReadyCalendar(workflow), cockpit), workflow);
        result.CloseReadiness!.Blockers.Should().Contain(b => b.ContributorId == "private-capital" && b.Type == "ScopeMismatch");
        result.PrivateCapitalCloseCockpit.Should().BeNull();
    }

    [Fact]
    public async Task UnprovenSubject_DoesNotLoadOtherwiseReadyWorkflow()
    {
        var workflow = CreateWorkflow();
        var workflows = new Mock<IOperationsContinuityWorkflowService>(MockBehavior.Strict);
        var source = new Mock<ICloseReadinessSubjectSource>();
        source.Setup(x => x.GetSubjectAsync(It.IsAny<CloseReadinessScopeDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CloseReadinessScopeDto scope, CancellationToken _) =>
                new(scope, "ScopeMismatch", DateTimeOffset.UtcNow, "wrong-owner", ["other-account"]));
        var service = new FinancialOperationsCommandCenterReadService(workflows.Object,
            ledgerBookService: CreateBookService(), closeSubjectSource: source.Object);
        var result = await ReadScoped(service, workflow);
        result.CloseReadiness!.Blockers.Should().Contain(b => b.ContributorId == "subject-scope" && b.RecordIds.Contains("other-account"));
        result.IsReadyToComplete.Should().BeFalse();
        workflows.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SubjectReassignedDuringEvaluation_BlocksEvenWhenAllContributorsAreReady()
    {
        var workflow = CreateWorkflow();
        var source = new Mock<ICloseReadinessSubjectSource>();
        var reads = 0;
        source.Setup(x => x.GetSubjectAsync(It.IsAny<CloseReadinessScopeDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CloseReadinessScopeDto scope, CancellationToken _) =>
                new(scope, "Ready", DateTimeOffset.UtcNow, ++reads == 1 ? "before" : "after", ["account"]));
        var result = await ReadScoped(AcceptanceService(workflow, CreateClosePlanService(workflow), source.Object), workflow);
        result.CloseReadiness!.Blockers.Should().Contain(b => b.ContributorId == "subject-snapshot" && b.Type == "Stale");
        result.IsReadyToComplete.Should().BeFalse();
    }

    private static IAccountingClosePostingWorkbench ReadyPostingWorkbench()
    {
        var posting = new Mock<IAccountingClosePostingWorkbench>();
        posting.Setup(x => x.EvaluateAsync(It.IsAny<AccountingClosePostingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClosePostingGateDto("closing-entries", "Closing entries", ClosePostingGateStateDto.NotRequired,
                true, 0m, 0, "Temporary account balances are clear."));
        return posting.Object;
    }

    private static FinancialOperationsCommandCenterReadService AcceptanceService(OperationsContinuityWorkflowDto workflow,
        IAccountingCloseManagementService plans, ICloseReadinessSubjectSource? source = null)
        => new(new StubOperationsContinuityWorkflowService(workflow), new StubCloseCalendarService(ReadyCalendar(workflow)),
            new StubPrivateCapitalCloseCockpitService(FreshCockpit(workflow)), CreateBookService(), plans, source ?? CreateSubjectSource());
}

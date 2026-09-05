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
    public async Task RetainedConfigurationMutation_ChangesPlanToken_AndRequiresConsistentRefresh()
    {
        var workflow = CreateWorkflow();
        var plans = new AccountingCloseManagementService(new StubOperationsContinuityWorkflowService(workflow), ReadyPostingWorkbench());
        var initial = (await plans.GetPeriodPlanAsync(workflow.WorkflowId))!;
        var reader = new Mock<IAccountingCloseManagementService>();
        var reads = 0;
        reader.Setup(x => x.GetPeriodPlanScopedAsync(workflow.WorkflowId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                var captured = await plans.GetPeriodPlanAsync(workflow.WorkflowId);
                if (++reads == 1)
                {
                    await plans.ConfigurePeriodPlanAsync(new UpsertClosePeriodPlanConfigurationRequestDto(
                        workflow.WorkflowId,
                        MaterialityPolicy: initial.MaterialityPolicy with { AmountThreshold = initial.MaterialityPolicy.AmountThreshold + 1m },
                        Actor: "controller",
                        EvidenceLinks: [$"evidence:close-plan:{workflow.WorkflowId:D}:configuration:book:{workflow.LedgerBookId:D}"]), "controller");
                }
                return captured;
            });
        var service = AcceptanceService(workflow, reader.Object);

        var mixed = await ReadScoped(service, workflow);
        mixed.IsReadyToComplete.Should().BeFalse();
        mixed.CloseReadiness!.Blockers.Should().Contain(blocker => blocker.ContributorId == "close-plan-snapshot" && blocker.Type == "Stale");
        var retained = (await plans.GetPeriodPlanAsync(workflow.WorkflowId))!;
        retained.WorkflowVersion.Should().Be(initial.WorkflowVersion);
        retained.EvidenceVersion.Should().NotBe(initial.EvidenceVersion);
        retained.MaterialityPolicy.AmountThreshold.Should().Be(initial.MaterialityPolicy.AmountThreshold + 1m);
        var repaired = await ReadScoped(service, workflow);
        repaired.IsReadyToComplete.Should().BeTrue();
        repaired.CloseReadiness!.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task CalendarReviewerCount_DoesNotReplaceRequiredTaskSignOffAuthority()
    {
        var now = DateTimeOffset.UtcNow;
        var workflow = CreateWorkflow(
            approvals:
            [
                new("submission", OperationsApprovalStateDto.Submitted, "operator", null, "Submitted", now.AddMinutes(-1), null, []),
                new("decision", OperationsApprovalStateDto.Approved, "operator", "controller", "Approved", now.AddMinutes(-1), now, [])
            ],
            closeChecklist: Enumerable.Range(1, 6).Select(index => new OperationsCloseChecklistTaskDto(
                $"task-{index}", OperationsGateKeyDto.Approval, $"Control {index}", "Controller", "Retained task evidence", 1,
                null, null, "Done", null, $"evidence:task-{index}", null, false, now, "controller")).ToArray());
        var workflows = new StubOperationsContinuityWorkflowService(workflow);
        var calendars = new OperationsCloseCalendarService(workflows);
        var calendar = await calendars.GetCalendarAsync(workflow.FundAccountId, workflow.PeriodId);
        calendar.Items.Should().ContainSingle().Which.IsReadyToClose.Should().BeTrue();
        calendar.Items[0].RequiredApprovalCount.Should().Be(6);
        calendar.Items[0].CompletedApprovalCount.Should().Be(1);
        var service = new FinancialOperationsCommandCenterReadService(workflows, calendars,
            new StubPrivateCapitalCloseCockpitService(FreshCockpit(workflow)), CreateBookService(), CreateClosePlanService(workflow), CreateSubjectSource());
        var ready = await ReadScoped(service, workflow);
        ready.CloseReadiness!.IsReadyToClose.Should().BeTrue();
        ready.CloseReadiness.Contributors.Should().Contain(contributor => contributor.ContributorId == "close-plan" && contributor.Status == "Ready");

        calendar = calendar with { Items = [calendar.Items[0] with { OpenChecklistCount = 1 }] };
        var unfinished = await ReadScoped(CreateService(workflow, calendar, FreshCockpit(workflow)), workflow);
        unfinished.CloseReadiness!.IsReadyToClose.Should().BeFalse();
        unfinished.CloseReadiness.Blockers.Should().Contain(blocker => blocker.ContributorId == "calendar");
    }

    [Fact]
    public async Task FirstPublicationOutputs_RemainVisibleWithoutBecomingPrerequisites_AndPublishedEvidenceIsRequired()
    {
        var categories = new[] { "exports", "restatement-lineage" }.Select(key =>
            new OperationsAccountingRecordEvidenceCategoryDto(key, key, false, "Pending publication", null, [], [])).ToArray();
        var workflow = CreateWorkflow(accountingRecordSummary: new OperationsAccountingRecordSummaryDto(
            "accounting-record", false, 0, 2, "Publication outputs pending", categories, []));
        var firstPublication = await ReadScoped(CreateService(workflow, ReadyCalendar(workflow), FreshCockpit(workflow)), workflow);
        firstPublication.IsReadyToComplete.Should().BeTrue();
        firstPublication.Metrics.Should().Contain(metric => metric.MetricId == "evidence" && metric.Value == "2" && metric.Status == "Review");
        firstPublication.CloseSupportDecision!.RetainedEvidenceGapCount.Should().Be(2);
        firstPublication.CloseReadiness!.Blockers.Should().BeEmpty();

        var published = workflow with { Status = OperationsWorkflowStatusDto.Closed };
        var missingRetainedOutput = await ReadScoped(CreateService(published, ReadyCalendar(published), FreshCockpit(published)), published);
        missingRetainedOutput.IsReadyToComplete.Should().BeFalse();
        missingRetainedOutput.QueueRows.Should().Contain(row => row.QueueId.EndsWith(":exports") && row.IsBlocked);
        missingRetainedOutput.QueueRows.Should().Contain(row => row.QueueId.EndsWith(":restatement-lineage") && row.IsBlocked);
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

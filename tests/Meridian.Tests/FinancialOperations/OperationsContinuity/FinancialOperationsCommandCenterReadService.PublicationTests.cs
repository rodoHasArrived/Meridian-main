using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Identity.Auth;
using Meridian.Tests.Application;
using Meridian.Tests.FinancialOperations.PrivateCapital;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Moq;

namespace Meridian.Tests.FinancialOperations.OperationsContinuity;

public sealed partial class FinancialOperationsCommandCenterReadServiceTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("stale")]
    [InlineData("account")]
    [InlineData("book")]
    [InlineData("version")]
    [InlineData("nav")]
    [InlineData("cockpit-stale")]
    public async Task DirectClose_RechecksRealSharedEvidenceAndPublishesOnlyAfterRepair(string defect)
    {
        IFinancialOperationsCommandCenterReadService? authority = null;
        var guard = new ClosePublicationReadinessGuard(() => authority, new PublicationTenantContext());
        var workflowService = OperationsContinuityWorkflowServiceTests.CreateGuardedService(guard, out var repository, out var audit);
        var activity = PrivateCapitalCloseCockpitServiceTests.BuildActivity() with { ProjectedAtUtc = DateTimeOffset.UtcNow };
        var submitted = await OperationsContinuityWorkflowServiceTests.CreateApprovalSubmittedWorkflowAsync(
            workflowService, activity.LedgerBookId, "2026-06");
        var approvals = OperationsContinuityWorkflowServiceTests.RequiredChecklistControlApprovals();
        var approved = await workflowService.ApproveWorkflowAsync(submitted.WorkflowId, new(
            submitted.Version, "ops-user", "reviewer", "Review retained close evidence", "report-pack-1",
            ChecklistControlApprovals: approvals));
        approved.Success.Should().BeTrue();
        var workflow = approved.Workflow!;
        var scope = new CloseReadinessScopeDto("fund-alpha", workflow.LedgerBookId,
            workflow.FundAccountId, "entity-master", workflow.PeriodId);
        var plans = new AccountingCloseManagementService(workflowService, ReadyPostingWorkbench());
        await RetainPublicationPlanSignOffsAsync(plans, workflow);
        var retained = (await plans.GetPeriodPlanAsync(workflow.WorkflowId))!;
        retained.ValidationIssues.Should().BeEmpty();
        retained.Tasks.Should().OnlyContain(task => task.Status == CloseTaskStatusDto.SignedOff);

        // These are retained financial-source fixtures. Both the cockpit and command center
        // project the actual, still-open workflow, and the real publication guard authorizes it.
        var currentActivity = activity;
        var journalWorkbench = new Mock<IManualJournalEntryWorkbenchService>();
        journalWorkbench.Setup(x => x.GetPrivateCapitalActivityAsync("fund-alpha", activity.LedgerBookId,
                It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(() => currentActivity);
        var cockpitService = new PrivateCapitalCloseCockpitService(journalWorkbench.Object, workflowService);
        var planSource = new Mock<IAccountingCloseManagementService>();
        ClosePeriodPlanDto? currentPlan = retained;
        planSource.Setup(x => x.GetPeriodPlanScopedAsync(workflow.WorkflowId,
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => currentPlan);
        var bookSource = new Mock<ILedgerBookService>();
        bookSource.Setup(x => x.GetBookAsync(workflow.LedgerBookId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LedgerBookDto(workflow.LedgerBookId!.Value, "fund-alpha", Guid.NewGuid(),
                default, "Close book", "USD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        authority = new FinancialOperationsCommandCenterReadService(workflowService,
            new OperationsCloseCalendarService(workflowService), cockpitService, bookSource.Object,
            planSource.Object, CreateSubjectSource());

        var before = await authority.GetCommandCenterAsync(scope.FundProfileId, scope.LedgerBookId,
            scope.FundAccountId, scope.PeriodId, scope.EntityId, tenantId: "tenant-alpha", companyId: "company-alpha");
        before.CloseReadiness!.Blockers.Should().BeEmpty("all retained prerequisites are present before the defect");
        before.IsReadyToComplete.Should().BeTrue();
        before.ActiveWorkflow!.ClosePackage.Should().BeNull();
        before.PrivateCapitalCloseCockpit!.Lanes.Should().Contain(lane =>
            lane.LaneId == "close-package" && !lane.IsReady && !lane.RequiredForClose);
        before.PrivateCapitalCloseCockpit.Lanes.Should().Contain(lane =>
            lane.LaneId == "period-lock" && !lane.IsReady && !lane.RequiredForClose);
        before.ActiveWorkflow.EvidencePackages.Should().Contain(package =>
            package.PackageId.StartsWith("approval-history:") && package.IsReady && package.RequiredForClose);
        currentPlan = defect switch
        {
            "missing" => null,
            "stale" => retained with { EvaluatedAtUtc = DateTimeOffset.UtcNow.AddHours(-1) },
            "account" => retained with { FundAccountId = Guid.NewGuid() },
            "book" => retained with { LedgerBookId = Guid.NewGuid() },
            "version" => retained with { WorkflowVersion = retained.WorkflowVersion + 1 },
            _ => retained
        };
        if (defect == "nav")
            currentActivity = PrivateCapitalCloseCockpitServiceTests.BuildActivity(includeAdministratorNavEvidence: false)
                with
            { ProjectedAtUtc = DateTimeOffset.UtcNow };
        if (defect == "cockpit-stale")
            currentActivity = activity with { ProjectedAtUtc = DateTimeOffset.UtcNow.AddHours(-1) };
        var request = new OperationsCloseWorkflowRequestDto(workflow.Version, "ops-user", "Publish close", "report-pack-1",
            ChecklistControlApprovals: approvals, CloseScope: scope);

        var blocked = await workflowService.CloseWorkflowAsync(workflow.WorkflowId, request);
        blocked.Success.Should().BeFalse();
        blocked.Blockers.Should().NotBeEmpty();
        (await repository.GetAsync(workflow.WorkflowId))!.IsClosed.Should().BeFalse();
        (await audit.GetTimelineAsync(workflow.WorkflowId)).Should().NotContain(entry => entry.EventType == "workflow-closed");

        currentPlan = await plans.GetPeriodPlanAsync(workflow.WorkflowId);
        currentActivity = activity with { ProjectedAtUtc = DateTimeOffset.UtcNow };
        var repaired = await workflowService.CloseWorkflowAsync(workflow.WorkflowId, request);
        repaired.Success.Should().BeTrue("repair must restore publication through the actual backend boundary");
        repaired.Workflow!.ClosePackage.Should().NotBeNull();
        repaired.Workflow.ClosePackage!.ChecklistControlApprovals.Should().BeEquivalentTo(approvals);
        (await repository.GetAsync(workflow.WorkflowId))!.IsClosed.Should().BeTrue();
        (await audit.GetTimelineAsync(workflow.WorkflowId)).Should().ContainSingle(entry => entry.EventType == "workflow-closed");
    }

    private static async Task RetainPublicationPlanSignOffsAsync(AccountingCloseManagementService plans,
        OperationsContinuityWorkflowDto workflow)
    {
        var plan = (await plans.GetPeriodPlanAsync(workflow.WorkflowId))!;
        foreach (var task in plan.Tasks)
        {
            foreach (var requirement in task.SignOffRequirements)
            {
                for (var count = requirement.ApprovedCount; count < requirement.RequiredApprovalCount; count++)
                {
                    var actor = $"independent-{task.TaskId}-{count}";
                    var evidence = $"/evidence/signoff/{workflow.WorkflowId:D}/{workflow.LedgerBookId:D}/{task.TaskId}/{requirement.Role}";
                    await plans.SignOffCloseTaskAsync(new(workflow.WorkflowId, task.TaskId, requirement.Role,
                        ManualJournalEntryStatusDto.Approved, actor, "Retained independent close-control review", [evidence]), actor);
                }
            }
        }
    }

    private sealed class PublicationTenantContext : IWorkstationTenantContextAccessor
    {
        public bool TryGetCurrent(out WorkstationTenantContext context)
        {
            context = GetRequired();
            return true;
        }

        public WorkstationTenantContext GetRequired()
            => new("tenant-alpha", "company-alpha", "controller", "Controller", UserPermission.None);
    }
}

using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Moq;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.AccountingClose;

namespace Meridian.Tests.FinancialOperations.OperationsContinuity;

public sealed partial class FinancialOperationsCommandCenterReadServiceTests
{
    [Fact]
    public async Task CompleteScopeAndFreshContributors_ProduceOneReadyDecision()
    {
        var workflow = CreateWorkflow();
        var service = CreateService(workflow, ReadyCalendar(workflow), FreshCockpit(workflow));
        var result = await ReadScoped(service, workflow);
        result.CloseReadiness!.IsComplete.Should().BeTrue();
        result.CloseReadiness.IsReadyToClose.Should().BeTrue();
        result.CloseReadiness.Contributors.Should().HaveCount(6);
        result.CloseReadiness.Blockers.Should().BeEmpty();
        result.IsReadyToComplete.Should().BeTrue();
        result.CloseSupportDecision!.IsReady.Should().BeTrue();
    }

    [Fact]
    public async Task MissingScope_DoesNotReadOrInferAWorkflow()
    {
        var workflows = new Mock<IOperationsContinuityWorkflowService>(MockBehavior.Strict);
        var service = new FinancialOperationsCommandCenterReadService(workflows.Object);
        var result = await service.GetCommandCenterAsync(fundProfileId: "fund-alpha");
        result.ActiveWorkflow.Should().BeNull();
        result.FundAccountId.Should().BeNull();
        result.CloseReadiness!.IsComplete.Should().BeFalse();
        result.CloseReadiness.Blockers.Should().Contain(b => b.Code == "close.scope.required");
        workflows.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("calendar")]
    [InlineData("private-capital")]
    public async Task UnregisteredContributor_BlocksEvenWhenWorkflowIsReady(string missing)
    {
        var workflow = CreateWorkflow();
        var service = CreateService(workflow, missing == "calendar" ? null : ReadyCalendar(workflow),
            missing == "private-capital" ? null : FreshCockpit(workflow));
        var result = await ReadScoped(service, workflow);
        result.CloseReadiness!.IsComplete.Should().BeFalse();
        result.CloseReadiness.Blockers.Should().Contain(b => b.ContributorId == missing);
        result.IsReadyToComplete.Should().BeFalse();
        result.CloseSupportDecision!.IsReady.Should().BeFalse();
    }

    [Theory]
    [InlineData("fund")]
    [InlineData("book")]
    [InlineData("account")]
    [InlineData("entity")]
    [InlineData("period")]
    public async Task MismatchedCockpit_IsExcludedAndBlocks(string dimension)
    {
        var workflow = CreateWorkflow();
        var cockpit = FreshCockpit(workflow);
        cockpit = dimension switch
        {
            "fund" => cockpit with { FundProfileId = "another-fund" },
            "book" => cockpit with { LedgerBookId = Guid.NewGuid() },
            "account" => cockpit with { FundAccountId = Guid.NewGuid() },
            "entity" => cockpit with { EntityId = "another-entity" },
            _ => cockpit with { PeriodId = "another-period" }
        };
        var result = await ReadScoped(CreateService(workflow, ReadyCalendar(workflow), cockpit), workflow);
        result.PrivateCapitalCloseCockpit.Should().BeNull();
        result.CloseReadiness!.IsComplete.Should().BeFalse();
        result.CloseReadiness.Blockers.Should().Contain(b => b.ContributorId == "private-capital" && b.Type == "ScopeMismatch");
    }

    [Fact]
    public async Task StaleCockpit_CannotBeMadeFreshByAReadyWorkflow()
    {
        var workflow = CreateWorkflow();
        var cockpit = FreshCockpit(workflow) with { ProjectedAtUtc = DateTimeOffset.UtcNow.AddHours(-1) };
        var result = await ReadScoped(CreateService(workflow, ReadyCalendar(workflow), cockpit), workflow);
        result.CloseReadiness!.Blockers.Should().Contain(b => b.ContributorId == "private-capital" && b.Type == "Stale");
        result.IsReadyToComplete.Should().BeFalse();
    }

    [Fact]
    public async Task FailedContributor_RemainsVisibleAsIncompleteWithoutLeakingException()
    {
        var workflow = CreateWorkflow();
        var calendar = new Mock<IOperationsCloseCalendarService>();
        calendar.Setup(x => x.GetCalendarAsync(workflow.FundAccountId, workflow.PeriodId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("private connection details"));
        var service = new FinancialOperationsCommandCenterReadService(new StubOperationsContinuityWorkflowService(workflow),
            calendar.Object, new StubPrivateCapitalCloseCockpitService(FreshCockpit(workflow)), CreateBookService(),
            closeSubjectSource: CreateSubjectSource());
        var result = await ReadScoped(service, workflow);
        result.CloseReadiness!.IsComplete.Should().BeFalse();
        result.CloseReadiness.Blockers.Should().Contain(b => b.ContributorId == "calendar");
        result.Summary.Should().NotContain("private connection details");
    }

    [Fact]
    public async Task WrongBookWorkflow_CannotReplaceTheDeclaredScope()
    {
        var workflow = CreateWorkflow() with { LedgerBookId = Guid.NewGuid() };
        var result = await ReadScoped(CreateService(workflow), workflow);
        result.LedgerBookId.Should().Be(BookId);
        result.ActiveWorkflow.Should().BeNull();
        result.IsReadyToComplete.Should().BeFalse();
    }

    [Fact]
    public async Task CalendarFromAnotherWorkflowVersion_CannotClearTheClose()
    {
        var workflow = CreateWorkflow();
        var calendar = ReadyCalendar(workflow);
        calendar = calendar with { Items = [calendar.Items[0] with { Version = workflow.Version + 1 }] };
        var result = await ReadScoped(CreateService(workflow, calendar, FreshCockpit(workflow)), workflow);
        result.CloseReadiness!.IsComplete.Should().BeFalse();
        result.CloseReadiness.Blockers.Should().Contain(b => b.ContributorId == "calendar");
        result.IsReadyToComplete.Should().BeFalse();
    }

    [Fact]
    public async Task WorkflowReopenedDuringEvaluation_RequiresRefresh()
    {
        var workflow = CreateWorkflow();
        var original = new StubOperationsContinuityWorkflowService(workflow);
        var workflows = new Mock<IOperationsContinuityWorkflowService>();
        workflows.Setup(x => x.ListAsync(workflow.FundAccountId, workflow.PeriodId, null,
            It.IsAny<CancellationToken>(), BookId)).Returns(original.ListAsync());
        workflows.SetupSequence(x => x.GetAsync(workflow.WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow).ReturnsAsync(workflow with { Version = workflow.Version + 1 });
        var service = new FinancialOperationsCommandCenterReadService(workflows.Object,
            new StubCloseCalendarService(ReadyCalendar(workflow)),
            new StubPrivateCapitalCloseCockpitService(FreshCockpit(workflow)), CreateBookService(),
            CreateClosePlanService(workflow), CreateSubjectSource());
        var result = await ReadScoped(service, workflow);
        result.CloseReadiness!.IsComplete.Should().BeFalse();
        result.CloseReadiness.Blockers.Should().Contain(b => b.ContributorId == "workflow-snapshot" && b.Type == "Stale");
        result.IsReadyToComplete.Should().BeFalse();
    }

    [Fact]
    public async Task Cancellation_IsPropagated()
    {
        var workflow = CreateWorkflow();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var action = () => CreateService(workflow).GetCommandCenterAsync(ct: cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private static IAccountingCloseManagementService CreateClosePlanService(OperationsContinuityWorkflowDto workflow)
    {
        var mock = new Mock<IAccountingCloseManagementService>();
        mock.Setup(x => x.GetPeriodPlanScopedAsync(workflow.WorkflowId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClosePeriodPlanDto("plan", "fund-alpha", BookId, workflow.PeriodId,
                default, default, default, false, [], [], new MaterialityPolicyDto("policy", 0, 0, "USD", "Controller", true),
                ClosingEntriesGate: new("closing-entries", "Closing entries", ClosePostingGateStateDto.NotRequired, true, 0, 0, "Temporary balances clear."),
                WorkflowVersion: workflow.Version, WorkflowId: workflow.WorkflowId, FundAccountId: workflow.FundAccountId,
                EvidenceVersion: "retained-plan-v1", EvaluatedAtUtc: DateTimeOffset.UtcNow));
        return mock.Object;
    }

    private static ICloseReadinessSubjectSource CreateSubjectSource()
    {
        var source = new Mock<ICloseReadinessSubjectSource>();
        source.Setup(x => x.GetSubjectAsync(It.IsAny<CloseReadinessScopeDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CloseReadinessScopeDto scope, CancellationToken _) =>
                new CloseReadinessSubjectDto(scope, "Ready", DateTimeOffset.UtcNow, "membership-v1",
                    [scope.FundAccountId!.Value.ToString("D"), scope.EntityId!]));
        return source.Object;
    }

    private static Task<FinancialOperationsCommandCenterDto> ReadScoped(
        FinancialOperationsCommandCenterReadService service, OperationsContinuityWorkflowDto workflow)
        => service.GetCommandCenterAsync("fund-alpha", BookId, workflow.FundAccountId, workflow.PeriodId, "entity-alpha");

    private static PrivateCapitalCloseCockpitDto FreshCockpit(OperationsContinuityWorkflowDto workflow)
        => CreateCockpit(workflow, navReady: true) with { ProjectedAtUtc = DateTimeOffset.UtcNow };

    private static OperationsCloseCalendarDto ReadyCalendar(OperationsContinuityWorkflowDto workflow)
        => new(DateTimeOffset.UtcNow, [new(workflow.WorkflowId, workflow.FundAccountId, workflow.PeriodId,
            workflow.Status, workflow.Version, null, null, null, null, "Ready", 100, true, 0, 0, 0, 0,
            "/workstation/accounting")]);
}

using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Storage;
using Meridian.Storage.Ledger;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.FinancialOperations.AccountingClose;

public sealed partial class AccountingCloseServicesTests
{
    [Theory]
    [InlineData(CloseDeadlineAnchorDto.PeriodEnd, 1, CloseDeadlineDayCountDto.BusinessDays, CloseDeadlineAdjustmentDto.None, false, "2026-11-02")]
    [InlineData(CloseDeadlineAnchorDto.PeriodEnd, 1, CloseDeadlineDayCountDto.BusinessDays, CloseDeadlineAdjustmentDto.None, true, "2026-11-03")]
    [InlineData(CloseDeadlineAnchorDto.PeriodEnd, 1, CloseDeadlineDayCountDto.CalendarDays, CloseDeadlineAdjustmentDto.None, false, "2026-11-01")]
    [InlineData(CloseDeadlineAnchorDto.PeriodEnd, 0, CloseDeadlineDayCountDto.CalendarDays, CloseDeadlineAdjustmentDto.FollowingBusinessDay, false, "2026-11-02")]
    [InlineData(CloseDeadlineAnchorDto.PeriodEnd, 0, CloseDeadlineDayCountDto.CalendarDays, CloseDeadlineAdjustmentDto.PrecedingBusinessDay, false, "2026-10-30")]
    [InlineData(CloseDeadlineAnchorDto.PeriodStart, -1, CloseDeadlineDayCountDto.BusinessDays, CloseDeadlineAdjustmentDto.None, false, "2026-09-30")]
    public async Task Scenario_PrepareNextPeriod_SeptemberToOctoberUsesExplicitCalendarRules(
        CloseDeadlineAnchorDto anchor, int offset, CloseDeadlineDayCountDto dayCount,
        CloseDeadlineAdjustmentDto adjustment, bool hasHoliday, string expected)
    {
        using var fixture = new PreparationFixture();
        var rule = new CloseDeadlineRuleDto("reconciliation-review", anchor, offset, dayCount, adjustment);
        var template = await fixture.CaptureAsync(rule, hasHoliday);

        var preview = await fixture.PreviewAsync(template);

        preview.CanCreate.Should().BeTrue();
        preview.TargetBook.LedgerBookId.Should().Be(fixture.TargetBook.LedgerBookId);
        preview.TargetPeriod.PeriodId.Should().Be(fixture.TargetPeriod.PeriodId);
        preview.TargetPeriod.StartDate.Should().Be(new DateOnly(2026, 10, 1));
        preview.TargetPeriod.EndDate.Should().Be(new DateOnly(2026, 10, 31));
        preview.Tasks.Single(task => task.TaskId == rule.TaskId).DueDate.Should().Be(DateOnly.Parse(expected));
        preview.Tasks.Single(task => task.TaskId == rule.TaskId).SourceDueDate.Should().Be(new DateOnly(2026, 10, 1));
        preview.Calendar.CalendarId.Should().Be("explicit-close-calendar");
        preview.Calendar.Version.Should().Be("2026-v1");
        await fixture.Workflows.DidNotReceive().StartPreparedWorkflowAsync(
            Arg.Any<OperationsStartWorkflowRequestDto>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Scenario_PrepareNextPeriod_CreatesFreshSignoffsAndLeavesSeptemberHistoryUntouched()
    {
        using var fixture = new PreparationFixture();
        var sourceBefore = JsonSerializer.Serialize(fixture.SourcePlan);
        var workflowBefore = JsonSerializer.Serialize(fixture.SourceWorkflow);
        var template = await fixture.CaptureAsync();
        var preview = await fixture.PreviewAsync(template);

        var result = await fixture.CreateAsync(preview);

        result.WorkflowId.Should().NotBe(fixture.SourceWorkflow.WorkflowId);
        result.TemplateId.Should().Be(template.TemplateId);
        result.TemplateVersion.Should().Be(template.Version);
        result.SourceWorkflowId.Should().Be(fixture.SourceWorkflow.WorkflowId);
        result.TargetLedgerBookId.Should().Be(fixture.TargetBook.LedgerBookId);
        result.TargetPeriodId.Should().Be(fixture.TargetPeriod.PeriodId);
        result.Plan.PeriodId.Should().Be(fixture.TargetPeriod.PeriodId.ToString("D"));
        result.Plan.PeriodStart.Should().Be(fixture.TargetPeriod.StartDate);
        result.Plan.PeriodEnd.Should().Be(fixture.TargetPeriod.EndDate);
        result.Plan.IsPeriodLocked.Should().BeFalse();
        result.Plan.LateAdjustments.Should().BeEmpty();
        result.Plan.EvidenceReviews.Should().BeEmpty();
        result.Plan.Tasks.Select(task => task.TaskId).Should().Equal(fixture.SourcePlan.Tasks.Select(task => task.TaskId));
        result.Plan.Tasks.Should().OnlyContain(task => task.Status != CloseTaskStatusDto.SignedOff);
        result.Plan.Tasks.Should().OnlyContain(task => task.SignOffs.Count == 0 && task.EvidenceLinks.Count == 0);
        result.Plan.Tasks.SelectMany(task => task.SignOffRequirements).Should()
            .OnlyContain(requirement => requirement.ApprovedCount == 0 && !requirement.IsSatisfied);
        var certification = result.Plan.Tasks.Single(task => task.TaskId == "report-certification");
        certification.Status.Should().Be(CloseTaskStatusDto.WaitingOnDependency);
        certification.Dependencies.Should().ContainSingle(dependency =>
            dependency.DependsOnTaskId == "reconciliation-review" && dependency.Reason == "Independent reconciliation precedes certification.");
        certification.SignOffRequirements.Should().ContainSingle(requirement =>
            requirement.Role == "Controller" && requirement.RequiredApprovalCount == 2 &&
            requirement.EvidenceRequirement == "Fresh report certification evidence");
        preview.Tasks.Should().OnlyContain(task => task.OwnerChanged && task.Owner == "October controller");
        result.Plan.Tasks.Should().OnlyContain(task => task.Owner == "October controller");
        result.History.Should().NotBeEmpty();
        JsonSerializer.Serialize(fixture.SourcePlan).Should().Be(sourceBefore);
        JsonSerializer.Serialize(fixture.SourceWorkflow).Should().Be(workflowBefore);
        fixture.SourcePlan.IsPeriodLocked.Should().BeTrue();
        fixture.SourcePlan.EvidenceReviews.Should().ContainSingle();
        fixture.SourcePlan.LateAdjustments.Should().ContainSingle();
        fixture.CreatedWorkflows.Single().Approvals.Should().BeEmpty();
        fixture.CreatedWorkflows.Single().EvidenceLinks.Should().BeEmpty();
        fixture.CreatedWorkflows.Single().ClosePackage.Should().BeNull();
        await fixture.Store.DidNotReceive().SavePeriodAsync(Arg.Any<LedgerAccountingPeriod>(),
            Arg.Any<long>(), Arg.Any<PeriodCloseEventRecord?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("source-version")]
    [InlineData("source-owner")]
    [InlineData("target-version")]
    [InlineData("target-dates")]
    [InlineData("target-lock")]
    [InlineData("target-policy")]
    public async Task Scenario_PrepareNextPeriod_RejectsPreviewAfterSourceOrAuthorityChanges(string drift)
    {
        using var fixture = new PreparationFixture();
        var template = await fixture.CaptureAsync();
        var preview = await fixture.PreviewAsync(template);
        switch (drift)
        {
            case "source-version":
                fixture.SourceWorkflow = fixture.SourceWorkflow with { Version = fixture.SourceWorkflow.Version + 1 };
                fixture.SourcePlan = fixture.SourcePlan with { WorkflowVersion = fixture.SourceWorkflow.Version };
                break;
            case "source-owner":
                fixture.SourcePlan = fixture.SourcePlan with
                {
                    Tasks = fixture.SourcePlan.Tasks.Select(task => task with { Owner = "Changed owner" }).ToArray()
                };
                break;
            case "target-version":
                fixture.TargetPeriod = fixture.TargetPeriod with { Version = fixture.TargetPeriod.Version + 1 };
                break;
            case "target-dates":
                fixture.TargetPeriod = fixture.TargetPeriod with { EndDate = new DateOnly(2026, 10, 30) };
                break;
            case "target-lock":
                fixture.TargetPeriod = fixture.TargetPeriod with { Status = "HardClosed" };
                break;
            case "target-policy":
                fixture.TargetBook = fixture.TargetBook with { AccountingPolicyVersion = "changed-after-preview" };
                break;
        }

        var create = () => fixture.CreateAsync(preview);

        await create.Should().ThrowAsync<InvalidOperationException>();
        fixture.CreatedWorkflows.Should().BeEmpty();
        await fixture.Workflows.DidNotReceive().StartPreparedWorkflowAsync(
            Arg.Any<OperationsStartWorkflowRequestDto>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Scenario_PrepareNextPeriod_UnresolvedOwnersBlockCreation()
    {
        using var fixture = new PreparationFixture();
        var template = await fixture.CaptureAsync();
        var preview = await fixture.Service.PreviewAsync(new PreviewClosePreparationRequestDto(
            template.TemplateId, template.Version, fixture.TargetBook.LedgerBookId,
            fixture.TargetPeriod.PeriodId, []), PreparationFixture.Actor, PreparationFixture.Tenant, PreparationFixture.Company);

        preview.CanCreate.Should().BeFalse();
        preview.Issues.Should().Contain(issue => issue.IsBlocking && issue.Code.Contains("OWNER", StringComparison.OrdinalIgnoreCase));
        var create = () => fixture.CreateAsync(preview);
        await create.Should().ThrowAsync<InvalidOperationException>();
        fixture.CreatedWorkflows.Should().BeEmpty();
    }

    [Fact]
    public async Task Scenario_PrepareNextPeriod_RequiresAcknowledgementOfChangedPolicy()
    {
        using var fixture = new PreparationFixture();
        var template = await fixture.CaptureAsync();
        fixture.TargetBook = fixture.TargetBook with { AccountingPolicyId = "policy-october", AccountingPolicyVersion = "v2" };

        var preview = await fixture.PreviewAsync(template);
        var acknowledged = await fixture.PreviewAsync(template, acknowledgePolicy: true);

        preview.PolicyChanged.Should().BeTrue();
        preview.CanCreate.Should().BeFalse();
        preview.Issues.Should().Contain(issue => issue.IsBlocking && issue.Code.Contains("POLICY", StringComparison.OrdinalIgnoreCase));
        acknowledged.PolicyChanged.Should().BeTrue();
        acknowledged.CanCreate.Should().BeTrue();
    }

    [Fact]
    public async Task Scenario_PrepareNextPeriod_CurrencyMismatchCannotBeAcknowledgedAway()
    {
        using var fixture = new PreparationFixture();
        var template = await fixture.CaptureAsync();
        fixture.TargetBook = fixture.TargetBook with { BaseCurrency = "EUR" };

        var preview = await fixture.PreviewAsync(template, acknowledgePolicy: true);

        preview.CanCreate.Should().BeFalse();
        preview.Issues.Should().Contain(issue => issue.IsBlocking && issue.Code.Contains("CURRENCY", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Scenario_PrepareNextPeriod_RejectsPeriodFromDifferentBook()
    {
        using var fixture = new PreparationFixture();
        var template = await fixture.CaptureAsync();
        fixture.TargetPeriod = fixture.TargetPeriod with { LedgerBookId = Guid.NewGuid() };

        var preview = () => fixture.PreviewAsync(template);

        await preview.Should().ThrowAsync<InvalidOperationException>();
        fixture.CreatedWorkflows.Should().BeEmpty();
    }

    [Fact]
    public async Task Scenario_PrepareNextPeriod_RejectsTargetBookForAnotherAccountInTheSameFund()
    {
        using var fixture = new PreparationFixture();
        var template = await fixture.CaptureAsync();
        fixture.TargetBook = fixture.TargetBook with { FundStructureNodeId = Guid.NewGuid() };

        var preview = () => fixture.PreviewAsync(template);

        await preview.Should().ThrowAsync<InvalidOperationException>();
        fixture.CreatedWorkflows.Should().BeEmpty();
    }

    [Fact]
    public async Task Scenario_PrepareNextPeriod_UsesAuthoritativeFiscalDatesInsteadOfThePeriodLabel()
    {
        using var fixture = new PreparationFixture();
        var template = await fixture.CaptureAsync();
        fixture.TargetPeriod = fixture.TargetPeriod with
        {
            StartDate = new DateOnly(2026, 10, 2),
            EndDate = new DateOnly(2026, 10, 29)
        };
        var preview = await fixture.PreviewAsync(template);

        var result = await fixture.CreateAsync(preview);

        preview.TargetPeriod.Label.Should().Be("2026-10");
        preview.Tasks.Single(task => task.TaskId == "reconciliation-review").DueDate.Should().Be(new DateOnly(2026, 10, 30));
        result.Plan.PeriodId.Should().Be(fixture.TargetPeriod.PeriodId.ToString("D"));
        result.Plan.PeriodStart.Should().Be(new DateOnly(2026, 10, 2));
        result.Plan.PeriodEnd.Should().Be(new DateOnly(2026, 10, 29));
    }

    [Fact]
    public async Task Scenario_PrepareNextPeriod_RetryAndRestartReturnOneRetainedPlan()
    {
        using var fixture = new PreparationFixture();
        var template = await fixture.CaptureAsync();
        var preview = await fixture.PreviewAsync(template);
        var first = await fixture.CreateAsync(preview);

        var retry = await fixture.CreateAsync(preview);
        var restarted = fixture.NewService();
        var recovered = await restarted.CreateAsync(new CreatePreparedClosePlanRequestDto(
            preview.PreviewId, "prepare-october"), PreparationFixture.Actor, PreparationFixture.Tenant, PreparationFixture.Company);

        first.WasAlreadyCreated.Should().BeFalse();
        retry.WasAlreadyCreated.Should().BeTrue();
        recovered.WasAlreadyCreated.Should().BeTrue();
        retry.WorkflowId.Should().Be(first.WorkflowId);
        recovered.WorkflowId.Should().Be(first.WorkflowId);
        recovered.TemplateVersion.Should().Be(template.Version);
        recovered.History.Should().BeEquivalentTo(first.History);
        fixture.CreatedWorkflows.Should().ContainSingle();
        await fixture.Workflows.Received(1).StartPreparedWorkflowAsync(
            Arg.Any<OperationsStartWorkflowRequestDto>(), first.WorkflowId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Scenario_PrepareNextPeriod_InterruptedConfigurationResumesTheSameWorkflowAfterRestart()
    {
        using var fixture = new PreparationFixture();
        var template = await fixture.CaptureAsync();
        var preview = await fixture.PreviewAsync(template);
        fixture.FailNextConfiguration = true;
        var interrupted = () => fixture.CreateAsync(preview);

        await interrupted.Should().ThrowAsync<IOException>();
        var reservedWorkflowId = fixture.CreatedWorkflows.Should().ContainSingle().Subject.WorkflowId;
        var restarted = fixture.NewService();
        var recovered = await restarted.CreateAsync(new CreatePreparedClosePlanRequestDto(
            preview.PreviewId, "prepare-october"), PreparationFixture.Actor, PreparationFixture.Tenant, PreparationFixture.Company);

        recovered.WorkflowId.Should().Be(reservedWorkflowId);
        recovered.Plan.Tasks.Should().HaveCount(fixture.SourcePlan.Tasks.Count);
        recovered.Plan.Tasks.Should().OnlyContain(task => task.SignOffs.Count == 0);
        fixture.CreatedWorkflows.Should().ContainSingle();
        await fixture.Workflows.Received(2).StartPreparedWorkflowAsync(
            Arg.Any<OperationsStartWorkflowRequestDto>(), reservedWorkflowId, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scenario_PrepareNextPeriod_RetainedConfigurationRecoversAfterLostReceiptAndSourceDrift(bool updateTargetSetup)
    {
        using var fixture = new PreparationFixture();
        var template = await fixture.CaptureAsync();
        var preview = await fixture.PreviewAsync(template);
        fixture.FailNextConfigurationAfterSave = true;
        var interrupted = () => fixture.CreateAsync(preview);

        await interrupted.Should().ThrowAsync<IOException>();
        var reservedWorkflowId = fixture.CreatedWorkflows.Should().ContainSingle().Subject.WorkflowId;
        fixture.SourceWorkflow = fixture.SourceWorkflow with { Version = fixture.SourceWorkflow.Version + 1 };
        fixture.SourcePlan = fixture.SourcePlan with
        {
            WorkflowVersion = fixture.SourceWorkflow.Version,
            Tasks = fixture.SourcePlan.Tasks.Select(task => task with { Owner = "Changed after retention" }).ToArray()
        };
        var expectedOwner = "October controller";
        ClosePeriodPlanDto? reconfigured = null;
        if (updateTargetSetup)
        {
            expectedOwner = "October replacement controller";
            var retainedPlan = await fixture.DestinationPlans.GetPeriodPlanAsync(reservedWorkflowId);
            reconfigured = await fixture.DestinationPlans.ConfigurePeriodPlanAsync(new UpsertClosePeriodPlanConfigurationRequestDto(
                reservedWorkflowId,
                TaskConfigurations: retainedPlan!.Configuration!.TaskConfigurations.Select(task => task with { Owner = expectedOwner }).ToArray(),
                EvidenceLinks: [$"evidence:close-plan:{reservedWorkflowId:D}:book:{fixture.TargetBook.LedgerBookId:D}:configuration-approval"]),
                PreparationFixture.Actor);
        }

        var recovered = await fixture.NewService().CreateAsync(new CreatePreparedClosePlanRequestDto(
            preview.PreviewId, "prepare-october"), PreparationFixture.Actor, PreparationFixture.Tenant, PreparationFixture.Company);

        recovered.WorkflowId.Should().Be(reservedWorkflowId);
        recovered.WasAlreadyCreated.Should().BeTrue();
        recovered.Plan.Tasks.Should().OnlyContain(task => task.Owner == expectedOwner && task.SignOffs.Count == 0);
        recovered.Plan.Configuration!.Preparation!.TemplateVersion.Should().Be(template.Version);
        recovered.Plan.Configuration.EvidenceLinks.Should().Contain(link => link.StartsWith("close-plan-preparation:", StringComparison.Ordinal));
        if (reconfigured is not null)
            recovered.Plan.Configuration.Should().BeEquivalentTo(reconfigured.Configuration);
        recovered.History.Should().Contain(history => history.EventType == "PlanCreated");
        fixture.CreatedWorkflows.Should().ContainSingle();
        await fixture.Workflows.Received(1).StartPreparedWorkflowAsync(
            Arg.Any<OperationsStartWorkflowRequestDto>(), reservedWorkflowId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Scenario_PrepareNextPeriod_ConcurrentRequestsAcrossInstancesCreateOnePlan()
    {
        using var fixture = new PreparationFixture();
        var template = await fixture.CaptureAsync();
        var preview = await fixture.PreviewAsync(template);
        var secondInstance = fixture.NewService();

        var results = await Task.WhenAll(fixture.CreateAsync(preview), secondInstance.CreateAsync(
            new CreatePreparedClosePlanRequestDto(preview.PreviewId, "prepare-october"),
            PreparationFixture.Actor, PreparationFixture.Tenant, PreparationFixture.Company));

        results.Select(result => result.WorkflowId).Distinct().Should().ContainSingle();
        results.Count(result => result.WasAlreadyCreated).Should().Be(1);
        fixture.CreatedWorkflows.Should().ContainSingle();
        await fixture.Workflows.Received(1).StartPreparedWorkflowAsync(
            Arg.Any<OperationsStartWorkflowRequestDto>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Scenario_PrepareNextPeriod_DifferentKeyCannotCreateSecondPlanForTheSameTarget()
    {
        using var fixture = new PreparationFixture();
        var template = await fixture.CaptureAsync();
        var firstPreview = await fixture.PreviewAsync(template);
        var secondPreview = await fixture.PreviewAsync(template);
        await fixture.CreateAsync(firstPreview);

        var duplicate = () => fixture.Service.CreateAsync(new CreatePreparedClosePlanRequestDto(
            secondPreview.PreviewId, "different-preparation-key"), PreparationFixture.Actor, PreparationFixture.Tenant, PreparationFixture.Company);

        await duplicate.Should().ThrowAsync<InvalidOperationException>();
        fixture.CreatedWorkflows.Should().ContainSingle();
        await fixture.Workflows.Received(1).StartPreparedWorkflowAsync(
            Arg.Any<OperationsStartWorkflowRequestDto>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Scenario_PrepareNextPeriod_ExplicitEmptyDependenciesStayEmpty()
    {
        using var fixture = new PreparationFixture();
        fixture.SourcePlan = fixture.SourcePlan with
        {
            Tasks = fixture.SourcePlan.Tasks.Select(task => task with { Dependencies = [] }).ToArray()
        };
        var template = await fixture.CaptureAsync();
        var preview = await fixture.PreviewAsync(template);

        var result = await fixture.CreateAsync(preview);

        result.Plan.Tasks.Should().OnlyContain(task => task.Dependencies.Count == 0);
        result.Plan.Tasks.Should().OnlyContain(task => task.Status != CloseTaskStatusDto.WaitingOnDependency);
    }

    [Fact]
    public async Task Scenario_PrepareNextPeriod_RejectsSourceProjectionWithUnresolvedWorkflowTasks()
    {
        using var fixture = new PreparationFixture();
        fixture.SourcePlan = fixture.SourcePlan with
        {
            Tasks = [.. fixture.SourcePlan.Tasks, fixture.SourcePlan.Tasks[0] with
            {
                TaskId = "custom-valuation-review",
                DisplayName = "Independent valuation review",
                Dependencies = [new CloseDependencyDto("valuation-reconciliation", "reconciliation-review", "Reconcile before valuation review.")]
            }]
        };
        var capture = () => fixture.CaptureAsync();

        await capture.Should().ThrowAsync<InvalidOperationException>();
        fixture.CreatedWorkflows.Should().BeEmpty();
    }

    [Fact]
    public async Task Scenario_PrepareNextPeriod_CaptureRetainsImmutableTemplateVersions()
    {
        using var fixture = new PreparationFixture();
        var first = await fixture.CaptureAsync();
        var second = await fixture.Service.CaptureTemplateAsync(fixture.CaptureRequest() with
        {
            TemplateId = first.TemplateId,
            Name = "Revised close controls"
        }, PreparationFixture.Actor, PreparationFixture.Tenant, PreparationFixture.Company);
        var restarted = fixture.NewService();

        var retainedFirst = await restarted.GetTemplateAsync(first.TemplateId, first.Version);
        var retainedSecond = await restarted.GetTemplateAsync(second.TemplateId, second.Version);

        second.TemplateId.Should().Be(first.TemplateId);
        second.Version.Should().Be(first.Version + 1);
        retainedFirst.Should().BeEquivalentTo(first);
        retainedSecond.Should().BeEquivalentTo(second);
        second.History.Should().Contain(history => history.Actor == PreparationFixture.Actor);
    }

    [Fact]
    public async Task Scenario_PrepareNextPeriod_ExplicitlySelectedOlderTemplateVersionCanBePrepared()
    {
        using var fixture = new PreparationFixture();
        var original = await fixture.CaptureAsync();
        await fixture.Service.CaptureTemplateAsync(fixture.CaptureRequest() with
        {
            TemplateId = original.TemplateId,
            Name = "Version two"
        }, PreparationFixture.Actor, PreparationFixture.Tenant, PreparationFixture.Company);
        var preview = await fixture.PreviewAsync(original);

        var result = await fixture.CreateAsync(preview);

        result.TemplateVersion.Should().Be(original.Version);
        result.Plan.Configuration!.Preparation!.TemplateVersion.Should().Be(original.Version);
    }

    [Fact]
    public async Task Scenario_PrepareNextPeriod_NewTemplateCaptureAfterPreviewRequiresFreshPreview()
    {
        using var fixture = new PreparationFixture();
        var template = await fixture.CaptureAsync();
        var preview = await fixture.PreviewAsync(template);
        await fixture.Service.CaptureTemplateAsync(fixture.CaptureRequest() with
        {
            TemplateId = template.TemplateId,
            Name = "Changed after preview"
        }, PreparationFixture.Actor, PreparationFixture.Tenant, PreparationFixture.Company);

        var create = () => fixture.CreateAsync(preview);

        await create.Should().ThrowAsync<InvalidOperationException>();
        fixture.CreatedWorkflows.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scenario_PrepareNextPeriod_InitialPreparationCannotOverwriteRetainedConfiguration(bool existingPreparation)
    {
        using var fixture = new PreparationFixture();
        var workflowId = Guid.NewGuid();
        await fixture.Workflows.StartPreparedWorkflowAsync(new OperationsStartWorkflowRequestDto(
            fixture.SourceWorkflow.FundAccountId, fixture.TargetPeriod.PeriodId.ToString("D"), null, "custodian",
            PreparationFixture.Actor, LedgerBookId: fixture.TargetBook.LedgerBookId), workflowId);
        var lineage = new ClosePlanPreparationLineageDto(Guid.NewGuid(), 1, fixture.SourceWorkflow.WorkflowId,
            fixture.TargetPeriod.PeriodId, fixture.TargetPeriod.StartDate, fixture.TargetPeriod.EndDate,
            DateTimeOffset.UtcNow, PreparationFixture.Actor, []);
        var initialRequest = new UpsertClosePeriodPlanConfigurationRequestDto(workflowId,
            TaskConfigurations: [new CloseTaskConfigurationDto("reconciliation-review", Owner: "Retained owner")],
            EvidenceLinks: [$"evidence:close-plan:{workflowId:D}:book:{fixture.TargetBook.LedgerBookId:D}:configuration-approval"])
        {
            Preparation = existingPreparation ? lineage : null
        };
        var initialPlan = await fixture.DestinationPlans.ConfigurePeriodPlanAsync(initialRequest, PreparationFixture.Actor);
        var overwrite = () => fixture.DestinationPlans.ConfigurePeriodPlanAsync(initialRequest with
        {
            TaskConfigurations = [new CloseTaskConfigurationDto("reconciliation-review", Owner: "Attempted overwrite")],
            Preparation = lineage
        }, PreparationFixture.Actor);

        await overwrite.Should().ThrowAsync<InvalidOperationException>();

        var retained = await fixture.DestinationPlans.GetPeriodPlanAsync(workflowId);
        retained!.Configuration.Should().BeEquivalentTo(initialPlan!.Configuration);
        retained.Tasks.Single(task => task.TaskId == "reconciliation-review").Owner.Should().Be("Retained owner");
    }

    private sealed class PreparationFixture : IDisposable
    {
        public const string Actor = "close-operator";
        public const string Tenant = "tenant-alpha";
        public const string Company = "company-alpha";
        private static readonly DateTimeOffset CapturedAt = DateTimeOffset.Parse("2026-10-01T12:00:00Z");
        private readonly TemporaryStorageRoot _storage = new();
        private readonly TimeProvider? _timeProvider;
        private readonly Dictionary<Guid, OperationsContinuityWorkflowDto> _createdWorkflows = [];

        public PreparationFixture(TimeProvider? timeProvider = null)
        {
            _timeProvider = timeProvider;
            SourceWorkflow = BuildLockedCloseWorkflow(BuildCloseWorkflow(Guid.NewGuid(), "Done", "Done", "September preparer", "September reviewer")) with
            {
                PeriodId = "2026-09",
                Approvals = [new OperationsApprovalDto("september-approval", OperationsApprovalStateDto.Approved,
                    "September preparer", "September reviewer", "Reviewed September close", CapturedAt, CapturedAt, [])]
            };
            var sourceBookId = SourceWorkflow.LedgerBookId!.Value;
            var sourcePeriod = new LedgerAccountingPeriod(Guid.NewGuid(), sourceBookId, 2026, 9, "2026-09",
                new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "HardClosed", CapturedAt, CapturedAt, 3);
            SourceBook = new LedgerBookDto(sourceBookId, "fund-alpha", SourceWorkflow.FundAccountId,
                FundStructureNodeKindDto.Fund, "September primary book", "USD", CapturedAt, CapturedAt,
                AccountingPolicyId: "close-policy", AccountingPolicyVersion: "v1");
            TargetBook = SourceBook with { LedgerBookId = Guid.NewGuid(), DisplayName = "October primary book" };
            TargetPeriod = new LedgerAccountingPeriod(Guid.NewGuid(), TargetBook.LedgerBookId, 2026, 10,
                "2026-10", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), "Open", CapturedAt, null, 1);
            var materiality = new MaterialityPolicyDto("close-materiality", 10000m, 0.01m, "USD", "Controller", true);
            SourcePlan = new ClosePeriodPlanDto($"close-plan-{SourceWorkflow.WorkflowId:D}", "fund-alpha", sourceBookId,
                "2026-09", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 2), true,
                [
                    CompletedTask("reconciliation-review", "Reconciliation review", new DateOnly(2026, 10, 1), [], 1, "Fresh reconciliation evidence"),
                    CompletedTask("report-certification", "Report certification", new DateOnly(2026, 10, 2),
                        [new CloseDependencyDto("certification-reconciliation", "reconciliation-review", "Independent reconciliation precedes certification.")],
                        2, "Fresh report certification evidence")
                ],
                [new LateAdjustmentRequestDto("september-adjustment", Guid.NewGuid(), "September preparer", CapturedAt,
                    15000m, "USD", "September adjustment", ManualJournalEntryStatusDto.Approved, materiality,
                    ["evidence:september-journal"], "September reviewer", CapturedAt)], materiality,
                EvidenceReviews: [new CloseEvidenceReviewDto("september-review", "SeptemberEvidence", null,
                    "September reviewer", CapturedAt, "Reviewed September only", ["evidence:september-reviewed"])],
                WorkflowVersion: SourceWorkflow.Version, WorkflowId: SourceWorkflow.WorkflowId, FundAccountId: SourceWorkflow.FundAccountId,
                EvidenceVersion: "september-retained-evidence");

            Workflows.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call =>
                call.ArgAt<Guid>(0) == SourceWorkflow.WorkflowId ? SourceWorkflow : _createdWorkflows.GetValueOrDefault(call.ArgAt<Guid>(0)));
            Workflows.ListAsync(Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<OperationsWorkflowStatusDto?>(),
                Arg.Any<CancellationToken>(), Arg.Any<Guid?>()).Returns(Array.Empty<OperationsContinuityWorkflowSummaryDto>());
            Workflows.StartPreparedWorkflowAsync(Arg.Any<OperationsStartWorkflowRequestDto>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call => StartPrepared(call.ArgAt<OperationsStartWorkflowRequestDto>(0), call.ArgAt<Guid>(1)));
            Books.GetBookAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call =>
                call.ArgAt<Guid>(0) == SourceBook.LedgerBookId ? SourceBook : call.ArgAt<Guid>(0) == TargetBook.LedgerBookId ? TargetBook : null);
            Store.GetPeriodAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call =>
                call.ArgAt<Guid>(0) == TargetPeriod.PeriodId ? TargetPeriod : call.ArgAt<Guid>(0) == sourcePeriod.PeriodId ? sourcePeriod : null);
            Store.ListPeriodsAsync(Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
                .Returns(call => (IReadOnlyList<LedgerAccountingPeriod>)(call.ArgAt<Guid?>(0) == sourceBookId ? [sourcePeriod] : [TargetPeriod]));
            var destinationPlans = DestinationPlans = new AccountingCloseManagementService(Workflows,
                new StorageOptions { RootPath = _storage.RootPath });
            ClosePlans.GetPeriodPlanAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call =>
                ReadPlanAsync(call.ArgAt<Guid>(0), destinationPlans));
            ClosePlans.GetPeriodPlanScopedAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(call => ReadPlanAsync(call.ArgAt<Guid>(0), destinationPlans));
            ClosePlans.ConfigurePeriodPlanAsync(Arg.Any<UpsertClosePeriodPlanConfigurationRequestDto>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => ConfigureAsync(call.ArgAt<UpsertClosePeriodPlanConfigurationRequestDto>(0), call.ArgAt<string>(1), destinationPlans));
            ClosePlans.ConfigurePeriodPlanScopedAsync(Arg.Any<UpsertClosePeriodPlanConfigurationRequestDto>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(call =>
                    ConfigureAsync(call.ArgAt<UpsertClosePeriodPlanConfigurationRequestDto>(0), call.ArgAt<string>(1), destinationPlans));
            Service = NewService();
        }

        public IOperationsContinuityWorkflowService Workflows { get; } = Substitute.For<IOperationsContinuityWorkflowService>();
        public IAccountingCloseManagementService ClosePlans { get; } = Substitute.For<IAccountingCloseManagementService>();
        public ILedgerBookService Books { get; } = Substitute.For<ILedgerBookService>();
        public ILedgerJournalStore Store { get; } = Substitute.For<ILedgerJournalStore>();
        public OperationsContinuityWorkflowDto SourceWorkflow { get; set; }
        public ClosePeriodPlanDto SourcePlan { get; set; }
        public LedgerBookDto SourceBook { get; }
        public LedgerBookDto TargetBook { get; set; }
        public LedgerAccountingPeriod TargetPeriod { get; set; }
        public bool FailNextConfiguration { get; set; }
        public bool FailNextConfigurationAfterSave { get; set; }
        public AccountingClosePreparationService Service { get; }
        public AccountingCloseManagementService DestinationPlans { get; }
        public IReadOnlyCollection<OperationsContinuityWorkflowDto> CreatedWorkflows => _createdWorkflows.Values;

        public AccountingClosePreparationService NewService() => new(ClosePlans, Workflows, Books, Store,
            new StorageOptions { RootPath = _storage.RootPath }, _timeProvider);

        public CaptureClosePlanTemplateRequestDto CaptureRequest(CloseDeadlineRuleDto? rule = null, bool hasHoliday = false) =>
            new(SourceWorkflow.WorkflowId, "Monthly close controls",
                SourcePlan.Tasks.Select(task => rule is not null && rule.TaskId == task.TaskId ? rule :
                    new CloseDeadlineRuleDto(task.TaskId, CloseDeadlineAnchorDto.PeriodEnd,
                        task.TaskId == "report-certification" ? 2 : 1,
                        CloseDeadlineDayCountDto.BusinessDays, CloseDeadlineAdjustmentDto.None)).ToArray(),
                new ClosePreparationCalendarDto("explicit-close-calendar", "2026-v1", [0, 6],
                    hasHoliday ? [new DateOnly(2026, 11, 2)] : []));

        public Task<ClosePlanTemplateDto> CaptureAsync(CloseDeadlineRuleDto? rule = null, bool hasHoliday = false) =>
            Service.CaptureTemplateAsync(CaptureRequest(rule, hasHoliday), Actor, Tenant, Company);

        public Task<ClosePreparationPreviewDto> PreviewAsync(ClosePlanTemplateDto template, bool acknowledgePolicy = false) =>
            Service.PreviewAsync(new PreviewClosePreparationRequestDto(template.TemplateId, template.Version,
                TargetBook.LedgerBookId, TargetPeriod.PeriodId,
                [new ClosePreparationOwnerMappingDto("Controller", "October controller")], acknowledgePolicy), Actor, Tenant, Company);

        public Task<PreparedClosePlanResultDto> CreateAsync(ClosePreparationPreviewDto preview) =>
            Service.CreateAsync(new CreatePreparedClosePlanRequestDto(preview.PreviewId, "prepare-october"), Actor, Tenant, Company);

        private Task<ClosePeriodPlanDto?> ReadPlanAsync(Guid workflowId, AccountingCloseManagementService destinationPlans) =>
            workflowId == SourceWorkflow.WorkflowId ? Task.FromResult<ClosePeriodPlanDto?>(SourcePlan) : destinationPlans.GetPeriodPlanAsync(workflowId);

        private async Task<ClosePeriodPlanDto?> ConfigureAsync(UpsertClosePeriodPlanConfigurationRequestDto request, string actor,
            AccountingCloseManagementService destinationPlans)
        {
            if (FailNextConfiguration)
            {
                FailNextConfiguration = false;
                throw new IOException("Simulated interrupted close-plan retention.");
            }
            var plan = await destinationPlans.ConfigurePeriodPlanAsync(request, actor);
            if (FailNextConfigurationAfterSave)
            {
                FailNextConfigurationAfterSave = false;
                throw new IOException("Simulated lost close-plan retention receipt.");
            }
            return plan;
        }

        private OperationsTransitionResultDto StartPrepared(OperationsStartWorkflowRequestDto request, Guid workflowId)
        {
            request.SecurityMasterSnapshotId.Should().BeNull();
            request.EvidenceLinks.Should().BeNullOrEmpty();
            if (!_createdWorkflows.TryGetValue(workflowId, out var workflow))
            {
                workflow = BuildCloseWorkflow(workflowId, "Pending", "Pending") with
                {
                    FundAccountId = request.FundAccountId,
                    LedgerBookId = request.LedgerBookId,
                    PeriodId = request.PeriodId,
                    Status = OperationsWorkflowStatusDto.NotStarted,
                    Version = 1,
                    Approvals = [],
                    EvidenceLinks = [],
                    ClosePackage = null
                };
                _createdWorkflows.Add(workflowId, workflow);
            }
            return new OperationsTransitionResultDto(true, null, null, workflow, [], [], workflow.Version);
        }

        private static CloseTaskDto CompletedTask(string id, string label, DateOnly due,
            IReadOnlyList<CloseDependencyDto> dependencies, int approvalCount, string evidenceRequirement) =>
            new(id, label, CloseTaskStatusDto.SignedOff, "Controller", due, dependencies,
                Enumerable.Range(0, approvalCount).Select(index => new CloseSignOffDto($"september-{id}-{index}", "Controller",
                    $"September reviewer {index}", ManualJournalEntryStatusDto.Approved, CapturedAt, ["evidence:september-signoff"])).ToArray(),
                ["evidence:september-task"], SignOffRequirements:
                [new CloseSignOffRequirementDto($"requirement-{id}", "Controller", approvalCount, approvalCount, true, evidenceRequirement)]);

        public void Dispose() => _storage.Dispose();
    }
}

using System.Collections.Immutable;
using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.FinancialOperations.OperationsContinuity;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.FinancialOperations.AccountingClose;

/// <summary>
/// Guards month-end accounting close scenarios where FX rates, posting balance, source-event lineage,
/// period locks, and evidence gates must remain deterministic for operator replay.
/// </summary>
public sealed class AccountingCloseServicesTests
{
    [Fact]
    public async Task Scenario_ClosePlan_DependencyWaitsUntilPredecessorIsSignedOff()
    {
        var workflowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(BuildCloseWorkflow(
                workflowId,
                firstTaskStatus: "Pending",
                secondTaskStatus: "Pending"));
        var service = new AccountingCloseManagementService(workflowService);

        var plan = await service.GetPeriodPlanAsync(workflowId);

        plan.Should().NotBeNull();
        var dependentTask = plan!.Tasks.Single(task => task.TaskId == "report-certification");
        dependentTask.Dependencies.Should().ContainSingle(dependency => dependency.DependsOnTaskId == "reconciliation-review");
        dependentTask.Status.Should().Be(CloseTaskStatusDto.WaitingOnDependency);
        plan.ValidationIssues.Should().ContainSingle(issue =>
            issue.Code == "CloseTaskWaitingOnDependency" &&
            issue.TargetId == "report-certification");
    }

    [Fact]
    public async Task Scenario_ClosePlan_DependencyAdvancesAfterPredecessorIsSignedOff()
    {
        var workflowId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(BuildCloseWorkflow(
                workflowId,
                firstTaskStatus: "Done",
                secondTaskStatus: "Pending"));
        var service = new AccountingCloseManagementService(workflowService);

        var plan = await service.GetPeriodPlanAsync(workflowId);

        plan.Should().NotBeNull();
        var dependentTask = plan!.Tasks.Single(task => task.TaskId == "report-certification");
        dependentTask.Dependencies.Should().ContainSingle(dependency => dependency.DependsOnTaskId == "reconciliation-review");
        dependentTask.Status.Should().Be(CloseTaskStatusDto.NotStarted);
        plan.ValidationIssues.Should().NotContain(issue =>
            issue.Code == "CloseTaskWaitingOnDependency" &&
            issue.TargetId == "report-certification");
    }

    [Fact]
    public async Task Scenario_ClosePlan_ConfigurationRetainsDependencyReason()
    {
        var workflowId = Guid.Parse("23232323-2323-2323-2323-232323232323");
        var ledgerBookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(BuildCloseWorkflow(
                workflowId,
                firstTaskStatus: "Pending",
                secondTaskStatus: "Pending"));
        var service = new AccountingCloseManagementService(workflowService);

        var plan = await service.ConfigurePeriodPlanAsync(
            new UpsertClosePeriodPlanConfigurationRequestDto(
                workflowId,
                MaterialityPolicy: new MaterialityPolicyDto(
                    "close-materiality-2026-03",
                    AmountThreshold: 25_000m,
                    PercentThreshold: 0.02m,
                    Currency: "usd",
                    ReviewRole: "CFO",
                    RequiresLateAdjustmentApproval: true),
                TaskConfigurations:
                [
                    new CloseTaskConfigurationDto(
                        "report-certification",
                        DependsOnTaskIds: ["reconciliation-review"],
                        DependencyConfigurations:
                        [
                            new CloseTaskDependencyConfigurationDto(
                                "reconciliation-review",
                                "NAV package must be signed off before investor statement certification.")
                        ])
                ],
                Actor: "controller-reviewer",
                EvidenceLinks:
                [
                    $"evidence:close-plan:{workflowId:D}:2026-03:book:{ledgerBookId:D}:configuration-approval"
                ]),
            "controller-reviewer");

        plan.Should().NotBeNull();
        var dependency = plan!.Tasks
            .Single(task => task.TaskId == "report-certification")
            .Dependencies
            .Should()
            .ContainSingle(dependency => dependency.DependsOnTaskId == "reconciliation-review")
            .Subject;
        dependency.Reason.Should().Be("NAV package must be signed off before investor statement certification.");
        plan.Configuration.Should().NotBeNull();
        plan.Configuration!.TaskConfigurations.Single().DependencyConfigurations.Should().ContainSingle(config =>
            config.DependsOnTaskId == "reconciliation-review" &&
            config.Reason == "NAV package must be signed off before investor statement certification.");
    }

    [Fact]
    public async Task Scenario_ClosePlan_ConfigurationRejectsStaleSetupVersion()
    {
        var workflowId = Guid.Parse("24242424-2424-2424-2424-242424242424");
        var ledgerBookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(BuildCloseWorkflow(
                workflowId,
                firstTaskStatus: "Pending",
                secondTaskStatus: "Pending"));
        var service = new AccountingCloseManagementService(workflowService);

        var retainedPlan = await service.ConfigurePeriodPlanAsync(
            new UpsertClosePeriodPlanConfigurationRequestDto(
                workflowId,
                MaterialityPolicy: new MaterialityPolicyDto(
                    "close-materiality-2026-03",
                    AmountThreshold: 25_000m,
                    PercentThreshold: 0.02m,
                    Currency: "USD",
                    ReviewRole: "CFO",
                    RequiresLateAdjustmentApproval: true),
                TaskConfigurations:
                [
                    new CloseTaskConfigurationDto(
                        "report-certification",
                        RequiredApprovalCount: 2,
                        RequiredApprovalRole: "Controller",
                        RequiredEvidence: "Controller package sign-off evidence")
                ],
                Actor: "controller-reviewer",
                EvidenceLinks:
                [
                    $"evidence:close-plan:{workflowId:D}:2026-03:book:{ledgerBookId:D}:configuration-approval"
                ]),
            "controller-reviewer");
        retainedPlan!.Configuration.Should().NotBeNull();
        var staleVersion = retainedPlan.Configuration!.ConfiguredAtUtc!.Value.AddTicks(-1);

        var staleWrite = async () => await service.ConfigurePeriodPlanAsync(
            new UpsertClosePeriodPlanConfigurationRequestDto(
                workflowId,
                MaterialityPolicy: retainedPlan.Configuration.MaterialityPolicy with { ReviewRole = "Treasurer" },
                TaskConfigurations: retainedPlan.Configuration.TaskConfigurations,
                Actor: "controller-reviewer",
                EvidenceLinks:
                [
                    $"evidence:close-plan:{workflowId:D}:2026-03:book:{ledgerBookId:D}:configuration-approval"
                ],
                ExpectedConfiguredAtUtc: staleVersion),
            "controller-reviewer");

        await staleWrite.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*changed at*reload the close plan*");
        var currentPlan = await service.GetPeriodPlanAsync(workflowId);
        currentPlan!.Configuration!.MaterialityPolicy.ReviewRole.Should().Be("CFO");
    }

    [Fact]
    public async Task Scenario_ClosePlan_MissingRequiredSignOffBlocksPlanValidationUntilEvidenceIsRetained()
    {
        var workflowId = Guid.Parse("25252525-2525-2525-2525-252525252525");
        var ledgerBookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(BuildCloseWorkflow(
                workflowId,
                firstTaskStatus: "Done",
                secondTaskStatus: "Done"));
        var service = new AccountingCloseManagementService(workflowService);

        var planBeforeSignOff = await service.GetPeriodPlanAsync(workflowId);

        planBeforeSignOff.Should().NotBeNull();
        planBeforeSignOff!.ValidationIssues.Should().Contain(issue =>
            issue.Code == "CloseTaskSignOffMissing" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "reconciliation-review");
        planBeforeSignOff.ValidationIssues.Should().Contain(issue =>
            issue.Code == "CloseTaskSignOffMissing" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "report-certification");
        planBeforeSignOff.OperatingCoverage.Should().ContainSingle(item =>
            item.ControlId == "sign-off-matrix" &&
            item.State == AccountingReadinessStateDto.Blocked &&
            item.BlockingIssueCount == 2);
        planBeforeSignOff.OperatingCoverage.Should().ContainSingle(item =>
            item.ControlId == "period-lock" &&
            item.State == AccountingReadinessStateDto.Blocked &&
            item.BlockingIssueCount == 2);

        var reconciliationEvidence = $"evidence:close-task:reconciliation-review:Controller:2026-03:book:{ledgerBookId:D}:control-signoff";
        await service.SignOffCloseTaskAsync(
            new SignOffCloseTaskRequestDto(
                workflowId,
                "reconciliation-review",
                "Controller",
                ManualJournalEntryStatusDto.Approved,
                "controller-reviewer",
                "Retained reconciliation close sign-off.",
                [reconciliationEvidence]),
            "controller-reviewer");
        var reportEvidence = $"evidence:close-task:report-certification:Controller:2026-03:book:{ledgerBookId:D}:control-signoff";
        var planAfterSignOff = await service.SignOffCloseTaskAsync(
            new SignOffCloseTaskRequestDto(
                workflowId,
                "report-certification",
                "Controller",
                ManualJournalEntryStatusDto.Approved,
                "controller-reviewer",
                "Retained report certification sign-off.",
                [reportEvidence]),
            "controller-reviewer");

        planAfterSignOff.Should().NotBeNull();
        planAfterSignOff!.ValidationIssues.Should().NotContain(issue => issue.Code == "CloseTaskSignOffMissing");
        planAfterSignOff.Tasks.Should().OnlyContain(task =>
            task.SignOffRequirements.All(requirement => requirement.IsSatisfied));
        planAfterSignOff.OperatingCoverage.Should().ContainSingle(item =>
            item.ControlId == "sign-off-matrix" &&
            item.State == AccountingReadinessStateDto.ReadyForReview &&
            item.EvidenceCount == 2 &&
            item.BlockingIssueCount == 0);
        planAfterSignOff.OperatingCoverage.Should().ContainSingle(item =>
            item.ControlId == "period-lock" &&
            item.State == AccountingReadinessStateDto.ReadyForReview &&
            item.BlockingIssueCount == 0);
    }

    [Fact]
    public async Task Scenario_ClosePlan_ConfiguredSignOffMatrixRequiresEveryRoleBeforeValidationClears()
    {
        var workflowId = Guid.Parse("28282828-2828-2828-2828-282828282828");
        var ledgerBookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(BuildCloseWorkflow(
                workflowId,
                firstTaskStatus: "Done",
                secondTaskStatus: "Done"));
        var service = new AccountingCloseManagementService(workflowService);

        var configuredPlan = await service.ConfigurePeriodPlanAsync(
            new UpsertClosePeriodPlanConfigurationRequestDto(
                workflowId,
                MaterialityPolicy: new MaterialityPolicyDto(
                    "close-materiality-2026-03",
                    AmountThreshold: 25_000m,
                    PercentThreshold: 0.02m,
                    Currency: "USD",
                    ReviewRole: "CFO",
                    RequiresLateAdjustmentApproval: true),
                TaskConfigurations:
                [
                    new CloseTaskConfigurationDto(
                        "report-certification",
                        RequiredApprovalCount: 1,
                        RequiredApprovalRole: "Controller",
                        RequiredEvidence: "Controller close package evidence",
                        SignOffRequirementConfigurations:
                        [
                            new CloseTaskSignOffRequirementConfigurationDto(
                                "Controller",
                                1,
                                "Controller close package evidence"),
                            new CloseTaskSignOffRequirementConfigurationDto(
                                "CFO",
                                1,
                                "CFO final report package evidence")
                        ])
                ],
                Actor: "controller-reviewer",
                EvidenceLinks:
                [
                    $"evidence:close-plan:{workflowId:D}:2026-03:book:{ledgerBookId:D}:matrix-configuration"
                ]),
            "controller-reviewer");

        var configuredTask = configuredPlan!.Tasks.Single(task => task.TaskId == "report-certification");
        configuredTask.SignOffRequirements.Should().HaveCount(2);
        configuredPlan.ValidationIssues.Should().Contain(issue =>
            issue.Code == "CloseTaskSignOffMissing" &&
            issue.TargetId == "report-certification");

        var controllerSignedPlan = await service.SignOffCloseTaskAsync(
            new SignOffCloseTaskRequestDto(
                workflowId,
                "report-certification",
                "Controller",
                ManualJournalEntryStatusDto.Approved,
                "controller-reviewer",
                "Retained controller sign-off.",
                [$"evidence:close-task:report-certification:Controller:2026-03:book:{ledgerBookId:D}:control-signoff"]),
            "controller-reviewer");

        var controllerSignedTask = controllerSignedPlan!.Tasks.Single(task => task.TaskId == "report-certification");
        controllerSignedTask.SignOffRequirements.Single(requirement => requirement.Role == "Controller").IsSatisfied.Should().BeTrue();
        controllerSignedTask.SignOffRequirements.Single(requirement => requirement.Role == "CFO").IsSatisfied.Should().BeFalse();
        controllerSignedPlan.ValidationIssues.Should().Contain(issue =>
            issue.Code == "CloseTaskSignOffMissing" &&
            issue.TargetId == "report-certification");

        var fullySignedPlan = await service.SignOffCloseTaskAsync(
            new SignOffCloseTaskRequestDto(
                workflowId,
                "report-certification",
                "CFO",
                ManualJournalEntryStatusDto.Approved,
                "cfo-reviewer",
                "Retained CFO sign-off.",
                [$"evidence:close-task:report-certification:CFO:2026-03:book:{ledgerBookId:D}:control-signoff"]),
            "cfo-reviewer");

        var fullySignedTask = fullySignedPlan!.Tasks.Single(task => task.TaskId == "report-certification");
        fullySignedTask.SignOffRequirements.Should().OnlyContain(requirement => requirement.IsSatisfied);
        fullySignedTask.Status.Should().Be(CloseTaskStatusDto.SignedOff);
        fullySignedPlan.ValidationIssues.Should().NotContain(issue =>
            issue.Code == "CloseTaskSignOffMissing" &&
            issue.TargetId == "report-certification");
    }

    [Fact]
    public async Task Scenario_ClosePlan_ReviewCloseEvidenceRetainsActiveBlockerWithoutClearingValidation()
    {
        var workflowId = Guid.Parse("26262626-2626-2626-2626-262626262626");
        var ledgerBookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(BuildCloseWorkflow(
                workflowId,
                firstTaskStatus: "Done",
                secondTaskStatus: "Done"));
        var service = new AccountingCloseManagementService(workflowService);
        var issueEvidence = $"evidence:close-review:CloseTaskSignOffMissing:reconciliation-review:{workflowId:D}:2026-03:book:{ledgerBookId:D}:blocker-review";

        var reviewedPlan = await service.ReviewCloseEvidenceAsync(
            new ReviewCloseEvidenceRequestDto(
                workflowId,
                "CloseTaskSignOffMissing",
                "reconciliation-review",
                "controller-reviewer",
                "Controller reviewed the retained blocker evidence while sign-off remains unresolved.",
                [issueEvidence]),
            "controller-reviewer");

        reviewedPlan.Should().NotBeNull();
        reviewedPlan!.EvidenceReviews.Should().ContainSingle(review =>
            review.IssueCode == "CloseTaskSignOffMissing" &&
            review.TargetId == "reconciliation-review" &&
            review.ReviewedBy == "controller-reviewer" &&
            review.EvidenceLinks.Contains(issueEvidence));
        reviewedPlan.ValidationIssues.Should().Contain(issue =>
            issue.Code == "CloseTaskSignOffMissing" &&
            issue.TargetId == "reconciliation-review" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        reviewedPlan.OperatingCoverage.Should().ContainSingle(item =>
            item.ControlId == "blocker-evidence-review" &&
            item.State == AccountingReadinessStateDto.Blocked &&
            item.EvidenceCount == 1 &&
            item.BlockingIssueCount == 1);
    }

    [Fact]
    public async Task Scenario_ClosePlan_ReviewCloseEvidenceRejectsInactiveIssues()
    {
        var workflowId = Guid.Parse("27272727-2727-2727-2727-272727272727");
        var ledgerBookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(BuildCloseWorkflow(
                workflowId,
                firstTaskStatus: "Done",
                secondTaskStatus: "Done"));
        var service = new AccountingCloseManagementService(workflowService);
        var issueEvidence = $"evidence:close-review:InactiveCloseIssue:reconciliation-review:{workflowId:D}:2026-03:book:{ledgerBookId:D}:blocker-review";

        var action = async () => await service.ReviewCloseEvidenceAsync(
            new ReviewCloseEvidenceRequestDto(
                workflowId,
                "InactiveCloseIssue",
                "reconciliation-review",
                "controller-reviewer",
                "Controller tried to review an inactive issue.",
                [issueEvidence]),
            "controller-reviewer");

        await action.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*is not active on workflow*");
    }

    [Fact]
    public async Task Scenario_ClosePlan_SignOffRequiresActorIndependentFromTaskAcknowledgement()
    {
        var workflowId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var ledgerBookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(BuildCloseWorkflow(
                workflowId,
                firstTaskStatus: "Pending",
                secondTaskStatus: "Pending",
                firstTaskAcknowledgedBy: "close-preparer"));
        var service = new AccountingCloseManagementService(workflowService);
        var evidence = $"evidence:close-task:reconciliation-review:Controller:2026-03:book:{ledgerBookId:D}:control-signoff";
        var request = new SignOffCloseTaskRequestDto(
            workflowId,
            "reconciliation-review",
            "Controller",
            ManualJournalEntryStatusDto.Approved,
            "close-preparer",
            "Controller sign-off retained.",
            [evidence]);

        var sameActor = async () => await service.SignOffCloseTaskAsync(request, "close-preparer");

        await sameActor.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*independent from acknowledgement actor 'close-preparer'*");

        var plan = await service.SignOffCloseTaskAsync(request with { Actor = "controller-reviewer" }, "controller-reviewer");

        plan.Should().NotBeNull();
        var signedOffTask = plan!.Tasks.Single(task => task.TaskId == "reconciliation-review");
        signedOffTask.Status.Should().Be(CloseTaskStatusDto.SignedOff);
        signedOffTask.SignOffs.Should().ContainSingle(signOff =>
            signOff.Actor == "controller-reviewer" &&
            signOff.ApprovalState == ManualJournalEntryStatusDto.Approved &&
            signOff.EvidenceLinks.Contains(evidence));
        signedOffTask.SignOffRequirements.Should().ContainSingle().Subject.IsSatisfied.Should().BeTrue();
    }

    [Fact]
    public async Task Scenario_ClosePlan_RejectedSignOffBlocksTaskAndCloseCalendar()
    {
        var workflowId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var ledgerBookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(BuildCloseWorkflow(
                workflowId,
                firstTaskStatus: "Pending",
                secondTaskStatus: "Pending"));
        var service = new AccountingCloseManagementService(workflowService);
        var evidence = $"evidence:close-task:reconciliation-review:Controller:2026-03:book:{ledgerBookId:D}:control-review-rejection";
        var plan = await service.SignOffCloseTaskAsync(
            new SignOffCloseTaskRequestDto(
                workflowId,
                "reconciliation-review",
                "Controller",
                ManualJournalEntryStatusDto.Rejected,
                "controller-reviewer",
                "Close support packet rejected pending remediation.",
                [evidence]),
            "controller-reviewer");

        plan.Should().NotBeNull();
        var rejectedTask = plan!.Tasks.Single(task => task.TaskId == "reconciliation-review");
        rejectedTask.Status.Should().Be(CloseTaskStatusDto.Blocked);
        rejectedTask.SignOffs.Should().ContainSingle(signOff =>
            signOff.Actor == "controller-reviewer" &&
            signOff.ApprovalState == ManualJournalEntryStatusDto.Rejected &&
            signOff.EvidenceLinks.Contains(evidence));
        rejectedTask.SignOffRequirements.Should().ContainSingle().Subject.IsSatisfied.Should().BeFalse();
        plan.CloseCalendar.Should().ContainSingle(milestone =>
            milestone.TaskId == "reconciliation-review" &&
            milestone.IsBlocked &&
            !milestone.IsSatisfied &&
            milestone.ApprovedSignOffCount == 0);
        plan.ValidationIssues.Should().Contain(issue =>
            issue.Code == "CloseTaskSignOffRejected" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "reconciliation-review");
    }

    [Fact]
    public async Task Scenario_ClosePlan_RejectedSignOffBlocksAdditionalRoleDecisionsUntilRemediation()
    {
        var workflowId = Guid.Parse("45454545-4545-4545-4545-454545454545");
        var ledgerBookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(BuildCloseWorkflow(
                workflowId,
                firstTaskStatus: "Pending",
                secondTaskStatus: "Pending"));
        var service = new AccountingCloseManagementService(workflowService);
        var rejectionEvidence = $"evidence:close-task:reconciliation-review:Controller:2026-03:book:{ledgerBookId:D}:control-review-rejection";
        var approvalEvidence = $"evidence:close-task:reconciliation-review:Controller:2026-03:book:{ledgerBookId:D}:control-review-approval-after-rejection";

        await service.SignOffCloseTaskAsync(
            new SignOffCloseTaskRequestDto(
                workflowId,
                "reconciliation-review",
                "Controller",
                ManualJournalEntryStatusDto.Rejected,
                "controller-reviewer",
                "Close support packet rejected pending remediation.",
                [rejectionEvidence]),
            "controller-reviewer");

        var approvalAfterRejection = async () => await service.SignOffCloseTaskAsync(
            new SignOffCloseTaskRequestDto(
                workflowId,
                "reconciliation-review",
                "Controller",
                ManualJournalEntryStatusDto.Approved,
                "controller-reviewer-2",
                "Attempted approval before remediation.",
                [approvalEvidence]),
            "controller-reviewer-2");

        await approvalAfterRejection.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*retained rejected sign-off*remediated*");

        var plan = await service.GetPeriodPlanAsync(workflowId);
        plan.Should().NotBeNull();
        var rejectedTask = plan!.Tasks.Single(task => task.TaskId == "reconciliation-review");
        rejectedTask.Status.Should().Be(CloseTaskStatusDto.Blocked);
        rejectedTask.SignOffs.Should().ContainSingle(signOff =>
            signOff.Actor == "controller-reviewer" &&
            signOff.ApprovalState == ManualJournalEntryStatusDto.Rejected &&
            signOff.EvidenceLinks.Contains(rejectionEvidence));
    }

    [Fact]
    public async Task Scenario_ClosePlan_MaterialLateAdjustmentBlocksValidationUntilReviewed()
    {
        var workflowId = Guid.Parse("46464646-4646-4646-4646-464646464646");
        var ledgerBookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var journalEntryId = Guid.Parse("11112222-3333-4444-5555-666677778888");
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(BuildCloseWorkflow(
                workflowId,
                firstTaskStatus: "Done",
                secondTaskStatus: "Done"));
        var service = new AccountingCloseManagementService(workflowService);
        var requestEvidence = $"evidence:late-adjustment:{journalEntryId:D}:2026-03:book:{ledgerBookId:D}:request";

        var planWithPendingAdjustment = await service.RequestLateAdjustmentAsync(
            new CreateLateAdjustmentRequestDto(
                workflowId,
                journalEntryId,
                25_000m,
                "USD",
                "Material valuation support arrived after close review.",
                "fund-accountant",
                [requestEvidence]),
            "fund-accountant");

        planWithPendingAdjustment.Should().NotBeNull();
        var pendingAdjustment = planWithPendingAdjustment!.LateAdjustments.Should().ContainSingle().Subject;
        pendingAdjustment.ApprovalState.Should().Be(ManualJournalEntryStatusDto.Submitted);
        planWithPendingAdjustment.ValidationIssues.Should().Contain(issue =>
            issue.Code == "LateAdjustmentRequiresApproval" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == pendingAdjustment.RequestId);
        planWithPendingAdjustment.OperatingCoverage.Should().ContainSingle(item =>
            item.ControlId == "late-adjustments" &&
            item.State == AccountingReadinessStateDto.Blocked &&
            item.EvidenceCount == 1 &&
            item.BlockingIssueCount == 1);

        var reviewEvidence = $"evidence:late-adjustment-review:{pendingAdjustment.RequestId}:2026-03:book:{ledgerBookId:D}:approval";
        var reviewedPlan = await service.ReviewLateAdjustmentAsync(
            new ReviewLateAdjustmentRequestDto(
                workflowId,
                pendingAdjustment.RequestId,
                ManualJournalEntryStatusDto.Approved,
                "controller-reviewer",
                "Controller approval retained for material late adjustment.",
                [reviewEvidence]),
            "controller-reviewer");

        reviewedPlan.Should().NotBeNull();
        reviewedPlan!.LateAdjustments.Should().ContainSingle().Subject.ApprovalState.Should()
            .Be(ManualJournalEntryStatusDto.Approved);
        reviewedPlan.ValidationIssues.Should().NotContain(issue =>
            issue.Code == "LateAdjustmentRequiresApproval" &&
            issue.TargetId == pendingAdjustment.RequestId);
        reviewedPlan.OperatingCoverage.Should().ContainSingle(item =>
            item.ControlId == "late-adjustments" &&
            item.State == AccountingReadinessStateDto.ReadyForReview &&
            item.EvidenceCount == 2 &&
            item.BlockingIssueCount == 0);
    }

    [Fact]
    public async Task Scenario_ClosePlan_LockPeriodBlocksBeforeWorkflowCloseWhenSignOffsAreMissing()
    {
        var workflowId = Guid.Parse("47474747-4747-4747-4747-474747474747");
        var ledgerBookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var workflow = BuildCloseWorkflow(workflowId, firstTaskStatus: "Done", secondTaskStatus: "Done");
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(workflow);
        var postingWorkbench = CreateMutationGatedPostingWorkbench();
        postingWorkbench.EvaluateAsync(
                Arg.Any<AccountingClosePostingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(new ClosePostingGateDto(
                "period-close-posting:signoffs-missing",
                "Post closing entries",
                ClosePostingGateStateDto.Posted,
                true,
                0m,
                0,
                "Closing entries are already posted."));
        var service = new AccountingCloseManagementService(workflowService, postingWorkbench);

        var result = await LockClosePeriodScopedAsync(
            service,
            new LockClosePeriodRequestDto(
                workflowId,
                ExpectedWorkflowVersion: workflow.Version,
                Actor: "controller-reviewer",
                Rationale: "Lock close period after report certification.",
                ReportPackId: "report-pack-2026-03",
                EvidenceLinks:
                [
                    $"evidence:close-package:{workflowId:D}:2026-03:book:{ledgerBookId:D}:period-lock"
                ],
                ChecklistControlApprovals:
                [
                    new OperationsChecklistControlApprovalDto("reconciliation-review", "controller-reviewer", DateTimeOffset.Parse("2026-04-03T12:00:00Z")),
                    new OperationsChecklistControlApprovalDto("report-certification", "controller-reviewer", DateTimeOffset.Parse("2026-04-03T12:05:00Z"))
                ],
                ControllerRole: "Controller"),
            "controller-reviewer");

        result.Should().NotBeNull();
        result!.IsLocked.Should().BeFalse();
        result.Transition.Should().BeNull();
        result.Plan.Should().NotBeNull();
        result.Issues.Should().Contain(issue =>
            issue.Code == "CloseTaskSignOffMissing" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        await workflowService.DidNotReceiveWithAnyArgs().CloseWorkflowAsync(default, default!, default);
    }

    [Fact]
    public async Task Scenario_ClosePlan_LockPeriodFailsClosedWhenPostingGateIsUnavailableAfterSignOffsPass()
    {
        var workflowId = Guid.Parse("47474747-4747-4747-4747-474747474748");
        var ledgerBookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var workflow = BuildCloseWorkflow(workflowId, firstTaskStatus: "Done", secondTaskStatus: "Done");
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>()).Returns(workflow);
        var postingWorkbench = CreateMutationGatedPostingWorkbench();
        var unavailableGate = new ClosePostingGateDto(
            "period-close-posting:unavailable",
            "Post closing entries",
            ClosePostingGateStateDto.Unavailable,
            false,
            0m,
            0,
            "The closing-entry workbench is unavailable.");
        postingWorkbench.EnsureClosingDraftQueuedAsync(
                Arg.Any<AccountingClosePostingContext>(),
                Arg.Any<AccountingClosePostingCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(unavailableGate);
        postingWorkbench.EvaluateAsync(
                Arg.Any<AccountingClosePostingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(unavailableGate);
        var service = new AccountingCloseManagementService(workflowService, postingWorkbench);
        await ApproveRequiredCloseTasksAsync(service, workflowId, ledgerBookId);

        var result = await LockClosePeriodScopedAsync(
            service,
            new LockClosePeriodRequestDto(
                workflowId,
                workflow.Version,
                "controller-reviewer",
                "Lock after all retained close controls pass.",
                "report-pack-2026-03",
                [$"evidence:close-package:{workflowId:D}:2026-03:book:{ledgerBookId:D}:period-lock"],
                ControllerRole: "Controller"),
            "controller-reviewer");

        result.Should().NotBeNull();
        result!.IsLocked.Should().BeFalse();
        result.Plan!.ClosingEntriesGate.Should().NotBeNull();
        result.Plan.ClosingEntriesGate!.State.Should().Be(ClosePostingGateStateDto.Unavailable);
        result.Issues.Should().ContainSingle(issue =>
            issue.Code == "PeriodClosePostingGateUnavailable" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        await workflowService.DidNotReceiveWithAnyArgs().CloseWorkflowAsync(default, default!, default);
    }

    [Fact]
    public async Task Scenario_ClosePlan_HardCloseFailsClosedWhenMutationConsistencyGateIsUnavailable()
    {
        var workflowId = Guid.Parse("47474747-4747-4747-4747-474747474755");
        var ledgerBookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var workflow = BuildCloseWorkflow(workflowId, firstTaskStatus: "Done", secondTaskStatus: "Done");
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>()).Returns(workflow);
        var service = new AccountingCloseManagementService(workflowService);
        await ApproveRequiredCloseTasksAsync(service, workflowId, ledgerBookId);

        var result = await LockClosePeriodScopedAsync(
            service,
            new LockClosePeriodRequestDto(
                workflowId,
                workflow.Version,
                "controller-reviewer",
                "Verify the durable close fence is mandatory.",
                "report-pack-2026-03",
                [$"evidence:close-package:{workflowId:D}:2026-03:book:{ledgerBookId:D}:period-lock"],
                ControllerRole: "Controller"),
            "controller-reviewer");

        result.Should().NotBeNull();
        result!.IsLocked.Should().BeFalse();
        result.Issues.Should().ContainSingle(issue =>
            issue.Code == "ClosePeriodMutationConsistencyGateUnavailable" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        await workflowService.DidNotReceiveWithAnyArgs().CloseWorkflowAsync(default, default!, default);
    }

    [Fact]
    public async Task Scenario_ClosePlan_StaleWorkflowVersionStopsBeforeClosingDraftOrHardCloseMutation()
    {
        var workflowId = Guid.Parse("47474747-4747-4747-4747-474747474749");
        var ledgerBookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var workflow = BuildCloseWorkflow(workflowId, firstTaskStatus: "Done", secondTaskStatus: "Done");
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>()).Returns(workflow);
        var postingWorkbench = CreateMutationGatedPostingWorkbench();
        postingWorkbench.EvaluateAsync(
                Arg.Any<AccountingClosePostingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(new ClosePostingGateDto(
                "period-close-posting:stale-version",
                "Post closing entries",
                ClosePostingGateStateDto.Posted,
                true,
                0m,
                0,
                "Temporary balances are zero."));
        var service = new AccountingCloseManagementService(workflowService, postingWorkbench);
        await ApproveRequiredCloseTasksAsync(service, workflowId, ledgerBookId);

        var result = await LockClosePeriodScopedAsync(
            service,
            new LockClosePeriodRequestDto(
                workflowId,
                ExpectedWorkflowVersion: workflow.Version - 1,
                Actor: "controller-reviewer",
                Rationale: "Attempt stale close-period lock.",
                ReportPackId: "report-pack-2026-03",
                EvidenceLinks:
                [
                    $"evidence:close-package:{workflowId:D}:2026-03:book:{ledgerBookId:D}:period-lock"
                ],
                ControllerRole: "Controller"),
            "controller-reviewer");

        result.Should().NotBeNull();
        result!.IsLocked.Should().BeFalse();
        result.Issues.Should().ContainSingle(issue =>
            issue.Code == "ClosePeriodLockVersionMismatch" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        await postingWorkbench.DidNotReceive().EnsureClosingDraftQueuedAsync(
            Arg.Any<AccountingClosePostingContext>(),
            Arg.Any<AccountingClosePostingCommand>(),
            Arg.Any<CancellationToken>());
        await postingWorkbench.DidNotReceive().FinalizeHardCloseAsync(
            Arg.Any<AccountingClosePostingContext>(),
            Arg.Any<AccountingClosePostingCommand>(),
            Arg.Any<CancellationToken>());
        await workflowService.DidNotReceiveWithAnyArgs().CloseWorkflowAsync(default, default!, default);
    }

    [Fact]
    public async Task Scenario_ClosePlan_PrepareClosingEntriesOnly_NeverHardClosesEvenWhenGateIsReady()
    {
        var workflowId = Guid.Parse("47474747-4747-4747-4747-474747474750");
        var ledgerBookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var workflow = BuildCloseWorkflow(workflowId, firstTaskStatus: "Done", secondTaskStatus: "Done");
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>()).Returns(workflow);
        var postingWorkbench = CreateMutationGatedPostingWorkbench();
        var readyGate = new ClosePostingGateDto(
            "period-close-posting:prepare-only",
            "Post closing entries",
            ClosePostingGateStateDto.Posted,
            true,
            0m,
            0,
            "Closing entries are already posted.");
        postingWorkbench.EnsureClosingDraftQueuedAsync(
                Arg.Any<AccountingClosePostingContext>(),
                Arg.Any<AccountingClosePostingCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(readyGate);
        postingWorkbench.EvaluateAsync(
                Arg.Any<AccountingClosePostingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(readyGate);
        var service = new AccountingCloseManagementService(workflowService, postingWorkbench);
        await ApproveRequiredCloseTasksAsync(service, workflowId, ledgerBookId);

        var result = await service.LockClosePeriodAsync(
            new LockClosePeriodRequestDto(
                workflowId,
                workflow.Version,
                "controller-reviewer",
                "Prepare the governed closing-entry batch.",
                "report-pack-2026-03",
                [$"evidence:close-package:{workflowId:D}:2026-03:book:{ledgerBookId:D}:period-lock"],
                PrepareClosingEntriesOnly: true),
            "controller-reviewer");

        result.Should().NotBeNull();
        result!.IsLocked.Should().BeFalse();
        result.Plan!.ClosingEntriesGate.Should().Be(readyGate);
        result.Issues.Should().BeEmpty();
        await postingWorkbench.Received(1).EnsureClosingDraftQueuedAsync(
            Arg.Any<AccountingClosePostingContext>(),
            Arg.Any<AccountingClosePostingCommand>(),
            Arg.Any<CancellationToken>());
        await postingWorkbench.DidNotReceive().FinalizeHardCloseAsync(
            Arg.Any<AccountingClosePostingContext>(),
            Arg.Any<AccountingClosePostingCommand>(),
            Arg.Any<CancellationToken>());
        await workflowService.DidNotReceiveWithAnyArgs().CloseWorkflowAsync(default, default!, default);
    }

    [Fact]
    public async Task Scenario_ClosePlan_HardCloseWithoutControllerAuthorityFailsBeforeAnyCloseMutation()
    {
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        var postingWorkbench = CreateMutationGatedPostingWorkbench();
        var service = new AccountingCloseManagementService(workflowService, postingWorkbench);
        var request = new LockClosePeriodRequestDto(
            Guid.Parse("47474747-4747-4747-4747-474747474752"),
            ExpectedWorkflowVersion: 7,
            Actor: "accounting-operator",
            Rationale: "Attempt hard close without retained Controller authority.",
            ReportPackId: "report-pack-2026-03",
            EvidenceLinks: ["evidence://close/attempt"]);

        var act = () => LockClosePeriodScopedAsync(service, request, "accounting-operator");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*ControllerRole*required*");
        await workflowService.DidNotReceive().GetAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
        await postingWorkbench.DidNotReceive().EnsureClosingDraftQueuedAsync(
            Arg.Any<AccountingClosePostingContext>(),
            Arg.Any<AccountingClosePostingCommand>(),
            Arg.Any<CancellationToken>());
        await postingWorkbench.DidNotReceive().FinalizeHardCloseAsync(
            Arg.Any<AccountingClosePostingContext>(),
            Arg.Any<AccountingClosePostingCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Scenario_ClosePlan_UnscopedHardCloseIsRejectedBeforeWorkflowRead()
    {
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        var postingWorkbench = CreateMutationGatedPostingWorkbench();
        var service = new AccountingCloseManagementService(workflowService, postingWorkbench);
        var request = new LockClosePeriodRequestDto(
            Guid.Parse("47474747-4747-4747-4747-474747474756"),
            ExpectedWorkflowVersion: 7,
            Actor: "controller-reviewer",
            Rationale: "Attempt an unscoped hard close.",
            ReportPackId: "report-pack-2026-03",
            EvidenceLinks: ["evidence://close/attempt"],
            ControllerRole: "Controller");

        var act = () => service.LockClosePeriodAsync(request, "controller-reviewer");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*authenticated tenant and company scope*");
        await workflowService.DidNotReceive().GetAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Scenario_ClosePlan_UnscopedReopenIsRejectedBeforeWorkflowRead()
    {
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        var postingWorkbench = CreateMutationGatedPostingWorkbench();
        var service = new AccountingCloseManagementService(workflowService, postingWorkbench);

        var act = () => service.ReopenClosePeriodAsync(
            BuildReopenRequest(
                Guid.Parse("47474747-4747-4747-4747-474747474757"),
                version: 7),
            "controller-reviewer");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*authenticated tenant and company scope*");
        await workflowService.DidNotReceive().GetAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Scenario_ClosePlan_RechecksWorkflowVersionImmediatelyBeforeLedgerHardClose()
    {
        var workflowId = Guid.Parse("47474747-4747-4747-4747-474747474751");
        var ledgerBookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var currentWorkflow = BuildCloseWorkflow(workflowId, firstTaskStatus: "Done", secondTaskStatus: "Done");
        var originalVersion = currentWorkflow.Version;
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(_ => currentWorkflow);
        var postingWorkbench = CreateMutationGatedPostingWorkbench();
        var readyGate = new ClosePostingGateDto(
            "period-close-posting:jit-version",
            "Post closing entries",
            ClosePostingGateStateDto.Posted,
            true,
            0m,
            0,
            "Closing entries are posted.");
        postingWorkbench.EnsureClosingDraftQueuedAsync(
                Arg.Any<AccountingClosePostingContext>(),
                Arg.Any<AccountingClosePostingCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                currentWorkflow = currentWorkflow with { Version = originalVersion + 1 };
                return readyGate;
            });
        postingWorkbench.EvaluateAsync(
                Arg.Any<AccountingClosePostingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(readyGate);
        var service = new AccountingCloseManagementService(workflowService, postingWorkbench);
        await ApproveRequiredCloseTasksAsync(service, workflowId, ledgerBookId);

        var result = await LockClosePeriodScopedAsync(
            service,
            new LockClosePeriodRequestDto(
                workflowId,
                originalVersion,
                "controller-reviewer",
                "Attempt close across a concurrent workflow mutation.",
                "report-pack-2026-03",
                [$"evidence:close-package:{workflowId:D}:2026-03:book:{ledgerBookId:D}:period-lock"],
                ControllerRole: "Controller"),
            "controller-reviewer");

        result.Should().NotBeNull();
        result!.IsLocked.Should().BeFalse();
        result.Plan!.WorkflowVersion.Should().Be(originalVersion + 1);
        result.Issues.Should().ContainSingle(issue => issue.Code == "ClosePeriodLockVersionMismatch");
        await postingWorkbench.DidNotReceive().FinalizeHardCloseAsync(
            Arg.Any<AccountingClosePostingContext>(),
            Arg.Any<AccountingClosePostingCommand>(),
            Arg.Any<CancellationToken>());
        await workflowService.DidNotReceiveWithAnyArgs().CloseWorkflowAsync(default, default!, default);
    }

    [Fact]
    public async Task Scenario_ClosePlan_CasFailureAfterLedgerHardClose_ExactRetryConvergesWorkflow()
    {
        var workflowId = Guid.Parse("47474747-4747-4747-4747-474747474752");
        var ledgerBookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var currentWorkflow = BuildCloseWorkflow(workflowId, firstTaskStatus: "Done", secondTaskStatus: "Done");
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>()).Returns(_ => currentWorkflow);
        var closeSequence = new List<string>();
        var closeAttempts = 0;
        workflowService.CloseWorkflowAsync(
                workflowId,
                Arg.Any<OperationsCloseWorkflowRequestDto>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                closeSequence.Add("workflow-close");
                closeAttempts++;
                if (closeAttempts == 1)
                {
                    currentWorkflow = currentWorkflow with { Version = currentWorkflow.Version + 1 };
                    return new OperationsTransitionResultDto(
                        false,
                        "VERSION_CONFLICT",
                        "Concurrent workflow mutation.",
                        currentWorkflow,
                        [],
                        [],
                        currentWorkflow.Version);
                }

                currentWorkflow = BuildLockedCloseWorkflow(currentWorkflow, currentWorkflow.Version + 1);
                return new OperationsTransitionResultDto(
                    true,
                    null,
                    null,
                    currentWorkflow,
                    [],
                    [],
                    currentWorkflow.Version);
            });
        var postingWorkbench = CreateMutationGatedPostingWorkbench();
        var readyGate = new ClosePostingGateDto(
            "period-close-posting:cas-retry",
            "Post closing entries",
            ClosePostingGateStateDto.Posted,
            true,
            0m,
                0,
                "Closing entries are posted.");
        var finalizationCommands = new List<AccountingClosePostingCommand>();
        postingWorkbench.EnsureClosingDraftQueuedAsync(
                Arg.Any<AccountingClosePostingContext>(),
                Arg.Any<AccountingClosePostingCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(readyGate);
        postingWorkbench.EvaluateAsync(
                Arg.Any<AccountingClosePostingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(readyGate);
        postingWorkbench.FinalizeHardCloseAsync(
                Arg.Any<AccountingClosePostingContext>(),
                Arg.Any<AccountingClosePostingCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new LedgerPeriodDto(
                Guid.NewGuid(),
                ledgerBookId,
                2026,
                3,
                "2026-03",
                new DateOnly(2026, 3, 1),
                new DateOnly(2026, 3, 31),
                LedgerPeriodStatusDto.HardClosed,
                DateTimeOffset.Parse("2026-03-01T00:00:00Z"),
                DateTimeOffset.Parse("2026-04-03T12:09:00Z"),
                3))
            .AndDoes(call =>
            {
                closeSequence.Add("ledger-finalize");
                finalizationCommands.Add(call.ArgAt<AccountingClosePostingCommand>(1));
            });
        var service = new AccountingCloseManagementService(workflowService, postingWorkbench);
        await ApproveRequiredCloseTasksAsync(service, workflowId, ledgerBookId);

        LockClosePeriodRequestDto Request(long version) => new(
            workflowId,
            version,
            "controller-reviewer",
            "Lock close period after report certification.",
            "report-pack-2026-03",
            [$"evidence:close-package:{workflowId:D}:2026-03:book:{ledgerBookId:D}:period-lock"],
            CorrelationId: "close-cas-retry",
            ControllerRole: "Controller");

        var first = await LockClosePeriodScopedAsync(service, Request(currentWorkflow.Version), "controller-reviewer");
        var retry = await LockClosePeriodScopedAsync(service, Request(currentWorkflow.Version), "controller-reviewer");

        first!.IsLocked.Should().BeFalse();
        first.Issues.Should().Contain(issue => issue.Code == "CloseWorkflowTransitionPendingAfterLedgerHardClose");
        retry!.IsLocked.Should().BeTrue();
        retry.Issues.Should().BeEmpty();
        closeSequence.Should().Equal(
            "ledger-finalize",
            "workflow-close",
            "ledger-finalize",
            "workflow-close",
            "ledger-finalize");
        finalizationCommands.Should().HaveCount(3);
        finalizationCommands.Should().OnlyContain(command =>
            command.Actor == "controller-reviewer"
            && command.Role == "Controller"
            && command.CorrelationId == "close-cas-retry"
            && command.EvidenceLinks.Contains(
                $"evidence:close-package:{workflowId:D}:2026-03:book:{ledgerBookId:D}:period-lock",
                StringComparer.Ordinal));
        await postingWorkbench.Received(3).FinalizeHardCloseAsync(
            Arg.Any<AccountingClosePostingContext>(),
            Arg.Any<AccountingClosePostingCommand>(),
            Arg.Any<CancellationToken>());
        await workflowService.Received(2).CloseWorkflowAsync(
            workflowId,
            Arg.Any<OperationsCloseWorkflowRequestDto>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Scenario_ClosePlan_ReopenRechecksWorkflowVersionBeforeLedgerMutation()
    {
        var workflowId = Guid.Parse("47474747-4747-4747-4747-474747474753");
        var initial = BuildLockedCloseWorkflow(
            BuildCloseWorkflow(workflowId, firstTaskStatus: "Done", secondTaskStatus: "Done"));
        var changed = initial with { Version = initial.Version + 1 };
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>()).Returns(initial, changed);
        var postingWorkbench = CreateMutationGatedPostingWorkbench();
        postingWorkbench.EvaluateAsync(
                Arg.Any<AccountingClosePostingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(new ClosePostingGateDto(
                "period-close-posting:reopen-jit",
                "Post closing entries",
                ClosePostingGateStateDto.Posted,
                true,
                0m,
                0,
                "Closing entries are posted."));
        var service = new AccountingCloseManagementService(workflowService, postingWorkbench);

        var result = await ReopenClosePeriodScopedAsync(
            service,
            BuildReopenRequest(workflowId, initial.Version),
            "controller-reviewer");

        result.Should().NotBeNull();
        result!.IsReopened.Should().BeFalse();
        result.Plan!.WorkflowVersion.Should().Be(changed.Version);
        result.Issues.Should().ContainSingle(issue => issue.Code == "ClosePeriodReopenVersionMismatch");
        await postingWorkbench.DidNotReceive().ReopenAndQueueClosingReversalsAsync(
            Arg.Any<AccountingClosePostingContext>(),
            Arg.Any<AccountingClosePostingCommand>(),
            Arg.Any<CancellationToken>());
        await workflowService.DidNotReceiveWithAnyArgs().ReopenWorkflowAsync(default, default!, default);
    }

    [Fact]
    public async Task Scenario_ClosePlan_CasFailureAfterLedgerReopen_ExactRetryConvergesWorkflow()
    {
        var workflowId = Guid.Parse("47474747-4747-4747-4747-474747474754");
        var currentWorkflow = BuildLockedCloseWorkflow(
            BuildCloseWorkflow(workflowId, firstTaskStatus: "Done", secondTaskStatus: "Done"));
        var consistencyLeases = new TrackingMutationLeaseState();
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>()).Returns(_ => currentWorkflow);
        var reopenAttempts = 0;
        workflowService.ReopenWorkflowAsync(
                workflowId,
                Arg.Any<OperationsReopenWorkflowRequestDto>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                consistencyLeases.ActiveCount.Should().Be(
                    1,
                    "the cross-host accounting-period fence must cover the Operations workflow reopen");
                reopenAttempts++;
                if (reopenAttempts == 1)
                {
                    currentWorkflow = currentWorkflow with { Version = currentWorkflow.Version + 1 };
                    return new OperationsTransitionResultDto(
                        false,
                        "VERSION_CONFLICT",
                        "Concurrent workflow mutation.",
                        currentWorkflow,
                        [],
                        [],
                        currentWorkflow.Version);
                }

                currentWorkflow = currentWorkflow with
                {
                    Version = currentWorkflow.Version + 1,
                    Status = OperationsWorkflowStatusDto.ApprovalPending,
                    ClosePackage = null
                };
                return new OperationsTransitionResultDto(
                    true,
                    null,
                    null,
                    currentWorkflow,
                    [],
                    [],
                    currentWorkflow.Version);
            });
        var postingWorkbench = Substitute.For<IAccountingClosePostingWorkbench, IAccountingCloseMutationGate>();
        ((IAccountingCloseMutationGate)postingWorkbench)
            .AcquireAsync(
                Arg.Any<AccountingClosePostingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(call => consistencyLeases.Acquire(call.ArgAt<CancellationToken>(1)));
        var reversalGate = new ClosePostingGateDto(
            "period-close-posting:reopen-cas",
            "Post closing entries",
            ClosePostingGateStateDto.ReversalQueued,
            false,
            0m,
            0,
            "No active retained closing batch requires reversal.");
        postingWorkbench.ReopenAndQueueClosingReversalsAsync(
                Arg.Any<AccountingClosePostingContext>(),
                Arg.Any<AccountingClosePostingCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(reversalGate)
            .AndDoes(call =>
            {
                call.ArgAt<AccountingClosePostingCommand>(1).ConsistencyLeaseHeld.Should().BeTrue();
                consistencyLeases.ActiveCount.Should().Be(
                    1,
                    "the posting boundary and workflow transition must share one durable fence");
            });
        postingWorkbench.EvaluateAsync(
                Arg.Any<AccountingClosePostingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(reversalGate);
        var service = new AccountingCloseManagementService(workflowService, postingWorkbench);

        var first = await ReopenClosePeriodScopedAsync(
            service,
            BuildReopenRequest(workflowId, currentWorkflow.Version),
            "controller-reviewer");
        var retry = await ReopenClosePeriodScopedAsync(
            service,
            BuildReopenRequest(workflowId, currentWorkflow.Version),
            "controller-reviewer");

        first!.IsReopened.Should().BeFalse();
        first.Issues.Should().Contain(issue => issue.Code == "CloseWorkflowReopenPendingAfterLedgerReopen");
        retry!.IsReopened.Should().BeTrue();
        retry.Issues.Should().BeEmpty();
        await postingWorkbench.Received(2).ReopenAndQueueClosingReversalsAsync(
            Arg.Any<AccountingClosePostingContext>(),
            Arg.Any<AccountingClosePostingCommand>(),
            Arg.Any<CancellationToken>());
        await workflowService.Received(2).ReopenWorkflowAsync(
            workflowId,
            Arg.Any<OperationsReopenWorkflowRequestDto>(),
            Arg.Any<CancellationToken>());
        consistencyLeases.AcquireCount.Should().Be(2);
        consistencyLeases.ActiveCount.Should().Be(0);
    }

    [Fact]
    public async Task Scenario_ClosePlan_LockPeriodDelegatesToOperationsWorkflowAfterCloseControlsPass()
    {
        var workflowId = Guid.Parse("48484848-4848-4848-4848-484848484848");
        var ledgerBookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var consistencyLeases = new TrackingMutationLeaseState();
        var workflow = BuildCloseWorkflow(workflowId, firstTaskStatus: "Done", secondTaskStatus: "Done");
        var lockedWorkflow = workflow with
        {
            Status = OperationsWorkflowStatusDto.Closed,
            ClosePackage = new OperationsClosePackagePublicationDto(
                "close-package-2026-03",
                "report-pack-2026-03",
                "manifest-2026-03",
                "/workstation/reporting/packages/manifest-2026-03",
                "sha256-close-package",
                DateTimeOffset.Parse("2026-04-03T12:10:00Z"),
                "controller-reviewer",
                "Lock close period after report certification.",
                [
                    new OperationsEvidenceLinkDto(
                        $"evidence:close-package:{workflowId:D}:2026-03:book:{ledgerBookId:D}:period-lock",
                        "Close package evidence",
                        null,
                        "Accounting close management",
                        DateTimeOffset.Parse("2026-04-03T12:09:00Z"))
                ],
                [
                    new OperationsChecklistControlApprovalDto("reconciliation-review", "controller-reviewer", DateTimeOffset.Parse("2026-04-03T12:00:00Z")),
                    new OperationsChecklistControlApprovalDto("report-certification", "controller-reviewer", DateTimeOffset.Parse("2026-04-03T12:05:00Z"))
                ])
        };
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(workflow);
        var closeSequence = new List<string>();
        workflowService.CloseWorkflowAsync(workflowId, Arg.Any<OperationsCloseWorkflowRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(new OperationsTransitionResultDto(
                true,
                null,
                null,
                lockedWorkflow,
                [],
                [],
                NewVersion: workflow.Version + 1))
            .AndDoes(_ =>
            {
                consistencyLeases.ActiveCount.Should().Be(
                    1,
                    "the cross-host accounting-period fence must cover the Operations workflow close");
                closeSequence.Add("workflow-close");
            });
        var postingWorkbench = Substitute.For<IAccountingClosePostingWorkbench, IAccountingCloseMutationGate>();
        ((IAccountingCloseMutationGate)postingWorkbench)
            .AcquireAsync(
                Arg.Any<AccountingClosePostingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(call => consistencyLeases.Acquire(call.ArgAt<CancellationToken>(1)));
        var readyGate = new ClosePostingGateDto(
            "period-close-posting:test",
            "Post closing entries",
            ClosePostingGateStateDto.Posted,
            true,
            0m,
            0,
            "Retained closing entries are posted and temporary balances are zero.");
        postingWorkbench.EnsureClosingDraftQueuedAsync(
                Arg.Any<AccountingClosePostingContext>(),
                Arg.Any<AccountingClosePostingCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(readyGate);
        postingWorkbench.EvaluateAsync(
                Arg.Any<AccountingClosePostingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(readyGate);
        postingWorkbench.FinalizeHardCloseAsync(
                Arg.Any<AccountingClosePostingContext>(),
                Arg.Any<AccountingClosePostingCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new LedgerPeriodDto(
                Guid.Parse("49494949-4949-4949-4949-494949494949"),
                ledgerBookId,
                2026,
                3,
                "2026-03",
                new DateOnly(2026, 3, 1),
                new DateOnly(2026, 3, 31),
                LedgerPeriodStatusDto.HardClosed,
                DateTimeOffset.Parse("2026-03-01T00:00:00Z"),
                DateTimeOffset.Parse("2026-04-03T12:09:00Z"),
                3))
            .AndDoes(call =>
            {
                call.ArgAt<AccountingClosePostingCommand>(1).ConsistencyLeaseHeld.Should().BeTrue();
                consistencyLeases.ActiveCount.Should().Be(
                    1,
                    "both hard-close finalization passes must run under the coordinator-held fence");
                closeSequence.Add("ledger-finalize");
            });
        var service = new AccountingCloseManagementService(workflowService, postingWorkbench);
        var reconciliationEvidence = $"evidence:close-task:reconciliation-review:Controller:2026-03:book:{ledgerBookId:D}:control-signoff";
        await service.SignOffCloseTaskAsync(
            new SignOffCloseTaskRequestDto(
                workflowId,
                "reconciliation-review",
                "Controller",
                ManualJournalEntryStatusDto.Approved,
                "controller-reviewer",
                "Retained reconciliation close sign-off.",
                [reconciliationEvidence]),
            "controller-reviewer");
        var reportEvidence = $"evidence:close-task:report-certification:Controller:2026-03:book:{ledgerBookId:D}:control-signoff";
        await service.SignOffCloseTaskAsync(
            new SignOffCloseTaskRequestDto(
                workflowId,
                "report-certification",
                "Controller",
                ManualJournalEntryStatusDto.Approved,
                "controller-reviewer",
                "Retained report certification sign-off.",
                [reportEvidence]),
            "controller-reviewer");

        var result = await LockClosePeriodScopedAsync(
            service,
            new LockClosePeriodRequestDto(
                workflowId,
                ExpectedWorkflowVersion: workflow.Version,
                Actor: "controller-reviewer",
                Rationale: "Lock close period after report certification.",
                ReportPackId: "report-pack-2026-03",
                EvidenceLinks:
                [
                    $"evidence:close-package:{workflowId:D}:2026-03:book:{ledgerBookId:D}:period-lock"
                ],
                ChecklistControlApprovals:
                [
                    new OperationsChecklistControlApprovalDto("reconciliation-review", "controller-reviewer", DateTimeOffset.Parse("2026-04-03T12:00:00Z")),
                    new OperationsChecklistControlApprovalDto("report-certification", "controller-reviewer", DateTimeOffset.Parse("2026-04-03T12:05:00Z"))
                ],
                CorrelationId: "close-lock-2026-03",
                ClosePackageId: "close-package-2026-03",
                ClosePackageManifestId: "manifest-2026-03",
                ClosePackageRetainedManifestRoute: "/workstation/reporting/packages/manifest-2026-03",
                ControllerRole: "Controller"),
            "controller-reviewer");

        result.Should().NotBeNull();
        result!.IsLocked.Should().BeTrue();
        result.Issues.Should().BeEmpty();
        result.Plan.Should().NotBeNull();
        result.Plan!.IsPeriodLocked.Should().BeTrue();
        result.Transition.Should().NotBeNull();
        result.Transition!.Success.Should().BeTrue();
        closeSequence.Should().Equal(
            "ledger-finalize",
            "workflow-close",
            "ledger-finalize");
        await workflowService.Received(1).CloseWorkflowAsync(
            workflowId,
            Arg.Is<OperationsCloseWorkflowRequestDto>(request =>
                request.ExpectedVersion == workflow.Version &&
                request.Actor == "controller-reviewer" &&
                request.ReportPackId == "report-pack-2026-03" &&
                request.CorrelationId == "close-lock-2026-03" &&
                request.ChecklistControlApprovals.Count == 2 &&
                request.EvidenceLinks!.Any(link => link.EvidenceId.Contains("period-lock", StringComparison.OrdinalIgnoreCase))),
            Arg.Any<CancellationToken>());
        await postingWorkbench.Received(2).FinalizeHardCloseAsync(
            Arg.Is<AccountingClosePostingContext>(context =>
                context.WorkflowId == workflowId &&
                context.LedgerBookId == ledgerBookId &&
                context.PeriodId == "2026-03"),
            Arg.Is<AccountingClosePostingCommand>(command =>
                command.Actor == "controller-reviewer" &&
                command.ActionOrigin == OperationsActionOriginDto.HumanOperator),
            Arg.Any<CancellationToken>());
        consistencyLeases.AcquireCount.Should().Be(1);
        consistencyLeases.ActiveCount.Should().Be(0);
    }

    [Fact]
    public async Task Scenario_ClosePlan_PostCommitReportingHandoffFailureIsRetryableWithoutReopeningLedger()
    {
        var workflowId = Guid.Parse("48484848-4848-4848-4848-484848484849");
        var ledgerBookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var periodId = Guid.Parse("49494949-4949-4949-4949-494949494950");
        const string completionId = "hard-close-49494949494949494949494949494950-v3";
        var workflow = BuildCloseWorkflow(workflowId, firstTaskStatus: "Done", secondTaskStatus: "Done");
        var lockedWorkflow = workflow with
        {
            Status = OperationsWorkflowStatusDto.Closed,
            ClosePackage = new OperationsClosePackagePublicationDto(
                "close-package-retry",
                "report-pack-retry",
                "manifest-retry",
                "/workstation/reporting/packages/manifest-retry",
                "sha256-close-package-retry",
                DateTimeOffset.Parse("2026-04-03T12:10:00Z"),
                "controller-reviewer",
                "Retry the reporting evidence handoff without reopening.",
                [],
                [])
        };
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        var currentWorkflow = workflow;
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>()).Returns(_ => currentWorkflow);
        var closeSequence = new List<string>();
        workflowService.CloseWorkflowAsync(
                workflowId,
                Arg.Any<OperationsCloseWorkflowRequestDto>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                closeSequence.Add("workflow-close");
                currentWorkflow = lockedWorkflow;
                return new OperationsTransitionResultDto(
                    true,
                    null,
                    null,
                    currentWorkflow,
                    [],
                    [],
                    NewVersion: currentWorkflow.Version);
            });
        var postingWorkbench = CreateMutationGatedPostingWorkbench();
        var readyGate = new ClosePostingGateDto(
            "period-close-posting:retry",
            "Post closing entries",
            ClosePostingGateStateDto.Posted,
            true,
            0m,
            0,
            "Closing entries are retained and posted.");
        postingWorkbench.EnsureClosingDraftQueuedAsync(
                Arg.Any<AccountingClosePostingContext>(),
                Arg.Any<AccountingClosePostingCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(readyGate);
        postingWorkbench.EvaluateAsync(
                Arg.Any<AccountingClosePostingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(readyGate);
        var hardClosed = new LedgerPeriodDto(
            periodId,
            ledgerBookId,
            2026,
            3,
            "2026-03",
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 31),
            LedgerPeriodStatusDto.HardClosed,
            DateTimeOffset.Parse("2026-03-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-04-03T12:09:00Z"),
            3);
        var finalizationContexts = new List<AccountingClosePostingContext>();
        var finalizationCommands = new List<AccountingClosePostingCommand>();
        var finalizationAttempts = 0;
        postingWorkbench.FinalizeHardCloseAsync(
                Arg.Any<AccountingClosePostingContext>(),
                Arg.Any<AccountingClosePostingCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                finalizationAttempts++;
                closeSequence.Add($"ledger-finalize-{finalizationAttempts}");
                finalizationContexts.Add(call.ArgAt<AccountingClosePostingContext>(0));
                finalizationCommands.Add(call.ArgAt<AccountingClosePostingCommand>(1));
                return finalizationAttempts == 2
                    ? Task.FromException<LedgerPeriodDto>(new ReportingCloseEvidenceHandoffException(
                        hardClosed,
                        completionId,
                        "Ledger hard close is committed, but reporting evidence retention is pending.",
                        new InvalidOperationException("evidence store unavailable")))
                    : Task.FromResult(hardClosed);
            });

        var service = new AccountingCloseManagementService(workflowService, postingWorkbench);
        await ApproveRequiredCloseTasksAsync(service, workflowId, ledgerBookId);
        var request = new LockClosePeriodRequestDto(
            workflowId,
            workflow.Version,
            "controller-reviewer",
            "Retry-safe reporting close handoff.",
            "report-pack-retry",
            [$"evidence:close-package:{workflowId:D}:2026-03:book:{ledgerBookId:D}:period-lock"],
            CorrelationId: "close-lock-retry",
            ClosePackageId: "close-package-retry",
            ClosePackageManifestId: "manifest-retry",
            ClosePackageRetainedManifestRoute: "/workstation/reporting/packages/manifest-retry",
            ControllerRole: "Controller");

        var first = await LockClosePeriodScopedAsync(service, request, "controller-reviewer");

        first.Should().NotBeNull();
        first!.IsLocked.Should().BeFalse("the workflow publication waits for reporting evidence retention");
        first.Plan!.IsPeriodLocked.Should().BeTrue("the underlying ledger hard close already committed");
        first.Issues.Should().ContainSingle(issue =>
            issue.Code == "CloseReportingEvidenceHandoffPending"
            && issue.TargetId == completionId
            && issue.SuggestedAction!.Contains("Retry this same close command", StringComparison.Ordinal)
            && issue.SuggestedAction.Contains("do not reopen", StringComparison.Ordinal));
        await workflowService.Received(1).CloseWorkflowAsync(
            workflowId,
            Arg.Any<OperationsCloseWorkflowRequestDto>(),
            Arg.Any<CancellationToken>());

        var retry = await LockClosePeriodScopedAsync(service, request, "controller-reviewer");

        retry.Should().NotBeNull();
        retry!.IsLocked.Should().BeTrue();
        retry.Issues.Should().ContainSingle(issue => issue.Code == "ClosePeriodAlreadyLocked");
        closeSequence.Should().Equal(
            "ledger-finalize-1",
            "workflow-close",
            "ledger-finalize-2",
            "ledger-finalize-3");
        finalizationContexts.Should().HaveCount(3);
        finalizationContexts.Should().OnlyContain(context =>
            context.WorkflowId == workflowId
            && context.LedgerBookId == ledgerBookId
            && context.PeriodId == "2026-03");
        finalizationCommands.Should().HaveCount(3);
        finalizationCommands.Should().OnlyContain(command =>
            command.Actor == "controller-reviewer"
            && command.Role == "Controller"
            && command.CorrelationId == "close-lock-retry"
            && command.EvidenceLinks.Contains(
                $"evidence:close-package:{workflowId:D}:2026-03:book:{ledgerBookId:D}:period-lock",
                StringComparer.Ordinal));
        await postingWorkbench.Received(3).FinalizeHardCloseAsync(
            Arg.Any<AccountingClosePostingContext>(),
            Arg.Any<AccountingClosePostingCommand>(),
            Arg.Any<CancellationToken>());
        await workflowService.Received(1).CloseWorkflowAsync(
            workflowId,
            Arg.Any<OperationsCloseWorkflowRequestDto>(),
            Arg.Any<CancellationToken>());
        await postingWorkbench.DidNotReceive().ReopenAndQueueClosingReversalsAsync(
            Arg.Any<AccountingClosePostingContext>(),
            Arg.Any<AccountingClosePostingCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Scenario_MonthEndFxTranslation_ReplayUsesStableAdjustmentIdAndRateLineage()
    {
        var service = new FxTranslationService();
        var rate = new FxRate("EUR", "USD", new DateOnly(2026, 03, 31), 1.10m, "fx:ecb:20260331", "ECB-EURUSD-20260331");

        var first = service.Translate("ledger-a", new DateOnly(2026, 03, 31), "Cash", 100m, rate);
        var replay = service.Translate("ledger-a", new DateOnly(2026, 03, 31), "Cash", 100m, rate);

        first.ReportingAmount.Should().Be(110m);
        first.AdjustmentAmount.Should().Be(10m);
        first.SourceEventId.Should().Be("fx:ecb:20260331");
        first.RateId.Should().Be("ECB-EURUSD-20260331");
        replay.AdjustmentId.Should().Be(first.AdjustmentId);
    }

    [Fact]
    public void Scenario_MonthEndTrialBalance_OutOfBalanceActivityBlocksCloseEvidence()
    {
        var projection = new TrialBalanceProjectionService();
        var entries = ImmutableArray.Create(
            NewEntry("evt-1", "approval-1", new JournalLine("Cash", 100m, "USD", true, "evt-1", "approval-1")),
            NewEntry("evt-2", "approval-2", new JournalLine("Revenue", 90m, "USD", false, "evt-2", "approval-2")));

        var trial = projection.BuildTrialBalance(entries);
        var stateMachine = new MonthEndCloseStateMachine();
        var validating = new ClosePeriod("ledger-a", new DateOnly(2026, 03, 31), ClosePeriodState.Validating, new CloseEvidence(true, true, true), []);

        var next = stateMachine.Transition(validating, new CloseEvidence(true, true, true), projection.IsBalanced(trial));

        projection.IsBalanced(trial).Should().BeFalse();
        next.State.Should().Be(ClosePeriodState.Blocked);
        next.Blockers.Should().Contain("Trial balance is out of balance.");
    }

    [Fact]
    public void Scenario_MonthEndTrialBalance_PreservesLineDimensionsAsSeparateCloseRows()
    {
        var projection = new TrialBalanceProjectionService();
        var entityAlpha = new LedgerDimensionSetDto(
            FundId: "fund-alpha",
            EntityId: "entity-alpha",
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Department"] = "Investments"
            });
        var entityBeta = new LedgerDimensionSetDto(
            FundId: "fund-alpha",
            EntityId: "entity-beta",
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Department"] = "Operations"
            });
        var entries = ImmutableArray.Create(
            NewEntry(
                "evt-entity-alpha",
                "approval-entity-alpha",
                new JournalLine("Cash", 125m, "USD", true, "evt-entity-alpha", "approval-entity-alpha", Dimensions: entityAlpha),
                new JournalLine("Revenue", 125m, "USD", false, "evt-entity-alpha", "approval-entity-alpha", Dimensions: entityAlpha)),
            NewEntry(
                "evt-entity-beta",
                "approval-entity-beta",
                new JournalLine("Cash", 75m, "USD", true, "evt-entity-beta", "approval-entity-beta", Dimensions: entityBeta),
                new JournalLine("Revenue", 75m, "USD", false, "evt-entity-beta", "approval-entity-beta", Dimensions: entityBeta)));

        var trial = projection.BuildTrialBalance(entries);

        trial.Where(line => line.AccountCode == "Cash").Should().HaveCount(2);
        var alphaCash = trial.Single(line => line.AccountCode == "Cash" && line.Net == 125m);
        alphaCash.Dimensions.Should().NotBeNull();
        alphaCash.Dimensions!.EntityId.Should().Be("entity-alpha");
        alphaCash.Dimensions.ExternalGlDimensions["Department"].Should().Be("Investments");
        alphaCash.SourceEventIds.Should().Contain("evt-entity-alpha");
        var betaCash = trial.Single(line => line.AccountCode == "Cash" && line.Net == 75m);
        betaCash.Dimensions.Should().NotBeNull();
        betaCash.Dimensions!.EntityId.Should().Be("entity-beta");
        betaCash.Dimensions.ExternalGlDimensions["Department"].Should().Be("Operations");
        betaCash.SourceEventIds.Should().Contain("evt-entity-beta");
        projection.IsBalanced(trial).Should().BeTrue();
    }

    [Fact]
    public void Scenario_MonthEndTrialBalance_FiltersByRequestedDimensionScope()
    {
        var projection = new TrialBalanceProjectionService();
        var entityAlpha = new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-alpha", CostCenterId: "investment-ops");
        var entityBeta = new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-beta", CostCenterId: "investment-ops");
        var entries = ImmutableArray.Create(
            NewEntry(
                "evt-alpha",
                "approval-alpha",
                new JournalLine("Cash", 40m, "USD", true, "evt-alpha", "approval-alpha", Dimensions: entityAlpha),
                new JournalLine("Revenue", 40m, "USD", false, "evt-alpha", "approval-alpha", Dimensions: entityAlpha)),
            NewEntry(
                "evt-beta",
                "approval-beta",
                new JournalLine("Cash", 60m, "USD", true, "evt-beta", "approval-beta", Dimensions: entityBeta),
                new JournalLine("Revenue", 60m, "USD", false, "evt-beta", "approval-beta", Dimensions: entityBeta)));

        var filtered = projection.BuildTrialBalance(entries, new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-alpha"));

        filtered.Should().HaveCount(2);
        filtered.Select(line => line.Dimensions?.EntityId).Should().OnlyContain(entityId => entityId == "entity-alpha");
        filtered.Sum(line => line.Debit).Should().Be(40m);
        filtered.Sum(line => line.Credit).Should().Be(40m);
        filtered.SelectMany(line => line.SourceEventIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Should()
            .BeEquivalentTo(["evt-alpha"]);
        projection.IsBalanced(filtered).Should().BeTrue();
    }

    [Fact]
    public void Scenario_MonthEndRollForward_PreservesDimensionScopedRows()
    {
        var projection = new TrialBalanceProjectionService();
        var entityAlpha = new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-alpha", CostCenterId: "investment-ops");
        var entityBeta = new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-beta", CostCenterId: "fund-admin");
        var opening = ImmutableArray.Create(
            new TrialBalanceLine(
                "Cash",
                500m,
                0m,
                500m,
                SourceEventIds: ImmutableArray.Create("opening-alpha"),
                ApprovalIds: ImmutableArray.Create("approval-opening-alpha"),
                Dimensions: entityAlpha),
            new TrialBalanceLine(
                "Cash",
                250m,
                0m,
                250m,
                SourceEventIds: ImmutableArray.Create("opening-beta"),
                ApprovalIds: ImmutableArray.Create("approval-opening-beta"),
                Dimensions: entityBeta));
        var activity = ImmutableArray.Create(
            new TrialBalanceLine(
                "Cash",
                75m,
                0m,
                75m,
                SourceEventIds: ImmutableArray.Create("activity-alpha"),
                ApprovalIds: ImmutableArray.Create("approval-activity-alpha"),
                Dimensions: entityAlpha),
            new TrialBalanceLine(
                "Cash",
                25m,
                0m,
                25m,
                SourceEventIds: ImmutableArray.Create("activity-beta"),
                ApprovalIds: ImmutableArray.Create("approval-activity-beta"),
                Dimensions: entityBeta));

        var rollForward = projection.BuildRollForward(opening, activity, []);

        rollForward.Should().HaveCount(2);
        var alpha = rollForward.Single(line => line.Dimensions?.EntityId == "entity-alpha");
        alpha.OpeningBalance.Should().Be(500m);
        alpha.Activity.Should().Be(75m);
        alpha.ClosingBalance.Should().Be(575m);
        alpha.Dimensions!.CostCenterId.Should().Be("investment-ops");
        alpha.SourceEventIds.Should().BeEquivalentTo(["activity-alpha"]);
        alpha.ApprovalIds.Should().BeEquivalentTo(["approval-activity-alpha"]);
        var beta = rollForward.Single(line => line.Dimensions?.EntityId == "entity-beta");
        beta.OpeningBalance.Should().Be(250m);
        beta.Activity.Should().Be(25m);
        beta.ClosingBalance.Should().Be(275m);
        beta.Dimensions!.CostCenterId.Should().Be("fund-admin");
        beta.SourceEventIds.Should().BeEquivalentTo(["activity-beta"]);
        beta.ApprovalIds.Should().BeEquivalentTo(["approval-activity-beta"]);
    }

    [Fact]
    public void Scenario_MonthEndRollForward_PreservesDimensionScopedFxAdjustments()
    {
        var projection = new TrialBalanceProjectionService();
        var fx = new FxTranslationService();
        var entityAlpha = new LedgerDimensionSetDto(
            FundId: "fund-alpha",
            EntityId: "entity-alpha",
            BookId: "book-gaap",
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Department"] = "Investments"
            });
        var entityBeta = new LedgerDimensionSetDto(
            FundId: "fund-alpha",
            EntityId: "entity-beta",
            BookId: "book-gaap",
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Department"] = "Operations"
            });
        var activity = ImmutableArray.Create(
            new TrialBalanceLine(
                "Cash",
                100m,
                0m,
                100m,
                SourceEventIds: ImmutableArray.Create("activity-alpha"),
                ApprovalIds: ImmutableArray.Create("approval-activity-alpha"),
                Dimensions: entityAlpha),
            new TrialBalanceLine(
                "Cash",
                50m,
                0m,
                50m,
                SourceEventIds: ImmutableArray.Create("activity-beta"),
                ApprovalIds: ImmutableArray.Create("approval-activity-beta"),
                Dimensions: entityBeta));
        var adjustments = fx.TranslateTrialBalance(
            "ledger-a",
            new DateOnly(2026, 03, 31),
            activity,
            new FxRate("EUR", "USD", new DateOnly(2026, 03, 31), 1.10m, "fx:ecb:20260331", "ECB-EURUSD-20260331"));

        var rollForward = projection.BuildRollForward([], activity, adjustments);

        adjustments.Should().HaveCount(2);
        adjustments.Should().OnlyContain(adjustment => adjustment.Dimensions != null);
        adjustments.Select(adjustment => adjustment.AdjustmentId).Should().OnlyHaveUniqueItems();
        var alpha = rollForward.Single(line => line.Dimensions?.EntityId == "entity-alpha");
        alpha.Activity.Should().Be(100m);
        alpha.TranslationAdjustment.Should().Be(10m);
        alpha.ClosingBalance.Should().Be(110m);
        alpha.Dimensions!.ExternalGlDimensions["Department"].Should().Be("Investments");
        alpha.SourceEventIds.Should().BeEquivalentTo(["activity-alpha", "fx:ecb:20260331"]);
        alpha.ApprovalIds.Should().BeEquivalentTo(["approval-activity-alpha"]);
        var beta = rollForward.Single(line => line.Dimensions?.EntityId == "entity-beta");
        beta.Activity.Should().Be(50m);
        beta.TranslationAdjustment.Should().Be(5m);
        beta.ClosingBalance.Should().Be(55m);
        beta.Dimensions!.ExternalGlDimensions["Department"].Should().Be("Operations");
        beta.SourceEventIds.Should().BeEquivalentTo(["activity-beta", "fx:ecb:20260331"]);
        beta.ApprovalIds.Should().BeEquivalentTo(["approval-activity-beta"]);
    }

    [Fact]
    public void Scenario_MonthEndPosting_OutOfBalanceJournalIsRejectedBeforeReplay()
    {
        var posting = new AccountingPostingService();
        var entry = NewEntry("evt-oob", "approval-oob", new JournalLine("Cash", 100m, "USD", true, "evt-oob", "approval-oob"));

        var result = posting.PostWithResult("ledger-a", [entry]);

        result.Accepted.Should().BeFalse();
        result.RejectedReasons.Should().ContainSingle(reason => reason.Contains("out of balance", StringComparison.OrdinalIgnoreCase));
        posting.Replay("ledger-a").Should().BeEmpty();
    }

    [Fact]
    public void Scenario_MonthEndPosting_ClosedPeriodRejectsLateJournal()
    {
        var posting = new AccountingPostingService();
        var lockedPeriod = new ClosePeriod(
            "ledger-a",
            new DateOnly(2026, 03, 01),
            ClosePeriodState.Closed,
            new CloseEvidence(true, true, true),
            [],
            DateTimeOffset.Parse("2026-04-01T00:00:00Z"),
            "controller");

        var result = posting.PostWithResult("ledger-a", [BalancedEntry("evt-late", "approval-late")], lockedPeriod);

        result.Accepted.Should().BeFalse();
        result.RejectedReasons.Should().ContainSingle(reason => reason.Contains("locked", StringComparison.OrdinalIgnoreCase));
        posting.Replay("ledger-a").Should().BeEmpty();
    }

    [Fact]
    public void Scenario_MonthEndClose_EvidenceChecksGateClosedState()
    {
        var stateMachine = new MonthEndCloseStateMachine();
        var current = new ClosePeriod("ledger-a", new DateOnly(2026, 03, 31), ClosePeriodState.Validating, new CloseEvidence(false, false, false), []);
        var evidence = new CloseEvidence(
            TrialBalanceSignedOff: true,
            ReconciliationSignedOff: true,
            ApprovalsCompleted: true,
            Checks: ImmutableArray.Create(new CloseEvidenceCheck("packet", "Controller packet", true, false, "evt-close", "approval-close", "Controller approval is pending.")));

        var next = stateMachine.Transition(current, evidence, isTrialBalanceBalanced: true);

        next.State.Should().Be(ClosePeriodState.Blocked);
        next.Blockers.Should().ContainSingle(reason => reason.Contains("Controller packet", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scenario_MonthEndClose_AllGatesPassingLocksThePeriod()
    {
        var stateMachine = new MonthEndCloseStateMachine();
        var current = new ClosePeriod("ledger-a", new DateOnly(2026, 03, 31), ClosePeriodState.Validating, new CloseEvidence(true, true, true), []);

        var next = stateMachine.Transition(current, new CloseEvidence(true, true, true), isTrialBalanceBalanced: true);

        next.State.Should().Be(ClosePeriodState.Closed);
        next.Blockers.Should().BeEmpty();
        next.IsLocked.Should().BeTrue();
    }

    [Fact]
    public void Scenario_MonthEndPosting_ReplayOrdersByPeriodDateAndJournalIdWithAuditLineage()
    {
        var posting = new AccountingPostingService();
        var idB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var idA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        posting.Post("ledger-a", [
            BalancedEntry("evt-b", "approval-b", idB),
            BalancedEntry("evt-a", "approval-a", idA)
        ]);

        var replay = posting.Replay("ledger-a");
        var audit = posting.Audit("ledger-a");
        replay[0].JournalEntryId.Should().Be(idA);
        replay[1].JournalEntryId.Should().Be(idB);
        audit[0].SourceEventId.Should().Be("evt-a");
        audit[0].ApprovalId.Should().Be("approval-a");
        audit[0].AccountCodes.Should().BeEquivalentTo("Cash", "Revenue");
    }

    private static JournalEntry BalancedEntry(string sourceEventId, string approvalId, Guid? journalEntryId = null)
        => new(
            journalEntryId ?? Guid.NewGuid(),
            "ledger-a",
            new DateOnly(2026, 03, 02),
            sourceEventId,
            "balanced accrual",
            ImmutableArray.Create(
                new JournalLine("Cash", 100m, "USD", true, sourceEventId, approvalId),
                new JournalLine("Revenue", 100m, "USD", false, sourceEventId, approvalId)));

    private static JournalEntry NewEntry(string sourceEventId, string approvalId, params JournalLine[] lines)
        => new(
            Guid.NewGuid(),
            "ledger-a",
            new DateOnly(2026, 03, 31),
            sourceEventId,
            "month-end source event",
            lines.Select(line => string.IsNullOrWhiteSpace(line.ApprovalId) ? line with { ApprovalId = approvalId } : line).ToImmutableArray());

    private static OperationsContinuityWorkflowDto BuildCloseWorkflow(
        Guid workflowId,
        string firstTaskStatus,
        string secondTaskStatus,
        string? firstTaskAcknowledgedBy = null,
        string? secondTaskAcknowledgedBy = null)
    {
        var now = DateTimeOffset.Parse("2026-04-01T00:00:00Z");
        return new OperationsContinuityWorkflowDto(
            workflowId,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "2026-03",
            SecurityMasterSnapshotId: null,
            BrokerSource: "custodian",
            CreatedAtUtc: now,
            UpdatedAtUtc: now,
            Version: 7,
            Status: OperationsWorkflowStatusDto.ApprovalPending,
            BrokerIntakeState: OperationsBrokerIntakeStateDto.Complete,
            SecurityMasterState: OperationsSecurityMasterStateDto.Complete,
            LedgerPostingState: OperationsLedgerPostingStateDto.Complete,
            ReconciliationState: OperationsReconciliationStateDto.Complete,
            ApprovalState: OperationsApprovalStateDto.Pending,
            Gates: [],
            Timeline: [],
            BreakCases: [],
            LedgerPreview: null,
            Approvals: [],
            ReportPackReadiness: new OperationsReportPackReadinessDto(false, null, "Awaiting close certification.", []),
            CloseChecklist:
            [
                new OperationsCloseChecklistTaskDto(
                    "reconciliation-review",
                    OperationsGateKeyDto.Reconciliation,
                    "Reconciliation review",
                    "Controller",
                    "Retained reconciliation approval evidence",
                    RequiredApprovalCount: 1,
                    ExpiresOn: null,
                    DueDate: new DateOnly(2026, 04, 2),
                    Status: firstTaskStatus,
                    BlockingReason: null,
                    EvidencePointer: null,
                    RemediationRoute: "/workstation/accounting/close",
                    CanAcknowledge: true,
                    AcknowledgedAtUtc: firstTaskAcknowledgedBy is null ? null : now,
                    AcknowledgedBy: firstTaskAcknowledgedBy),
                new OperationsCloseChecklistTaskDto(
                    "report-certification",
                    OperationsGateKeyDto.Approval,
                    "Report certification",
                    "Controller",
                    "Retained report certification evidence",
                    RequiredApprovalCount: 1,
                    ExpiresOn: null,
                    DueDate: new DateOnly(2026, 04, 3),
                    Status: secondTaskStatus,
                    BlockingReason: null,
                    EvidencePointer: null,
                    RemediationRoute: "/workstation/reporting",
                    CanAcknowledge: true,
                    AcknowledgedAtUtc: secondTaskAcknowledgedBy is null ? null : now,
                    AcknowledgedBy: secondTaskAcknowledgedBy)
            ],
            EvidenceLinks: [],
            Blockers: [],
            NextActions: [],
            LedgerBookId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
    }

    private static OperationsContinuityWorkflowDto BuildLockedCloseWorkflow(
        OperationsContinuityWorkflowDto workflow,
        long? version = null)
        => workflow with
        {
            Version = version ?? workflow.Version,
            Status = OperationsWorkflowStatusDto.Closed,
            ClosePackage = new OperationsClosePackagePublicationDto(
                $"close-package-{workflow.WorkflowId:D}",
                "report-pack-2026-03",
                $"manifest-{workflow.WorkflowId:D}",
                $"/workstation/reporting/packages/manifest-{workflow.WorkflowId:D}",
                "sha256-close-package",
                DateTimeOffset.Parse("2026-04-03T12:10:00Z"),
                "controller-reviewer",
                "Lock close period after report certification.",
                [],
                [])
        };

    private static ReopenClosePeriodRequestDto BuildReopenRequest(Guid workflowId, long version)
        => new(
            workflowId,
            version,
            "controller-reviewer",
            "Fund Controller",
            "Reopen the close for a governed restatement.",
            "incident-2026-03",
            "A material restatement requires corrected accounting evidence.",
            "reopen-approval-2026-03",
            "March financial statements and downstream reports require recertification.",
            ["evidence:restatement:2026-03:reopen-approval-2026-03"],
            "reopen-correlation-2026-03");

    private static IAccountingClosePostingWorkbench CreateMutationGatedPostingWorkbench(
        TrackingMutationLeaseState? consistencyLeases = null)
    {
        var leases = consistencyLeases ?? new TrackingMutationLeaseState();
        var postingWorkbench = Substitute.For<IAccountingClosePostingWorkbench, IAccountingCloseMutationGate>();
        ((IAccountingCloseMutationGate)postingWorkbench)
            .AcquireAsync(
                Arg.Any<AccountingClosePostingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(call => leases.Acquire(call.ArgAt<CancellationToken>(1)));
        return postingWorkbench;
    }

    private static Task<ClosePeriodLockResultDto?> LockClosePeriodScopedAsync(
        AccountingCloseManagementService service,
        LockClosePeriodRequestDto request,
        string actor,
        CancellationToken ct = default)
        => service.LockClosePeriodScopedAsync(
            request,
            actor,
            tenantId: "tenant-alpha",
            companyId: "company-alpha",
            ct: ct);

    private static Task<ClosePeriodReopenResultDto?> ReopenClosePeriodScopedAsync(
        AccountingCloseManagementService service,
        ReopenClosePeriodRequestDto request,
        string actor,
        CancellationToken ct = default)
        => service.ReopenClosePeriodScopedAsync(
            request,
            actor,
            tenantId: "tenant-alpha",
            companyId: "company-alpha",
            ct: ct);

    private static async Task ApproveRequiredCloseTasksAsync(
        AccountingCloseManagementService service,
        Guid workflowId,
        Guid ledgerBookId)
    {
        await service.SignOffCloseTaskAsync(
            new SignOffCloseTaskRequestDto(
                workflowId,
                "reconciliation-review",
                "Controller",
                ManualJournalEntryStatusDto.Approved,
                "controller-reviewer",
                "Retained reconciliation close sign-off.",
                [$"evidence:close-task:reconciliation-review:Controller:2026-03:book:{ledgerBookId:D}:control-signoff"]),
            "controller-reviewer");
        await service.SignOffCloseTaskAsync(
            new SignOffCloseTaskRequestDto(
                workflowId,
                "report-certification",
                "Controller",
                ManualJournalEntryStatusDto.Approved,
                "controller-reviewer",
                "Retained report certification sign-off.",
                [$"evidence:close-task:report-certification:Controller:2026-03:book:{ledgerBookId:D}:control-signoff"]),
            "controller-reviewer");
    }

    private sealed class TrackingMutationLeaseState
    {
        private int _acquireCount;
        private int _activeCount;

        public int AcquireCount => Volatile.Read(ref _acquireCount);

        public int ActiveCount => Volatile.Read(ref _activeCount);

        public ValueTask<IAsyncDisposable> Acquire(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _acquireCount);
            Interlocked.Increment(ref _activeCount);
            return ValueTask.FromResult<IAsyncDisposable>(new Lease(this));
        }

        private sealed class Lease(TrackingMutationLeaseState owner) : IAsyncDisposable
        {
            private TrackingMutationLeaseState? _owner = owner;

            public ValueTask DisposeAsync()
            {
                var captured = Interlocked.Exchange(ref _owner, null);
                if (captured is not null)
                {
                    Interlocked.Decrement(ref captured._activeCount);
                }

                return ValueTask.CompletedTask;
            }
        }
    }
}

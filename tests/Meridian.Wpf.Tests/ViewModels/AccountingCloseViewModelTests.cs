using System.Collections.Immutable;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.Ui.Services.Services.Accounting;
using Meridian.Wpf.ViewModels.Accounting;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class AccountingCloseViewModelTests
{
    [Fact]
    public async Task ConfigureClosePlanCommand_RetainsLoadedPlanThroughSharedCloseManagementService()
    {
        var workflowId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var ledgerBookId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var configuredAtUtc = DateTimeOffset.Parse("2026-06-02T02:30:00Z");
        var closePlan = BuildClosePlan(ledgerBookId) with
        {
            Configuration = new ClosePeriodPlanConfigurationDto(
                workflowId,
                BuildClosePlan(ledgerBookId).MaterialityPolicy,
                ConfiguredBy: "controller",
                ConfiguredAtUtc: configuredAtUtc,
                EvidenceLinks: ["evidence/close-plan-configuration"])
        };
        var service = new CapturingCloseManagementService(closePlan);
        var viewModel = new AccountingCloseViewModel(Substitute.For<IAccountingProjectionQueryService>(), service);

        viewModel.ApplyClosePlan(workflowId, closePlan);

        viewModel.ConfigureClosePlanCommand.CanExecute(null).Should().BeTrue();

        await viewModel.ConfigureClosePlanCommand.ExecuteAsync(null);

        service.Actor.Should().Be("wpf-accounting-controller");
        service.Request.Should().NotBeNull();
        service.Request!.WorkflowId.Should().Be(workflowId);
        service.Request.Actor.Should().Be("wpf-accounting-controller");
        service.Request.CorrelationId.Should().Be($"wpf-close-plan-configuration-{workflowId:D}");
        service.Request.ActionOrigin.Should().Be(OperationsActionOriginDto.HumanOperator);
        service.Request.ExpectedConfiguredAtUtc.Should().Be(configuredAtUtc);
        service.Request.MaterialityPolicy.Should().Be(closePlan.MaterialityPolicy);
        service.Request.TaskConfigurations.Should().ContainSingle().Which.Should().Match<CloseTaskConfigurationDto>(task =>
            task.TaskId == "task-nav" &&
            task.DisplayName == "Finalize NAV package" &&
            task.Owner == "fund-accounting" &&
            task.DueDate == new DateOnly(2026, 6, 4) &&
            task.RequiredApprovalCount == 2 &&
            task.RequiredEvidence == "Controller NAV sign-off evidence" &&
            task.DependsOnTaskIds.SequenceEqual(new[] { "task-reconciliation" }));
        service.Request.EvidenceLinks.Should().Contain([
            $"wpf://accounting/close/setup/{workflowId:D}",
            "evidence://close-plan-configuration/fund/fund-alpha/period/2026-05",
            $"evidence://close-plan-configuration/ledger-book/{ledgerBookId:D}",
            "evidence/nav-package",
            "evidence/late-adjustment"
        ]);
        viewModel.ClosePlanSetupStatusText.Should().Be("Retained close-plan setup for 2026-05.");
    }

    [Fact]
    public async Task LoadClosePlanCommand_LoadsSharedClosePlanByWorkflowId()
    {
        var workflowId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var ledgerBookId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var closePlan = BuildClosePlan(ledgerBookId);
        var service = new CapturingCloseManagementService(closePlan);
        var viewModel = new AccountingCloseViewModel(Substitute.For<IAccountingProjectionQueryService>(), service)
        {
            CloseWorkflowIdText = workflowId.ToString("D")
        };

        viewModel.LoadClosePlanCommand.CanExecute(null).Should().BeTrue();

        await viewModel.LoadClosePlanCommand.ExecuteAsync(null);

        viewModel.CloseWorkflowIdText.Should().Be(workflowId.ToString("D"));
        viewModel.ClosePlanSetupStatusText.Should().Be("Loaded close plan 2026-05 for governed setup retention.");
        viewModel.ConfigureClosePlanCommand.CanExecute(null).Should().BeTrue();
        viewModel.SignOffCloseTaskCommand.CanExecute(null).Should().BeTrue();
        viewModel.ReviewLateAdjustmentCommand.CanExecute(null).Should().BeFalse();
        viewModel.ReviewCloseEvidenceCommand.CanExecute(null).Should().BeFalse();
        viewModel.LockClosePeriodCommand.CanExecute(null).Should().BeTrue();
        viewModel.CloseTaskRows.Should().ContainSingle(row => row.Name == "task-nav");
        viewModel.CloseSignOffMatrixRows.Should().ContainSingle(row => row.Name == "task-nav:controller");
        viewModel.CloseLateAdjustmentRows.Should().ContainSingle(row => row.Name == "late-adjustment-1");
        viewModel.CloseOperatingCoverageRows.Should().HaveCount(6);
        viewModel.CloseOperatingCoverageRows.Should().Contain(row =>
            row.Name == "Dependency graph" &&
            row.Status == "Blocked" &&
            row.Detail == "Complete predecessor close tasks before dependent close work advances." &&
            row.Evidence.Contains("1 blocking issue", StringComparison.OrdinalIgnoreCase) &&
            row.Evidence.Contains("CloseTaskWaitingOnDependency", StringComparison.OrdinalIgnoreCase));
        viewModel.CloseOperatingCoverageRows.Should().Contain(row =>
            row.Name == "Period lock" &&
            row.Status == "Ready for review" &&
            row.Detail == "Retain close-package evidence and lock the period.");
        viewModel.CloseWorkflowSteps.Should().HaveCount(6);
        viewModel.CloseWorkflowSteps.Should().Contain(step =>
            step.StepId == "close-setup" &&
            step.Status == "Draft loaded" &&
            step.Evidence == "2 evidence links" &&
            step.DisabledReason == null &&
            ReferenceEquals(step.Command, viewModel.ConfigureClosePlanCommand));
        viewModel.CloseWorkflowSteps.Should().Contain(step =>
            step.StepId == "checklist-signoff" &&
            step.Status == "1 open" &&
            step.Evidence == "0/1 requirement rows satisfied" &&
            step.DisabledReason == null &&
            ReferenceEquals(step.Command, viewModel.SignOffCloseTaskCommand));
        viewModel.CloseWorkflowSteps.Should().Contain(step =>
            step.StepId == "late-adjustment-request" &&
            step.Status == "1 retained" &&
            step.DisabledReason == "Enter a journal entry id before requesting a late adjustment.");
        viewModel.CloseWorkflowSteps.Should().Contain(step =>
            step.StepId == "late-adjustment-review" &&
            step.Status == "Reviewed" &&
            step.Evidence == "1/1 reviewed" &&
            step.DisabledReason == "Close plan 2026-05 has no submitted late adjustment to review.");
        viewModel.CloseWorkflowSteps.Should().Contain(step =>
            step.StepId == "blocker-review" &&
            step.Status == "Clear" &&
            step.Evidence == "No blockers" &&
            step.DisabledReason == "Close plan 2026-05 has no unreviewed active blockers.");
        viewModel.CloseWorkflowSteps.Should().Contain(step =>
            step.StepId == "period-lock" &&
            step.Status == "Open" &&
            step.Evidence == $"Ledger book {ledgerBookId:D}" &&
            step.DisabledReason == null &&
            ReferenceEquals(step.Command, viewModel.LockClosePeriodCommand));
    }

    [Fact]
    public void ApplyClosePlan_ShowsFallbackWhenSharedOperatingCoverageIsMissing()
    {
        var closePlan = BuildClosePlan(Guid.Parse("11111111-2222-3333-4444-555555555555")) with
        {
            OperatingCoverage = []
        };
        var viewModel = new AccountingCloseViewModel(Substitute.For<IAccountingProjectionQueryService>());

        viewModel.ApplyClosePlan(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), closePlan);

        viewModel.CloseOperatingCoverageRows.Should().ContainSingle(row =>
            row.Name == "Operating coverage" &&
            row.Status == "Missing" &&
            row.Detail.Contains("did not return", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApplyCloseProjection_ProjectsEvidencePackageStatusAndApprovalHistory()
    {
        var posting = new AccountingPostingService();
        var query = new AccountingProjectionQueryService(posting, new TrialBalanceProjectionService());
        var viewModel = new AccountingCloseViewModel(query);
        posting.Post(
            "ledger-a",
            [
                new JournalEntry(
                    Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                    "ledger-a",
                    new DateOnly(2026, 03, 31),
                    "evt-post",
                    "close accrual",
                    ImmutableArray.Create(
                        new JournalLine("Cash", 100m, "USD", true, "evt-post", "approval-post"),
                        new JournalLine("Revenue", 100m, "USD", false, "evt-post", "approval-post")))
            ]);
        var evidence = new CloseEvidence(
            TrialBalanceSignedOff: true,
            ReconciliationSignedOff: true,
            ApprovalsCompleted: true,
            Checks:
            [
                new CloseEvidenceCheck(
                    "controller-packet",
                    "Controller close packet",
                    Required: true,
                    Passed: true,
                    SourceEventId: "evt-close",
                    ApprovalId: "approval-close",
                    Detail: "Controller package approved.")
            ]);
        var projection = query.GetCloseProjection("ledger-a", new DateOnly(2026, 03, 31), evidence);

        viewModel.ApplyCloseProjection(projection);

        viewModel.EvidencePackageStatusText.Should().Contain(projection.EvidencePackage.PackageId);
        viewModel.EvidencePackageStatusText.Should().Contain("2 source events");
        viewModel.EvidencePackageStatusText.Should().Contain("2 approvals");
        viewModel.ApprovalHistory.Should().Contain(row =>
            row.ApprovalId == "approval-close" &&
            row.Label == "Controller close packet");
        viewModel.ApprovalHistory.Should().Contain(row =>
            row.ApprovalId == "approval-post" &&
            row.JournalEntryId == Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
    }

    [Fact]
    public void LoadClosePlanCommand_RequiresValidWorkflowId()
    {
        var closePlan = BuildClosePlan(Guid.Parse("11111111-2222-3333-4444-555555555555"));
        var service = new CapturingCloseManagementService(closePlan);
        var viewModel = new AccountingCloseViewModel(Substitute.For<IAccountingProjectionQueryService>(), service)
        {
            CloseWorkflowIdText = "not-a-guid"
        };

        viewModel.LoadClosePlanCommand.CanExecute(null).Should().BeFalse();

        viewModel.CloseWorkflowIdText = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee").ToString("D");

        viewModel.LoadClosePlanCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void ConfigureClosePlanCommand_RequiresWorkflowContext()
    {
        var closePlan = BuildClosePlan(Guid.Parse("11111111-2222-3333-4444-555555555555"));
        var service = new CapturingCloseManagementService(closePlan);
        var viewModel = new AccountingCloseViewModel(Substitute.For<IAccountingProjectionQueryService>(), service);

        viewModel.ApplyClosePlan(closePlan);

        viewModel.ConfigureClosePlanCommand.CanExecute(null).Should().BeFalse();
        viewModel.ClosePlanSetupStatusText.Should().Be("Close plan 2026-05 loaded without workflow context; setup retention is disabled.");
    }

    [Fact]
    public void ApplyClosePlan_ProjectsMaterialityDependencySignOffLateAdjustmentAndEvidenceReviewRows()
    {
        var workflowId = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        var ledgerBookId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var closePlan = BuildClosePlan(ledgerBookId) with
        {
            ValidationIssues =
            [
                new AccountingConfigurationValidationIssueDto(
                    "CloseTaskSignOffMissing",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    "Close task 'Finalize NAV package' requires retained controller sign-off.",
                    "task-nav",
                    "Retain controller sign-off evidence before locking the period.")
            ]
        };
        var viewModel = new AccountingCloseViewModel(Substitute.For<IAccountingProjectionQueryService>());

        viewModel.ApplyClosePlan(workflowId, 7, closePlan);

        viewModel.CloseMaterialityRows.Should().Contain(row =>
            row.Name == "materiality-alpha" &&
            row.Status == "Approval required" &&
            row.Detail.Contains("2,500.00 USD / 0.50%", StringComparison.Ordinal) &&
            row.Evidence.Contains(ledgerBookId.ToString("D"), StringComparison.OrdinalIgnoreCase));
        viewModel.CloseMaterialityRows.Should().Contain(row =>
            row.Name == "Period lock" &&
            row.Status == "Open" &&
            row.Detail.Contains("governed setup", StringComparison.OrdinalIgnoreCase));
        viewModel.CloseTaskRows.Should().ContainSingle(row =>
            row.Name == "task-nav" &&
            row.Status == "ReadyForSignOff" &&
            row.Detail.Contains("sign-offs 0/2", StringComparison.Ordinal) &&
            row.Evidence.Contains("Controller sign-off pending", StringComparison.OrdinalIgnoreCase));
        viewModel.CloseDependencyRows.Should().ContainSingle(row =>
            row.Name == "task-nav" &&
            row.Status == "task-reconciliation" &&
            row.Detail.Contains("Reconciliation must clear", StringComparison.OrdinalIgnoreCase));
        viewModel.CloseSignOffMatrixRows.Should().ContainSingle(row =>
            row.Name == "task-nav:controller" &&
            row.Status == "Open" &&
            row.Detail.Contains("0/2 approval", StringComparison.OrdinalIgnoreCase) &&
            row.Evidence == "Controller NAV sign-off evidence");
        viewModel.CloseLateAdjustmentRows.Should().ContainSingle(row =>
            row.Name == "late-adjustment-1" &&
            row.Status == "Approved" &&
            row.Detail.Contains("1,250.00 USD", StringComparison.Ordinal) &&
            row.Evidence.Contains("No retained review decision", StringComparison.OrdinalIgnoreCase));
        viewModel.CloseEvidenceReviewRows.Should().Contain(row =>
            row.Name == "task:task-nav" &&
            row.Status == "Retained" &&
            row.Evidence == "evidence/nav-package");
        viewModel.CloseEvidenceReviewRows.Should().Contain(row =>
            row.Name == "late-adjustment:late-adjustment-1" &&
            row.Status == "Retained" &&
            row.Evidence == "evidence/late-adjustment");
        viewModel.CloseEvidenceReviewRows.Should().Contain(row =>
            row.Name == "CloseTaskSignOffMissing" &&
            row.Status == "Review required" &&
            row.Detail.Contains("requires retained controller sign-off", StringComparison.OrdinalIgnoreCase));
        viewModel.ClosePeriodLockIssueRows.Should().ContainSingle(row =>
            row.Name == "CloseTaskSignOffMissing" &&
            row.Status == "Critical" &&
            row.Evidence.Contains("Retain controller sign-off", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ConfigureClosePlanCommand_UsesDesktopEditedCloseSetupDraft()
    {
        var workflowId = Guid.Parse("abababab-bbbb-cccc-dddd-eeeeeeeeeeee");
        var ledgerBookId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var closePlan = BuildClosePlan(ledgerBookId);
        var service = new CapturingCloseManagementService(closePlan);
        var viewModel = new AccountingCloseViewModel(Substitute.For<IAccountingProjectionQueryService>(), service);

        viewModel.ApplyClosePlan(workflowId, closePlan);
        viewModel.CloseSetupAmountThreshold = 5_000m;
        viewModel.CloseSetupPercentThreshold = 1.25m;
        viewModel.CloseSetupCurrency = "EUR";
        viewModel.CloseSetupReviewRole = "assistant-controller";
        viewModel.CloseSetupRequiresLateAdjustmentApproval = false;
        viewModel.CloseSetupTaskDisplayName = "Approve final NAV package";
        viewModel.CloseSetupTaskOwner = "nav-controller";
        viewModel.CloseSetupTaskDueDateText = "2026-06-06";
        viewModel.CloseSetupTaskRequiredApprovalCount = 3;
        viewModel.CloseSetupTaskRequiredApprovalRole = "CFO";
        viewModel.CloseSetupTaskRequiredEvidence = "Controller, administrator, and CFO retained evidence";
        viewModel.CloseSetupTaskSignOffRequirementsText = string.Join(
            Environment.NewLine,
            "Controller | 1 | Controller retained evidence",
            "Administrator | 1 | Administrator retained evidence",
            "CFO | 1 | CFO retained evidence");
        viewModel.CloseSetupTaskDependsOnTaskIdsText = "task-reconciliation; task-pricing, task-cash";
        viewModel.CloseSetupTaskDependencyReason = string.Join(
            Environment.NewLine,
            "task-reconciliation: Reconciliation must clear before final NAV approval.",
            "task-pricing: Pricing controls must clear before final NAV approval.",
            "task-cash: Cash controls must clear before final NAV approval.");

        await viewModel.ConfigureClosePlanCommand.ExecuteAsync(null);

        service.Request.Should().NotBeNull();
        service.Request!.MaterialityPolicy.Should().Be(new MaterialityPolicyDto(
            "materiality-alpha",
            5_000m,
            1.25m,
            "EUR",
            "assistant-controller",
            false));
        service.Request.TaskConfigurations.Should().ContainSingle().Which.Should().Match<CloseTaskConfigurationDto>(task =>
            task.TaskId == "task-nav" &&
            task.DisplayName == "Approve final NAV package" &&
            task.Owner == "nav-controller" &&
            task.DueDate == new DateOnly(2026, 6, 6) &&
            task.RequiredApprovalCount == 1 &&
            task.RequiredApprovalRole == "Controller" &&
            task.RequiredEvidence == "Controller retained evidence" &&
            task.DependsOnTaskIds.SequenceEqual(new[] { "task-reconciliation", "task-pricing", "task-cash" }));
        service.Request.TaskConfigurations.Single().SignOffRequirementConfigurations.Should().BeEquivalentTo(
            [
                new CloseTaskSignOffRequirementConfigurationDto("Controller", 1, "Controller retained evidence"),
                new CloseTaskSignOffRequirementConfigurationDto("Administrator", 1, "Administrator retained evidence"),
                new CloseTaskSignOffRequirementConfigurationDto("CFO", 1, "CFO retained evidence")
            ],
            options => options.WithStrictOrdering());
        service.Request.TaskConfigurations.Single().DependencyConfigurations.Should().BeEquivalentTo(
            [
                new CloseTaskDependencyConfigurationDto("task-reconciliation", "Reconciliation must clear before final NAV approval."),
                new CloseTaskDependencyConfigurationDto("task-pricing", "Pricing controls must clear before final NAV approval."),
                new CloseTaskDependencyConfigurationDto("task-cash", "Cash controls must clear before final NAV approval.")
            ],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task ConfigureClosePlanCommand_SelectsRetainedTaskBeforeDesktopSetupEdits()
    {
        var workflowId = Guid.Parse("abababab-bbbb-cccc-dddd-eeeeeeeeeeee");
        var ledgerBookId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var closePlan = BuildClosePlanWithReportTask(ledgerBookId);
        var service = new CapturingCloseManagementService(closePlan);
        var viewModel = new AccountingCloseViewModel(Substitute.For<IAccountingProjectionQueryService>(), service);

        viewModel.ApplyClosePlan(workflowId, closePlan);

        viewModel.CloseSetupTaskOptions.Should().HaveCount(2);
        viewModel.SelectedCloseSetupTaskId.Should().Be("task-nav");

        viewModel.SelectedCloseSetupTaskId = "task-report";

        viewModel.CloseSetupTaskId.Should().Be("task-report");
        viewModel.CloseSetupTaskDisplayName.Should().Be("Publish close package");
        viewModel.CloseSetupTaskOwner.Should().Be("investor-ops");
        viewModel.CloseSetupTaskRequiredApprovalRole.Should().Be("CFO");
        viewModel.CloseSetupTaskRequiredApprovalCount.Should().Be(2);
        viewModel.CloseSetupTaskSignOffRequirementsText.Should().Be("CFO | 2 | CFO close package sign-off evidence");
        viewModel.CloseSetupTaskDependsOnTaskIdsText.Should().Be("task-nav");

        viewModel.CloseSetupTaskOwner = "close-ops";
        viewModel.CloseSetupTaskRequiredApprovalRole = "controller";
        viewModel.CloseSetupTaskRequiredEvidence = "Controller retained close package evidence";
        viewModel.CloseSetupTaskSignOffRequirementsText = string.Join(
            Environment.NewLine,
            "controller | 2 | Controller retained close package evidence",
            "CFO | 1 | CFO retained close package evidence");
        viewModel.CloseSetupTaskDependsOnTaskIdsText = "task-nav, task-cash";
        viewModel.CloseSetupTaskDependencyReason = "NAV and cash controls must clear before report publication.";

        await viewModel.ConfigureClosePlanCommand.ExecuteAsync(null);

        service.Request.Should().NotBeNull();
        service.Request!.TaskConfigurations.Should().HaveCount(2);
        service.Request.TaskConfigurations.Single(task => task.TaskId == "task-nav").Should().Match<CloseTaskConfigurationDto>(task =>
            task.DisplayName == "Finalize NAV package" &&
            task.Owner == "fund-accounting" &&
            task.RequiredApprovalRole == "controller" &&
            task.DependsOnTaskIds.SequenceEqual(new[] { "task-reconciliation" }));
        service.Request.TaskConfigurations.Single(task => task.TaskId == "task-nav").SignOffRequirementConfigurations.Should().BeEquivalentTo(
            [
                new CloseTaskSignOffRequirementConfigurationDto("controller", 2, "Controller NAV sign-off evidence")
            ],
            options => options.WithStrictOrdering());
        service.Request.TaskConfigurations.Single(task => task.TaskId == "task-report").Should().Match<CloseTaskConfigurationDto>(task =>
            task.DisplayName == "Publish close package" &&
            task.Owner == "close-ops" &&
            task.RequiredApprovalRole == "controller" &&
            task.RequiredApprovalCount == 2 &&
            task.RequiredEvidence == "Controller retained close package evidence" &&
            task.DependsOnTaskIds.SequenceEqual(new[] { "task-nav", "task-cash" }));
        service.Request.TaskConfigurations.Single(task => task.TaskId == "task-report").SignOffRequirementConfigurations.Should().BeEquivalentTo(
            [
                new CloseTaskSignOffRequirementConfigurationDto("controller", 2, "Controller retained close package evidence"),
                new CloseTaskSignOffRequirementConfigurationDto("CFO", 1, "CFO retained close package evidence")
            ],
            options => options.WithStrictOrdering());
        service.Request.TaskConfigurations.Single(task => task.TaskId == "task-report").DependencyConfigurations.Should().BeEquivalentTo(
            [
                new CloseTaskDependencyConfigurationDto("task-nav", "NAV and cash controls must clear before report publication."),
                new CloseTaskDependencyConfigurationDto("task-cash", "NAV and cash controls must clear before report publication.")
            ],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task ConfigureClosePlanCommand_BlocksInvalidDesktopMaterialitySetup()
    {
        var workflowId = Guid.Parse("abababab-bbbb-cccc-dddd-eeeeeeeeeeee");
        var closePlan = BuildClosePlan(Guid.Parse("11111111-2222-3333-4444-555555555555"));
        var service = new CapturingCloseManagementService(closePlan);
        var viewModel = new AccountingCloseViewModel(Substitute.For<IAccountingProjectionQueryService>(), service);

        viewModel.ApplyClosePlan(workflowId, closePlan);

        viewModel.CloseSetupAmountThreshold = -1m;

        viewModel.ConfigureClosePlanCommand.CanExecute(null).Should().BeFalse();
        viewModel.ClosePlanSetupStatusText.Should().Be("Enter a non-negative materiality amount threshold before retaining close setup.");

        await viewModel.ConfigureClosePlanCommand.ExecuteAsync(null);

        service.Request.Should().BeNull();

        viewModel.CloseSetupAmountThreshold = 2_500m;
        viewModel.CloseSetupPercentThreshold = -0.1m;
        viewModel.ClosePlanSetupStatusText.Should().Be("Enter a non-negative materiality percent threshold before retaining close setup.");

        viewModel.CloseSetupPercentThreshold = 0.5m;
        viewModel.CloseSetupCurrency = "US";
        viewModel.ClosePlanSetupStatusText.Should().Be("Enter a three-letter materiality currency before retaining close setup.");

        viewModel.CloseSetupCurrency = "USD";
        viewModel.CloseSetupReviewRole = "";
        viewModel.ClosePlanSetupStatusText.Should().Be("Enter a materiality review role before retaining close setup.");
        viewModel.ConfigureClosePlanCommand.CanExecute(null).Should().BeFalse();
        service.Request.Should().BeNull();
    }

    [Fact]
    public async Task ConfigureClosePlanCommand_BlocksInvalidDesktopTaskAndSignOffSetup()
    {
        var workflowId = Guid.Parse("abababab-bbbb-cccc-dddd-eeeeeeeeeeee");
        var closePlan = BuildClosePlan(Guid.Parse("11111111-2222-3333-4444-555555555555"));
        var service = new CapturingCloseManagementService(closePlan);
        var viewModel = new AccountingCloseViewModel(Substitute.For<IAccountingProjectionQueryService>(), service);

        viewModel.ApplyClosePlan(workflowId, closePlan);

        viewModel.CloseSetupTaskId = "";

        viewModel.ConfigureClosePlanCommand.CanExecute(null).Should().BeFalse();
        viewModel.ClosePlanSetupStatusText.Should().Be("Select a retained close checklist task before retaining close setup.");

        await viewModel.ConfigureClosePlanCommand.ExecuteAsync(null);

        service.Request.Should().BeNull();

        viewModel.CloseSetupTaskId = "task-missing";
        viewModel.ClosePlanSetupStatusText.Should().Be("Close checklist task task-missing is not loaded in this close plan.");

        viewModel.CloseSetupTaskId = "task-nav";
        viewModel.CloseSetupTaskDueDateText = "06/04/2026";
        viewModel.ClosePlanSetupStatusText.Should().Be("Close task due date must use yyyy-MM-dd format before retaining close setup.");

        viewModel.CloseSetupTaskDueDateText = "2026-06-04";
        viewModel.CloseSetupTaskRequiredApprovalCount = 0;
        viewModel.ClosePlanSetupStatusText.Should().Be("Enter a positive required approval count before retaining close setup.");

        viewModel.CloseSetupTaskRequiredApprovalCount = 2;
        viewModel.CloseSetupTaskRequiredApprovalRole = "";
        viewModel.ClosePlanSetupStatusText.Should().Be("Enter an approval role before retaining close setup.");

        viewModel.CloseSetupTaskRequiredApprovalRole = "controller";
        viewModel.CloseSetupTaskRequiredEvidence = "";
        viewModel.ClosePlanSetupStatusText.Should().Be("Enter required sign-off evidence before retaining close setup.");
        viewModel.ConfigureClosePlanCommand.CanExecute(null).Should().BeFalse();
        service.Request.Should().BeNull();
    }

    [Fact]
    public async Task SignOffCloseTaskCommand_RetainsGovernedTaskSignOffEvidence()
    {
        var workflowId = Guid.Parse("dddddddd-eeee-ffff-aaaa-bbbbbbbbbbbb");
        var ledgerBookId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var closePlan = BuildClosePlan(ledgerBookId);
        var signedPlan = closePlan with
        {
            Tasks =
            [
                closePlan.Tasks[0] with
                {
                    Status = CloseTaskStatusDto.SignedOff,
                    SignOffs =
                    [
                        new CloseSignOffDto(
                            "signoff-task-nav-controller",
                            "controller",
                            "wpf-accounting-controller",
                            ManualJournalEntryStatusDto.Approved,
                            DateTimeOffset.Parse("2026-06-04T15:00:00Z"),
                            ["evidence/nav-package-signoff"],
                            "Controller retained NAV package sign-off.")
                    ],
                    SignOffRequirements =
                    [
                        closePlan.Tasks[0].SignOffRequirements[0] with
                        {
                            ApprovedCount = 2,
                            IsSatisfied = true
                        }
                    ]
                }
            ]
        };
        var service = new CapturingCloseManagementService(closePlan)
        {
            SignOffResult = signedPlan
        };
        var viewModel = new AccountingCloseViewModel(Substitute.For<IAccountingProjectionQueryService>(), service);

        viewModel.ApplyClosePlan(workflowId, 7, closePlan);

        viewModel.SignOffCloseTaskCommand.CanExecute(null).Should().BeTrue();

        await viewModel.SignOffCloseTaskCommand.ExecuteAsync(null);

        service.SignOffActor.Should().Be("wpf-accounting-controller");
        service.SignOffRequest.Should().NotBeNull();
        service.SignOffRequest!.WorkflowId.Should().Be(workflowId);
        service.SignOffRequest.TaskId.Should().Be("task-nav");
        service.SignOffRequest.Role.Should().Be("controller");
        service.SignOffRequest.Decision.Should().Be(ManualJournalEntryStatusDto.Approved);
        service.SignOffRequest.Actor.Should().Be("wpf-accounting-controller");
        service.SignOffRequest.ActionOrigin.Should().Be(OperationsActionOriginDto.HumanOperator);
        service.SignOffRequest.CorrelationId.Should().Be($"wpf-close-task-signoff-{workflowId:D}-task-nav-controller");
        service.SignOffRequest.EvidenceLinks.Should().Contain(link =>
            link.Contains(workflowId.ToString("D"), StringComparison.OrdinalIgnoreCase) &&
            link.Contains("task-nav", StringComparison.OrdinalIgnoreCase) &&
            link.Contains("controller", StringComparison.OrdinalIgnoreCase) &&
            link.Contains("2026-05", StringComparison.OrdinalIgnoreCase));
        service.SignOffRequest.EvidenceLinks.Should().Contain(link =>
            link.Contains($"book/{ledgerBookId:D}", StringComparison.OrdinalIgnoreCase) &&
            link.Contains("task-nav", StringComparison.OrdinalIgnoreCase));
        service.SignOffRequest.EvidenceLinks.Should().Contain("evidence/nav-package");
        viewModel.CloseTaskSignOffStatusText.Should().Be("Retained controller sign-off evidence for close task task-nav.");
        viewModel.SignOffCloseTaskCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task SignOffCloseTaskCommand_RetainsConfiguredRejectedMatrixDecision()
    {
        var workflowId = Guid.Parse("dddddddd-eeee-ffff-aaaa-bbbbbbbbbbbb");
        var ledgerBookId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var closePlan = BuildClosePlan(ledgerBookId);
        var service = new CapturingCloseManagementService(closePlan);
        var viewModel = new AccountingCloseViewModel(Substitute.For<IAccountingProjectionQueryService>(), service);

        viewModel.ApplyClosePlan(workflowId, 7, closePlan);
        viewModel.CloseTaskSignOffTaskId.Should().Be("task-nav");
        viewModel.CloseTaskSignOffRole.Should().Be("controller");

        viewModel.CloseTaskSignOffDecision = ManualJournalEntryStatusDto.Rejected.ToString();
        viewModel.CloseTaskSignOffNotes = "Controller rejected NAV package until pricing support is remediated.";

        viewModel.SignOffCloseTaskCommand.CanExecute(null).Should().BeTrue();

        await viewModel.SignOffCloseTaskCommand.ExecuteAsync(null);

        service.SignOffRequest.Should().NotBeNull();
        service.SignOffRequest!.WorkflowId.Should().Be(workflowId);
        service.SignOffRequest.TaskId.Should().Be("task-nav");
        service.SignOffRequest.Role.Should().Be("controller");
        service.SignOffRequest.Decision.Should().Be(ManualJournalEntryStatusDto.Rejected);
        service.SignOffRequest.Notes.Should().Be("Controller rejected NAV package until pricing support is remediated.");
        service.SignOffRequest.EvidenceLinks.Should().Contain(link =>
            link.Contains(workflowId.ToString("D"), StringComparison.OrdinalIgnoreCase) &&
            link.Contains("task-nav", StringComparison.OrdinalIgnoreCase) &&
            link.Contains("controller", StringComparison.OrdinalIgnoreCase));
        viewModel.CloseTaskSignOffStatusText.Should().Be("Retained controller rejection evidence for close task task-nav.");
    }

    [Fact]
    public async Task SignOffCloseTaskCommand_BlocksInvalidDesktopMatrixDraft()
    {
        var workflowId = Guid.Parse("dddddddd-eeee-ffff-aaaa-bbbbbbbbbbbb");
        var closePlan = BuildClosePlan(Guid.Parse("11111111-2222-3333-4444-555555555555"));
        var service = new CapturingCloseManagementService(closePlan);
        var viewModel = new AccountingCloseViewModel(Substitute.For<IAccountingProjectionQueryService>(), service);

        viewModel.ApplyClosePlan(workflowId, closePlan);

        viewModel.CloseTaskSignOffTaskId = "";

        viewModel.SignOffCloseTaskCommand.CanExecute(null).Should().BeFalse();
        viewModel.CloseTaskSignOffStatusText.Should().Be("Select a retained close checklist task before retaining sign-off evidence.");

        await viewModel.SignOffCloseTaskCommand.ExecuteAsync(null);

        service.SignOffRequest.Should().BeNull();

        viewModel.CloseTaskSignOffTaskId = "task-missing";
        viewModel.CloseTaskSignOffStatusText.Should().Be("Close checklist task task-missing is not loaded in this close plan.");

        viewModel.CloseTaskSignOffTaskId = "task-nav";
        viewModel.CloseTaskSignOffRole = "CFO";
        viewModel.CloseTaskSignOffStatusText.Should().Be("Close checklist task task-nav does not allow sign-off role CFO.");

        viewModel.CloseTaskSignOffRole = "controller";
        viewModel.CloseTaskSignOffDecision = "Submitted";
        viewModel.CloseTaskSignOffStatusText.Should().Be("Select Approved or Rejected before retaining close task sign-off evidence.");
        viewModel.SignOffCloseTaskCommand.CanExecute(null).Should().BeFalse();
        service.SignOffRequest.Should().BeNull();
    }

    [Fact]
    public async Task RequestLateAdjustmentCommand_RetainsGovernedRequestEvidence()
    {
        var workflowId = Guid.Parse("ffffffff-aaaa-bbbb-cccc-dddddddddddd");
        var ledgerBookId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var journalEntryId = Guid.Parse("22222222-3333-4444-5555-666666666666");
        var closePlan = BuildClosePlan(ledgerBookId);
        var service = new CapturingCloseManagementService(closePlan);
        var viewModel = new AccountingCloseViewModel(Substitute.For<IAccountingProjectionQueryService>(), service);

        viewModel.ApplyClosePlan(workflowId, 7, closePlan);
        viewModel.LateAdjustmentJournalEntryIdText = journalEntryId.ToString("D");
        viewModel.LateAdjustmentAmountText = "12500.50";
        viewModel.LateAdjustmentCurrency = "usd";
        viewModel.LateAdjustmentReason = "Administrator NAV support arrived after close review.";

        viewModel.RequestLateAdjustmentCommand.CanExecute(null).Should().BeTrue();

        await viewModel.RequestLateAdjustmentCommand.ExecuteAsync(null);

        service.LateAdjustmentActor.Should().Be("wpf-accounting-controller");
        service.LateAdjustmentRequest.Should().NotBeNull();
        service.LateAdjustmentRequest!.WorkflowId.Should().Be(workflowId);
        service.LateAdjustmentRequest.JournalEntryId.Should().Be(journalEntryId);
        service.LateAdjustmentRequest.Amount.Should().Be(12_500.50m);
        service.LateAdjustmentRequest.Currency.Should().Be("USD");
        service.LateAdjustmentRequest.Reason.Should().Be("Administrator NAV support arrived after close review.");
        service.LateAdjustmentRequest.RequestedBy.Should().Be("wpf-accounting-controller");
        service.LateAdjustmentRequest.ActionOrigin.Should().Be(OperationsActionOriginDto.HumanOperator);
        service.LateAdjustmentRequest.CorrelationId.Should().Be($"wpf-late-adjustment-request-{workflowId:D}-{journalEntryId:D}");
        service.LateAdjustmentRequest.EvidenceLinks.Should().Contain(link =>
            link.Contains("late-adjustment", StringComparison.OrdinalIgnoreCase) &&
            link.Contains(workflowId.ToString("D"), StringComparison.OrdinalIgnoreCase) &&
            link.Contains(journalEntryId.ToString("D"), StringComparison.OrdinalIgnoreCase));
        service.LateAdjustmentRequest.EvidenceLinks.Should().Contain(link =>
            link.Contains($"book/{ledgerBookId:D}", StringComparison.OrdinalIgnoreCase) &&
            link.Contains("2026-05", StringComparison.OrdinalIgnoreCase));
        viewModel.LateAdjustmentRequestStatusText.Should().Be($"Requested retained late adjustment for journal {journalEntryId:D}.");
    }

    [Fact]
    public async Task RequestLateAdjustmentCommand_BlocksInvalidDesktopDraft()
    {
        var workflowId = Guid.Parse("ffffffff-aaaa-bbbb-cccc-dddddddddddd");
        var closePlan = BuildClosePlan(Guid.Parse("11111111-2222-3333-4444-555555555555"));
        var service = new CapturingCloseManagementService(closePlan);
        var viewModel = new AccountingCloseViewModel(Substitute.For<IAccountingProjectionQueryService>(), service);

        viewModel.ApplyClosePlan(workflowId, closePlan);

        viewModel.RequestLateAdjustmentCommand.CanExecute(null).Should().BeFalse();
        viewModel.LateAdjustmentRequestStatusText.Should().Be("Enter a journal entry id before requesting a late adjustment.");

        await viewModel.RequestLateAdjustmentCommand.ExecuteAsync(null);

        service.LateAdjustmentRequest.Should().BeNull();

        viewModel.LateAdjustmentJournalEntryIdText = Guid.Parse("22222222-3333-4444-5555-666666666666").ToString("D");
        viewModel.LateAdjustmentAmountText = "0";
        viewModel.LateAdjustmentRequestStatusText.Should().Be("Enter a non-zero late adjustment amount before retaining the request.");

        viewModel.LateAdjustmentAmountText = "1000";
        viewModel.LateAdjustmentCurrency = "US";
        viewModel.LateAdjustmentRequestStatusText.Should().Be("Enter a three-letter late adjustment currency before retaining the request.");

        viewModel.LateAdjustmentCurrency = "USD";
        viewModel.LateAdjustmentReason = "";
        viewModel.LateAdjustmentRequestStatusText.Should().Be("Enter a late adjustment reason before retaining the request.");
        viewModel.RequestLateAdjustmentCommand.CanExecute(null).Should().BeFalse();
        service.LateAdjustmentRequest.Should().BeNull();
    }

    [Fact]
    public async Task ReviewLateAdjustmentCommand_RetainsGovernedReviewEvidence()
    {
        var workflowId = Guid.Parse("eeeeeeee-ffff-aaaa-bbbb-cccccccccccc");
        var ledgerBookId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var closePlan = BuildClosePlan(ledgerBookId, lateAdjustmentSubmitted: true);
        var reviewedPlan = closePlan with
        {
            LateAdjustments =
            [
                closePlan.LateAdjustments[0] with
                {
                    ApprovalState = ManualJournalEntryStatusDto.Approved,
                    DecidedBy = "wpf-accounting-controller",
                    DecidedAtUtc = DateTimeOffset.Parse("2026-06-04T18:00:00Z"),
                    DecisionNotes = "WPF Accounting Close approved late adjustment late-adjustment-1."
                }
            ]
        };
        var service = new CapturingCloseManagementService(closePlan)
        {
            ReviewResult = reviewedPlan
        };
        var viewModel = new AccountingCloseViewModel(Substitute.For<IAccountingProjectionQueryService>(), service);

        viewModel.ApplyClosePlan(workflowId, 7, closePlan);

        viewModel.LateAdjustmentReviewRequestId.Should().Be("late-adjustment-1");
        viewModel.LateAdjustmentReviewDecision.Should().Be(ManualJournalEntryStatusDto.Approved.ToString());
        viewModel.LateAdjustmentReviewNotes.Should().Contain("approved late adjustment late-adjustment-1");
        viewModel.ReviewLateAdjustmentCommand.CanExecute(null).Should().BeTrue();

        await viewModel.ReviewLateAdjustmentCommand.ExecuteAsync(null);

        service.ReviewActor.Should().Be("wpf-accounting-controller");
        service.ReviewRequest.Should().NotBeNull();
        service.ReviewRequest!.WorkflowId.Should().Be(workflowId);
        service.ReviewRequest.RequestId.Should().Be("late-adjustment-1");
        service.ReviewRequest.Decision.Should().Be(ManualJournalEntryStatusDto.Approved);
        service.ReviewRequest.Actor.Should().Be("wpf-accounting-controller");
        service.ReviewRequest.ActionOrigin.Should().Be(OperationsActionOriginDto.HumanOperator);
        service.ReviewRequest.CorrelationId.Should().Be($"wpf-late-adjustment-review-{workflowId:D}-late-adjustment-1-approved");
        service.ReviewRequest.EvidenceLinks.Should().Contain(link =>
            link.Contains(workflowId.ToString("D"), StringComparison.OrdinalIgnoreCase) &&
            link.Contains("late-adjustment-1", StringComparison.OrdinalIgnoreCase) &&
            link.Contains("2026-05", StringComparison.OrdinalIgnoreCase) &&
            link.Contains("approval", StringComparison.OrdinalIgnoreCase));
        service.ReviewRequest.EvidenceLinks.Should().Contain(link =>
            link.Contains($"book/{ledgerBookId:D}", StringComparison.OrdinalIgnoreCase) &&
            link.Contains("late-adjustment-1", StringComparison.OrdinalIgnoreCase));
        service.ReviewRequest.EvidenceLinks.Should().Contain("evidence/late-adjustment");
        viewModel.LateAdjustmentReviewStatusText.Should().Be("Approved late adjustment late-adjustment-1 with retained WPF review evidence.");
        viewModel.ReviewLateAdjustmentCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task ReviewLateAdjustmentCommand_UsesSelectedDesktopDecisionAndNotes()
    {
        var workflowId = Guid.Parse("edededed-aaaa-bbbb-cccc-dddddddddddd");
        var ledgerBookId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var closePlan = BuildClosePlan(ledgerBookId, lateAdjustmentSubmitted: true) with
        {
            LateAdjustments =
            [
                BuildClosePlan(ledgerBookId, lateAdjustmentSubmitted: true).LateAdjustments[0],
                BuildClosePlan(ledgerBookId, lateAdjustmentSubmitted: true).LateAdjustments[0] with
                {
                    RequestId = "late-adjustment-2",
                    JournalEntryId = Guid.Parse("11111111-3333-4444-5555-666666666666"),
                    Reason = "Custodian corrected capital activity after first review."
                }
            ]
        };
        var service = new CapturingCloseManagementService(closePlan);
        var viewModel = new AccountingCloseViewModel(Substitute.For<IAccountingProjectionQueryService>(), service);

        viewModel.ApplyClosePlan(workflowId, 7, closePlan);
        viewModel.LateAdjustmentReviewRequestId = "late-adjustment-2";
        viewModel.LateAdjustmentReviewDecision = ManualJournalEntryStatusDto.Rejected.ToString();
        viewModel.LateAdjustmentReviewNotes = "Rejected after administrator support failed tie-out.";

        viewModel.ReviewLateAdjustmentCommand.CanExecute(null).Should().BeTrue();

        await viewModel.ReviewLateAdjustmentCommand.ExecuteAsync(null);

        service.ReviewRequest.Should().NotBeNull();
        service.ReviewRequest!.RequestId.Should().Be("late-adjustment-2");
        service.ReviewRequest.Decision.Should().Be(ManualJournalEntryStatusDto.Rejected);
        service.ReviewRequest.Notes.Should().Be("Rejected after administrator support failed tie-out.");
        service.ReviewRequest.CorrelationId.Should().Be($"wpf-late-adjustment-review-{workflowId:D}-late-adjustment-2-rejected");
        service.ReviewRequest.EvidenceLinks.Should().Contain(link =>
            link.Contains("late-adjustment-2", StringComparison.OrdinalIgnoreCase) &&
            link.Contains("rejection", StringComparison.OrdinalIgnoreCase));
        viewModel.LateAdjustmentReviewStatusText.Should().Be("Rejected late adjustment late-adjustment-2 with retained WPF review evidence.");
    }

    [Fact]
    public async Task ReviewCloseEvidenceCommand_RetainsActiveBlockerReviewWithoutClearingValidation()
    {
        var workflowId = Guid.Parse("dededede-1111-2222-3333-444444444444");
        var ledgerBookId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var closePlan = BuildClosePlan(ledgerBookId) with
        {
            ValidationIssues =
            [
                new AccountingConfigurationValidationIssueDto(
                    "CloseTaskSignOffMissing",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    "Close task 'Finalize NAV package' requires retained controller sign-off.",
                    "task-nav",
                    "Retain controller sign-off evidence before locking the period.")
            ]
        };
        var reviewedPlan = closePlan with
        {
            EvidenceReviews =
            [
                new CloseEvidenceReviewDto(
                    "close-review-close-task-signoff-missing-task-nav",
                    "CloseTaskSignOffMissing",
                    "task-nav",
                    "wpf-accounting-controller",
                    DateTimeOffset.Parse("2026-06-04T16:15:00Z"),
                    "WPF Accounting Close reviewed blocker CloseTaskSignOffMissing for task-nav.",
                    [
                        $"wpf://accounting/close/evidence-review/{workflowId:D}/CloseTaskSignOffMissing/task-nav",
                        $"evidence://close-review/workflow/{workflowId:D}/period/2026-05/book/{ledgerBookId:D}/issue/CloseTaskSignOffMissing/target/task-nav"
                    ])
            ]
        };
        var service = new CapturingCloseManagementService(closePlan)
        {
            EvidenceReviewResult = reviewedPlan
        };
        var viewModel = new AccountingCloseViewModel(Substitute.For<IAccountingProjectionQueryService>(), service);

        viewModel.ApplyClosePlan(workflowId, 7, closePlan);

        viewModel.CloseEvidenceReviewIssueCode.Should().Be("CloseTaskSignOffMissing");
        viewModel.CloseEvidenceReviewTargetId.Should().Be("task-nav");
        viewModel.CloseEvidenceReviewNotes.Should().Contain("reviewed blocker CloseTaskSignOffMissing");
        viewModel.ReviewCloseEvidenceCommand.CanExecute(null).Should().BeTrue();

        await viewModel.ReviewCloseEvidenceCommand.ExecuteAsync(null);

        service.EvidenceReviewActor.Should().Be("wpf-accounting-controller");
        service.EvidenceReviewRequest.Should().NotBeNull();
        service.EvidenceReviewRequest!.WorkflowId.Should().Be(workflowId);
        service.EvidenceReviewRequest.IssueCode.Should().Be("CloseTaskSignOffMissing");
        service.EvidenceReviewRequest.TargetId.Should().Be("task-nav");
        service.EvidenceReviewRequest.Actor.Should().Be("wpf-accounting-controller");
        service.EvidenceReviewRequest.ActionOrigin.Should().Be(OperationsActionOriginDto.HumanOperator);
        service.EvidenceReviewRequest.CorrelationId.Should().Be($"wpf-close-evidence-review-{workflowId:D}-closetasksignoffmissing-task-nav");
        service.EvidenceReviewRequest.Notes.Should().Contain("reviewed blocker CloseTaskSignOffMissing for task-nav");
        service.EvidenceReviewRequest.EvidenceLinks.Should().Contain(link =>
            link.Contains(workflowId.ToString("D"), StringComparison.OrdinalIgnoreCase) &&
            link.Contains("CloseTaskSignOffMissing", StringComparison.OrdinalIgnoreCase) &&
            link.Contains("task-nav", StringComparison.OrdinalIgnoreCase));
        service.EvidenceReviewRequest.EvidenceLinks.Should().Contain(link =>
            link.Contains($"book/{ledgerBookId:D}", StringComparison.OrdinalIgnoreCase) &&
            link.Contains("2026-05", StringComparison.OrdinalIgnoreCase));
        viewModel.CloseEvidenceReviewStatusText.Should().Be("Retained WPF evidence review for blocker CloseTaskSignOffMissing.");
        viewModel.ReviewCloseEvidenceCommand.CanExecute(null).Should().BeFalse();
        viewModel.ClosePeriodLockIssueRows.Should().ContainSingle(row =>
            row.Name == "CloseTaskSignOffMissing" &&
            row.Status == "Critical");
        viewModel.CloseEvidenceReviewRows.Should().Contain(row =>
            row.Name == "CloseTaskSignOffMissing" &&
            row.Status == "Review retained" &&
            row.Detail.Contains("wpf-accounting-controller reviewed CloseTaskSignOffMissing", StringComparison.OrdinalIgnoreCase) &&
            row.Evidence.Contains("close-review", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReviewCloseEvidenceCommand_UsesSelectedDesktopBlockerAndNotes()
    {
        var workflowId = Guid.Parse("cdcdcdcd-1111-2222-3333-444444444444");
        var ledgerBookId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var closePlan = BuildClosePlan(ledgerBookId) with
        {
            ValidationIssues =
            [
                new AccountingConfigurationValidationIssueDto(
                    "CloseTaskSignOffMissing",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    "Close task 'Finalize NAV package' requires retained controller sign-off.",
                    "task-nav",
                    "Retain controller sign-off evidence before locking the period."),
                new AccountingConfigurationValidationIssueDto(
                    "LateAdjustmentRequiresApproval",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    "Late adjustment late-adjustment-1 requires controller review.",
                    "late-adjustment-1",
                    "Approve or reject the retained late adjustment before locking the period.")
            ]
        };
        var service = new CapturingCloseManagementService(closePlan);
        var viewModel = new AccountingCloseViewModel(Substitute.For<IAccountingProjectionQueryService>(), service);

        viewModel.ApplyClosePlan(workflowId, 7, closePlan);
        viewModel.CloseEvidenceReviewIssueCode = "LateAdjustmentRequiresApproval";
        viewModel.CloseEvidenceReviewTargetId = "late-adjustment-1";
        viewModel.CloseEvidenceReviewNotes = "Reviewed late-adjustment blocker after operator evidence packet was retained.";

        viewModel.ReviewCloseEvidenceCommand.CanExecute(null).Should().BeTrue();

        await viewModel.ReviewCloseEvidenceCommand.ExecuteAsync(null);

        service.EvidenceReviewRequest.Should().NotBeNull();
        service.EvidenceReviewRequest!.IssueCode.Should().Be("LateAdjustmentRequiresApproval");
        service.EvidenceReviewRequest.TargetId.Should().Be("late-adjustment-1");
        service.EvidenceReviewRequest.Notes.Should().Be("Reviewed late-adjustment blocker after operator evidence packet was retained.");
        service.EvidenceReviewRequest.CorrelationId.Should().Be($"wpf-close-evidence-review-{workflowId:D}-lateadjustmentrequiresapproval-late-adjustment-1");
        service.EvidenceReviewRequest.EvidenceLinks.Should().Contain(link =>
            link.Contains("LateAdjustmentRequiresApproval", StringComparison.OrdinalIgnoreCase) &&
            link.Contains("late-adjustment-1", StringComparison.OrdinalIgnoreCase));
        viewModel.CloseEvidenceReviewStatusText.Should().Be("Retained WPF evidence review for blocker LateAdjustmentRequiresApproval.");
    }

    [Fact]
    public async Task LockClosePeriodCommand_BuildsGovernedRequestAndRendersSharedBlockers()
    {
        var workflowId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
        var ledgerBookId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var closePlan = BuildClosePlan(ledgerBookId);
        var service = new CapturingCloseManagementService(closePlan)
        {
            LockResult = new ClosePeriodLockResultDto(
                false,
                closePlan,
                null,
                [
                    new AccountingConfigurationValidationIssueDto(
                        "CloseTaskSignOffMissing",
                        AccountingConfigurationValidationSeverityDto.Critical,
                        "Close task 'Finalize NAV package' requires retained controller sign-off.",
                        "task-nav",
                        "Retain controller sign-off evidence before locking the period.")
                ])
        };
        var viewModel = new AccountingCloseViewModel(Substitute.For<IAccountingProjectionQueryService>(), service);

        viewModel.ApplyClosePlan(workflowId, 7, closePlan);

        viewModel.LockClosePeriodCommand.CanExecute(null).Should().BeTrue();

        await viewModel.LockClosePeriodCommand.ExecuteAsync(null);

        service.LockActor.Should().Be("wpf-accounting-controller");
        service.LockRequest.Should().NotBeNull();
        service.LockRequest!.WorkflowId.Should().Be(workflowId);
        service.LockRequest.ExpectedWorkflowVersion.Should().Be(7);
        service.LockRequest.Actor.Should().Be("wpf-accounting-controller");
        service.LockRequest.ActionOrigin.Should().Be(OperationsActionOriginDto.HumanOperator);
        service.LockRequest.ReportPackId.Should().Be("report-pack-fund-alpha-2026-05");
        service.LockRequest.CorrelationId.Should().Be($"wpf-close-period-lock-{workflowId:D}");
        service.LockRequest.ClosePackageId.Should().Be("close-package-fund-alpha-2026-05");
        service.LockRequest.ClosePackageManifestId.Should().Be("manifest-fund-alpha-2026-05");
        service.LockRequest.ClosePackageRetainedManifestRoute.Should().Be("/workstation/reporting/packages/manifest-fund-alpha-2026-05");
        service.LockRequest.EvidenceLinks.Should().Contain(link =>
            link.Contains(workflowId.ToString("D"), StringComparison.OrdinalIgnoreCase) &&
            link.Contains("2026-05", StringComparison.OrdinalIgnoreCase) &&
            link.Contains("period-lock", StringComparison.OrdinalIgnoreCase));
        service.LockRequest.EvidenceLinks.Should().Contain(link =>
            link.Contains($"book/{ledgerBookId:D}", StringComparison.OrdinalIgnoreCase) &&
            link.Contains("period-lock", StringComparison.OrdinalIgnoreCase));
        viewModel.ClosePeriodLockStatusText.Should().Be("Close period lock is blocked by 1 issue(s).");
        viewModel.ClosePeriodLockIssueRows.Should().ContainSingle(row =>
            row.Name == "CloseTaskSignOffMissing" &&
            row.Status == "Critical" &&
            row.Detail.Contains("controller sign-off", StringComparison.OrdinalIgnoreCase) &&
            row.Evidence.Contains("Retain controller sign-off", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LockClosePeriodCommand_UpdatesLoadedPlanWhenSharedServiceLocksPeriod()
    {
        var workflowId = Guid.Parse("cccccccc-dddd-eeee-ffff-aaaaaaaaaaaa");
        var ledgerBookId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var closePlan = BuildClosePlan(ledgerBookId, signedOff: true);
        var lockedPlan = closePlan with { IsPeriodLocked = true };
        var service = new CapturingCloseManagementService(closePlan)
        {
            LockResult = new ClosePeriodLockResultDto(
                true,
                lockedPlan,
                new OperationsTransitionResultDto(true, null, null, null, [], [], NewVersion: 8))
        };
        var viewModel = new AccountingCloseViewModel(Substitute.For<IAccountingProjectionQueryService>(), service);

        viewModel.ApplyClosePlan(workflowId, 7, closePlan);

        await viewModel.LockClosePeriodCommand.ExecuteAsync(null);

        service.LockRequest.Should().NotBeNull();
        service.LockRequest!.ChecklistControlApprovals.Should().ContainSingle(approval =>
            approval.TaskId == "task-nav" &&
            approval.ApprovedBy == "controller" &&
            approval.ApprovedAtUtc == DateTimeOffset.Parse("2026-06-04T15:00:00Z"));
        service.LockRequest.EvidenceLinks.Should().Contain("evidence/nav-package-signoff");
        viewModel.ClosePeriodLockStatusText.Should().Be("Locked close period 2026-05 with retained close-package evidence.");
        viewModel.ClosePlanSetupStatusText.Should().Be("Close plan 2026-05 is locked; setup changes require a governed reopen workflow.");
        viewModel.LockClosePeriodCommand.CanExecute(null).Should().BeFalse();
        viewModel.ClosePeriodLockIssueRows.Should().BeEmpty();
    }

    private static ClosePeriodPlanDto BuildClosePlan(
        Guid ledgerBookId,
        bool signedOff = false,
        bool lateAdjustmentSubmitted = false)
    {
        var materialityPolicy = new MaterialityPolicyDto(
            "materiality-alpha",
            2_500m,
            0.5m,
            "USD",
            "controller",
            true);
        var signOffs = signedOff
            ?
            [
                new CloseSignOffDto(
                    "signoff-task-nav-controller",
                    "controller",
                    "controller",
                    ManualJournalEntryStatusDto.Approved,
                    DateTimeOffset.Parse("2026-06-04T15:00:00Z"),
                    ["evidence/nav-package-signoff"],
                    "Controller retained NAV package sign-off.")
            ]
            : Array.Empty<CloseSignOffDto>();
        var task = new CloseTaskDto(
            "task-nav",
            "Finalize NAV package",
            CloseTaskStatusDto.ReadyForSignOff,
            "fund-accounting",
            new DateOnly(2026, 6, 4),
            [
                new CloseDependencyDto(
                    "dependency-recon",
                    "task-reconciliation",
                    "Reconciliation must clear before NAV package sign-off.")
            ],
            signOffs,
            ["evidence/nav-package"],
            "Controller sign-off pending.",
            [
                new CloseSignOffRequirementDto(
                    "requirement-task-nav-controller",
                    "controller",
                    2,
                    0,
                    false,
                    "Controller NAV sign-off evidence")
            ]);
        var adjustment = new LateAdjustmentRequestDto(
            "late-adjustment-1",
            Guid.Parse("99999999-8888-7777-6666-555555555555"),
            "controller",
            DateTimeOffset.Parse("2026-06-02T03:00:00Z"),
            1_250m,
            "USD",
            "Late custodian fee accrual.",
            lateAdjustmentSubmitted ? ManualJournalEntryStatusDto.Submitted : ManualJournalEntryStatusDto.Approved,
            materialityPolicy,
            ["evidence/late-adjustment"]);

        return new ClosePeriodPlanDto(
            "close-plan-alpha-202605",
            "fund-alpha",
            ledgerBookId,
            "2026-05",
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            new DateOnly(2026, 6, 5),
            false,
            [task],
            [adjustment],
            materialityPolicy,
            OperatingCoverage:
            [
                new CloseOperatingCoverageItemDto(
                    "close-plan-setup",
                    "Close plan setup",
                    AccountingReadinessStateDto.ReadyForReview,
                    EvidenceCount: 1,
                    BlockingIssueCount: 0,
                    "Review the retained close-plan configuration before period lock.",
                    ["evidence/close-plan-configuration"]),
                new CloseOperatingCoverageItemDto(
                    "dependency-graph",
                    "Dependency graph",
                    AccountingReadinessStateDto.Blocked,
                    EvidenceCount: 1,
                    BlockingIssueCount: 1,
                    "Complete predecessor close tasks before dependent close work advances.",
                    ["evidence/nav-package"],
                    [
                        new AccountingConfigurationValidationIssueDto(
                            "CloseTaskWaitingOnDependency",
                            AccountingConfigurationValidationSeverityDto.Warning,
                            "NAV package is waiting on reconciliation.",
                            "task-nav",
                            "Complete predecessor close tasks before dependent close work advances.")
                    ]),
                new CloseOperatingCoverageItemDto(
                    "sign-off-matrix",
                    "Sign-off matrix",
                    signedOff ? AccountingReadinessStateDto.ReadyForReview : AccountingReadinessStateDto.Blocked,
                    EvidenceCount: signOffs.Length,
                    BlockingIssueCount: signedOff ? 0 : 1,
                    signedOff
                        ? "Review retained sign-off matrix approvals before report certification."
                        : "Retain required close sign-off approvals with scoped evidence.",
                    signOffs.SelectMany(static signOff => signOff.EvidenceLinks).ToArray()),
                new CloseOperatingCoverageItemDto(
                    "late-adjustments",
                    "Late adjustments",
                    lateAdjustmentSubmitted ? AccountingReadinessStateDto.Blocked : AccountingReadinessStateDto.ReadyForReview,
                    EvidenceCount: adjustment.EvidenceLinks.Count,
                    BlockingIssueCount: lateAdjustmentSubmitted ? 1 : 0,
                    lateAdjustmentSubmitted
                        ? "Approve or reject material late adjustments using the controller review gate."
                        : "Review retained late-adjustment decisions before final close.",
                    adjustment.EvidenceLinks),
                new CloseOperatingCoverageItemDto(
                    "blocker-evidence-review",
                    "Blocker evidence review",
                    AccountingReadinessStateDto.NotStarted,
                    EvidenceCount: 0,
                    BlockingIssueCount: 0,
                    "Retain blocker-review notes when active close issues are investigated."),
                new CloseOperatingCoverageItemDto(
                    "period-lock",
                    "Period lock",
                    AccountingReadinessStateDto.ReadyForReview,
                    EvidenceCount: 0,
                    BlockingIssueCount: 0,
                    "Retain close-package evidence and lock the period.")
            ]);
    }

    private static ClosePeriodPlanDto BuildClosePlanWithReportTask(Guid ledgerBookId)
    {
        var closePlan = BuildClosePlan(ledgerBookId);
        var reportTask = new CloseTaskDto(
            "task-report",
            "Publish close package",
            CloseTaskStatusDto.ReadyForSignOff,
            "investor-ops",
            new DateOnly(2026, 6, 5),
            [
                new CloseDependencyDto(
                    "dependency-report-nav",
                    "task-nav",
                    "NAV package must be finalized before report publication.")
            ],
            [],
            ["evidence/report-package"],
            "CFO sign-off pending.",
            [
                new CloseSignOffRequirementDto(
                    "requirement-task-report-cfo",
                    "CFO",
                    2,
                    0,
                    false,
                    "CFO close package sign-off evidence")
            ]);

        return closePlan with { Tasks = [.. closePlan.Tasks, reportTask] };
    }

    private sealed class CapturingCloseManagementService(ClosePeriodPlanDto closePlan) : IAccountingCloseManagementService
    {
        public UpsertClosePeriodPlanConfigurationRequestDto? Request { get; private set; }
        public SignOffCloseTaskRequestDto? SignOffRequest { get; private set; }
        public CreateLateAdjustmentRequestDto? LateAdjustmentRequest { get; private set; }
        public ReviewLateAdjustmentRequestDto? ReviewRequest { get; private set; }
        public ReviewCloseEvidenceRequestDto? EvidenceReviewRequest { get; private set; }
        public LockClosePeriodRequestDto? LockRequest { get; private set; }
        public string? Actor { get; private set; }
        public string? SignOffActor { get; private set; }
        public string? LateAdjustmentActor { get; private set; }
        public string? ReviewActor { get; private set; }
        public string? EvidenceReviewActor { get; private set; }
        public string? LockActor { get; private set; }
        public ClosePeriodPlanDto? SignOffResult { get; init; }
        public ClosePeriodPlanDto? LateAdjustmentResult { get; init; }
        public ClosePeriodPlanDto? ReviewResult { get; init; }
        public ClosePeriodPlanDto? EvidenceReviewResult { get; init; }
        public ClosePeriodLockResultDto? LockResult { get; init; }

        public Task<ClosePeriodPlanDto?> GetPeriodPlanAsync(Guid workflowId, CancellationToken ct = default)
            => Task.FromResult<ClosePeriodPlanDto?>(closePlan);

        public Task<ClosePeriodPlanDto?> RequestLateAdjustmentAsync(
            CreateLateAdjustmentRequestDto request,
            string actor,
            CancellationToken ct = default)
        {
            LateAdjustmentRequest = request;
            LateAdjustmentActor = actor;
            return Task.FromResult<ClosePeriodPlanDto?>(LateAdjustmentResult ?? closePlan);
        }

        public Task<ClosePeriodPlanDto?> ReviewLateAdjustmentAsync(
            ReviewLateAdjustmentRequestDto request,
            string actor,
            CancellationToken ct = default)
        {
            ReviewRequest = request;
            ReviewActor = actor;
            return Task.FromResult<ClosePeriodPlanDto?>(ReviewResult ?? closePlan);
        }

        public Task<ClosePeriodPlanDto?> SignOffCloseTaskAsync(
            SignOffCloseTaskRequestDto request,
            string actor,
            CancellationToken ct = default)
        {
            SignOffRequest = request;
            SignOffActor = actor;
            return Task.FromResult<ClosePeriodPlanDto?>(SignOffResult ?? closePlan);
        }

        public Task<ClosePeriodPlanDto?> ReviewCloseEvidenceAsync(
            ReviewCloseEvidenceRequestDto request,
            string actor,
            CancellationToken ct = default)
        {
            EvidenceReviewRequest = request;
            EvidenceReviewActor = actor;
            return Task.FromResult<ClosePeriodPlanDto?>(EvidenceReviewResult ?? closePlan);
        }

        public Task<ClosePeriodPlanDto?> ConfigurePeriodPlanAsync(
            UpsertClosePeriodPlanConfigurationRequestDto request,
            string actor,
            CancellationToken ct = default)
        {
            Request = request;
            Actor = actor;
            return Task.FromResult<ClosePeriodPlanDto?>(closePlan);
        }

        public Task<ClosePeriodLockResultDto?> LockClosePeriodAsync(
            LockClosePeriodRequestDto request,
            string actor,
            CancellationToken ct = default)
        {
            LockRequest = request;
            LockActor = actor;
            return Task.FromResult<ClosePeriodLockResultDto?>(LockResult ?? new ClosePeriodLockResultDto(false, closePlan, null));
        }
    }
}

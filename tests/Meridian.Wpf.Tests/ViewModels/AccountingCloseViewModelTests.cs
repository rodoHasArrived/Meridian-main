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
        var closePlan = BuildClosePlan(ledgerBookId);
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
        viewModel.CloseSetupTaskDependsOnTaskIdsText = "task-reconciliation; task-pricing, task-cash";

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
            task.RequiredApprovalCount == 3 &&
            task.RequiredApprovalRole == "CFO" &&
            task.RequiredEvidence == "Controller, administrator, and CFO retained evidence" &&
            task.DependsOnTaskIds.SequenceEqual(new[] { "task-reconciliation", "task-pricing", "task-cash" }));
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

    private static ClosePeriodPlanDto BuildClosePlan(Guid ledgerBookId, bool signedOff = false)
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
            ManualJournalEntryStatusDto.Approved,
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
            materialityPolicy);
    }

    private sealed class CapturingCloseManagementService(ClosePeriodPlanDto closePlan) : IAccountingCloseManagementService
    {
        public UpsertClosePeriodPlanConfigurationRequestDto? Request { get; private set; }
        public SignOffCloseTaskRequestDto? SignOffRequest { get; private set; }
        public LockClosePeriodRequestDto? LockRequest { get; private set; }
        public string? Actor { get; private set; }
        public string? SignOffActor { get; private set; }
        public string? LockActor { get; private set; }
        public ClosePeriodPlanDto? SignOffResult { get; init; }
        public ClosePeriodLockResultDto? LockResult { get; init; }

        public Task<ClosePeriodPlanDto?> GetPeriodPlanAsync(Guid workflowId, CancellationToken ct = default)
            => Task.FromResult<ClosePeriodPlanDto?>(closePlan);

        public Task<ClosePeriodPlanDto?> RequestLateAdjustmentAsync(
            CreateLateAdjustmentRequestDto request,
            string actor,
            CancellationToken ct = default)
            => Task.FromResult<ClosePeriodPlanDto?>(closePlan);

        public Task<ClosePeriodPlanDto?> ReviewLateAdjustmentAsync(
            ReviewLateAdjustmentRequestDto request,
            string actor,
            CancellationToken ct = default)
            => Task.FromResult<ClosePeriodPlanDto?>(closePlan);

        public Task<ClosePeriodPlanDto?> SignOffCloseTaskAsync(
            SignOffCloseTaskRequestDto request,
            string actor,
            CancellationToken ct = default)
        {
            SignOffRequest = request;
            SignOffActor = actor;
            return Task.FromResult<ClosePeriodPlanDto?>(SignOffResult ?? closePlan);
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

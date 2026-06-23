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

    private static ClosePeriodPlanDto BuildClosePlan(Guid ledgerBookId)
    {
        var materialityPolicy = new MaterialityPolicyDto(
            "materiality-alpha",
            2_500m,
            0.5m,
            "USD",
            "controller",
            true);
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
            [],
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
        public string? Actor { get; private set; }

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
            => Task.FromResult<ClosePeriodPlanDto?>(closePlan);

        public Task<ClosePeriodPlanDto?> ConfigurePeriodPlanAsync(
            UpsertClosePeriodPlanConfigurationRequestDto request,
            string actor,
            CancellationToken ct = default)
        {
            Request = request;
            Actor = actor;
            return Task.FromResult<ClosePeriodPlanDto?>(closePlan);
        }
    }
}

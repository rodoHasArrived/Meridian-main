using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class SettingsViewModelOperationsControlTests
{
    [Fact]
    public void RefreshOperationsControlCommand_ShouldLoadApprovalPolicyAndCloseCalendarRows()
    {
        WpfTestThread.Run(async () =>
        {
            var workflowId = Guid.Parse("1a02a8a4-8249-49a2-bdf4-0e41f3c1163e");
            var fundAccountId = Guid.Parse("7a77e2bc-c599-4ca8-83d3-32200e10619b");
            var client = new StubOperationsControlCenterClient
            {
                Policy = new OperationsApprovalPolicyMatrixDto(
                    "operations-approval-policy",
                    "v1",
                    new DateTimeOffset(2026, 6, 10, 18, 0, 0, TimeSpan.Zero),
                    [
                        new OperationsApprovalPolicyMatrixRowDto(
                            "close-lock",
                            "FinancialOperations",
                            "ApproveResults",
                            OperationsGateKeyDto.Approval,
                            "Close lock requested",
                            "financial-operations.approve",
                            "Fund accountant",
                            "Controller",
                            2,
                            true,
                            true,
                            true,
                            "NAV support package and retained report pack",
                            "OperationsCloseLockApproved",
                            "/operations/continuity/close-calendar",
                            "high")
                    ]),
                Calendar = new OperationsCloseCalendarDto(
                    new DateTimeOffset(2026, 6, 10, 19, 0, 0, TimeSpan.Zero),
                    [
                        new OperationsCloseCalendarItemDto(
                            workflowId,
                            fundAccountId,
                            "2026-05",
                            OperationsWorkflowStatusDto.ApprovalPending,
                            7,
                            new DateOnly(2026, 6, 12),
                            "nav-review",
                            "Review NAV package",
                            "controller",
                            "warning",
                            82,
                            false,
                            1,
                            2,
                            2,
                            1,
                            "/operations/continuity/workflows/1a02a8a4-8249-49a2-bdf4-0e41f3c1163e")
                    ])
            };
            var viewModel = CreateViewModel(client);

            viewModel.RefreshOperationsControlCommand.CanExecute(null).Should().BeTrue();
            await viewModel.RefreshOperationsControlCommand.ExecuteAsync(null);

            viewModel.OperationsApprovalPolicyRows.Should().ContainSingle();
            var policyRow = viewModel.OperationsApprovalPolicyRows.Single();
            policyRow.WorkflowArea.Should().Be("Financial Operations");
            policyRow.GateLabel.Should().Be("Approval");
            policyRow.Action.Should().Be("Approve Results");
            policyRow.ApprovalRuleLabel.Should().Be("2 distinct approvals");
            policyRow.ControlLabel.Should().Be("independent reviewer, report pack, checklist controls");
            policyRow.EvidenceRequirement.Should().Contain("NAV support package");
            policyRow.Route.Should().Be("/operations/continuity/close-calendar");

            viewModel.OperationsCloseCalendarRows.Should().ContainSingle();
            var calendarRow = viewModel.OperationsCloseCalendarRows.Single();
            calendarRow.WorkflowId.Should().Be(workflowId);
            calendarRow.FundAccountId.Should().Be(fundAccountId);
            calendarRow.StatusLabel.Should().Be("Approval Pending");
            calendarRow.DueLabel.Should().Be("Jun 12, 2026 - Review NAV package");
            calendarRow.OwnerLabel.Should().Be("controller");
            calendarRow.ReadinessLabel.Should().Be("82/100 warning");
            calendarRow.BlockerLabel.Should().Be("1 blocker");
            calendarRow.ApprovalLabel.Should().Be("1/2 approvals");
            calendarRow.Route.Should().Be("/operations/continuity/workflows/1a02a8a4-8249-49a2-bdf4-0e41f3c1163e");

            viewModel.OperationsApprovalPolicyRuleCount.Should().Be(1);
            viewModel.OperationsCloseCalendarItemCount.Should().Be(1);
            viewModel.OperationsCloseCalendarBlockedCount.Should().Be(1);
            viewModel.OperationsCloseCalendarReadyCount.Should().Be(0);
            viewModel.OperationsControlStatusText.Should().Contain("Loaded 1 approval rule");
            viewModel.OperationsControlGeneratedAtText.Should().Be("Jun 10, 19:00 UTC");
        });
    }

    [Fact]
    public void Constructor_ShouldDisableOperationsControlRefreshWhenClientIsUnavailable()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = CreateViewModel(null);

            viewModel.RefreshOperationsControlCommand.CanExecute(null).Should().BeFalse();
            viewModel.OperationsApprovalPolicyRows.Should().BeEmpty();
            viewModel.OperationsCloseCalendarRows.Should().BeEmpty();
            viewModel.OperationsControlStatusText.Should().Contain("unavailable");
            viewModel.OperationsApprovalPolicySummaryText.Should().Contain("shared Operations Continuity client");
            viewModel.HasOperationsApprovalPolicyRows.Should().BeFalse();
            viewModel.HasOperationsCloseCalendarRows.Should().BeFalse();
        });
    }

    [Fact]
    public void SettingsPageMarkup_ShouldExposeOperationsControlAutomationIds()
    {
        var source = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\SettingsPage.xaml"));

        source.Should().Contain("SettingsOperationsControlTab");
        source.Should().Contain("SettingsOperationsControlRefreshButton");
        source.Should().Contain("SettingsOperationsApprovalPolicyGrid");
        source.Should().Contain("SettingsOperationsCloseCalendarGrid");
        source.Should().Contain("OperationsApprovalPolicyRows");
        source.Should().Contain("OperationsCloseCalendarRows");
        source.Should().Contain("Binding=\"{Binding Route}\"");
    }

    private static SettingsViewModel CreateViewModel(IOperationsControlCenterClient? client)
        => new(
            ConfigService.Instance,
            NotificationService.Instance,
            StatusService.Instance,
            operationsControlClient: client);

    private sealed class StubOperationsControlCenterClient : IOperationsControlCenterClient
    {
        public OperationsApprovalPolicyMatrixDto? Policy { get; init; }
        public OperationsCloseCalendarDto? Calendar { get; init; }

        public Task<OperationsApprovalPolicyMatrixDto?> GetApprovalPolicyMatrixAsync(CancellationToken ct = default)
            => Task.FromResult(Policy);

        public Task<OperationsCloseCalendarDto?> GetCloseCalendarAsync(CancellationToken ct = default)
            => Task.FromResult(Calendar);

        public Task<IReadOnlyList<OperationsContinuityWorkflowSummaryDto>?> GetWorkflowsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OperationsContinuityWorkflowSummaryDto>?>(null);

        public Task<OperationsContinuityWorkflowDto?> GetWorkflowAsync(Guid workflowId, CancellationToken ct = default)
            => Task.FromResult<OperationsContinuityWorkflowDto?>(null);
    }
}

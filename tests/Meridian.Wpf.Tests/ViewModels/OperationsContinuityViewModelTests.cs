using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class OperationsContinuityViewModelTests
{
    [Fact]
    public async Task RefreshAsync_ProjectsWorkflowsQueueCalendarAndPolicy()
    {
        var viewModel = new OperationsContinuityViewModel(new FakeOperationsClient
        {
            Workflows = [CreateSummary()],
            Detail = CreateDetail(),
            Calendar = CreateCalendar(),
            Policy = CreatePolicy()
        });
        using var scope = viewModel;

        await viewModel.RefreshAsync();

        viewModel.WorkflowRows.Should().ContainSingle()
            .Which.ReadinessTone.Should().Be(
                WorkstationReadinessTone.SignoffRequired, "an approval-pending workflow is a review state");
        viewModel.SelectedWorkflowId.Should().Be(CreateSummary().WorkflowId);
        viewModel.GateRows.Should().HaveCount(2);
        viewModel.BlockerRows.Should().ContainSingle();
        viewModel.ChecklistRows.Should().ContainSingle();
        viewModel.ApprovalPolicyRows.Should().ContainSingle();
        viewModel.CloseCalendarRows.Should().ContainSingle();
        viewModel.HasWorkflowsError.Should().BeFalse();
        viewModel.HasDetailError.Should().BeFalse();
        viewModel.HasCalendarError.Should().BeFalse();
        viewModel.HasPolicyError.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshAsync_QueueKeepsOnlyOpenItemsAndRollsUp()
    {
        var viewModel = new OperationsContinuityViewModel(new FakeOperationsClient
        {
            Workflows = [CreateSummary()],
            Detail = CreateDetail(),
            Calendar = CreateCalendar(),
            Policy = CreatePolicy()
        });
        using var scope = viewModel;

        await viewModel.RefreshAsync();

        viewModel.QueueRows.Should().NotBeEmpty("open breaks, blockers, checklist tasks, and calendar items queue up");
        viewModel.QueueRows.Should().OnlyContain(
            static row => row.ReadinessTone != WorkstationReadinessTone.EvidenceLinked,
            "ready items are closed work and stay out of the operator queue");
        viewModel.QueueRows.Should().Contain(static row => row.Id.StartsWith("break:"));
        viewModel.QueueRows.Should().NotContain(
            static row => row.Id == "lane:cash", "the ready cash lane is not open work");
        viewModel.QueueRollup.StatusLabel.Should().Be("Blocked", "a blocked blocker row escalates the rollup");
        viewModel.QueueRollup.BlockedCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RefreshAsync_NextActionPrefersMostBlockedGate()
    {
        var viewModel = new OperationsContinuityViewModel(new FakeOperationsClient
        {
            Workflows = [CreateSummary()],
            Detail = CreateDetail(),
            Calendar = CreateCalendar(),
            Policy = CreatePolicy()
        });
        using var scope = viewModel;

        await viewModel.RefreshAsync();

        viewModel.NextAction.Label.Should().Be(
            "Clear reconciliation breaks", "the blocked reconciliation gate outranks the review-required approval gate");
        viewModel.NextAction.ReadinessTone.Should().Be(WorkstationReadinessTone.Blocked);
        viewModel.NextAction.DisabledReason.Should().BeNull("the action carries a route");
    }

    [Fact]
    public async Task RefreshAsync_MissingClient_DegradesPerPanelWithoutThrowing()
    {
        using var viewModel = new OperationsContinuityViewModel();

        await viewModel.RefreshAsync();

        viewModel.HasWorkflowsError.Should().BeTrue();
        viewModel.HasCalendarError.Should().BeTrue();
        viewModel.HasPolicyError.Should().BeTrue();
        viewModel.WorkflowRows.Should().BeEmpty();
        viewModel.QueueRollup.StatusLabel.Should().Be("Blocked", "missing shared close authority must not appear clear");
        viewModel.NextAction.DisabledReason.Should().NotBeNull();
    }

    [Fact]
    public void ResolveNextAction_ClosedWorkflowWithoutActions_ExplainsGovernedReopen()
    {
        var detail = CreateDetail() with
        {
            Status = OperationsWorkflowStatusDto.Closed,
            NextActions = [],
            Gates =
            [
                new OperationsGateDto(
                    OperationsGateKeyDto.Approval,
                    "Approval",
                    OperationsGateStatusDto.Passed,
                    IsRequired: true,
                    "Approval complete.",
                    Blockers: [],
                    NextActions: [],
                    CompletedAtUtc: DateTimeOffset.Parse("2026-08-05T05:00:00Z"),
                    CompletedBy: "ops")
            ]
        };

        var action = OperationsContinuityMapper.ResolveNextAction(detail, isLoading: false, detailError: null);

        action.DisabledReason.Should().Be(
            "This workflow is closed and locked; use the governed reopen command to make changes.");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("stale")]
    [InlineData("scope")]
    [InlineData("version")]
    public async Task SharedCloseDecision_BlocksAndRecoversWithoutTrustingClearLocalGates(string issue)
    {
        var scope = new CloseReadinessScopeDto("fund-alpha", Guid.NewGuid(), CreateSummary().FundAccountId, "entity-alpha", "2026-07");
        var detail = CreateDetail() with
        {
            LedgerBookId = scope.LedgerBookId,
            Status = OperationsWorkflowStatusDto.ReadyForClose,
            Gates = [],
            Blockers = [],
            BreakCases = [],
            CloseChecklist = [],
            ReconciliationLanes = [],
            NextActions = [new("close-workflow", "Close workflow", "/accounting/operations-continuity", null)]
        };
        var projection = new CloseReadinessProjectionDto(scope, DateTimeOffset.UtcNow, "Ready", true, true, [], []);
        var commandCenter = new FinancialOperationsCommandCenterDto(DateTimeOffset.UtcNow, scope.FundProfileId,
            scope.LedgerBookId, scope.FundAccountId, scope.PeriodId, "Ready", true, "Ready", 0, 0, 0, [], [],
            ActiveWorkflow: detail, CloseReadiness: projection);
        var client = new FakeOperationsClient
        {
            Workflows = [CreateSummary()],
            Detail = detail,
            CommandCenter = issue switch
            {
                "missing" => commandCenter with { CloseReadiness = null },
                "scope" => commandCenter with { CloseReadiness = projection with { Scope = scope with { EntityId = "other-entity" } } },
                "version" => commandCenter with { ActiveWorkflow = detail with { Version = detail.Version - 1 } },
                _ => commandCenter with
                {
                    CloseReadiness = projection with
                    {
                        Status = "Blocked",
                        IsReadyToClose = false,
                        Blockers = [new("evidence-stale", "report-evidence", "Stale", 1, "Error", "Controller", "The retained report evidence is stale.", ["report-1"])]
                    }
                }
            }
        };
        using var vm = new OperationsContinuityViewModel(client) { Parameter = scope };
        await vm.RefreshAsync();
        vm.CloseReadiness.IsReady.Should().BeFalse();
        vm.NextAction.DisabledReason.Should().NotBeNull();
        vm.QueueRows.Should().Contain(row => row.Id == "shared-close-readiness");

        client.CommandCenter = commandCenter;
        await vm.RefreshAsync();
        vm.CloseReadiness.IsReady.Should().BeTrue();
        vm.CloseReadiness.Label.Should().Be("Ready to close");
        vm.NextAction.DisabledReason.Should().BeNull();
        vm.QueueRows.Should().NotContain(row => row.Id == "shared-close-readiness");
        vm.EntityInput = "other-entity";
        vm.CloseReadiness.IsReady.Should().BeFalse("editing the scope invalidates the prior decision before another request");
    }

    [Fact]
    public async Task EditingScope_DropsAnInFlightReadyResponse()
    {
        var scope = new CloseReadinessScopeDto("fund-alpha", Guid.NewGuid(), CreateSummary().FundAccountId, "entity-alpha", "2026-07");
        var detail = CreateDetail() with { LedgerBookId = scope.LedgerBookId };
        var pending = new TaskCompletionSource<FinancialOperationsCommandCenterDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeOperationsClient { Workflows = [CreateSummary()], Detail = detail, CloseReadinessLoader = () => pending.Task };
        using var vm = new OperationsContinuityViewModel(client) { Parameter = scope };
        var refresh = vm.RefreshAsync();
        vm.EntityInput = "other-entity";
        pending.SetResult(new FinancialOperationsCommandCenterDto(DateTimeOffset.UtcNow, scope.FundProfileId,
            scope.LedgerBookId, scope.FundAccountId, scope.PeriodId, "Ready", true, "Ready", 0, 0, 0, [], [],
            ActiveWorkflow: detail, CloseReadiness: new(scope, DateTimeOffset.UtcNow, "Ready", true, true, [], [])));
        await refresh;
        vm.CloseReadiness.IsReady.Should().BeFalse();
        vm.QueueRollup.StatusLabel.Should().Be("Blocked");
    }

    private static OperationsContinuityWorkflowSummaryDto CreateSummary()
        => new(
            WorkflowId: Guid.Parse("7d3c2f10-6a5b-4c8d-9e1f-0a2b3c4d5e6f"),
            FundAccountId: Guid.Parse("0e2a1c94-3f5b-4f6c-9a51-1c2d3e4f5a6b"),
            PeriodId: "2026-07",
            SecurityMasterSnapshotId: null,
            BrokerSource: "alpaca",
            Status: OperationsWorkflowStatusDto.ApprovalPending,
            Version: 4,
            CreatedAtUtc: DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            UpdatedAtUtc: DateTimeOffset.Parse("2026-08-05T06:00:00Z"),
            Gates: [],
            NextActions: []);

    private static OperationsContinuityWorkflowDto CreateDetail()
        => new(
            WorkflowId: Guid.Parse("7d3c2f10-6a5b-4c8d-9e1f-0a2b3c4d5e6f"),
            FundAccountId: Guid.Parse("0e2a1c94-3f5b-4f6c-9a51-1c2d3e4f5a6b"),
            PeriodId: "2026-07",
            SecurityMasterSnapshotId: null,
            BrokerSource: "alpaca",
            CreatedAtUtc: DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            UpdatedAtUtc: DateTimeOffset.Parse("2026-08-05T06:00:00Z"),
            Version: 4,
            Status: OperationsWorkflowStatusDto.ApprovalPending,
            BrokerIntakeState: OperationsBrokerIntakeStateDto.Complete,
            SecurityMasterState: OperationsSecurityMasterStateDto.Complete,
            LedgerPostingState: OperationsLedgerPostingStateDto.Posted,
            ReconciliationState: OperationsReconciliationStateDto.ExceptionsOpen,
            ApprovalState: OperationsApprovalStateDto.Pending,
            Gates:
            [
                new OperationsGateDto(
                    OperationsGateKeyDto.Reconciliation,
                    "Reconciliation",
                    OperationsGateStatusDto.Blocked,
                    IsRequired: true,
                    "Open reconciliation breaks must be cleared.",
                    Blockers: [],
                    NextActions:
                    [
                        new OperationsNextActionDto(
                            "clear-breaks",
                            "Clear reconciliation breaks",
                            "/accounting/reconciliation",
                            OperationsGateKeyDto.Reconciliation)
                    ],
                    CompletedAtUtc: null,
                    CompletedBy: null),
                new OperationsGateDto(
                    OperationsGateKeyDto.Approval,
                    "Approval",
                    OperationsGateStatusDto.ReviewRequired,
                    IsRequired: true,
                    "Approval decision is pending.",
                    Blockers: [],
                    NextActions:
                    [
                        new OperationsNextActionDto(
                            "submit-approval",
                            "Submit approval",
                            "/accounting/approvals",
                            OperationsGateKeyDto.Approval)
                    ],
                    CompletedAtUtc: null,
                    CompletedBy: null)
            ],
            Timeline: [],
            BreakCases:
            [
                new OperationsBreakCaseDto(
                    BreakId: "brk-1",
                    CheckId: "cash",
                    Category: "CashMismatch",
                    Severity: "Warning",
                    Status: "Open",
                    Owner: "ops",
                    DueDate: null,
                    ExpectedSource: null,
                    ActualSource: null,
                    ExpectedAmount: null,
                    ActualAmount: null,
                    Variance: 125.5m,
                    SecurityId: null,
                    Symbol: null,
                    SuggestedAction: "Match the cash entry.",
                    EvidenceLinks: [])
            ],
            LedgerPreview: null,
            Approvals: [],
            ReportPackReadiness: new OperationsReportPackReadinessDto(
                IsReady: false,
                ReportPackId: null,
                BlockingReason: "Report pack has not been generated.",
                EvidenceLinks: []),
            CloseChecklist:
            [
                new OperationsCloseChecklistTaskDto(
                    TaskId: "task-1",
                    Gate: OperationsGateKeyDto.Approval,
                    Label: "Collect approvals",
                    Owner: "controller",
                    RequiredEvidence: "Approval record",
                    RequiredApprovalCount: 1,
                    ExpiresOn: null,
                    DueDate: DateOnly.Parse("2026-08-07"),
                    Status: "Open",
                    BlockingReason: null,
                    EvidencePointer: null,
                    RemediationRoute: null,
                    CanAcknowledge: true,
                    AcknowledgedAtUtc: null,
                    AcknowledgedBy: null)
            ],
            EvidenceLinks: [],
            Blockers:
            [
                new OperationsWorkflowBlockerDto(
                    Code: "recon-open-breaks",
                    Message: "One reconciliation break is open.",
                    Gate: OperationsGateKeyDto.Reconciliation,
                    Severity: "Critical",
                    EvidenceLinks: [])
            ],
            NextActions: [],
            ReconciliationLanes:
            [
                new OperationsReconciliationLaneSummaryDto(
                    LaneId: "cash",
                    Label: "Cash",
                    Status: OperationsReconciliationLaneStatusDto.Ready,
                    IsReady: true,
                    BreakCount: 0,
                    Summary: "Cash lane matched.",
                    RouteHint: null,
                    EvidenceLinks: [])
            ]);

    private static OperationsCloseCalendarDto CreateCalendar()
        => new(
            GeneratedAtUtc: DateTimeOffset.Parse("2026-08-05T06:00:00Z"),
            Items:
            [
                new OperationsCloseCalendarItemDto(
                    WorkflowId: Guid.Parse("7d3c2f10-6a5b-4c8d-9e1f-0a2b3c4d5e6f"),
                    FundAccountId: Guid.Parse("0e2a1c94-3f5b-4f6c-9a51-1c2d3e4f5a6b"),
                    PeriodId: "2026-07",
                    Status: OperationsWorkflowStatusDto.ApprovalPending,
                    Version: 4,
                    NextDueDate: DateOnly.Parse("2026-08-07"),
                    NextDueTaskId: "task-1",
                    NextDueLabel: "Collect approvals",
                    NextDueOwner: "controller",
                    ReadinessSeverity: "warning",
                    ReadinessScore: 72,
                    IsReadyToClose: false,
                    BlockerCount: 1,
                    OpenChecklistCount: 1,
                    RequiredApprovalCount: 1,
                    CompletedApprovalCount: 0,
                    Route: "/accounting/operations-continuity")
            ]);

    private static OperationsApprovalPolicyMatrixDto CreatePolicy()
        => new(
            PolicyId: "ops-approval-policy",
            Version: "v3",
            GeneratedAtUtc: DateTimeOffset.Parse("2026-08-05T05:00:00Z"),
            Rows:
            [
                new OperationsApprovalPolicyMatrixRowDto(
                    PolicyKey: "close.publish",
                    WorkflowArea: "OperationsClose",
                    Action: "PublishClosePackage",
                    Gate: OperationsGateKeyDto.Approval,
                    Trigger: "Close command",
                    RequiredPermission: "operations.close",
                    SubmitterRole: "Operator",
                    ReviewerRole: "Controller",
                    RequiredDistinctApprovals: 2,
                    RequiresIndependentReviewer: true,
                    RequiresReportPack: true,
                    RequiresChecklistControlApprovals: true,
                    EvidenceRequirement: "Close package evidence",
                    AuditEventType: "OperationsClosePublished",
                    Route: "/accounting/operations-continuity",
                    Severity: "critical")
            ]);

    private sealed class FakeOperationsClient : IOperationsControlCenterClient
    {
        public FinancialOperationsCommandCenterDto? CommandCenter { get; set; }
        public Func<Task<FinancialOperationsCommandCenterDto?>>? CloseReadinessLoader { get; set; }

        public Task<FinancialOperationsCommandCenterDto?> GetCloseReadinessAsync(CloseReadinessScopeDto scope, CancellationToken ct = default)
            => CloseReadinessLoader?.Invoke() ?? Task.FromResult(CommandCenter);
        public IReadOnlyList<OperationsContinuityWorkflowSummaryDto>? Workflows { get; set; }

        public OperationsContinuityWorkflowDto? Detail { get; set; }

        public OperationsCloseCalendarDto? Calendar { get; set; }

        public OperationsApprovalPolicyMatrixDto? Policy { get; set; }

        public Task<OperationsApprovalPolicyMatrixDto?> GetApprovalPolicyMatrixAsync(CancellationToken ct = default)
            => Task.FromResult(Policy);

        public Task<OperationsCloseCalendarDto?> GetCloseCalendarAsync(CancellationToken ct = default)
            => Task.FromResult(Calendar);

        public Task<IReadOnlyList<OperationsContinuityWorkflowSummaryDto>?> GetWorkflowsAsync(CancellationToken ct = default)
            => Task.FromResult(Workflows);

        public Task<OperationsContinuityWorkflowDto?> GetWorkflowAsync(Guid workflowId, CancellationToken ct = default)
            => Task.FromResult(Detail);
    }
}

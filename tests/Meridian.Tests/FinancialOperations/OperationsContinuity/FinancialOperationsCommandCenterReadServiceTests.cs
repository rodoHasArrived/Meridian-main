using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;

namespace Meridian.Tests.FinancialOperations.OperationsContinuity;

public sealed class FinancialOperationsCommandCenterReadServiceTests
{
    [Fact]
    public async Task GetCommandCenterAsync_WhenEvidencePackageMissing_ShouldBlockCompletion()
    {
        var workflow = CreateWorkflow(evidencePackages:
        [
            new OperationsEvidencePackageSummaryDto(
                "accounting-record",
                "Accounting record evidence",
                EvidenceStatusDto.Missing,
                false,
                "Retained source support is missing.",
                "/workstation/reporting/evidence",
                2,
                4,
                0,
                [])
        ]);
        var service = CreateService(workflow);

        var commandCenter = await service.GetCommandCenterAsync(fundAccountId: workflow.FundAccountId, periodId: workflow.PeriodId);

        commandCenter.Status.Should().Be("Blocked");
        commandCenter.IsReadyToComplete.Should().BeFalse();
        var queueRow = commandCenter.QueueRows.Should().ContainSingle(row => row.SourceKind == "evidence-package").Subject;
        queueRow.IsBlocked.Should().BeTrue();
        queueRow.Title.Should().Be("Accounting record evidence");
        queueRow.SeverityLabel.Should().Be("Missing");
        queueRow.SlaLabel.Should().Be("2/4 categories");
        queueRow.BlockerType.Should().Be("Evidence package");
        queueRow.CloseReportImpact.Should().Be("Review retained support.");
        commandCenter.CloseSupportDecision.Should().NotBeNull();
        commandCenter.CloseSupportDecision!.Status.Should().Be("Blocked");
        commandCenter.CloseSupportDecision.RetainedEvidenceGapCount.Should().Be(1);
        commandCenter.CloseSupportDecision.Decisions.Should().Contain(row =>
            row.Category == "Retained evidence gaps"
            && row.IsBlocking
            && row.Label == "Accounting record evidence");
    }

    [Fact]
    public async Task GetCommandCenterAsync_WhenApprovalPending_ShouldBlockCompletion()
    {
        var workflow = CreateWorkflow(approvals:
        [
            new OperationsApprovalDto(
                "approval-close",
                OperationsApprovalStateDto.Submitted,
                "preparer",
                null,
                "Controller sign-off pending.",
                DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                null,
                [])
        ]);
        var service = CreateService(workflow);

        var commandCenter = await service.GetCommandCenterAsync(fundAccountId: workflow.FundAccountId, periodId: workflow.PeriodId);

        commandCenter.Status.Should().Be("Blocked");
        var queueRow = commandCenter.QueueRows.Should().ContainSingle(row => row.SourceKind == "approval").Subject;
        queueRow.IsBlocked.Should().BeTrue();
        queueRow.ActionLabel.Should().Be("Complete workflow approval.");
        queueRow.SeverityLabel.Should().Be("Pending approval");
        queueRow.SlaLabel.Should().Be("2026-06-01 00:00");
        queueRow.BlockerType.Should().Be("Approval");
        queueRow.CloseReportImpact.Should().Be("Close/report sign-off");
        commandCenter.CloseSupportDecision.Should().NotBeNull();
        commandCenter.CloseSupportDecision!.PendingApprovalCount.Should().Be(1);
        commandCenter.CloseSupportDecision.Decisions.Should().Contain(row =>
            row.Category == "Approvals"
            && row.IsBlocking
            && row.RequiredAction == "Complete workflow approval.");
    }

    [Fact]
    public async Task GetCommandCenterAsync_WhenBreakCaseOpen_ShouldProjectDeterministicQueueMetadata()
    {
        var workflow = CreateWorkflow(breakCases:
        [
            new OperationsBreakCaseDto(
                "cash-break",
                "cash-tie-out",
                "Cash",
                "Critical",
                "Open",
                "controller",
                new DateOnly(2026, 6, 2),
                "Custodian",
                "Meridian ledger",
                100m,
                80m,
                20m,
                null,
                null,
                "Resolve cash break before close report release.",
                [
                    new OperationsEvidenceLinkDto(
                        "cash-statement",
                        "Cash statement",
                        "/workstation/accounting/reconciliation",
                        "custodian",
                        DateTimeOffset.Parse("2026-06-01T00:00:00Z"))
                ],
                SlaState: "Warning",
                SlaDueAtUtc: DateTimeOffset.Parse("2026-06-02T18:00:00Z"),
                BlockedOutputs: ["close-report", "nav"])
        ]);
        var service = CreateService(workflow);

        var commandCenter = await service.GetCommandCenterAsync(fundAccountId: workflow.FundAccountId, periodId: workflow.PeriodId);

        var row = commandCenter.QueueRows.Should().ContainSingle(item => item.QueueId == "break:cash-break").Subject;
        row.StatusLabel.Should().Be("Open");
        row.OwnerLabel.Should().Be("controller");
        row.DueLabel.Should().Be("Warning 2026-06-02 18:00Z");
        row.SeverityLabel.Should().Be("Critical");
        row.SlaLabel.Should().Be("Warning 2026-06-02 18:00Z");
        row.BlockerType.Should().Be("close-report, nav");
        row.CloseReportImpact.Should().Be("Blocks close-report, nav");
    }

    [Fact]
    public async Task GetCommandCenterAsync_WhenPeriodLockSupportMissing_ShouldBlockCompletion()
    {
        var workflow = CreateWorkflow();
        var calendar = new OperationsCloseCalendarDto(
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
            [
                new OperationsCloseCalendarItemDto(
                    workflow.WorkflowId,
                    workflow.FundAccountId,
                    workflow.PeriodId,
                    workflow.Status,
                    workflow.Version,
                    DateOnly.FromDateTime(DateTime.Parse("2026-06-05")),
                    "period-lock",
                    "Retain period-lock evidence",
                    "Controller",
                    "Blocked",
                    62,
                    false,
                    1,
                    1,
                    1,
                    0,
                    "/workstation/accounting/approvals")
            ]);
        var service = CreateService(workflow, calendar);

        var commandCenter = await service.GetCommandCenterAsync(fundAccountId: workflow.FundAccountId, periodId: workflow.PeriodId);

        commandCenter.Status.Should().Be("Blocked");
        var queueRow = commandCenter.QueueRows.Should().ContainSingle(row => row.SourceKind == "close-calendar").Subject;
        queueRow.IsBlocked.Should().BeTrue();
        queueRow.Title.Should().Be("Retain period-lock evidence");
        queueRow.SeverityLabel.Should().Be("Blocked");
        queueRow.SlaLabel.Should().Be("2026-06-05");
        queueRow.BlockerType.Should().Be("period-lock");
        queueRow.CloseReportImpact.Should().Be("Period lock/reopen readiness");
        commandCenter.CloseSupportDecision.Should().NotBeNull();
        commandCenter.CloseSupportDecision!.LockReopenPosture.Should().Contain("Period lock");
        commandCenter.CloseSupportDecision.Decisions.Should().Contain(row =>
            row.Category == "Lock/reopen posture"
            && row.IsBlocking
            && row.Label == "Period lock calendar");
    }

    [Fact]
    public async Task GetCommandCenterAsync_WhenNavCloseSupportMissing_ShouldBlockCompletion()
    {
        var workflow = CreateWorkflow();
        var cockpit = CreateCockpit(workflow, navReady: false);
        var service = CreateService(workflow, cockpit: cockpit);

        var commandCenter = await service.GetCommandCenterAsync(fundAccountId: workflow.FundAccountId, periodId: workflow.PeriodId);

        commandCenter.Status.Should().Be("Blocked");
        commandCenter.QueueRows.Should().Contain(row =>
            row.SourceKind == "nav-support"
            && row.IsBlocked
            && row.Title == "NAV support package");
        commandCenter.CloseSupportDecision.Should().NotBeNull();
        commandCenter.CloseSupportDecision!.NavReportDependencyPosture.Should().Contain("0/1 private-capital close lane");
        commandCenter.CloseSupportDecision.Decisions.Should().Contain(row =>
            row.Category == "NAV/report dependencies"
            && row.IsBlocking
            && row.Label == "NAV and report dependency posture");
    }

    private static FinancialOperationsCommandCenterReadService CreateService(
        OperationsContinuityWorkflowDto workflow,
        OperationsCloseCalendarDto? calendar = null,
        PrivateCapitalCloseCockpitDto? cockpit = null)
        => new(
            new StubOperationsContinuityWorkflowService(workflow),
            calendar is null ? null : new StubCloseCalendarService(calendar),
            cockpit is null ? null : new StubPrivateCapitalCloseCockpitService(cockpit));

    private static OperationsContinuityWorkflowDto CreateWorkflow(
        IReadOnlyList<OperationsApprovalDto>? approvals = null,
        IReadOnlyList<OperationsEvidencePackageSummaryDto>? evidencePackages = null,
        IReadOnlyList<OperationsBreakCaseDto>? breakCases = null)
    {
        var workflowId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var fundAccountId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var createdAt = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
        return new OperationsContinuityWorkflowDto(
            workflowId,
            fundAccountId,
            "2026-06",
            null,
            "fixture",
            createdAt,
            createdAt,
            7,
            OperationsWorkflowStatusDto.ReadyForClose,
            OperationsBrokerIntakeStateDto.Complete,
            OperationsSecurityMasterStateDto.Complete,
            OperationsLedgerPostingStateDto.Complete,
            OperationsReconciliationStateDto.Complete,
            OperationsApprovalStateDto.Approved,
            [],
            [],
            breakCases ?? [],
            null,
            approvals ?? [],
            new OperationsReportPackReadinessDto(true, "report-pack-2026-06", null, []),
            [],
            [],
            [],
            [],
            new OperationsCloseReadinessDto(true, "Ready", 100, [], [], []),
            EvidencePackages: evidencePackages);
    }

    private static PrivateCapitalCloseCockpitDto CreateCockpit(OperationsContinuityWorkflowDto workflow, bool navReady)
        => new(
            "fund-alpha",
            null,
            workflow.FundAccountId,
            workflow.PeriodId,
            null,
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
            "/workstation/accounting/capital-accounts",
            navReady ? EvidenceStatusDto.Ready : EvidenceStatusDto.Blocked,
            navReady,
            navReady ? 100 : 60,
            1,
            0,
            0,
            0,
            0,
            navReady ? 1 : 0,
            navReady ? 0 : 1,
            [],
            [],
            [],
            [],
            [],
            [],
            NavSupportPackages:
            [
                new PrivateCapitalNavSupportPackageDto(
                    "nav-support",
                    "NAV support package",
                    navReady ? EvidenceStatusDto.Ready : EvidenceStatusDto.Blocked,
                    navReady,
                    navReady ? "NAV support retained." : "NAV support package still needs positions and pricing evidence.",
                    "/workstation/accounting/capital-accounts/nav",
                    1_000_000m,
                    "USD",
                    0,
                    [],
                    [],
                    ["Retain NAV support before close sign-off."])
            ]);

    private sealed class StubOperationsContinuityWorkflowService(OperationsContinuityWorkflowDto workflow)
        : IOperationsContinuityWorkflowService
    {
        public Task<IReadOnlyList<OperationsContinuityWorkflowSummaryDto>> ListAsync(
            Guid? fundAccountId = null,
            string? periodId = null,
            OperationsWorkflowStatusDto? status = null,
            CancellationToken ct = default,
            Guid? ledgerBookId = null)
            => Task.FromResult<IReadOnlyList<OperationsContinuityWorkflowSummaryDto>>(
                [
                    new OperationsContinuityWorkflowSummaryDto(
                        workflow.WorkflowId,
                        workflow.FundAccountId,
                        workflow.PeriodId,
                        workflow.SecurityMasterSnapshotId,
                        workflow.BrokerSource,
                        workflow.Status,
                        workflow.Version,
                        workflow.CreatedAtUtc,
                        workflow.UpdatedAtUtc,
                        workflow.Gates,
                        workflow.NextActions,
                        workflow.LedgerBookId)
                ]);

        public Task<OperationsContinuityWorkflowDto?> GetAsync(Guid workflowId, CancellationToken ct = default)
            => Task.FromResult<OperationsContinuityWorkflowDto?>(workflowId == workflow.WorkflowId ? workflow : null);

        public Task<IReadOnlyList<OperationsTimelineEntryDto>> GetTimelineAsync(Guid workflowId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<OperationsTimelineEntryDto>>([]);
        public Task<IReadOnlyList<OperationsCloseChecklistTaskDto>> GetChecklistAsync(Guid workflowId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<OperationsCloseChecklistTaskDto>>([]);
        public Task<OperationsTransitionResultDto> StartWorkflowAsync(OperationsStartWorkflowRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> ImportBrokerDataAsync(Guid workflowId, OperationsTransitionRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> NormalizeBrokerTransactionsAsync(Guid workflowId, OperationsTransitionRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> RefreshGatePostureAsync(Guid workflowId, OperationsGatePostureRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> ResolveSecurityMasterMappingsAsync(Guid workflowId, OperationsSecurityMasterResolveRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> ApproveSecurityMasterOverrideAsync(Guid workflowId, string overrideId, OperationsSecurityMasterOverrideApprovalRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> BuildLedgerDraftAsync(Guid workflowId, OperationsLedgerDraftRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> ValidateLedgerDraftAsync(Guid workflowId, OperationsLedgerValidationRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> PostLedgerEntriesAsync(Guid workflowId, OperationsLedgerPostRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> RunReconciliationAsync(Guid workflowId, OperationsReconciliationRunRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> ResolveBreakCaseAsync(Guid workflowId, string breakId, OperationsResolveBreakCaseRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> AssignBreakCaseAsync(Guid workflowId, string breakId, OperationsAssignBreakCaseRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> SubmitForApprovalAsync(Guid workflowId, OperationsSubmitApprovalRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> ApproveWorkflowAsync(Guid workflowId, OperationsApprovalDecisionRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> RejectWorkflowAsync(Guid workflowId, OperationsRejectWorkflowRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> CloseWorkflowAsync(Guid workflowId, OperationsCloseWorkflowRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> ReopenWorkflowAsync(Guid workflowId, OperationsReopenWorkflowRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> AcknowledgeChecklistTaskAsync(Guid workflowId, string taskId, OperationsChecklistAcknowledgeRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class StubCloseCalendarService(OperationsCloseCalendarDto calendar) : IOperationsCloseCalendarService
    {
        public Task<OperationsCloseCalendarDto> GetCalendarAsync(Guid? fundAccountId = null, string? periodId = null, CancellationToken ct = default)
            => Task.FromResult(calendar);

        public Task<OperationsCloseCalendarItemUpsertResultDto> UpsertItemAsync(OperationsCloseCalendarItemUpsertRequestDto request, string actor, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class StubPrivateCapitalCloseCockpitService(PrivateCapitalCloseCockpitDto cockpit) : IPrivateCapitalCloseCockpitService
    {
        public Task<PrivateCapitalCloseCockpitDto> GetCockpitAsync(string? fundProfileId = null, Guid? ledgerBookId = null, Guid? fundAccountId = null, string? periodId = null, string? entityId = null, CancellationToken ct = default)
            => Task.FromResult(cockpit);
    }
}

using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Services;
using Meridian.Ui.Shared.Workflows;

namespace Meridian.Tests.Ui;

public sealed class WorkstationWorkflowSummaryFinancialOperationsTests
{
    [Fact]
    public async Task GetAsync_WithScopedAccountAndNoOperationsWorkflow_ShouldBlockAccountingOnReceiveActivity()
    {
        var fundAccountId = Guid.Parse("6C8E1E09-2FA2-43E7-BDD2-C22C5D4C121A");
        var service = CreateSummaryService(new StubOperationsContinuityWorkflowService([]));

        var summary = await service.GetAsync(
            hasOperatingContext: true,
            operatingContextDisplayName: "Northwind Income",
            fundProfileId: fundAccountId.ToString("D"),
            fundDisplayName: "Northwind Income");

        var accounting = GetAccounting(summary);
        accounting.StatusLabel.Should().Be("Financial operations workflow not started");
        accounting.NextAction.Label.Should().Be("Receive Activity");
        accounting.NextAction.TargetPageTag.Should().Be("OperationsContinuity");
        accounting.PrimaryBlocker.Code.Should().Be("financial-operations-not-started");
        accounting.PrimaryBlocker.IsBlocking.Should().BeTrue();
        accounting.Evidence.Should().Contain(badge =>
            badge.Label == "Core flow" &&
            badge.Value == "Receive Activity" &&
            badge.Tone == "Warning");
    }

    [Fact]
    public async Task GetAsync_WithOpenOperationsBreaks_ShouldPrioritizeExceptionResolution()
    {
        var fundAccountId = Guid.Parse("D9C3FF21-1BC4-43F7-9313-E72372F5B2B8");
        var workflow = CreateWorkflow(
            fundAccountId,
            OperationsWorkflowStatusDto.ReconciliationActive,
            OperationsReconciliationStateDto.ExceptionsOpen,
            OperationsApprovalStateDto.Pending,
            breaks:
            [
                new OperationsBreakCaseDto(
                    "break-cash-1",
                    "cash-reconciliation",
                    "Cash",
                    "High",
                    "Open",
                    "ops-lead",
                    new DateOnly(2026, 6, 3),
                    "Bank statement",
                    "Ledger cash",
                    100m,
                    92m,
                    -8m,
                    null,
                    null,
                    "Assign the cash break and retain resolution evidence.",
                    [CreateEvidence("bank-statement-2026-05", "Bank statement")])
            ]);
        var service = CreateSummaryService(new StubOperationsContinuityWorkflowService([workflow]));

        var summary = await service.GetAsync(
            hasOperatingContext: true,
            operatingContextDisplayName: "Northwind Income",
            fundProfileId: fundAccountId.ToString("D"),
            fundDisplayName: "Northwind Income");

        var accounting = GetAccounting(summary);
        accounting.StatusLabel.Should().Be("Financial operations exceptions require review");
        accounting.NextAction.Label.Should().Be("Resolve Exceptions");
        accounting.NextAction.TargetPageTag.Should().Be("FundReconciliation");
        accounting.PrimaryBlocker.Code.Should().Be("financial-operations-exceptions");
        accounting.PrimaryBlocker.IsBlocking.Should().BeTrue();
        accounting.Evidence.Should().Contain(badge => badge.Label == "Core flow" && badge.Value == "Resolve Exceptions");
        accounting.Evidence.Should().Contain(badge => badge.Label == "Breaks" && badge.Value == "1" && badge.Tone == "Warning");
        accounting.Evidence.Should().Contain(badge => badge.Label == "Evidence" && badge.Value == "1" && badge.Tone == "Success");
        summary.AssuranceScore.Components.Should().Contain(component =>
            component.ComponentId == "accounting" &&
            component.Status == EvidenceStatusDto.ReviewRequired);
    }

    private static WorkstationWorkflowSummaryService CreateSummaryService(
        IOperationsContinuityWorkflowService operationsService)
    {
        var runReadService = new StrategyRunReadService(
            new StrategyRunStore(),
            new PortfolioReadService(),
            new LedgerReadService());

        return new WorkstationWorkflowSummaryService(
            runReadService,
            actionCatalog: WorkflowRegistry.CreateDefault(),
            operationsContinuityWorkflowService: operationsService);
    }

    private static WorkspaceWorkflowSummary GetAccounting(OperatorWorkflowHomeSummary summary)
        => summary.Workspaces.Single(static workspace => workspace.WorkspaceId == "accounting");

    private static OperationsContinuityWorkflowDto CreateWorkflow(
        Guid fundAccountId,
        OperationsWorkflowStatusDto status,
        OperationsReconciliationStateDto reconciliationState,
        OperationsApprovalStateDto approvalState,
        IReadOnlyList<OperationsBreakCaseDto> breaks)
    {
        var workflowId = Guid.Parse("701C8D64-8F16-44E9-B24C-733F25F5952F");
        var capturedAt = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var evidence = breaks
            .SelectMany(static breakCase => breakCase.EvidenceLinks)
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new OperationsContinuityWorkflowDto(
            workflowId,
            fundAccountId,
            "2026-05",
            SecurityMasterSnapshotId: null,
            BrokerSource: "custodian",
            CreatedAtUtc: capturedAt.AddHours(-2),
            UpdatedAtUtc: capturedAt,
            Version: 7,
            status,
            OperationsBrokerIntakeStateDto.Complete,
            OperationsSecurityMasterStateDto.Complete,
            OperationsLedgerPostingStateDto.Posted,
            reconciliationState,
            approvalState,
            Gates: CreateGates(reconciliationState, approvalState),
            Timeline: [],
            BreakCases: breaks,
            LedgerPreview: null,
            Approvals: [],
            ReportPackReadiness: new OperationsReportPackReadinessDto(
                IsReady: false,
                ReportPackId: null,
                BlockingReason: "Open financial operations exceptions remain.",
                EvidenceLinks: []),
            CloseChecklist: [],
            EvidenceLinks: evidence,
            Blockers: [],
            NextActions: [],
            CloseReadiness: new OperationsCloseReadinessDto(
                IsReadyToClose: false,
                Severity: "Critical",
                Score: 65,
                Components: [],
                Blockers: [],
                NextActions: []));
    }

    private static IReadOnlyList<OperationsGateDto> CreateGates(
        OperationsReconciliationStateDto reconciliationState,
        OperationsApprovalStateDto approvalState)
    {
        var reconciliationGate = reconciliationState is OperationsReconciliationStateDto.Complete or OperationsReconciliationStateDto.Cleared
            ? OperationsGateStatusDto.Passed
            : OperationsGateStatusDto.ReviewRequired;
        var approvalGate = approvalState == OperationsApprovalStateDto.Approved
            ? OperationsGateStatusDto.Passed
            : OperationsGateStatusDto.NotStarted;

        return
        [
            CreateGate(OperationsGateKeyDto.BrokerIngest, OperationsGateStatusDto.Passed),
            CreateGate(OperationsGateKeyDto.SecurityMaster, OperationsGateStatusDto.Passed),
            CreateGate(OperationsGateKeyDto.LedgerPosting, OperationsGateStatusDto.Passed),
            CreateGate(OperationsGateKeyDto.Reconciliation, reconciliationGate),
            CreateGate(OperationsGateKeyDto.Approval, approvalGate)
        ];
    }

    private static OperationsGateDto CreateGate(OperationsGateKeyDto key, OperationsGateStatusDto status)
        => new(
            key,
            key.ToString(),
            status,
            IsRequired: true,
            Description: key.ToString(),
            Blockers: [],
            NextActions: [],
            CompletedAtUtc: status == OperationsGateStatusDto.Passed ? new DateTimeOffset(2026, 6, 1, 11, 0, 0, TimeSpan.Zero) : null,
            CompletedBy: status == OperationsGateStatusDto.Passed ? "ops-user" : null);

    private static OperationsEvidenceLinkDto CreateEvidence(string evidenceId, string label)
        => new(
            evidenceId,
            label,
            Route: $"/api/workstation/evidence/{evidenceId}",
            Source: "test",
            CapturedAtUtc: new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));

    private sealed class StubOperationsContinuityWorkflowService : IOperationsContinuityWorkflowService
    {
        private readonly IReadOnlyList<OperationsContinuityWorkflowDto> _workflows;

        public StubOperationsContinuityWorkflowService(IReadOnlyList<OperationsContinuityWorkflowDto> workflows)
        {
            _workflows = workflows;
        }

        public Task<IReadOnlyList<OperationsContinuityWorkflowSummaryDto>> ListAsync(
            Guid? fundAccountId = null,
            string? periodId = null,
            OperationsWorkflowStatusDto? status = null,
            CancellationToken ct = default)
        {
            var summaries = _workflows
                .Where(workflow => !fundAccountId.HasValue || workflow.FundAccountId == fundAccountId.Value)
                .Where(workflow => string.IsNullOrWhiteSpace(periodId) || string.Equals(workflow.PeriodId, periodId, StringComparison.OrdinalIgnoreCase))
                .Where(workflow => !status.HasValue || workflow.Status == status.Value)
                .Select(ToSummary)
                .ToArray();
            return Task.FromResult<IReadOnlyList<OperationsContinuityWorkflowSummaryDto>>(summaries);
        }

        public Task<OperationsContinuityWorkflowDto?> GetAsync(Guid workflowId, CancellationToken ct = default)
            => Task.FromResult(_workflows.FirstOrDefault(workflow => workflow.WorkflowId == workflowId));

        public Task<IReadOnlyList<OperationsTimelineEntryDto>> GetTimelineAsync(Guid workflowId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OperationsTimelineEntryDto>>([]);

        public Task<IReadOnlyList<OperationsCloseChecklistTaskDto>> GetChecklistAsync(Guid workflowId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OperationsCloseChecklistTaskDto>>([]);

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
        public Task<OperationsTransitionResultDto> SubmitForApprovalAsync(Guid workflowId, OperationsSubmitApprovalRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> ApproveWorkflowAsync(Guid workflowId, OperationsApprovalDecisionRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> RejectWorkflowAsync(Guid workflowId, OperationsRejectWorkflowRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> CloseWorkflowAsync(Guid workflowId, OperationsCloseWorkflowRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> ReopenWorkflowAsync(Guid workflowId, OperationsReopenWorkflowRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> AcknowledgeChecklistTaskAsync(Guid workflowId, string taskId, OperationsChecklistAcknowledgeRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();

        private static OperationsContinuityWorkflowSummaryDto ToSummary(OperationsContinuityWorkflowDto workflow)
            => new(
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
                workflow.NextActions);
    }
}

using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Services;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed class AuditTrailExplorerServiceTests
{
    [Fact]
    public async Task SearchAsync_ProjectsExecutionPromotionAndControlAuditIntoCrossObjectTimeline()
    {
        var root = CreateTempRoot();
        try
        {
            await using var auditTrail = CreateAuditTrail(root);
            await auditTrail.RecordAsync(new ExecutionAuditEntry(
                AuditId: "audit-order",
                Category: "Order",
                Action: "OrderSubmitted",
                Outcome: "Accepted",
                OccurredAt: DateTimeOffset.Parse("2026-05-28T15:00:00Z"),
                Actor: "trader",
                OrderId: "order-1",
                RunId: "run-live",
                Symbol: "AAPL",
                CorrelationId: "corr-order",
                Message: "Order accepted",
                Metadata: new Dictionary<string, string> { ["gateway"] = "alpaca" }));
            await auditTrail.RecordAsync(new ExecutionAuditEntry(
                AuditId: "audit-control",
                Category: "Control",
                Action: "CircuitBreakerOpened",
                Outcome: "Accepted",
                OccurredAt: DateTimeOffset.Parse("2026-05-28T15:05:00Z"),
                Actor: "ops",
                RunId: "run-live",
                CorrelationId: "corr-control",
                Reason: "market halt"));
            await auditTrail.RecordAsync(new ExecutionAuditEntry(
                AuditId: "audit-promotion",
                Category: "Promotion",
                Action: "LivePromotionApproved",
                Outcome: "Approved",
                OccurredAt: DateTimeOffset.Parse("2026-05-28T15:10:00Z"),
                Actor: "approver",
                RunId: "run-live",
                CorrelationId: "corr-promotion",
                Scope: "strategy:alpha",
                Message: "Live promotion approved"));

            var service = new AuditTrailExplorerService(auditTrail);

            var result = await service.SearchAsync(new AuditTrailExplorerQueryDto(RunId: "run-live"));

            result.TotalMatched.Should().Be(3);
            result.Returned.Should().Be(3);
            result.Entries.Select(entry => entry.AuditId)
                .Should().Equal("audit-promotion", "audit-control", "audit-order");
            result.Entries.Should().Contain(entry =>
                entry.AuditId == "audit-promotion" &&
                entry.ObjectKind == "Promotion" &&
                entry.ObjectId == "corr-promotion");
            result.Entries.Should().Contain(entry =>
                entry.AuditId == "audit-order" &&
                entry.ObjectKind == "Order" &&
                entry.ObjectId == "order-1" &&
                entry.RelatedObjectIds!.Contains("run-live") &&
                entry.EvidenceRoute!.Contains("corr-order", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task SearchAsync_ProjectsManualOverridesAsOperatorActionsKeyedByOverrideId()
    {
        var root = CreateTempRoot();
        try
        {
            await using var auditTrail = CreateAuditTrail(root);
            await auditTrail.RecordAsync(new ExecutionAuditEntry(
                AuditId: "audit-override",
                Category: "Control",
                Action: "ManualOverrideCreated",
                Outcome: "Completed",
                OccurredAt: DateTimeOffset.Parse("2026-05-28T15:15:00Z"),
                Actor: "ops",
                RunId: "run-live",
                Symbol: "AAPL",
                CorrelationId: "corr-override",
                Message: "Risk review completed",
                Metadata: new Dictionary<string, string>
                {
                    ["overrideId"] = "ovr-live-1",
                    ["kind"] = "AllowLivePromotion",
                    ["strategyId"] = "strategy-alpha",
                    ["runId"] = "run-live"
                }));

            var service = new AuditTrailExplorerService(auditTrail);

            var result = await service.SearchAsync(new AuditTrailExplorerQueryDto(SearchText: "ovr-live-1"));

            var entry = result.Entries.Should().ContainSingle().Which;
            entry.ObjectKind.Should().Be("OperatorAction");
            entry.ObjectId.Should().Be("ovr-live-1");
            entry.RelatedObjectIds.Should().Contain(["run-live", "corr-override", "AAPL", "AllowLivePromotion", "strategy-alpha"]);
            entry.EvidenceRoute.Should().Be("/api/execution/controls/manual-overrides/ovr-live-1/clear");
            entry.ActionLedgerSource.Should().Be("ExecutionAuditTrail");
            entry.ActionLedgerSequence.Should().Be(1);
            entry.CurrentActionHash.Should().HaveLength(64);
            entry.PreviousActionHash.Should().BeNull();
            entry.ActionLedgerStatus.Should().Be("WalRetained");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task SearchAsync_ProjectsCircuitBreakersAsExecutionControlsWithControlRoute()
    {
        var root = CreateTempRoot();
        try
        {
            await using var auditTrail = CreateAuditTrail(root);
            await auditTrail.RecordAsync(new ExecutionAuditEntry(
                AuditId: "audit-circuit-breaker",
                Category: "Control",
                Action: "CircuitBreakerOpened",
                Outcome: "Completed",
                OccurredAt: DateTimeOffset.Parse("2026-05-28T15:20:00Z"),
                Actor: "ops",
                CorrelationId: "corr-circuit",
                Message: "Operator halt"));

            var service = new AuditTrailExplorerService(auditTrail);

            var result = await service.SearchAsync(new AuditTrailExplorerQueryDto(SearchText: "circuit"));

            var entry = result.Entries.Should().ContainSingle().Which;
            entry.ObjectKind.Should().Be("ExecutionControl");
            entry.ObjectId.Should().Be("corr-circuit");
            entry.EvidenceRoute.Should().Be("/api/execution/controls/circuit-breaker");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task SearchAsync_FiltersByTextMetadataAndLimit()
    {
        var root = CreateTempRoot();
        try
        {
            await using var auditTrail = CreateAuditTrail(root);
            await auditTrail.RecordAsync(new ExecutionAuditEntry(
                AuditId: "audit-older",
                Category: "Order",
                Action: "OrderSubmitted",
                Outcome: "Accepted",
                OccurredAt: DateTimeOffset.Parse("2026-05-28T15:00:00Z"),
                Metadata: new Dictionary<string, string> { ["gateway"] = "paper" }));
            await auditTrail.RecordAsync(new ExecutionAuditEntry(
                AuditId: "audit-match-1",
                Category: "Order",
                Action: "OrderSubmitted",
                Outcome: "Accepted",
                OccurredAt: DateTimeOffset.Parse("2026-05-28T15:01:00Z"),
                Metadata: new Dictionary<string, string> { ["gateway"] = "alpaca" }));
            await auditTrail.RecordAsync(new ExecutionAuditEntry(
                AuditId: "audit-match-2",
                Category: "Promotion",
                Action: "LivePromotionApproved",
                Outcome: "Approved",
                OccurredAt: DateTimeOffset.Parse("2026-05-28T15:02:00Z"),
                Message: "Approved for Alpaca live route"));

            var service = new AuditTrailExplorerService(auditTrail);

            var result = await service.SearchAsync(new AuditTrailExplorerQueryDto(
                SearchText: "alpaca",
                Limit: 1));

            result.TotalMatched.Should().Be(2);
            result.Returned.Should().Be(1);
            result.Entries.Should().ContainSingle(entry => entry.AuditId == "audit-match-2");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task SearchAsync_IncludesOperationsContinuityTimeline()
    {
        var workflowId = Guid.NewGuid();
        var fundAccountId = Guid.NewGuid();
        var timeline = new OperationsTimelineEntryDto(
            AuditId: Guid.NewGuid(),
            OccurredAtUtc: DateTimeOffset.Parse("2026-05-28T16:00:00Z"),
            WorkflowId: workflowId,
            FundAccountId: fundAccountId,
            PeriodId: "2026-05",
            EventType: "reconciliation-run",
            FromState: OperationsWorkflowStatusDto.ReconciliationActive,
            ToState: OperationsWorkflowStatusDto.ApprovalPending,
            Gate: OperationsGateKeyDto.Reconciliation,
            FromGateStatus: OperationsGateStatusDto.InProgress,
            ToGateStatus: OperationsGateStatusDto.Passed,
            Actor: "ops-user",
            Rationale: "Clean broker/custodian reconciliation completed.",
            CorrelationId: "corr-recon",
            CorrelationKeys: new OperationsContinuityCorrelationKeysDto(
                RunId: "run-close",
                FundAccountId: fundAccountId,
                LedgerBatchId: "ledger-batch-1",
                ReconciliationCaseId: "case-closed"),
            References:
            [
                new OperationsEvidenceLinkDto(
                    "recon-evidence",
                    "Reconciliation evidence",
                    "/api/workstation/operations/continuity/reconciliation/case-closed",
                    "reconciliation-run",
                    DateTimeOffset.Parse("2026-05-28T16:00:00Z"))
            ],
            PreviousHash: "prev",
            CurrentHash: "hash");
        var service = new AuditTrailExplorerService(
            operationsContinuity: new StubOperationsContinuityWorkflowService(
                new OperationsContinuityWorkflowSummaryDto(
                    workflowId,
                    fundAccountId,
                    "2026-05",
                    null,
                    "custodian",
                    OperationsWorkflowStatusDto.ApprovalPending,
                    4,
                    DateTimeOffset.Parse("2026-05-28T15:00:00Z"),
                    DateTimeOffset.Parse("2026-05-28T16:00:00Z"),
                    [],
                    []),
                [timeline]));

        var result = await service.SearchAsync(new AuditTrailExplorerQueryDto(
            SearchText: "custodian reconciliation",
            RunId: "run-close"));

        result.TotalMatched.Should().Be(1);
        var entry = result.Entries.Should().ContainSingle().Which;
        entry.ObjectKind.Should().Be("Reconciliation");
        entry.ObjectId.Should().Be("case-closed");
        entry.Category.Should().Be("OperationsContinuity");
        entry.Action.Should().Be("reconciliation-run");
        entry.Actor.Should().Be("ops-user");
        entry.RelatedObjectIds.Should().Contain([workflowId.ToString("D"), fundAccountId.ToString("D"), "ledger-batch-1"]);
        entry.Metadata.Should().ContainKey("previousHash").WhoseValue.Should().Be("prev");
        entry.Metadata.Should().ContainKey("currentHash").WhoseValue.Should().Be("hash");
        entry.EvidenceRoute.Should().Be("/api/workstation/operations/continuity/reconciliation/case-closed");
        entry.ActionLedgerSource.Should().Be("OperationsContinuityTimeline");
        entry.ActionLedgerSequence.Should().Be(1);
        entry.PreviousActionHash.Should().Be("prev");
        entry.CurrentActionHash.Should().Be("hash");
        entry.ActionLedgerStatus.Should().Be("HashChained");
    }

    [Fact]
    public async Task SearchAsync_FiltersByNormalizedObjectAndRelatedObjectsAcrossSources()
    {
        var root = CreateTempRoot();
        try
        {
            var workflowId = Guid.NewGuid();
            var fundAccountId = Guid.NewGuid();
            var sharedRunId = "run-close";
            await using var auditTrail = CreateAuditTrail(root);
            await auditTrail.RecordAsync(new ExecutionAuditEntry(
                AuditId: "audit-execution",
                Category: "Promotion",
                Action: "LivePromotionApproved",
                Outcome: "Approved",
                OccurredAt: DateTimeOffset.Parse("2026-05-28T16:00:00Z"),
                Actor: "trader",
                RunId: sharedRunId,
                CorrelationId: "corr-promotion",
                Message: "Promotion approved for close run"));
            await auditTrail.RecordAsync(new ExecutionAuditEntry(
                AuditId: "audit-unrelated",
                Category: "Order",
                Action: "OrderSubmitted",
                Outcome: "Accepted",
                OccurredAt: DateTimeOffset.Parse("2026-05-28T16:01:00Z"),
                Actor: "trader",
                RunId: "run-other",
                CorrelationId: "corr-other"));

            var operationsTimeline = new OperationsTimelineEntryDto(
                AuditId: Guid.Parse("00000000-0000-0000-0000-000000000123"),
                OccurredAtUtc: DateTimeOffset.Parse("2026-05-28T16:00:00Z"),
                WorkflowId: workflowId,
                FundAccountId: fundAccountId,
                PeriodId: "2026-05",
                EventType: "approval-submitted",
                FromState: OperationsWorkflowStatusDto.ApprovalPending,
                ToState: OperationsWorkflowStatusDto.ApprovalPending,
                Gate: OperationsGateKeyDto.Approval,
                FromGateStatus: OperationsGateStatusDto.InProgress,
                ToGateStatus: OperationsGateStatusDto.InProgress,
                Actor: "controller",
                Rationale: "Ready for sign-off",
                CorrelationId: "corr-approval",
                CorrelationKeys: new OperationsContinuityCorrelationKeysDto(
                    RunId: sharedRunId,
                    FundAccountId: fundAccountId,
                    LedgerBatchId: "ledger-batch-1",
                    ReconciliationCaseId: "case-closed"),
                References:
                [
                    new OperationsEvidenceLinkDto(
                        "approval-evidence",
                        "Approval evidence",
                        "/api/workstation/operations/continuity/approval/case-closed",
                        "approval",
                        DateTimeOffset.Parse("2026-05-28T16:00:00Z"))
                ],
                PreviousHash: "prev",
                CurrentHash: "hash");
            var service = new AuditTrailExplorerService(
                auditTrail,
                new StubOperationsContinuityWorkflowService(
                    new OperationsContinuityWorkflowSummaryDto(
                        workflowId,
                        fundAccountId,
                        "2026-05",
                        null,
                        "custodian",
                        OperationsWorkflowStatusDto.ApprovalPending,
                        4,
                        DateTimeOffset.Parse("2026-05-28T15:00:00Z"),
                        DateTimeOffset.Parse("2026-05-28T16:00:00Z"),
                        [],
                        []),
                    [operationsTimeline]));

            var objectResult = await service.SearchAsync(new AuditTrailExplorerQueryDto(
                ObjectKind: "Evidence",
                ObjectId: "case-closed"));
            var relatedResult = await service.SearchAsync(new AuditTrailExplorerQueryDto(
                RelatedObjectId: sharedRunId,
                Limit: 10));
            var textResult = await service.SearchAsync(new AuditTrailExplorerQueryDto(
                SearchText: "/api/workstation/operations/continuity/approval/case-closed"));

            objectResult.Entries.Should().ContainSingle(entry =>
                entry.ObjectKind == "Evidence" &&
                entry.ObjectId == "case-closed" &&
                entry.RelatedObjectIds!.Contains(sharedRunId));
            relatedResult.Entries.Select(entry => entry.AuditId)
                .Should().Equal("00000000-0000-0000-0000-000000000123", "audit-execution");
            textResult.Entries.Should().ContainSingle(entry =>
                entry.EvidenceRoute == "/api/workstation/operations/continuity/approval/case-closed");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task SearchAsync_WithoutAuditTrail_ReturnsEmptyResult()
    {
        var service = new AuditTrailExplorerService();

        var result = await service.SearchAsync();

        result.TotalMatched.Should().Be(0);
        result.Returned.Should().Be(0);
        result.Entries.Should().BeEmpty();
    }

    private sealed class StubOperationsContinuityWorkflowService(
        OperationsContinuityWorkflowSummaryDto summary,
        IReadOnlyList<OperationsTimelineEntryDto> timeline)
        : IOperationsContinuityWorkflowService
    {
        public Task<IReadOnlyList<OperationsContinuityWorkflowSummaryDto>> ListAsync(
            Guid? fundAccountId = null,
            string? periodId = null,
            OperationsWorkflowStatusDto? status = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<OperationsContinuityWorkflowSummaryDto>>([summary]);
        }

        public Task<IReadOnlyList<OperationsTimelineEntryDto>> GetTimelineAsync(Guid workflowId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(workflowId == summary.WorkflowId ? timeline : []);
        }

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
        public Task<OperationsContinuityWorkflowDto?> GetAsync(Guid workflowId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OperationsCloseChecklistTaskDto>> GetChecklistAsync(Guid workflowId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> AcknowledgeChecklistTaskAsync(Guid workflowId, string taskId, OperationsChecklistAcknowledgeRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static ExecutionAuditTrailService CreateAuditTrail(string root) =>
        new(
            new ExecutionAuditTrailOptions(root),
            NullLogger<ExecutionAuditTrailService>.Instance);

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "Meridian.Tests", "audit-trail-explorer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

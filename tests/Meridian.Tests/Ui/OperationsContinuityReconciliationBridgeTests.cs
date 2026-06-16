using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class OperationsContinuityReconciliationBridgeTests
{
    [Fact]
    public async Task RunReconciliationAsync_ShouldProjectSecurityMasterIssuesAsWorkflowBreakCases()
    {
        var derivation = new OperationsStatusDerivationService();
        var repository = new InMemoryOperationsContinuityRepository(derivation);
        var auditStore = new InMemoryOperationsWorkflowAuditStore();
        var workflowService = new OperationsContinuityWorkflowService(repository, auditStore, derivation);
        var workflow = CreateLedgerPostedWorkflow();
        await repository.SaveAsync(workflow);

        var reconciliationDetail = CreateReconciliationDetail();
        var breakQueueItem = CreateBreakQueueItem(reconciliationDetail);
        var bridge = new OperationsContinuityReconciliationBridge(
            workflowService,
            new StaticReconciliationRunService(reconciliationDetail),
            new StaticReconciliationBreakQueueRepository([breakQueueItem]));

        var result = await bridge.RunReconciliationAsync(
            workflow.WorkflowId,
            new OperationsReconciliationRunRequestDto(
                workflow.Version,
                "ops-user",
                SourceRunId: reconciliationDetail.Summary.RunId));

        result.Success.Should().BeTrue();
        result.Workflow.Should().NotBeNull();
        result.Workflow!.BreakCases.Should().Contain(breakCase =>
            breakCase.CheckId == "SM_RECON_SECURITY_UNRESOLVED" &&
            breakCase.Category == "SecurityMasterCoverage" &&
            breakCase.Symbol == "MBS1" &&
            breakCase.EvidenceLinks.Any(link => link.Route == "/workstation/data/security-master") &&
            breakCase.RootCauseCode == "SecurityMasterCoverage" &&
            breakCase.ApprovalState == "ApprovalRequired" &&
            breakCase.BlockedOutputs != null &&
            breakCase.BlockedOutputs.Contains("Period close"));
        result.Workflow.BreakCases.Should().Contain(breakCase =>
            breakCase.CheckId == "FACTOR_PAYDOWN_AMOUNT_MISMATCH" &&
            breakCase.Category == "SecurityMasterAccounting" &&
            breakCase.ExpectedAmount == 3_000m &&
            breakCase.ActualAmount == 2_900m &&
            breakCase.Variance == -100m &&
            breakCase.Owner == "fund-controller" &&
            breakCase.SlaState == ReconciliationCaseSlaState.OnTrack.ToString() &&
            breakCase.SlaDueAtUtc == breakQueueItem.SlaDueAt &&
            breakCase.Materiality == 100m &&
            breakCase.RootCauseCode == "SecurityMasterAccounting" &&
            breakCase.ApprovalState == "awaiting-approval" &&
            breakCase.BlockedOutputs != null &&
            breakCase.BlockedOutputs.Contains("Investor statements"));
        result.Workflow.ReconciliationLanes.Should().HaveCount(7);
        result.Workflow.ReconciliationLanes.Should().Contain(lane =>
            lane.LaneId == "mbs-factor-reconciliation" &&
            lane.Status == OperationsReconciliationLaneStatusDto.ReviewRequired &&
            lane.BreakCount == 1 &&
            lane.EvidenceLinks.Any(link => link.Route == "/workstation/accounting/reconciliation"));
        result.Workflow.ReconciliationLanes.Should().Contain(lane =>
            lane.LaneId == "position-reconciliation" &&
            lane.Status == OperationsReconciliationLaneStatusDto.ReviewRequired &&
            lane.BreakCount == 1);
        result.Workflow.ReconciliationLanes.Should().Contain(lane =>
            lane.LaneId == "bank-reconciliation" &&
            lane.Status == OperationsReconciliationLaneStatusDto.Ready &&
            lane.Summary.Contains("normalized bank transaction", StringComparison.OrdinalIgnoreCase));
        result.Workflow.ReconciliationLanes.Should().Contain(lane =>
            lane.LaneId == "income-reconciliation" &&
            lane.Status == OperationsReconciliationLaneStatusDto.Ready &&
            lane.Summary.Contains("expected accounting event", StringComparison.OrdinalIgnoreCase));
        result.Workflow.ReconciliationLanes.Should().Contain(lane =>
            lane.LaneId == "gl-reconciliation" &&
            lane.Status == OperationsReconciliationLaneStatusDto.Ready &&
            lane.Summary.Contains("expected journal preview", StringComparison.OrdinalIgnoreCase));
        result.Workflow.Gates.Single(gate => gate.GateKey == OperationsGateKeyDto.SecurityMaster)
            .Status.Should().Be(OperationsGateStatusDto.Blocked);
    }

    private static OperationsContinuityWorkflow CreateLedgerPostedWorkflow()
    {
        var now = DateTimeOffset.UtcNow;
        var workflow = OperationsContinuityWorkflow.Start(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "2026-05",
            securityMasterSnapshotId: null,
            brokerSource: "custodian",
            now);

        workflow.BrokerIntakeState = OperationsBrokerIntakeStateDto.Complete;
        workflow.SecurityMasterState = OperationsSecurityMasterStateDto.Complete;
        workflow.LedgerPostingState = OperationsLedgerPostingStateDto.Complete;
        workflow.ReconciliationState = OperationsReconciliationStateDto.Pending;
        workflow.BrokerIngestGate = workflow.BrokerIngestGate.WithStatus(OperationsGateStatusDto.Passed);
        workflow.SecurityMasterGate = workflow.SecurityMasterGate.WithStatus(OperationsGateStatusDto.Passed);
        workflow.LedgerPostingGate = workflow.LedgerPostingGate.WithStatus(OperationsGateStatusDto.Passed);
        workflow.Version = 7;
        return workflow;
    }

    private static ReconciliationRunDetail CreateReconciliationDetail()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var summary = new ReconciliationRunSummary(
            ReconciliationRunId: "recon-ops-security-1",
            RunId: "run-ops-security-1",
            CreatedAt: createdAt,
            PortfolioAsOf: createdAt,
            LedgerAsOf: createdAt,
            MatchCount: 0,
            BreakCount: 0,
            OpenBreakCount: 0,
            HasTimingDrift: false,
            AmountTolerance: 0.01m,
            MaxAsOfDriftMinutes: 5,
            SecurityIssueCount: 1,
            HasSecurityCoverageIssues: true,
            BankTransactionCount: 2,
            ExpectedAccountingEventCount: 1,
            ExpectedJournalPreviewCount: 1,
            SecurityMasterAccountingIssueCount: 1,
            HasSecurityMasterAccountingIssues: true);

        return new ReconciliationRunDetail(
            summary,
            Matches: [],
            Breaks: [],
            SecurityCoverageIssues:
            [
                new ReconciliationSecurityCoverageIssueDto(
                    Source: "portfolio",
                    Symbol: "MBS1",
                    AccountName: "Mortgage pool",
                    Reason: "Portfolio position 'MBS1' is missing a Security Master match.",
                    Code: "SM_RECON_SECURITY_UNRESOLVED",
                    Severity: ReconciliationBreakSeverity.High,
                    EvidenceLink: "/workstation/data/security-master")
            ],
            SecurityMasterAccountingIssues:
            [
                new SecurityMasterAccountingIssueDto(
                    Code: "FACTOR_PAYDOWN_AMOUNT_MISMATCH",
                    Source: "actual-activity",
                    Symbol: "MBS1",
                    AccountId: "acct-1",
                    Reason: "Actual principal paydown differs from the Security Master expected amount.",
                    Severity: ReconciliationBreakSeverity.High,
                    EvidenceLink: "/workstation/accounting/reconciliation",
                    ExpectedAmount: 3_000m,
                    ActualAmount: 2_900m)
            ]);
    }

    private static ReconciliationBreakQueueItem CreateBreakQueueItem(ReconciliationRunDetail detail)
    {
        var createdAt = detail.Summary.CreatedAt;
        return new ReconciliationBreakQueueItem(
            BreakId: "recon-ops-security-1:security-accounting:actual-activity:mbs1:factor-paydown-amount-mismatch",
            RunId: detail.Summary.RunId,
            StrategyName: "Operations close",
            Category: ReconciliationBreakCategory.AmountMismatch,
            Status: ReconciliationBreakQueueStatus.InReview,
            Variance: -100m,
            Reason: "Actual principal paydown differs from the Security Master expected amount.",
            AssignedTo: "fund-controller",
            DetectedAt: createdAt.AddHours(-2),
            LastUpdatedAt: createdAt,
            Severity: ReconciliationBreakSeverity.High,
            RequiredSignoffRole: "Controller",
            SignoffStatus: "awaiting-approval",
            RoutingTarget: "Investor statements",
            RoutingDetail: "Principal paydown evidence requires controller approval.",
            RecommendedAction: "Approve factor paydown support.",
            Priority: ReconciliationCasePriority.High,
            SlaPolicyId: "default-high-high",
            SlaDueAt: createdAt.AddHours(8),
            SlaWarningAt: createdAt.AddHours(4),
            SlaState: ReconciliationCaseSlaState.OnTrack,
            RootCauseCode: "SecurityMasterAccounting",
            Score: new ReconciliationBreakScore(
                SeverityScore: 55,
                PriorityScore: 65,
                MaterialityComponent: 100m,
                AgeHours: 2,
                CounterpartyCriticalityComponent: 0,
                RecurringPatternComponent: 0,
                IsHighPriority: false,
                SlaDueAt: createdAt.AddHours(8)));
    }

    private sealed class StaticReconciliationRunService : IReconciliationRunService
    {
        private readonly ReconciliationRunDetail _detail;

        public StaticReconciliationRunService(ReconciliationRunDetail detail)
        {
            _detail = detail;
        }

        public Task<ReconciliationRunDetail?> RunAsync(ReconciliationRunRequest request, CancellationToken ct = default) =>
            Task.FromResult<ReconciliationRunDetail?>(_detail);

        public Task<ReconciliationRunDetail?> GetByIdAsync(string reconciliationRunId, CancellationToken ct = default) =>
            Task.FromResult<ReconciliationRunDetail?>(_detail);

        public Task<ReconciliationRunDetail?> GetLatestForRunAsync(string runId, CancellationToken ct = default) =>
            Task.FromResult<ReconciliationRunDetail?>(_detail);

        public Task<IReadOnlyList<ReconciliationRunSummary>> GetHistoryForRunAsync(string runId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReconciliationRunSummary>>([_detail.Summary]);
    }

    private sealed class StaticReconciliationBreakQueueRepository : IReconciliationBreakQueueRepository
    {
        private readonly IReadOnlyList<ReconciliationBreakQueueItem> _items;

        public StaticReconciliationBreakQueueRepository(IReadOnlyList<ReconciliationBreakQueueItem> items)
        {
            _items = items;
        }

        public Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetAllAsync(ReconciliationBreakQueueStatus? status = null, CancellationToken ct = default)
        {
            var items = status.HasValue
                ? _items.Where(item => item.Status == status.Value).ToArray()
                : _items;
            return Task.FromResult<IReadOnlyList<ReconciliationBreakQueueItem>>(items);
        }

        public Task<ReconciliationBreakQueueItem?> GetByIdAsync(string breakId, CancellationToken ct = default) =>
            Task.FromResult(_items.FirstOrDefault(item => string.Equals(item.BreakId, breakId, StringComparison.OrdinalIgnoreCase)));

        public Task<bool> CreateIfMissingAsync(ReconciliationBreakQueueItem item, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SaveAsync(ReconciliationBreakQueueItem item, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(string breakId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ReconciliationBreakQueueTransitionResult> StartReviewAsync(ReviewReconciliationBreakRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ReconciliationBreakQueueTransitionResult> ResolveAsync(ResolveReconciliationBreakRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ReconciliationBreakQueueAuditEvent>> GetAuditHistoryAsync(string breakId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReconciliationBreakQueueAuditEvent>>([]);
    }
}

using FluentAssertions;
using Meridian.Application.OperationsContinuity;
using Meridian.Contracts.Workstation;
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
        var bridge = new OperationsContinuityReconciliationBridge(
            workflowService,
            new StaticReconciliationRunService(reconciliationDetail));

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
            breakCase.EvidenceLinks.Any(link => link.Route == "/workstation/data/security-master"));
        result.Workflow.BreakCases.Should().Contain(breakCase =>
            breakCase.CheckId == "FACTOR_PAYDOWN_AMOUNT_MISMATCH" &&
            breakCase.Category == "SecurityMasterAccounting" &&
            breakCase.ExpectedAmount == 3_000m &&
            breakCase.ActualAmount == 2_900m &&
            breakCase.Variance == -100m);
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
}

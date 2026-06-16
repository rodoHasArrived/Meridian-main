using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

/// <summary>
/// W4 Fund-Ops Close-Lane Scenario Tests.
/// Exercises account-period close acceptance around the shared Operations Continuity workflow:
/// happy path, blocker path, and recovery path from rejected approval back to close.
/// These tests are support evidence for Wave 4 close governance; they do not by themselves close
/// broader report-pack publication, durable generalized casework, or browser/WPF parity criteria.
/// </summary>
[Trait("Category", "Scenario")]
[Trait("Category", "W4Acceptance")]
public sealed class FundOpsCloseLaneScenarioTests
{
    [Fact]
    public async Task FundOpsPeriodClose_FullLaneFromOpenToClosedWithAuditTrail()
    {
        // ---- STEP 1: open the accounting period ----

        var service = CreateService(out _, out var auditStore);
        var fundAccountId = Guid.NewGuid();

        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            fundAccountId,
            "2026-05",
            null,
            "custodian",
            "ops-user"));

        start.Success.Should().BeTrue("workflow should start cleanly");
        start.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.CollectingBrokerData,
            "the first gate is broker data import");

        var workflowId = start.Workflow.WorkflowId;

        // ---- STEP 2: import broker data ----

        var import = await service.ImportBrokerDataAsync(
            workflowId,
            new OperationsTransitionRequestDto(start.Workflow.Version, "ops-user"));

        import.Success.Should().BeTrue();

        // ---- STEP 3: normalize broker transactions ----

        var normalized = await service.NormalizeBrokerTransactionsAsync(
            workflowId,
            new OperationsTransitionRequestDto(import.Workflow!.Version, "ops-user", "Normalized broker activity"));

        normalized.Success.Should().BeTrue();

        // ---- STEP 4: resolve security master mappings ----

        var security = await service.ResolveSecurityMasterMappingsAsync(
            workflowId,
            new OperationsSecurityMasterResolveRequestDto(
                normalized.Workflow!.Version,
                "ops-user",
                "Resolved all instruments"));

        security.Success.Should().BeTrue();

        // ---- STEP 5: build and validate the ledger draft ----

        var draft = await service.BuildLedgerDraftAsync(
            workflowId,
            new OperationsLedgerDraftRequestDto(
                security.Workflow!.Version,
                "ops-user",
                PreviewId: "ledger-preview-may-2026",
                IsBalanced: true,
                Rationale: "Built period-close journal preview"));

        draft.Success.Should().BeTrue();

        var validated = await service.ValidateLedgerDraftAsync(
            workflowId,
            new OperationsLedgerValidationRequestDto(
                draft.Workflow!.Version,
                "ops-user",
                IsBalanced: true,
                PeriodOpen: true,
                Rationale: "Validated balanced draft against accounting rules"));

        validated.Success.Should().BeTrue();

        // ---- STEP 6: post the ledger entries ----

        var posted = await service.PostLedgerEntriesAsync(
            workflowId,
            new OperationsLedgerPostRequestDto(
                validated.Workflow!.Version,
                "ops-user",
                LedgerBatchId: "ledger-batch-may-2026",
                PostingKind: "period-close",
                PeriodOpen: true,
                Rationale: "Posted validated period-close accounting journals",
                JournalCandidate: CreateJournalCandidate(fundAccountId)));

        posted.Success.Should().BeTrue();

        // ---- STEP 7: run reconciliation (no breaks) ----

        var reconciled = await service.RunReconciliationAsync(
            workflowId,
            new OperationsReconciliationRunRequestDto(
                posted.Workflow!.Version,
                "ops-user",
                "Ran expected-vs-actual reconciliation",
                BreakCases: []));

        reconciled.Success.Should().BeTrue("clean reconciliation with no breaks should pass");

        // ---- STEP 8: mark the report pack as ready ----

        var posture = await service.RefreshGatePostureAsync(
            workflowId,
            new OperationsGatePostureRequestDto(
                reconciled.Workflow!.Version,
                "ops-user",
                ReportPackReady: true,
                ReportPackId: "report-pack-may-2026",
                Rationale: "Report pack signed off by fund administrator"));

        posture.Success.Should().BeTrue();

        // ---- STEP 9: operator submits the workflow for approval ----

        var submitted = await service.SubmitForApprovalAsync(
            workflowId,
            new OperationsSubmitApprovalRequestDto(
                posture.Workflow!.Version,
                "ops-user",
                Reviewer: "fund-controller",
                Rationale: "All gates clean — submitting period close for controller sign-off",
                ReportPackId: "report-pack-may-2026",
                ChecklistControlApprovals: RequiredChecklistControlApprovals()));

        submitted.Success.Should().BeTrue();
        submitted.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.ApprovalPending);

        // ---- STEP 10: controller approves the close ----

        var approved = await service.ApproveWorkflowAsync(
            workflowId,
            new OperationsApprovalDecisionRequestDto(
                submitted.Workflow.Version,
                "fund-controller",
                Reviewer: "fund-controller",
                Rationale: "Reviewed and approved — all evidence clean and consistent",
                ReportPackId: "report-pack-may-2026",
                ChecklistControlApprovals: RequiredChecklistControlApprovals()));

        approved.Success.Should().BeTrue();
        approved.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.ReadyForClose);
        approved.Workflow.Approvals.Should().Contain(a => a.Status == OperationsApprovalStateDto.Approved);

        // ---- STEP 11: close the period ----

        var closed = await service.CloseWorkflowAsync(
            workflowId,
            new OperationsCloseWorkflowRequestDto(
                approved.Workflow.Version,
                "ops-user",
                Rationale: "Closing May 2026 accounting period",
                ReportPackId: "report-pack-may-2026",
                ChecklistControlApprovals: RequiredChecklistControlApprovals()));

        closed.Success.Should().BeTrue();
        closed.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.Closed,
            "the period close sequence should end in Closed state");

        AssertCloseReadinessReady(closed.Workflow.CloseReadiness);
        closed.Workflow.ClosePackage.Should().NotBeNull("close publication should retain the signed close package manifest");
        closed.Workflow.ClosePackage!.ReportPackId.Should().Be("report-pack-may-2026");
        closed.Workflow.ClosePackage.EvidenceHash.Should().MatchRegex("^[a-f0-9]{64}$");
        closed.Workflow.ClosePackage.ChecklistControlApprovals.Should().HaveCount(6);
        AssertAccountingRecordAuditReady(closed.Workflow, "report-pack-may-2026");

        closed.Workflow.Gates.Should().OnlyContain(
            g => g.Status == OperationsGateStatusDto.Passed,
            "all gates must be in Passed state for a clean close");

        // ---- STEP 12: verify the full audit trail ----

        var timeline = await auditStore.GetTimelineAsync(workflowId);

        timeline.Should().NotBeEmpty("every workflow step produces an audit event");
        timeline.Select(e => e.EventType).Should().ContainInOrder(
            "workflow-started",
            "broker-imported");

        var closeEvent = timeline.LastOrDefault(e => e.EventType.Contains("closed", StringComparison.OrdinalIgnoreCase));
        closeEvent.Should().NotBeNull("the close step should produce an audit event");
        closeEvent!.Actor.Should().NotBeNullOrWhiteSpace("every audit event must carry an actor");
    }

    [Fact]
    public async Task FundOpsPeriodClose_ReconciliationBreakSurfacedAndResolvedBeforeApproval()
    {
        // Operator encounters a reconciliation break, resolves it, then proceeds to close.

        var service = CreateService(out _, out var auditStore);
        var fundAccountId = Guid.NewGuid();

        // Advance to the point where ledger entries are posted.
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            fundAccountId, "2026-05", null, "custodian", "ops-user"));
        var workflowId = start.Workflow!.WorkflowId;

        var import = await service.ImportBrokerDataAsync(workflowId,
            new OperationsTransitionRequestDto(start.Workflow.Version, "ops-user"));
        var normalized = await service.NormalizeBrokerTransactionsAsync(workflowId,
            new OperationsTransitionRequestDto(import.Workflow!.Version, "ops-user"));
        var security = await service.ResolveSecurityMasterMappingsAsync(workflowId,
            new OperationsSecurityMasterResolveRequestDto(normalized.Workflow!.Version, "ops-user"));
        var draft = await service.BuildLedgerDraftAsync(workflowId,
            new OperationsLedgerDraftRequestDto(security.Workflow!.Version, "ops-user", "preview-1", true));
        var validated = await service.ValidateLedgerDraftAsync(workflowId,
            new OperationsLedgerValidationRequestDto(draft.Workflow!.Version, "ops-user", true, true));
        var posted = await service.PostLedgerEntriesAsync(workflowId,
            new OperationsLedgerPostRequestDto(
                validated.Workflow!.Version,
                "ops-user",
                LedgerBatchId: "batch-break-test",
                PostingKind: "period-close",
                PeriodOpen: true,
                JournalCandidate: CreateJournalCandidate(fundAccountId)));

        // Introduce a reconciliation break.
        var breakId = Guid.NewGuid().ToString("N");
        var reconciled = await service.RunReconciliationAsync(workflowId,
            new OperationsReconciliationRunRequestDto(
                posted.Workflow!.Version,
                "ops-user",
                "Reconciliation found a position discrepancy",
                BreakCases:
                [
                    new OperationsBreakCaseDto(
                        breakId,
                        "position-check",
                        "Position",
                        "Critical",
                        "Open",
                        null,
                        null,
                        "Fund position per ledger",
                        "Custodian statement",
                        1000m,
                        950m,
                        50m,
                        null,
                        "AAPL",
                        "Confirm settlement timing with custodian",
                        [],
                        new OperationsContinuityCorrelationKeysDto(
                            RunId: "run-close-break-test",
                            FundAccountId: fundAccountId,
                            LedgerBatchId: "batch-break-test",
                            ReconciliationCaseId: breakId))
                ]));

        reconciled.Success.Should().BeTrue();
        reconciled.Workflow!.Gates.Should().Contain(
            g => g.GateKey == OperationsGateKeyDto.Reconciliation && g.Status != OperationsGateStatusDto.Passed,
            "a critical break should block the reconciliation gate");

        // Operator investigates and resolves the break.
        var resolved = await service.ResolveBreakCaseAsync(
            workflowId,
            breakId,
            new OperationsResolveBreakCaseRequestDto(
                reconciled.Workflow.Version,
                "ops-user",
                ResolutionStatus: "Resolved",
                Rationale: "Timing difference confirmed with custodian — T+1 settlement"));

        resolved.Success.Should().BeTrue("break case resolution should succeed");

        // Now approve and close.
        var posture = await service.RefreshGatePostureAsync(workflowId,
            new OperationsGatePostureRequestDto(
                resolved.Workflow!.Version, "ops-user",
                ReportPackReady: true, ReportPackId: "report-pack-break-test"));
        var submitted = await service.SubmitForApprovalAsync(workflowId,
            new OperationsSubmitApprovalRequestDto(
                posture.Workflow!.Version, "ops-user", "controller",
                "Break resolved — submitting for close", "report-pack-break-test",
                ChecklistControlApprovals: RequiredChecklistControlApprovals()));
        var approved = await service.ApproveWorkflowAsync(workflowId,
            new OperationsApprovalDecisionRequestDto(
                submitted.Workflow!.Version, "controller", "controller",
                "Reviewed resolution evidence — approved", "report-pack-break-test",
                ChecklistControlApprovals: RequiredChecklistControlApprovals()));
        var closed = await service.CloseWorkflowAsync(workflowId,
            new OperationsCloseWorkflowRequestDto(
                approved.Workflow!.Version, "ops-user",
                "Period closed after break resolution", "report-pack-break-test",
                ChecklistControlApprovals: RequiredChecklistControlApprovals()));

        closed.Success.Should().BeTrue();
        closed.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.Closed);

        // The audit timeline must capture both the break and its resolution.
        var timeline = await auditStore.GetTimelineAsync(workflowId);
        timeline.Should().Contain(e => e.EventType.Contains("reconcil", StringComparison.OrdinalIgnoreCase),
            "reconciliation step must appear in the audit trail");
    }

    [Fact]
    public async Task FundOpsPeriodClose_SubmitForApproval_BlockedWhenReportPackNotReady()
    {
        // The operator advances the workflow to the point where reconciliation is complete,
        // but forgets to mark the report pack as ready before submitting for approval.
        // The workflow should block submission and surface a clear blocker code.
        // Once the operator marks the report pack ready, submission succeeds.

        var service = CreateService(out _, out _);
        var fundAccountId = Guid.NewGuid();

        // Advance to post-reconciliation state.
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            fundAccountId, "2026-05", null, "custodian", "ops-user"));
        var workflowId = start.Workflow!.WorkflowId;

        var import = await service.ImportBrokerDataAsync(workflowId,
            new OperationsTransitionRequestDto(start.Workflow.Version, "ops-user"));
        var normalized = await service.NormalizeBrokerTransactionsAsync(workflowId,
            new OperationsTransitionRequestDto(import.Workflow!.Version, "ops-user"));
        var security = await service.ResolveSecurityMasterMappingsAsync(workflowId,
            new OperationsSecurityMasterResolveRequestDto(normalized.Workflow!.Version, "ops-user"));
        var draft = await service.BuildLedgerDraftAsync(workflowId,
            new OperationsLedgerDraftRequestDto(security.Workflow!.Version, "ops-user", "preview-rp-gate", true));
        var validated = await service.ValidateLedgerDraftAsync(workflowId,
            new OperationsLedgerValidationRequestDto(draft.Workflow!.Version, "ops-user", true, true));
        var posted = await service.PostLedgerEntriesAsync(workflowId,
            new OperationsLedgerPostRequestDto(
                validated.Workflow!.Version, "ops-user",
                LedgerBatchId: "batch-rp-gate",
                PostingKind: "period-close",
                PeriodOpen: true,
                JournalCandidate: CreateJournalCandidate(fundAccountId)));
        var reconciled = await service.RunReconciliationAsync(workflowId,
            new OperationsReconciliationRunRequestDto(
                posted.Workflow!.Version, "ops-user",
                "Clean reconciliation run",
                BreakCases: []));

        reconciled.Success.Should().BeTrue();

        // Attempt to submit for approval WITHOUT marking the report pack as ready first.
        // A dummy report-pack ID is provided; the workflow should still block because
        // ReportPackReadiness.IsReady is false in the domain state.
        var blockedSubmit = await service.SubmitForApprovalAsync(workflowId,
            new OperationsSubmitApprovalRequestDto(
                reconciled.Workflow!.Version,
                Actor: "ops-user",
                Reviewer: "controller",
                Rationale: "Trying to submit without a ready report pack",
                ReportPackId: "report-pack-not-yet-ready"));

        blockedSubmit.Success.Should().BeFalse(
            "submission must be blocked when the report pack has not been marked ready");
        blockedSubmit.Blockers.Should().NotBeEmpty("at least one blocker record must be present");
        blockedSubmit.Blockers[0].Code.Should().NotBeNullOrWhiteSpace(
            "the blocker must carry a machine-readable code so the UI can route the operator correctly");

        var blockedClose = await service.CloseWorkflowAsync(workflowId,
            new OperationsCloseWorkflowRequestDto(
                reconciled.Workflow.Version,
                "ops-user",
                "Attempting to close before report-pack and approval readiness",
                "report-pack-not-yet-ready",
                ChecklistControlApprovals: RequiredChecklistControlApprovals()));

        blockedClose.Success.Should().BeFalse("close execution must fail closed while W4 readiness is incomplete");
        blockedClose.ErrorCode.Should().Be("CLOSE_READINESS_FAILED");
        blockedClose.CloseReadiness.Should().NotBeNull();
        blockedClose.CloseReadiness!.IsReadyToClose.Should().BeFalse();
        blockedClose.CloseReadiness.Score.Should().BeLessThan(100);
        blockedClose.CloseReadiness.Components.Should().Contain(component =>
            component.Key == "reports" &&
            !component.IsReady &&
            component.RouteHint == "/workstation/reporting");
        blockedClose.CloseReadiness.Components.Should().Contain(component =>
            component.Key == "approvals" &&
            !component.IsReady &&
            component.RouteHint == "/workstation/accounting");
        blockedClose.CloseReadiness.Blockers.Select(blocker => blocker.Code).Should().Contain(
            [
                "REPORT_PACK_REQUIRED",
                "APPROVAL_REQUIRED"
            ]);
        blockedClose.NextActions.Select(action => action.Code).Should().Contain(
            [
                "REPORT_PACK_REQUIRED",
                "APPROVAL_REQUIRED"
            ]);

        // The approval state stays at Pending — the blocked submit must not record an approval submission.
        // Note: Status derives as ApprovalPending once all four gates are clean, which is expected
        // at this stage of the workflow. What must NOT change is the ApprovalState.
        var wfAfterBlock = await service.GetAsync(workflowId);
        wfAfterBlock!.ApprovalState.Should().Be(OperationsApprovalStateDto.Pending,
            "a blocked submission must not advance the approval state");

        // Operator corrects the gap by marking the report pack ready.
        var posture = await service.RefreshGatePostureAsync(workflowId,
            new OperationsGatePostureRequestDto(
                wfAfterBlock.Version, "ops-user",
                ReportPackReady: true,
                ReportPackId: "report-pack-rp-gate",
                Rationale: "Report pack generated and reviewed by fund administrator"));

        posture.Success.Should().BeTrue();
        posture.Workflow!.ReportPackReadiness.IsReady.Should().BeTrue(
            "the report pack gate should now show as ready");
        posture.Workflow.ReportPackReadiness.ReportPackId.Should().Be("report-pack-rp-gate");

        // Submission now succeeds.
        var submitted = await service.SubmitForApprovalAsync(workflowId,
            new OperationsSubmitApprovalRequestDto(
                posture.Workflow.Version,
                "ops-user",
                Reviewer: "controller",
                Rationale: "Report pack confirmed — submitting for approval",
                ReportPackId: "report-pack-rp-gate",
                ChecklistControlApprovals: RequiredChecklistControlApprovals()));

        submitted.Success.Should().BeTrue(
            "submission must succeed once the report pack is marked ready");
        submitted.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.ApprovalPending);
        submitted.Workflow.CloseReadiness.Should().NotBeNull();
        submitted.Workflow.CloseReadiness!.Components.Should().Contain(component =>
            component.Key == "reports" &&
            component.IsReady &&
            component.Score == component.Weight);
        submitted.Workflow.CloseReadiness.Components.Should().Contain(component =>
            component.Key == "approvals" &&
            !component.IsReady,
            "controller approval still has to complete before close can execute");
    }

    [Fact]
    public async Task FundOpsPeriodClose_RejectedApprovalCanRecoverThroughLedgerRemediationAndClose()
    {
        var service = CreateService(out _, out var auditStore);
        var fundAccountId = Guid.NewGuid();

        var workflow = await CreateApprovalSubmittedCloseWorkflowAsync(
            service,
            fundAccountId,
            periodId: "2026-05",
            reportPackId: "report-pack-recovery-1",
            ledgerBatchId: "ledger-batch-recovery-1");

        var rejected = await service.RejectWorkflowAsync(
            workflow.WorkflowId,
            new OperationsRejectWorkflowRequestDto(
                workflow.Version,
                "fund-controller",
                Reviewer: "controller",
                Rationale: "Ledger evidence needs remediation before close approval.",
                ReasonCode: "LedgerMismatch"));

        rejected.Success.Should().BeTrue("controller rejection should send the workflow back to the remediation lane");
        rejected.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.LedgerPostingDraft);
        rejected.Workflow.ApprovalState.Should().Be(OperationsApprovalStateDto.Rejected);
        rejected.Workflow.Gates.Single(gate => gate.GateKey == OperationsGateKeyDto.LedgerPosting)
            .Status.Should().Be(OperationsGateStatusDto.InProgress);

        var redrafted = await service.BuildLedgerDraftAsync(
            workflow.WorkflowId,
            new OperationsLedgerDraftRequestDto(
                rejected.Workflow.Version,
                "ops-user",
                PreviewId: "ledger-preview-recovery-2",
                IsBalanced: true,
                Rationale: "Remediated controller ledger mismatch."));
        var revalidated = await service.ValidateLedgerDraftAsync(
            workflow.WorkflowId,
            new OperationsLedgerValidationRequestDto(
                redrafted.Workflow!.Version,
                "ops-user",
                IsBalanced: true,
                PeriodOpen: true,
                Rationale: "Validated remediated ledger draft."));
        var reposted = await service.PostLedgerEntriesAsync(
            workflow.WorkflowId,
            new OperationsLedgerPostRequestDto(
                revalidated.Workflow!.Version,
                "ops-user",
                LedgerBatchId: "ledger-batch-recovery-2",
                PostingKind: "period-close",
                PeriodOpen: true,
                Rationale: "Posted remediated ledger journals.",
                JournalCandidate: CreateJournalCandidate(fundAccountId)));
        var rereconciled = await service.RunReconciliationAsync(
            workflow.WorkflowId,
            new OperationsReconciliationRunRequestDto(
                reposted.Workflow!.Version,
                "ops-user",
                "Re-ran reconciliation after ledger remediation.",
                BreakCases: []));
        var posture = await service.RefreshGatePostureAsync(
            workflow.WorkflowId,
            new OperationsGatePostureRequestDto(
                rereconciled.Workflow!.Version,
                "ops-user",
                ReportPackReady: true,
                ReportPackId: "report-pack-recovery-2",
                Rationale: "Regenerated report pack after ledger remediation."));
        var resubmitted = await service.SubmitForApprovalAsync(
            workflow.WorkflowId,
            new OperationsSubmitApprovalRequestDto(
                posture.Workflow!.Version,
                "ops-user",
                Reviewer: "controller",
                Rationale: "Remediated evidence is ready for controller approval.",
                ReportPackId: "report-pack-recovery-2",
                ChecklistControlApprovals: RequiredChecklistControlApprovals()));
        var approved = await service.ApproveWorkflowAsync(
            workflow.WorkflowId,
            new OperationsApprovalDecisionRequestDto(
                resubmitted.Workflow!.Version,
                "fund-controller",
                Reviewer: "controller",
                Rationale: "Reviewed remediated ledger evidence and approved close.",
                ReportPackId: "report-pack-recovery-2",
                ChecklistControlApprovals: RequiredChecklistControlApprovals()));
        var closed = await service.CloseWorkflowAsync(
            workflow.WorkflowId,
            new OperationsCloseWorkflowRequestDto(
                approved.Workflow!.Version,
                "ops-user",
                Rationale: "Close recovered period after remediation.",
                ReportPackId: "report-pack-recovery-2",
                ChecklistControlApprovals: RequiredChecklistControlApprovals()));

        closed.Success.Should().BeTrue();
        closed.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.Closed);
        AssertCloseReadinessReady(closed.Workflow.CloseReadiness);
        closed.Workflow.ClosePackage.Should().NotBeNull();
        closed.Workflow.ClosePackage!.ReportPackId.Should().Be("report-pack-recovery-2");
        closed.Workflow.ClosePackage.EvidenceHash.Should().MatchRegex("^[a-f0-9]{64}$");
        closed.Workflow.ClosePackage.RetainedManifestRoute.Should().Contain("/close-package/");
        closed.Workflow.ClosePackage.ChecklistControlApprovals.Should().HaveCount(6);
        AssertAccountingRecordAuditReady(closed.Workflow, "report-pack-recovery-2");

        var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().ContainInOrder(
            "approval-rejected",
            "ledger-draft-built",
            "ledger-draft-validated",
            "ledger-posted",
            "reconciliation-run",
            "approval-submitted",
            "approval-approved",
            "workflow-closed");
    }

    private static void AssertCloseReadinessReady(OperationsCloseReadinessDto? readiness)
    {
        readiness.Should().NotBeNull("close acceptance must expose the server-derived readiness score");
        readiness!.IsReadyToClose.Should().BeTrue();
        readiness.Severity.Should().Be("Info");
        readiness.Score.Should().Be(100);
        readiness.Blockers.Should().BeEmpty();
        readiness.NextActions.Should().BeEmpty();
        readiness.Components.Should().HaveCount(9);
        readiness.Components.Select(component => component.Key).Should().BeEquivalentTo(
        [
            "security-master",
            "provider-freshness",
            "positions",
            "cash",
            "ledger",
            "pricing",
            "reconciliation",
            "reports",
            "approvals"
        ]);
        readiness.Components.All(component =>
            component.IsReady &&
            component.Score == component.Weight &&
            component.Severity == "Info" &&
            component.BlockingReason == null).Should().BeTrue();
    }

    private static void AssertAccountingRecordAuditReady(
        OperationsContinuityWorkflowDto workflow,
        string reportPackId)
    {
        workflow.AccountingRecordSummary.Should().NotBeNull("closed workflows must expose the retained accounting record summary");
        workflow.AccountingRecordSummary!.IsAuditReady.Should().BeTrue();
        workflow.AccountingRecordSummary.CompleteCategoryCount.Should().Be(8);
        workflow.AccountingRecordSummary.RequiredCategoryCount.Should().Be(8);
        workflow.AccountingRecordSummary.AuditPackReadiness.Should().NotBeNull();
        workflow.AccountingRecordSummary.AuditPackReadiness!.IsComplete.Should().BeTrue();

        var reportPackCategory = workflow.AccountingRecordSummary.EvidenceCategories
            .Single(category => category.Key == "report-pack");

        reportPackCategory.IsComplete.Should().BeTrue();
        reportPackCategory.Status.Should().Contain(reportPackId);
        workflow.AccountingRecordSummary.EvidenceCategories
            .Single(category => category.Key == "exports")
            .RequiredEvidence.Should().Contain(
        [
            "export manifest",
            "retained evidence hash",
            "close-package publication"
        ]);
    }

    private static OperationsContinuityWorkflowService CreateService(
        out InMemoryOperationsContinuityRepository repository,
        out InMemoryOperationsWorkflowAuditStore auditStore)
    {
        var derivation = new OperationsStatusDerivationService();
        repository = new InMemoryOperationsContinuityRepository(derivation);
        auditStore = new InMemoryOperationsWorkflowAuditStore();
        return new OperationsContinuityWorkflowService(
            repository,
            auditStore,
            derivation,
            new RecordingLedgerJournalStore());
    }

    private static IReadOnlyList<OperationsChecklistControlApprovalDto> RequiredChecklistControlApprovals() =>
    [
        new("close-gate-brokeringest", "operations-lead", new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero)),
        new("close-gate-securitymaster", "security-master-lead", new DateTimeOffset(2026, 5, 31, 12, 1, 0, TimeSpan.Zero)),
        new("close-gate-ledgerposting", "ledger-lead", new DateTimeOffset(2026, 5, 31, 12, 2, 0, TimeSpan.Zero)),
        new("close-gate-reconciliation", "reconciliation-lead", new DateTimeOffset(2026, 5, 31, 12, 3, 0, TimeSpan.Zero)),
        new("close-gate-approval", "controller", new DateTimeOffset(2026, 5, 31, 12, 4, 0, TimeSpan.Zero)),
        new("close-gate-approval", "fund-admin", new DateTimeOffset(2026, 5, 31, 12, 5, 0, TimeSpan.Zero))
    ];

    private static async Task<OperationsContinuityWorkflowDto> CreateApprovalSubmittedCloseWorkflowAsync(
        OperationsContinuityWorkflowService service,
        Guid fundAccountId,
        string periodId,
        string reportPackId,
        string ledgerBatchId)
    {
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            fundAccountId,
            periodId,
            null,
            "custodian",
            "ops-user"));
        var workflowId = start.Workflow!.WorkflowId;
        var import = await service.ImportBrokerDataAsync(
            workflowId,
            new OperationsTransitionRequestDto(start.Workflow.Version, "ops-user"));
        var normalized = await service.NormalizeBrokerTransactionsAsync(
            workflowId,
            new OperationsTransitionRequestDto(import.Workflow!.Version, "ops-user", "Normalized broker activity"));
        var security = await service.ResolveSecurityMasterMappingsAsync(
            workflowId,
            new OperationsSecurityMasterResolveRequestDto(normalized.Workflow!.Version, "ops-user", "Resolved instruments"));
        var draft = await service.BuildLedgerDraftAsync(
            workflowId,
            new OperationsLedgerDraftRequestDto(security.Workflow!.Version, "ops-user", "ledger-preview-recovery-1", true));
        var validated = await service.ValidateLedgerDraftAsync(
            workflowId,
            new OperationsLedgerValidationRequestDto(draft.Workflow!.Version, "ops-user", true, true));
        var posted = await service.PostLedgerEntriesAsync(
            workflowId,
            new OperationsLedgerPostRequestDto(
                validated.Workflow!.Version,
                "ops-user",
                ledgerBatchId,
                "period-close",
                PeriodOpen: true,
                JournalCandidate: CreateJournalCandidate(fundAccountId)));
        var reconciled = await service.RunReconciliationAsync(
            workflowId,
            new OperationsReconciliationRunRequestDto(posted.Workflow!.Version, "ops-user", "Clean reconciliation", BreakCases: []));
        var posture = await service.RefreshGatePostureAsync(
            workflowId,
            new OperationsGatePostureRequestDto(
                reconciled.Workflow!.Version,
                "ops-user",
                ReportPackReady: true,
                ReportPackId: reportPackId,
                Rationale: "Report pack is ready"));
        var submitted = await service.SubmitForApprovalAsync(
            workflowId,
            new OperationsSubmitApprovalRequestDto(
                posture.Workflow!.Version,
                "ops-user",
                Reviewer: "controller",
                Rationale: "Submit clean close evidence.",
                ReportPackId: reportPackId,
                ChecklistControlApprovals: RequiredChecklistControlApprovals()));

        submitted.Success.Should().BeTrue();
        return submitted.Workflow!;
    }

    private static OperationsLedgerJournalCandidateDto CreateJournalCandidate(Guid? aggregateId = null, Guid? periodId = null)
    {
        var securityId = Guid.Parse("CB931872-F221-47C1-B922-1F61BFA93CF5");
        var idempotencyKey = $"{securityId:N}:fund-close:20260531:AccrueInterestIncome:test-source-hash";
        return new OperationsLedgerJournalCandidateDto(
            JournalEntryId: null,
            AggregateId: aggregateId ?? Guid.NewGuid(),
            PeriodId: periodId ?? Guid.NewGuid(),
            Timestamp: DateTimeOffset.Parse("2026-05-31T21:00:00Z"),
            Description: "Operations continuity month-end posting",
            Lines:
            [
                new OperationsLedgerJournalLineDto(
                    EntryId: null,
                    AccountName: "Cash",
                    AccountType: nameof(LedgerAccountType.Asset),
                    Debit: 100m,
                    Credit: 0m),
                new OperationsLedgerJournalLineDto(
                    EntryId: null,
                    AccountName: "Interest income",
                    AccountType: nameof(LedgerAccountType.Revenue),
                    Debit: 0m,
                    Credit: 100m)
            ],
            CommandId: Guid.NewGuid(),
            SourceEventId: Guid.NewGuid(),
            AccountingBasis: AccountingBasisKindDto.Primary,
            AccountingPolicyId: "legacy-v1",
            AccountingPolicyVersion: "legacy-v1",
            RuleId: "operations-continuity-scenario",
            RuleVersion: "v1",
            PostingKind: LedgerPostingKindDto.Originating,
            Metadata: new OperationsJournalEntryMetadataDto(
                ActivityType: "operations-continuity",
                Symbol: "OPS",
                SecurityId: securityId,
                LedgerBook: "fund-close"),
            IdempotencyKey: idempotencyKey,
            SecurityMasterProvenance: $"security-master:{securityId:N};snapshot:test-source-hash");
    }

    private sealed class RecordingLedgerJournalStore : ILedgerJournalStore
    {
        public List<LedgerJournalEntryWrite> Appended { get; } = [];

        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!entry.Entry.IsBalanced)
            {
                throw new LedgerValidationException("Journal entry must be balanced.");
            }

            Appended.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>([]);
        }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>([]);
        }

        public Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<LedgerAccountingPeriod?>(null);
        }

        public Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(
            Guid? ledgerBookId = null,
            string? status = null,
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerAccountingPeriod>>([]);
        }

        public Task<LedgerAccountingPeriod> SavePeriodAsync(
            LedgerAccountingPeriod period,
            long expectedVersion,
            PeriodCloseEventRecord? closeEvent = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(period);
        }

        public Task<LedgerBookRecord?> GetLedgerBookAsync(Guid ledgerBookId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<LedgerBookRecord?>(null);
        }

        public Task<IReadOnlyList<LedgerBookRecord>> ListLedgerBooksAsync(
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            FundStructureNodeKindDto? fundStructureNodeKind = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerBookRecord>>([]);
        }

        public Task<LedgerBookRecord> SaveLedgerBookAsync(LedgerBookRecord book, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(book);
        }
    }
}

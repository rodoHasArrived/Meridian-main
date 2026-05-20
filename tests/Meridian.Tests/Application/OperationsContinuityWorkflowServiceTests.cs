using FluentAssertions;
using Meridian.Application.OperationsContinuity;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Application;

public sealed class OperationsContinuityWorkflowServiceTests
{
    [Fact]
    public async Task StartWorkflowAsync_ShouldCreateInitialGatesAndAuditEvent()
    {
        var service = CreateService(out _, out var auditStore);
        var fundAccountId = Guid.NewGuid();

        var result = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            fundAccountId,
            "2026-05",
            SecurityMasterSnapshotId: Guid.NewGuid(),
            BrokerSource: "ibkr",
            Actor: "ops-user",
            Rationale: "Open monthly operations close workflow"));

        result.Success.Should().BeTrue();
        result.Workflow.Should().NotBeNull();
        result.Workflow!.FundAccountId.Should().Be(fundAccountId);
        result.Workflow.PeriodId.Should().Be("2026-05");
        result.Workflow.Status.Should().Be(OperationsWorkflowStatusDto.CollectingBrokerData);
        result.Workflow.Version.Should().Be(1);
        result.Workflow.Gates.Single(gate => gate.GateKey == OperationsGateKeyDto.BrokerIngest)
            .Status.Should().Be(OperationsGateStatusDto.InProgress);
        result.Workflow.Gates.Where(gate => gate.GateKey != OperationsGateKeyDto.BrokerIngest)
            .Should().OnlyContain(gate => gate.Status == OperationsGateStatusDto.NotStarted);

        var timeline = await auditStore.GetTimelineAsync(result.Workflow.WorkflowId);
        timeline.Should().ContainSingle();
        timeline[0].EventType.Should().Be("workflow-started");
        timeline[0].PreviousHash.Should().BeNull();
        timeline[0].CurrentHash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task StartWorkflowAsync_ShouldRejectDuplicateOpenWorkflowForSameFundAndPeriod()
    {
        var service = CreateService(out _, out _);
        var fundAccountId = Guid.NewGuid();

        var first = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            fundAccountId,
            "2026-05",
            SecurityMasterSnapshotId: Guid.NewGuid(),
            BrokerSource: "ibkr",
            Actor: "ops-user",
            Rationale: "Open monthly operations close workflow"));

        var second = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            fundAccountId,
            "2026-05",
            SecurityMasterSnapshotId: Guid.NewGuid(),
            BrokerSource: "ibkr",
            Actor: "ops-user",
            Rationale: "Retry duplicate monthly operations close workflow"));

        first.Success.Should().BeTrue();
        second.Success.Should().BeFalse();
        second.ErrorCode.Should().Be("WORKFLOW_ALREADY_EXISTS");
        second.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "OPERATIONS_CONTINUITY_WORKFLOW_ALREADY_EXISTS" &&
            blocker.Gate == null);
    }

    [Fact]
    public async Task StartWorkflowAsync_ShouldNotPersistWorkflowWhenAuditAppendFails()
    {
        var derivation = new OperationsStatusDerivationService();
        var repository = new InMemoryOperationsContinuityRepository(derivation);
        var auditStore = new ThrowingAuditStore("workflow-started");
        var service = new OperationsContinuityWorkflowService(repository, auditStore, derivation);

        var act = () => service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "bank",
            "ops-user"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Simulated audit append failure.");

        var workflows = await repository.ListAsync();
        workflows.Should().BeEmpty();
    }

    [Fact]
    public void Derive_ShouldReturnBlockedWhenAnyGateIsBlocked()
    {
        var derivation = new OperationsStatusDerivationService();
        var workflow = OperationsContinuityWorkflow.Start(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            DateTimeOffset.UtcNow);
        workflow.ReplaceGate(workflow.SecurityMasterGate.WithStatus(
            OperationsGateStatusDto.Blocked,
            blockers:
            [
                new OperationsWorkflowBlockerDto(
                    "SM_ACCOUNTING_CLASSIFICATION_MISSING",
                    "Security Master accounting classification is missing.",
                    OperationsGateKeyDto.SecurityMaster,
                    "Critical",
                    [])
            ]));

        derivation.Derive(workflow).Should().Be(OperationsWorkflowStatusDto.Blocked);
    }

    [Fact]
    public void Derive_ShouldReturnReadyForCloseWhenAllGatesPassedAndApprovalApproved()
    {
        var derivation = new OperationsStatusDerivationService();
        var workflow = OperationsContinuityWorkflow.Start(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            DateTimeOffset.UtcNow);
        foreach (var gate in workflow.Gates)
        {
            workflow.ReplaceGate(gate.WithStatus(OperationsGateStatusDto.Passed));
        }

        workflow.SetApprovalState(OperationsApprovalStateDto.Approved, DateTimeOffset.UtcNow);

        derivation.Derive(workflow).Should().Be(OperationsWorkflowStatusDto.ReadyForClose);
    }

    [Fact]
    public async Task ImportBrokerDataAsync_ShouldRejectVersionMismatchWithoutAppendingAudit()
    {
        var service = CreateService(out _, out var auditStore);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "bank",
            "ops-user"));

        var result = await service.ImportBrokerDataAsync(
            start.Workflow!.WorkflowId,
            new OperationsTransitionRequestDto(
                ExpectedVersion: 99,
                Actor: "ops-user",
                Rationale: "Import statement batch"));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("VERSION_MISMATCH");
        var timeline = await auditStore.GetTimelineAsync(start.Workflow.WorkflowId);
        timeline.Should().ContainSingle();
    }

    [Fact]
    public async Task ImportBrokerDataAsync_ShouldAppendAuditWithPreviousHash()
    {
        var service = CreateService(out _, out var auditStore);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "bank",
            "ops-user"));

        var import = await service.ImportBrokerDataAsync(
            start.Workflow!.WorkflowId,
            new OperationsTransitionRequestDto(
                start.Workflow.Version,
                "ops-user",
                "Imported custodian statement",
                EvidenceLinks:
                [
                    new OperationsEvidenceLinkDto(
                        "statement-2026-05",
                        "May custodian statement",
                        "/workstation/accounting",
                        "custodian-upload",
                        DateTimeOffset.UtcNow)
                ]));

        import.Success.Should().BeTrue();
        import.Workflow!.BrokerIntakeState.Should().Be(OperationsBrokerIntakeStateDto.Imported);
        import.Workflow.Version.Should().Be(2);

        var timeline = await auditStore.GetTimelineAsync(start.Workflow.WorkflowId);
        timeline.Should().HaveCount(2);
        timeline[1].PreviousHash.Should().Be(timeline[0].CurrentHash);
        timeline[1].References.Should().ContainSingle(link => link.EvidenceId == "statement-2026-05");
    }

    [Fact]
    public async Task StartWorkflowAsync_ShouldPreserveEvidenceIdentityWhileRedactingSensitiveAuditFields()
    {
        var service = CreateService(out _, out var auditStore);

        var result = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            "ops-user",
            Rationale: "Open close lane with api_key=do-not-store and Bearer raw-token-value",
            CorrelationId: "token=raw-correlation-token",
            EvidenceLinks:
            [
                new OperationsEvidenceLinkDto(
                    "statement-2026-05",
                    "Uploaded with password:raw-label-secret",
                    "https://operator:raw-route-secret@example.invalid/statement?api_key=raw-query-secret",
                    "credential=raw-source-secret",
                    DateTimeOffset.UtcNow)
            ]));

        result.Success.Should().BeTrue();

        var timeline = await auditStore.GetTimelineAsync(result.Workflow!.WorkflowId);
        var audit = timeline.Single();
        audit.Rationale.Should().Contain("api_key=[redacted]");
        audit.Rationale.Should().Contain("Bearer [redacted]");
        audit.Rationale.Should().NotContain("do-not-store");
        audit.Rationale.Should().NotContain("raw-token-value");
        audit.CorrelationId.Should().Be("token=[redacted]");
        audit.References.Should().ContainSingle();
        audit.References[0].EvidenceId.Should().Be("statement-2026-05");
        audit.References[0].Label.Should().Be("Uploaded with password:[redacted]");
        audit.References[0].Route.Should().Be("https://[redacted]@example.invalid/statement?api_key=[redacted]");
        audit.References[0].Source.Should().Be("credential=[redacted]");

        result.Workflow.EvidenceLinks.Should().ContainSingle(link =>
            link.EvidenceId == "statement-2026-05" &&
            link.Route == "https://[redacted]@example.invalid/statement?api_key=[redacted]");
    }

    [Fact]
    public async Task WorkflowCommands_ShouldAdvanceThroughApprovalAndClose()
    {
        var service = CreateService(out _, out var auditStore);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            "ops-user"));
        var workflowId = start.Workflow!.WorkflowId;

        var import = await service.ImportBrokerDataAsync(workflowId, new OperationsTransitionRequestDto(start.Workflow.Version, "ops-user"));
        var normalized = await service.NormalizeBrokerTransactionsAsync(workflowId, new OperationsTransitionRequestDto(
            import.Workflow!.Version,
            "ops-user",
            "Normalized imported broker activity"));
        var security = await service.ResolveSecurityMasterMappingsAsync(workflowId, new OperationsSecurityMasterResolveRequestDto(
            normalized.Workflow!.Version,
            "ops-user",
            "Resolved all instruments"));
        var draft = await service.BuildLedgerDraftAsync(workflowId, new OperationsLedgerDraftRequestDto(
            security.Workflow!.Version,
            "ops-user",
            PreviewId: "ledger-preview-1",
            IsBalanced: true,
            Rationale: "Built Security Master-derived journal preview"));
        var validated = await service.ValidateLedgerDraftAsync(workflowId, new OperationsLedgerValidationRequestDto(
            draft.Workflow!.Version,
            "ops-user",
            IsBalanced: true,
            PeriodOpen: true,
            Rationale: "Validated balanced draft"));
        var posted = await service.PostLedgerEntriesAsync(workflowId, new OperationsLedgerPostRequestDto(
            validated.Workflow!.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-1",
            PostingKind: "period-close",
            PeriodOpen: true,
            Rationale: "Posted validated accounting journals",
            JournalCandidate: CreateJournalCandidate()));
        var reconciled = await service.RunReconciliationAsync(workflowId, new OperationsReconciliationRunRequestDto(
            posted.Workflow!.Version,
            "ops-user",
            "Ran expected-vs-actual reconciliation",
            BreakCases: []));
        var posture = await service.RefreshGatePostureAsync(workflowId, new OperationsGatePostureRequestDto(
            reconciled.Workflow!.Version,
            "ops-user",
            ReportPackReady: true,
            ReportPackId: "report-pack-1",
            Rationale: "Report pack is ready"));
        var submitted = await service.SubmitForApprovalAsync(workflowId, new OperationsSubmitApprovalRequestDto(
            posture.Workflow!.Version,
            "ops-user",
            Reviewer: "reviewer",
            Rationale: "Submit clean workflow",
            ReportPackId: "report-pack-1"));
        var approved = await service.ApproveWorkflowAsync(workflowId, new OperationsApprovalDecisionRequestDto(
            submitted.Workflow!.Version,
            "ops-user",
            Reviewer: "reviewer",
            Rationale: "Approved close evidence",
            ReportPackId: "report-pack-1"));
        var closed = await service.CloseWorkflowAsync(workflowId, new OperationsCloseWorkflowRequestDto(
            approved.Workflow!.Version,
            "ops-user",
            Rationale: "Close accounting period",
            ReportPackId: "report-pack-1"));

        closed.Success.Should().BeTrue();
        closed.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.Closed);
        closed.Workflow.Gates.Should().OnlyContain(gate => gate.Status == OperationsGateStatusDto.Passed);
        closed.Workflow.ReportPackReadiness.IsReady.Should().BeTrue();
        closed.Workflow.Approvals.Should().Contain(approval => approval.Status == OperationsApprovalStateDto.Approved);

        var timeline = await auditStore.GetTimelineAsync(workflowId);
        timeline.Select(entry => entry.EventType).Should().ContainInOrder(
            "workflow-started",
            "broker-imported",
            "broker-transactions-normalized",
            "security-master-resolved",
            "ledger-draft-built",
            "ledger-draft-validated",
            "ledger-posted",
            "reconciliation-run",
            "gate-posture-refreshed",
            "approval-submitted",
            "approval-approved",
            "workflow-closed");
    }

    [Fact]
    public async Task SubmitForApprovalAsync_ShouldRejectMissingSubmissionMetadataWithoutAppendingAudit()
    {
        var service = CreateService(out _, out var auditStore);
        var workflow = await CreateLedgerPostedWorkflowAsync(service);
        var reconciled = await service.RunReconciliationAsync(workflow.WorkflowId, new OperationsReconciliationRunRequestDto(
            workflow.Version,
            "ops-user",
            "Ran expected-vs-actual reconciliation",
            BreakCases: []));
        var posture = await service.RefreshGatePostureAsync(workflow.WorkflowId, new OperationsGatePostureRequestDto(
            reconciled.Workflow!.Version,
            "ops-user",
            ReportPackReady: true,
            ReportPackId: "report-pack-1",
            Rationale: "Report pack is ready"));
        var timelineBefore = await auditStore.GetTimelineAsync(workflow.WorkflowId);

        var result = await service.SubmitForApprovalAsync(workflow.WorkflowId, new OperationsSubmitApprovalRequestDto(
            posture.Workflow!.Version,
            "ops-user",
            Reviewer: "",
            Rationale: "",
            ReportPackId: ""));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "APPROVAL_SUBMISSION_METADATA_REQUIRED" &&
            blocker.Gate == OperationsGateKeyDto.Approval);
        var timelineAfter = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timelineAfter.Should().HaveCount(timelineBefore.Count);
    }

    [Fact]
    public async Task SubmitForApprovalAsync_ShouldRejectBeforePrerequisiteGatesPassWithoutAppendingAudit()
    {
        var service = CreateService(out _, out var auditStore);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            "ops-user"));
        var posture = await service.RefreshGatePostureAsync(start.Workflow!.WorkflowId, new OperationsGatePostureRequestDto(
            start.Workflow.Version,
            "ops-user",
            ReportPackReady: true,
            ReportPackId: "report-pack-early",
            Rationale: "Report pack was uploaded before gates completed"));
        var timelineBefore = await auditStore.GetTimelineAsync(start.Workflow.WorkflowId);

        var result = await service.SubmitForApprovalAsync(start.Workflow.WorkflowId, new OperationsSubmitApprovalRequestDto(
            posture.Workflow!.Version,
            "ops-user",
            "reviewer",
            "Submit early approval",
            "report-pack-early"));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "OPERATIONS_PREREQUISITE_GATES_NOT_PASSED" &&
            blocker.Gate == null);
        var timelineAfter = await auditStore.GetTimelineAsync(start.Workflow.WorkflowId);
        timelineAfter.Should().HaveCount(timelineBefore.Count);
    }

    [Fact]
    public async Task BuildLedgerDraftAsync_ShouldBlockBeforeSecurityMasterResolution()
    {
        var service = CreateService(out _, out _);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            "ops-user"));

        var result = await service.BuildLedgerDraftAsync(start.Workflow!.WorkflowId, new OperationsLedgerDraftRequestDto(
            start.Workflow.Version,
            "ops-user",
            PreviewId: "ledger-preview-1",
            IsBalanced: true));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        result.Blockers.Should().ContainSingle(blocker => blocker.Code == "SECURITY_MASTER_RESOLUTION_REQUIRED");
    }

    [Fact]
    public async Task NormalizeBrokerTransactionsAsync_ShouldAdvanceBrokerGateTowardSecurityMasterResolution()
    {
        var service = CreateService(out _, out var auditStore);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            "ops-user"));
        var import = await service.ImportBrokerDataAsync(
            start.Workflow!.WorkflowId,
            new OperationsTransitionRequestDto(start.Workflow.Version, "ops-user"));

        var normalized = await service.NormalizeBrokerTransactionsAsync(
            start.Workflow.WorkflowId,
            new OperationsTransitionRequestDto(import.Workflow!.Version, "ops-user", "Normalized imported rows"));

        normalized.Success.Should().BeTrue();
        normalized.Workflow!.BrokerIntakeState.Should().Be(OperationsBrokerIntakeStateDto.Normalized);
        normalized.Workflow.SecurityMasterState.Should().Be(OperationsSecurityMasterStateDto.Pending);
        normalized.Workflow.Gates.Single(gate => gate.GateKey == OperationsGateKeyDto.SecurityMaster)
            .Status.Should().Be(OperationsGateStatusDto.InProgress);

        var timeline = await auditStore.GetTimelineAsync(start.Workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().Contain("broker-transactions-normalized");
    }

    [Fact]
    public async Task ResolveSecurityMasterMappingsAsync_ShouldRejectBeforeBrokerNormalizationWithoutAppendingAudit()
    {
        var service = CreateService(out _, out var auditStore);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            "ops-user"));
        var import = await service.ImportBrokerDataAsync(
            start.Workflow!.WorkflowId,
            new OperationsTransitionRequestDto(start.Workflow.Version, "ops-user"));
        var timelineBefore = await auditStore.GetTimelineAsync(start.Workflow.WorkflowId);

        var result = await service.ResolveSecurityMasterMappingsAsync(
            start.Workflow.WorkflowId,
            new OperationsSecurityMasterResolveRequestDto(
                import.Workflow!.Version,
                "ops-user",
                "Attempted resolution before normalization"));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "BROKER_NORMALIZATION_REQUIRED" &&
            blocker.Gate == OperationsGateKeyDto.BrokerIngest);
        var timelineAfter = await auditStore.GetTimelineAsync(start.Workflow.WorkflowId);
        timelineAfter.Should().HaveCount(timelineBefore.Count);
    }

    [Fact]
    public async Task ApproveSecurityMasterOverrideAsync_ShouldPermitLedgerDraftWithGovernedMetadata()
    {
        var service = CreateService(out _, out var auditStore);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            "ops-user"));
        var import = await service.ImportBrokerDataAsync(start.Workflow!.WorkflowId, new OperationsTransitionRequestDto(start.Workflow.Version, "ops-user"));
        var normalized = await service.NormalizeBrokerTransactionsAsync(
            start.Workflow.WorkflowId,
            new OperationsTransitionRequestDto(import.Workflow!.Version, "ops-user", "Normalized imported rows"));
        var security = await service.ResolveSecurityMasterMappingsAsync(start.Workflow.WorkflowId, new OperationsSecurityMasterResolveRequestDto(
            normalized.Workflow!.Version,
            "ops-user",
            OverrideRequestCount: 1));
        security.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.ApprovalPending);

        var approved = await service.ApproveSecurityMasterOverrideAsync(start.Workflow.WorkflowId, "override-1", new OperationsSecurityMasterOverrideApprovalRequestDto(
            security.Workflow.Version,
            "ops-user",
            "override-1",
            "Temporary mapping approved for month-end close",
            "policy-sm-override-v1",
            new DateOnly(2026, 6, 30)));
        var draft = await service.BuildLedgerDraftAsync(start.Workflow.WorkflowId, new OperationsLedgerDraftRequestDto(
            approved.Workflow!.Version,
            "ops-user",
            "ledger-preview-override",
            true));

        approved.Success.Should().BeTrue();
        approved.Workflow!.SecurityMasterState.Should().Be(OperationsSecurityMasterStateDto.OverridesApproved);
        draft.Success.Should().BeTrue();

        var timeline = await auditStore.GetTimelineAsync(start.Workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().Contain("security-master-override-approved");
    }

    [Fact]
    public async Task ApproveSecurityMasterOverrideAsync_ShouldRejectMismatchedRouteAndBodyOverrideIds()
    {
        var service = CreateService(out _, out _);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            "ops-user"));
        var import = await service.ImportBrokerDataAsync(start.Workflow!.WorkflowId, new OperationsTransitionRequestDto(start.Workflow.Version, "ops-user"));
        var normalized = await service.NormalizeBrokerTransactionsAsync(
            start.Workflow.WorkflowId,
            new OperationsTransitionRequestDto(import.Workflow!.Version, "ops-user", "Normalized imported rows"));
        var security = await service.ResolveSecurityMasterMappingsAsync(start.Workflow.WorkflowId, new OperationsSecurityMasterResolveRequestDto(
            normalized.Workflow!.Version,
            "ops-user",
            OverrideRequestCount: 1));

        var result = await service.ApproveSecurityMasterOverrideAsync(
            start.Workflow.WorkflowId,
            "override-route",
            new OperationsSecurityMasterOverrideApprovalRequestDto(
                security.Workflow!.Version,
                "ops-user",
                "override-body",
                "Temporary mapping approved for month-end close",
                "policy-sm-override-v1",
                new DateOnly(2026, 6, 30)));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.Blockers.Should().ContainSingle(blocker => blocker.Code == "SM_OVERRIDE_ID_MISMATCH");
    }

    [Fact]
    public async Task RunReconciliationAsync_ShouldRequirePostedLedgerEntries()
    {
        var service = CreateService(out _, out _);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);

        var result = await service.RunReconciliationAsync(workflow.WorkflowId, new OperationsReconciliationRunRequestDto(
            workflow.Version,
            "ops-user",
            BreakCases: []));

        result.Success.Should().BeFalse();
        result.Blockers.Should().ContainSingle(blocker => blocker.Code == "LEDGER_POSTING_REQUIRED");
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldAppendDurableJournalCandidateBeforeWorkflowPosting()
    {
        var journalStore = new RecordingLedgerJournalStore();
        var service = CreateService(out _, out var auditStore, journalStore);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);
        var candidate = CreateJournalCandidate();

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-1",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: candidate));

        result.Success.Should().BeTrue();
        journalStore.Appended.Should().ContainSingle();
        journalStore.Appended[0].PeriodId.Should().Be(candidate.PeriodId);
        journalStore.Appended[0].AggregateId.Should().Be(candidate.AggregateId);
        journalStore.Appended[0].Entry.IsBalanced.Should().BeTrue();
        result.Workflow!.LedgerPostingState.Should().Be(OperationsLedgerPostingStateDto.Complete);

        var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().Contain("ledger-posted");
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldRejectSuccessfulPostWhenJournalStoreIsMissing()
    {
        var service = CreateService(out _, out var auditStore, registerLedgerJournalStore: false);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-1",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: CreateJournalCandidate()));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("LEDGER_JOURNAL_STORE_UNAVAILABLE");
        result.Blockers.Should().ContainSingle(blocker => blocker.Code == "LEDGER_JOURNAL_STORE_UNAVAILABLE");

        var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().NotContain("ledger-posted");
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldUseTransactionalCommitStoreWhenRegistered()
    {
        var derivation = new OperationsStatusDerivationService();
        var repository = new InMemoryOperationsContinuityRepository(derivation);
        var auditStore = new InMemoryOperationsWorkflowAuditStore();
        var journalStore = new RecordingLedgerJournalStore();
        var commitStore = new RecordingTransactionalCommitStore(repository, auditStore, journalStore);
        var service = new OperationsContinuityWorkflowService(
            repository,
            auditStore,
            derivation,
            ledgerJournalStore: null,
            transactionalCommitStore: commitStore);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);
        var candidate = CreateJournalCandidate();

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-1",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: candidate));

        result.Success.Should().BeTrue();
        commitStore.CommitCount.Should().Be(1);
        journalStore.Appended.Should().ContainSingle();
        result.Workflow!.LedgerPostingState.Should().Be(OperationsLedgerPostingStateDto.Complete);

        var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().Contain("ledger-posted");
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldLeaveWorkflowUnchangedWhenTransactionalCommitFails()
    {
        var derivation = new OperationsStatusDerivationService();
        var repository = new InMemoryOperationsContinuityRepository(derivation);
        var auditStore = new InMemoryOperationsWorkflowAuditStore();
        var journalStore = new RecordingLedgerJournalStore();
        var commitStore = new RecordingTransactionalCommitStore(
            repository,
            auditStore,
            journalStore,
            throwBeforeCommit: true);
        var service = new OperationsContinuityWorkflowService(
            repository,
            auditStore,
            derivation,
            ledgerJournalStore: null,
            transactionalCommitStore: commitStore);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-1",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: CreateJournalCandidate()));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("LEDGER_JOURNAL_APPEND_REJECTED");
        journalStore.Appended.Should().BeEmpty();

        var persisted = await repository.GetAsync(workflow.WorkflowId);
        persisted!.LedgerPostingState.Should().Be(OperationsLedgerPostingStateDto.Validated);
        persisted.LedgerPostingGate.Status.Should().Be(
            workflow.Gates.Single(gate => gate.GateKey == OperationsGateKeyDto.LedgerPosting).Status);
        persisted.Version.Should().Be(workflow.Version);

        var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().NotContain("ledger-posted");
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldBlockClosedPeriodPosting()
    {
        var service = CreateService(out _, out _);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-closed-period",
            PostingKind: "period-close",
            PeriodOpen: false));

        result.Success.Should().BeTrue();
        result.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.Blocked);
        result.Workflow.Blockers.Should().Contain(blocker => blocker.Code == "LEDGER_PERIOD_CLOSED");
    }

    [Fact]
    public async Task ResolveBreakCaseAsync_ShouldClearCriticalBreakAndAllowApprovalSubmission()
    {
        var service = CreateService(out _, out _);
        var workflow = await CreateLedgerPostedWorkflowAsync(service);

        var reconciled = await service.RunReconciliationAsync(workflow.WorkflowId, new OperationsReconciliationRunRequestDto(
            workflow.Version,
            "ops-user",
            BreakCases:
            [
                new OperationsBreakCaseDto(
                    "break-1",
                    "expected-coupon",
                    "Accrual",
                    "Critical",
                    "Open",
                    null,
                    null,
                    "Security Master expected event",
                    "Custodian statement",
                    100m,
                    null,
                    100m,
                    null,
                    "BOND1",
                    "Upload evidence",
                    [])
            ]));
        reconciled.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.Blocked);

        var resolved = await service.ResolveBreakCaseAsync(
            workflow.WorkflowId,
            "break-1",
            new OperationsResolveBreakCaseRequestDto(
                reconciled.Workflow.Version,
                "ops-user",
                ResolutionStatus: "Resolved",
                Rationale: "Uploaded missing custodian evidence"));
        var posture = await service.RefreshGatePostureAsync(workflow.WorkflowId, new OperationsGatePostureRequestDto(
            resolved.Workflow!.Version,
            "ops-user",
            ReportPackReady: true,
            ReportPackId: "report-pack-1"));
        var submit = await service.SubmitForApprovalAsync(workflow.WorkflowId, new OperationsSubmitApprovalRequestDto(
            posture.Workflow!.Version,
            "ops-user",
            Reviewer: "reviewer",
            Rationale: "Submit after critical break cleared",
            ReportPackId: "report-pack-1"));

        resolved.Workflow!.ReconciliationState.Should().Be(OperationsReconciliationStateDto.Complete);
        resolved.Workflow.Status.Should().Be(OperationsWorkflowStatusDto.ApprovalPending);
        submit.Success.Should().BeTrue();
    }

    [Fact]
    public async Task RunReconciliationAsync_ShouldFeedSecurityAccountingIssueCountsIntoGatePosture()
    {
        var service = CreateService(out _, out _);
        var workflow = await CreateLedgerPostedWorkflowAsync(service);

        var reconciled = await service.RunReconciliationAsync(workflow.WorkflowId, new OperationsReconciliationRunRequestDto(
            workflow.Version,
            "ops-user",
            "Ran reconciliation with Security Master accounting-event issues",
            BreakCases: [],
            SecurityAccountingIssueCount: 2,
            ExpectedAccountingEventCount: 3,
            ExpectedJournalPreviewCount: 1));

        reconciled.Success.Should().BeTrue();
        reconciled.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.Blocked);
        reconciled.Workflow.Gates.Single(gate => gate.GateKey == OperationsGateKeyDto.SecurityMaster)
            .Status.Should().Be(OperationsGateStatusDto.Blocked);
        reconciled.Workflow.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "SM_ACCOUNTING_TERMS_INCOMPLETE" &&
            blocker.Gate == OperationsGateKeyDto.SecurityMaster);
    }

    [Fact]
    public async Task ApproveWorkflowAsync_ShouldRequireApprovalMetadata()
    {
        var service = CreateService(out _, out _);
        var workflow = await CreateApprovalSubmittedWorkflowAsync(service);

        var result = await service.ApproveWorkflowAsync(workflow.WorkflowId, new OperationsApprovalDecisionRequestDto(
            workflow.Version,
            "ops-user",
            Reviewer: "",
            Rationale: "",
            ReportPackId: ""));

        result.Success.Should().BeFalse();
        result.Blockers.Should().ContainSingle(blocker => blocker.Code == "APPROVAL_METADATA_REQUIRED");
    }

    [Fact]
    public async Task RejectWorkflowAsync_ShouldRouteLedgerMismatchBackToLedgerDraft()
    {
        var service = CreateService(out _, out var auditStore);
        var workflow = await CreateApprovalSubmittedWorkflowAsync(service);

        var rejected = await service.RejectWorkflowAsync(workflow.WorkflowId, new OperationsRejectWorkflowRequestDto(
            workflow.Version,
            "ops-user",
            Reviewer: "reviewer",
            Rationale: "Ledger entries need correction before close",
            ReasonCode: "LedgerMismatch"));

        rejected.Success.Should().BeTrue();
        rejected.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.LedgerPostingDraft);
        rejected.Workflow.ApprovalState.Should().Be(OperationsApprovalStateDto.Rejected);
        rejected.Workflow.Gates.Single(gate => gate.GateKey == OperationsGateKeyDto.LedgerPosting)
            .Status.Should().Be(OperationsGateStatusDto.InProgress);

        var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().Contain("approval-rejected");
    }

    [Fact]
    public async Task ReopenWorkflowAsync_ShouldRequireGovernedIncidentMetadata()
    {
        var service = CreateService(out _, out _);
        var workflow = await CreateApprovalSubmittedWorkflowAsync(service);
        var approved = await service.ApproveWorkflowAsync(workflow.WorkflowId, new OperationsApprovalDecisionRequestDto(
            workflow.Version,
            "ops-user",
            "reviewer",
            "Approved close",
            "report-pack-1"));
        var closed = await service.CloseWorkflowAsync(workflow.WorkflowId, new OperationsCloseWorkflowRequestDto(
            approved.Workflow!.Version,
            "ops-user",
            "Close workflow",
            "report-pack-1"));

        var denied = await service.ReopenWorkflowAsync(workflow.WorkflowId, new OperationsReopenWorkflowRequestDto(
            closed.Workflow!.Version,
            "ops-user",
            Rationale: "",
            IncidentId: "",
            IsGovernedAdmin: false));
        var reopened = await service.ReopenWorkflowAsync(workflow.WorkflowId, new OperationsReopenWorkflowRequestDto(
            closed.Workflow.Version,
            "ops-user",
            Rationale: "Incident requires reopened reconciliation",
            IncidentId: "INC-123",
            IsGovernedAdmin: true));

        denied.Success.Should().BeFalse();
        denied.Blockers.Should().ContainSingle(blocker => blocker.Code == "REOPEN_GOVERNANCE_METADATA_REQUIRED");
        reopened.Success.Should().BeTrue();
        reopened.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.ReconciliationActive);
        reopened.Workflow.EvidenceLinks.Should().Contain(link => link.EvidenceId == "INC-123");
    }

    [Fact]
    public async Task RefreshGatePostureAsync_ShouldRejectClosedWorkflowWithoutAppendingAudit()
    {
        var service = CreateService(out _, out var auditStore);
        var closed = await CreateClosedWorkflowAsync(service);
        var timelineBefore = await auditStore.GetTimelineAsync(closed.WorkflowId);

        var result = await service.RefreshGatePostureAsync(closed.WorkflowId, new OperationsGatePostureRequestDto(
            closed.Version,
            "ops-user",
            ReportPackReady: false,
            Rationale: "Attempt to mutate a closed workflow"));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "WORKFLOW_CLOSED" &&
            blocker.Gate == null);

        var timelineAfter = await auditStore.GetTimelineAsync(closed.WorkflowId);
        timelineAfter.Should().HaveCount(timelineBefore.Count);
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldRejectClosedWorkflowBeforeLedgerPreconditions()
    {
        var service = CreateService(out _, out var auditStore);
        var closed = await CreateClosedWorkflowAsync(service);
        var timelineBefore = await auditStore.GetTimelineAsync(closed.WorkflowId);

        var result = await service.PostLedgerEntriesAsync(closed.WorkflowId, new OperationsLedgerPostRequestDto(
            closed.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-after-close",
            PostingKind: "period-close",
            PeriodOpen: true,
            Rationale: "Attempt to post after close",
            JournalCandidate: CreateJournalCandidate()));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "WORKFLOW_CLOSED" &&
            blocker.Gate == OperationsGateKeyDto.LedgerPosting);

        var timelineAfter = await auditStore.GetTimelineAsync(closed.WorkflowId);
        timelineAfter.Should().HaveCount(timelineBefore.Count);
    }

    [Fact]
    public async Task ImportBrokerDataAsync_ShouldRejectRepeatedImportWithoutAppendingAudit()
    {
        var service = CreateService(out _, out var auditStore);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "bank",
            "ops-user"));

        var import = await service.ImportBrokerDataAsync(
            start.Workflow!.WorkflowId,
            new OperationsTransitionRequestDto(
                start.Workflow.Version,
                "ops-user",
                "Imported custodian statement"));

        var repeatedImport = await service.ImportBrokerDataAsync(
            start.Workflow.WorkflowId,
            new OperationsTransitionRequestDto(
                import.Workflow!.Version,
                "ops-user",
                "Retried broker import"));

        import.Success.Should().BeTrue();
        repeatedImport.Success.Should().BeFalse();
        repeatedImport.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        repeatedImport.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "BROKER_IMPORT_ALREADY_RECORDED" &&
            blocker.Gate == OperationsGateKeyDto.BrokerIngest);

        var timeline = await auditStore.GetTimelineAsync(start.Workflow.WorkflowId);
        timeline.Should().HaveCount(2);
    }

    [Fact]
    public async Task ImportBrokerDataAsync_ShouldLeaveWorkflowUnchangedWhenAuditAppendFails()
    {
        var derivation = new OperationsStatusDerivationService();
        var repository = new InMemoryOperationsContinuityRepository(derivation);
        var auditStore = new ThrowingAuditStore("broker-imported");
        var service = new OperationsContinuityWorkflowService(repository, auditStore, derivation);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "bank",
            "ops-user"));
        start.Workflow.Should().NotBeNull();
        var workflow = start.Workflow!;

        var act = () => service.ImportBrokerDataAsync(
            workflow.WorkflowId,
            new OperationsTransitionRequestDto(
                workflow.Version,
                "ops-user",
                "Imported custodian statement"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Simulated audit append failure.");

        var persisted = await repository.GetAsync(workflow.WorkflowId);
        persisted.Should().NotBeNull();
        persisted!.BrokerIntakeState.Should().Be(OperationsBrokerIntakeStateDto.Pending);
        persisted.BrokerIngestGate.Status.Should().Be(OperationsGateStatusDto.InProgress);
    }

    [Fact]
    public async Task FileAuditStore_ShouldAppendJsonlEventsAndMaintainHashChain()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "operations-continuity", Guid.NewGuid().ToString("N"));
        var store = new FileOperationsWorkflowAuditStore(root, NullLogger<FileOperationsWorkflowAuditStore>.Instance);
        var workflowId = Guid.NewGuid();
        var fundAccountId = Guid.NewGuid();

        var first = await store.AppendAsync(CreateAuditDraft(
            workflowId,
            fundAccountId,
            "workflow-started",
            OperationsWorkflowStatusDto.NotStarted,
            OperationsWorkflowStatusDto.CollectingBrokerData));
        var second = await store.AppendAsync(CreateAuditDraft(
            workflowId,
            fundAccountId,
            "broker-imported",
            OperationsWorkflowStatusDto.CollectingBrokerData,
            OperationsWorkflowStatusDto.CollectingBrokerData));

        var timeline = await store.GetTimelineAsync(workflowId);
        timeline.Should().HaveCount(2);
        timeline[0].CurrentHash.Should().Be(first.CurrentHash);
        timeline[1].CurrentHash.Should().Be(second.CurrentHash);
        timeline[1].PreviousHash.Should().Be(timeline[0].CurrentHash);

        var auditPath = Path.Combine(root, "operations-continuity", "audit", $"{workflowId:N}.jsonl");
        File.ReadAllLines(auditPath).Should().HaveCount(2);
    }

    private static OperationsContinuityWorkflowService CreateService(
        out InMemoryOperationsContinuityRepository repository,
        out InMemoryOperationsWorkflowAuditStore auditStore,
        RecordingLedgerJournalStore? ledgerJournalStore = null,
        bool registerLedgerJournalStore = true)
    {
        var derivation = new OperationsStatusDerivationService();
        repository = new InMemoryOperationsContinuityRepository(derivation);
        auditStore = new InMemoryOperationsWorkflowAuditStore();
        return new OperationsContinuityWorkflowService(
            repository,
            auditStore,
            derivation,
            registerLedgerJournalStore ? ledgerJournalStore ?? new RecordingLedgerJournalStore() : null);
    }

    private static async Task<OperationsContinuityWorkflowDto> CreateLedgerValidatedWorkflowAsync(
        OperationsContinuityWorkflowService service)
    {
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            "ops-user"));
        var import = await service.ImportBrokerDataAsync(start.Workflow!.WorkflowId, new OperationsTransitionRequestDto(start.Workflow.Version, "ops-user"));
        var normalized = await service.NormalizeBrokerTransactionsAsync(start.Workflow.WorkflowId, new OperationsTransitionRequestDto(import.Workflow!.Version, "ops-user"));
        var security = await service.ResolveSecurityMasterMappingsAsync(start.Workflow.WorkflowId, new OperationsSecurityMasterResolveRequestDto(normalized.Workflow!.Version, "ops-user"));
        var draft = await service.BuildLedgerDraftAsync(start.Workflow.WorkflowId, new OperationsLedgerDraftRequestDto(security.Workflow!.Version, "ops-user", "ledger-preview-1", true));
        var validated = await service.ValidateLedgerDraftAsync(start.Workflow.WorkflowId, new OperationsLedgerValidationRequestDto(draft.Workflow!.Version, "ops-user", true, true));
        return validated.Workflow!;
    }

    private static async Task<OperationsContinuityWorkflowDto> CreateApprovalSubmittedWorkflowAsync(
        OperationsContinuityWorkflowService service)
    {
        var workflow = await CreateLedgerPostedWorkflowAsync(service);
        var reconciled = await service.RunReconciliationAsync(workflow.WorkflowId, new OperationsReconciliationRunRequestDto(workflow.Version, "ops-user", BreakCases: []));
        var posture = await service.RefreshGatePostureAsync(workflow.WorkflowId, new OperationsGatePostureRequestDto(
            reconciled.Workflow!.Version,
            "ops-user",
            ReportPackReady: true,
            ReportPackId: "report-pack-1"));
        var submitted = await service.SubmitForApprovalAsync(workflow.WorkflowId, new OperationsSubmitApprovalRequestDto(
            posture.Workflow!.Version,
            "ops-user",
            "reviewer",
            "Submit for approval",
            "report-pack-1"));
        return submitted.Workflow!;
    }

    private static async Task<OperationsContinuityWorkflowDto> CreateClosedWorkflowAsync(
        OperationsContinuityWorkflowService service)
    {
        var workflow = await CreateApprovalSubmittedWorkflowAsync(service);
        var approved = await service.ApproveWorkflowAsync(workflow.WorkflowId, new OperationsApprovalDecisionRequestDto(
            workflow.Version,
            "ops-user",
            "reviewer",
            "Approved close",
            "report-pack-1"));
        var closed = await service.CloseWorkflowAsync(workflow.WorkflowId, new OperationsCloseWorkflowRequestDto(
            approved.Workflow!.Version,
            "ops-user",
            "Close workflow",
            "report-pack-1"));
        return closed.Workflow!;
    }

    private static async Task<OperationsContinuityWorkflowDto> CreateLedgerPostedWorkflowAsync(
        OperationsContinuityWorkflowService service)
    {
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);
        var posted = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-1",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: CreateJournalCandidate()));
        return posted.Workflow!;
    }

    private static OperationsLedgerJournalCandidateDto CreateJournalCandidate() =>
        new(
            JournalEntryId: null,
            AggregateId: Guid.NewGuid(),
            PeriodId: Guid.NewGuid(),
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
            AccountingBasis: AccountingBasisKindDto.Primary,
            AccountingPolicyId: "legacy-v1",
            AccountingPolicyVersion: "legacy-v1",
            PostingKind: LedgerPostingKindDto.Originating,
            Metadata: new OperationsJournalEntryMetadataDto(
                ActivityType: "operations-continuity",
                LedgerBook: "fund-close"));

    private static OperationsWorkflowAuditDraft CreateAuditDraft(
        Guid workflowId,
        Guid fundAccountId,
        string eventType,
        OperationsWorkflowStatusDto fromState,
        OperationsWorkflowStatusDto toState) =>
        new(
            workflowId,
            fundAccountId,
            "2026-05",
            eventType,
            fromState,
            toState,
            OperationsGateKeyDto.BrokerIngest,
            OperationsGateStatusDto.InProgress,
            OperationsGateStatusDto.InProgress,
            "ops-user",
            "Monthly close evidence",
            "corr-1",
            []);

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

    private sealed class RecordingTransactionalCommitStore : IOperationsContinuityTransactionalCommitStore
    {
        private readonly IOperationsContinuityRepository _repository;
        private readonly IOperationsWorkflowAuditStore _auditStore;
        private readonly RecordingLedgerJournalStore _journalStore;
        private readonly bool _throwBeforeCommit;

        public RecordingTransactionalCommitStore(
            IOperationsContinuityRepository repository,
            IOperationsWorkflowAuditStore auditStore,
            RecordingLedgerJournalStore journalStore,
            bool throwBeforeCommit = false)
        {
            _repository = repository;
            _auditStore = auditStore;
            _journalStore = journalStore;
            _throwBeforeCommit = throwBeforeCommit;
        }

        public int CommitCount { get; private set; }

        public async Task<OperationsContinuityTransactionalCommitResult> CommitLedgerPostingAsync(
            OperationsContinuityWorkflow workflow,
            OperationsWorkflowAuditDraft auditDraft,
            LedgerJournalEntryWrite journalEntry,
            CancellationToken ct = default)
        {
            CommitCount++;
            if (_throwBeforeCommit)
            {
                throw new LedgerValidationException("Simulated transactional commit rejection.");
            }

            await _journalStore.AppendAsync(journalEntry, ct).ConfigureAwait(false);
            var audit = await _auditStore.AppendAsync(auditDraft, ct).ConfigureAwait(false);
            workflow.Touch(audit.OccurredAtUtc);
            await _repository.SaveAsync(workflow, ct).ConfigureAwait(false);
            return new OperationsContinuityTransactionalCommitResult(workflow, audit);
        }
    }

    private sealed class ThrowingAuditStore(string failEventType) : IOperationsWorkflowAuditStore
    {
        private readonly InMemoryOperationsWorkflowAuditStore _inner = new();

        public Task<OperationsWorkflowAuditDto> AppendAsync(OperationsWorkflowAuditDraft draft, CancellationToken ct = default)
        {
            if (string.Equals(draft.EventType, failEventType, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Simulated audit append failure.");
            }

            return _inner.AppendAsync(draft, ct);
        }

        public Task<IReadOnlyList<OperationsWorkflowAuditDto>> GetTimelineAsync(Guid workflowId, CancellationToken ct = default)
            => _inner.GetTimelineAsync(workflowId, ct);
    }
}
